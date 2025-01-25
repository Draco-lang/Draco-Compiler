using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using Draco.Compiler.Internal.Symbols.Synthetized;

namespace Draco.Compiler.Internal.Symbols.Metadata;

/// <summary>
/// Utilities for reading up metadata symbols.
/// </summary>
internal static class MetadataSymbol
{
    /// <summary>
    /// Attributes of a static class.
    /// </summary>
    public static readonly TypeAttributes StaticClassAttributes =
        TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.Class;

    /// <summary>
    /// Turns the given type definition into a symbol.
    /// </summary>
    /// <param name="containingSymbol">The containing symbol.</param>
    /// <param name="typeDefinition">The type definition to turn into a symbol.</param>
    /// <returns>The symbol representing the type definition, contained in <paramref name="containingSymbol"/>.</returns>
    public static Symbol ToSymbol(Symbol containingSymbol, TypeDefinition typeDefinition)
    {
        if (typeDefinition.Attributes.HasFlag(StaticClassAttributes))
        {
            // Static classes are treated as modules, nothing extra to do
            return new MetadataStaticClassSymbol(containingSymbol, typeDefinition);
        }
        else
        {
            // Non-static classes
            return new MetadataTypeSymbol(containingSymbol, typeDefinition);
        }
    }

    /// <summary>
    /// Decodes the given constant.
    /// </summary>
    /// <param name="constant">The constant to decode.</param>
    /// <param name="metadataReader">The metadata reader to use.</param>
    /// <returns>The decoded constant.</returns>
    public static object? DecodeConstant(Constant constant, MetadataReader metadataReader)
    {
        var blob = metadataReader.GetBlobBytes(constant.Value);
        return constant.TypeCode switch
        {
            ConstantTypeCode.NullReference => null,

            ConstantTypeCode.Boolean => BitConverter.ToBoolean(blob),
            ConstantTypeCode.Char => BitConverter.ToChar(blob),

            ConstantTypeCode.String => BitConverter.ToString(blob),

            ConstantTypeCode.Byte => blob[0],
            ConstantTypeCode.UInt16 => BitConverter.ToUInt16(blob),
            ConstantTypeCode.UInt32 => BitConverter.ToUInt32(blob),
            ConstantTypeCode.UInt64 => BitConverter.ToUInt64(blob),

            ConstantTypeCode.SByte => (sbyte)blob[0],
            ConstantTypeCode.Int16 => BitConverter.ToInt16(blob),
            ConstantTypeCode.Int32 => BitConverter.ToInt32(blob),
            ConstantTypeCode.Int64 => BitConverter.ToInt64(blob),

            ConstantTypeCode.Single => BitConverter.ToSingle(blob),
            ConstantTypeCode.Double => BitConverter.ToDouble(blob),

            _ => throw new ArgumentOutOfRangeException(nameof(constant)),
        };
    }

    /// <summary>
    /// Decodes the given attribute list.
    /// </summary>
    /// <param name="handleCollection">The handle collection to decode.</param>
    /// <param name="symbol">The contextual metadata symbol.</param>
    /// <returns>The decoded attribute list.</returns>
    public static ImmutableArray<AttributeInstance> DecodeAttributeList(
        CustomAttributeHandleCollection handleCollection,
        IMetadataSymbol symbol) => handleCollection
        .Select(handle => DecodeAttribute(handle, symbol))
        .ToImmutableArray();

    /// <summary>
    /// Decodes an attribute from the given handle.
    /// </summary>
    /// <param name="handle">The handle to decode the attribute from.</param>
    /// <param name="symbol">The contextual metadata symbol.</param>
    /// <returns>The decoded attribute instance.</returns>
    public static AttributeInstance DecodeAttribute(CustomAttributeHandle handle, IMetadataSymbol symbol)
    {
        var assembly = symbol.Assembly;
        var metadataReader = assembly.MetadataReader;
        var typeProvider = assembly.DeclaringCompilation.TypeProvider;

        var attribute = metadataReader.GetCustomAttribute(handle);

        var constructor = GetFunctionFromHandle(attribute.Constructor, symbol)
                       ?? throw new InvalidOperationException("attribute constructor not found");

        var arguments = attribute.DecodeValue(typeProvider);
        var fixedArgs = arguments.FixedArguments
            .Select(a => new ConstantValue(a.Type, a.Value))
            .ToImmutableArray();
        var namedArgs = arguments.NamedArguments
            .ToImmutableDictionary(a => a.Name ?? string.Empty, a => new ConstantValue(a.Type, a.Value));

        return new AttributeInstance(constructor, fixedArgs, namedArgs);
    }

