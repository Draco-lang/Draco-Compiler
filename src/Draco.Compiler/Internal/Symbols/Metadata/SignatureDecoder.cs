using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;
using Draco.Compiler.Internal.Symbols.Synthetized;
using Draco.Compiler.Internal.Utilities;

namespace Draco.Compiler.Internal.Symbols.Metadata;

/// <summary>
/// Helper for decoding signature types.
/// </summary>
internal sealed class SignatureDecoder : ISignatureTypeProvider<TypeSymbol, Unit>
{
    // TODO: We return a special error type for now to swallow errors
    private static TypeSymbol UnknownType { get; } = new PrimitiveTypeSymbol("<unknown>");

    private readonly ModuleSymbol rootModule;

    public SignatureDecoder(ModuleSymbol rootModule)
    {
        this.rootModule = rootModule;
    }

    public TypeSymbol GetArrayType(TypeSymbol elementType, ArrayShape shape) => UnknownType;
    public TypeSymbol GetByReferenceType(TypeSymbol elementType) => UnknownType;
    public TypeSymbol GetFunctionPointerType(MethodSignature<TypeSymbol> signature) => UnknownType;
    public TypeSymbol GetGenericInstantiation(TypeSymbol genericType, ImmutableArray<TypeSymbol> typeArguments) => UnknownType;
    public TypeSymbol GetGenericMethodParameter(Unit genericContext, int index) => UnknownType;
    public TypeSymbol GetGenericTypeParameter(Unit genericContext, int index) => UnknownType;
    public TypeSymbol GetModifiedType(TypeSymbol modifier, TypeSymbol unmodifiedType, bool isRequired) => UnknownType;
    public TypeSymbol GetPinnedType(TypeSymbol elementType) => UnknownType;
    public TypeSymbol GetPointerType(TypeSymbol elementType) => UnknownType;
    public TypeSymbol GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
    {
        PrimitiveTypeCode.Boolean => IntrinsicSymbols.Bool,
        PrimitiveTypeCode.Int32 => IntrinsicSymbols.Int32,
        PrimitiveTypeCode.String => IntrinsicSymbols.String,
        PrimitiveTypeCode.Void => IntrinsicSymbols.Unit,
        _ => UnknownType,
    };
    public TypeSymbol GetSZArrayType(TypeSymbol elementType) => UnknownType;
    public TypeSymbol GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var definition = reader.GetTypeDefinition(handle);
        if (definition.IsNested)
        {
            // TODO
            return UnknownType;
        }

        // TODO: Ask Reflectronic about this... way
        // We try to look up the symbol by its full name from the root
        var @namespace = reader.GetString(definition.Namespace);
        var name = reader.GetString(definition.Name);
        var fullName = $"{@namespace}.{name}";
        var parts = fullName.Split('.').ToImmutableArray();
        var typeSymbol = this.rootModule
            .Lookup(parts)
            .OfType<TypeSymbol>()
            .Single();
        return typeSymbol;
    }
    public TypeSymbol GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        // TODO
        return UnknownType;
    }
    public TypeSymbol GetTypeFromSpecification(MetadataReader reader, Unit genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => UnknownType;
}