    /// <summary>
    /// Retrieves the type from the given handle.
    /// </summary>
    /// <param name="handle">The handle to get the type from.</param>
    /// <param name="symbol">The contextual metadata symbol.</param>
    /// <returns>The type symbol from the given handle.</returns>
    public static TypeSymbol GetTypeFromHandle(EntityHandle handle, IMetadataSymbol symbol)
    {
        var typeProvider = symbol.Assembly.DeclaringCompilation.TypeProvider;
        var metadataReader = symbol.Assembly.MetadataReader;
        return handle.Kind switch
        {
            HandleKind.TypeDefinition => typeProvider.GetTypeFromDefinition(metadataReader, (TypeDefinitionHandle)handle, 0),
            HandleKind.TypeReference => typeProvider.GetTypeFromReference(metadataReader, (TypeReferenceHandle)handle, 0),
            HandleKind.TypeSpecification => typeProvider.GetTypeFromSpecification(metadataReader, (Symbol)symbol, (TypeSpecificationHandle)handle, 0),
            _ => throw new InvalidOperationException(),
        };
    }

    /// <summary>
    /// Retrieves a function from the given handle.
    /// </summary>
    /// <param name="handle">The handle to get the function from.</param>
    /// <param name="symbol">The contextual metadata symbol.</param>
    /// <returns>The function symbol from the given handle.</returns>
    public static FunctionSymbol? GetFunctionFromHandle(EntityHandle handle, IMetadataSymbol symbol) => handle.Kind switch
    {
        HandleKind.MethodDefinition => GetFunctionFromDefinition((MethodDefinitionHandle)handle, symbol),
        HandleKind.MemberReference => GetFunctionFromReference((MemberReferenceHandle)handle, symbol),
        _ => throw new InvalidOperationException(),
    };

    private static FunctionSymbol? GetFunctionFromDefinition(MethodDefinitionHandle methodDef, IMetadataSymbol symbol)
    {
        // NOTE: This potentially loads functions multiple times, which isn't great...
        var assembly = symbol.Assembly;
        var metadataReader = assembly.MetadataReader;
        var provider = assembly.DeclaringCompilation.TypeProvider;
        var definition = metadataReader.GetMethodDefinition(methodDef);
        var containingType = provider.GetTypeFromDefinition(metadataReader, definition.GetDeclaringType(), 0);
        var function = new MetadataMethodSymbol(containingType, definition);
        return function;
    }

    private static FunctionSymbol? GetFunctionFromReference(MemberReferenceHandle methodRef, IMetadataSymbol symbol)
    {
        var assembly = symbol.Assembly;
        var metadataReader = assembly.MetadataReader;
        var provider = assembly.DeclaringCompilation.TypeProvider;
        var reference = metadataReader.GetMemberReference(methodRef);
        var name = metadataReader.GetString(reference.Name);
        var containingType = GetTypeFromHandle(reference.Parent, symbol);
        // We construct a pseudo-symbol for signature decoding
        var pseudoSymbol = new MetadataFunctionSignatureSymbol(containingType);
        var signature = reference.DecodeMethodSignature(provider, pseudoSymbol);
        return GetFunctionWithSignature(containingType, name, signature);
    }

    private static FunctionSymbol? GetFunctionWithSignature(
        TypeSymbol containingType,
        string name,
        MethodSignature<TypeSymbol> signature)
    {
        var functions = containingType.DefinedMembers
            .OfType<FunctionSymbol>()
            .Concat(containingType.DefinedPropertyAccessors);
        foreach (var function in functions)
        {
            if (function.Name != name) continue;
            if (SignaturesMatch(function, signature)) return function;
        }
        return null;
    }

    private static bool SignaturesMatch(FunctionSymbol function, MethodSignature<TypeSymbol> signature)
    {
        if (function.Parameters.Length != signature.ParameterTypes.Length) return false;
        if (function.GenericParameters.Length != signature.GenericParameterCount) return false;
        for (var i = 0; i < function.Parameters.Length; i++)
        {
            if (!SymbolEqualityComparer.Default.Equals(function.Parameters[i].Type, signature.ParameterTypes[i]))
            {
                return false;
            }
        }
        return true;
    }
}
