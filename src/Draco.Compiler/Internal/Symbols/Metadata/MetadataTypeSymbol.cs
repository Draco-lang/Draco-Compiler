using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Threading;
using Draco.Compiler.Api;
using Draco.Compiler.Api.Semantics;
using Draco.Compiler.Internal.Documentation;
using Draco.Compiler.Internal.Documentation.Extractors;
using Draco.Compiler.Internal.Utilities;

namespace Draco.Compiler.Internal.Symbols.Metadata;

/// <summary>
/// A type definition read up from metadata.
/// </summary>
internal sealed class MetadataTypeSymbol(
    Symbol containingSymbol,
    TypeDefinition typeDefinition) : TypeSymbol, IMetadataSymbol
{
    public override Compilation DeclaringCompilation => this.Assembly.DeclaringCompilation;

    public override ImmutableArray<AttributeInstance> Attributes =>
        InterlockedUtils.InitializeDefault(ref this.attributes, this.BuildAttributes);
    private ImmutableArray<AttributeInstance> attributes;

    public override IEnumerable<Symbol> DefinedMembers =>
        InterlockedUtils.InitializeDefault(ref this.definedMembers, this.BuildMembers);
    private ImmutableArray<Symbol> definedMembers;

    public override IEnumerable<FunctionSymbol> Constructors =>
        InterlockedUtils.InitializeDefault(ref this.constructors, this.BuildConstructors);
    private ImmutableArray<FunctionSymbol> constructors;

    public override string Name => LazyInitializer.EnsureInitialized(ref this.name, this.BuildName);
    private string? name;

    public override string MetadataName => LazyInitializer.EnsureInitialized(ref this.metadataName, this.BuildMetadataName);
    private string? metadataName;

    public override Visibility Visibility => typeDefinition.Attributes switch
    {
        var attr when attr.HasFlag(TypeAttributes.Public)
                   || attr.HasFlag(TypeAttributes.NestedPublic) => Visibility.Public,
        _ => Visibility.Internal,
    };

    public override ImmutableArray<TypeParameterSymbol> GenericParameters =>
        InterlockedUtils.InitializeDefault(ref this.genericParameters, this.BuildGenericParameters);
    private ImmutableArray<TypeParameterSymbol> genericParameters;

    public override SymbolDocumentation Documentation => LazyInitializer.EnsureInitialized(ref this.documentation, this.BuildDocumentation);
    private SymbolDocumentation? documentation;

    internal override string RawDocumentation => LazyInitializer.EnsureInitialized(ref this.rawDocumentation, this.BuildRawDocumentation);
    private string? rawDocumentation;

    public override Symbol ContainingSymbol { get; } = containingSymbol;

    public override bool IsValueType => this.IsEnumType || this.BaseTypes.Contains(
        this.Assembly.DeclaringCompilation.WellKnownTypes.SystemValueType,
        SymbolEqualityComparer.Default);

    public override bool IsDelegateType => this.BaseTypes.Contains(
        this.Assembly.DeclaringCompilation.WellKnownTypes.SystemDelegate,
        SymbolEqualityComparer.Default);

    public override bool IsEnumType => this.BaseTypes.Contains(
        this.Assembly.DeclaringCompilation.WellKnownTypes.SystemEnum,
        SymbolEqualityComparer.Default);

    public override bool IsAttributeType => this.BaseTypes.Contains(
        this.Assembly.DeclaringCompilation.WellKnownTypes.SystemAttribute,
        SymbolEqualityComparer.Default);

    public override bool IsInterface => typeDefinition.Attributes.HasFlag(TypeAttributes.Interface);

    public override bool IsAbstract => typeDefinition.Attributes.HasFlag(TypeAttributes.Abstract);

    public override bool IsSealed => typeDefinition.Attributes.HasFlag(TypeAttributes.Sealed);

    public override ImmutableArray<TypeSymbol> ImmediateBaseTypes => InterlockedUtils.InitializeDefault(ref this.baseTypes, this.BuildBaseTypes);
    private ImmutableArray<TypeSymbol> baseTypes;

    // NOTE: thread-safety does not matter, same instance
    public MetadataAssemblySymbol Assembly => this.assembly ??= this.AncestorChain.OfType<MetadataAssemblySymbol>().First();
    private MetadataAssemblySymbol? assembly;

    public MetadataReader MetadataReader => this.Assembly.MetadataReader;

    public override string ToString() => this.GenericParameters.Length == 0
        ? this.Name
        : $"{this.Name}<{string.Join(", ", this.GenericParameters)}>";

    private ImmutableArray<AttributeInstance> BuildAttributes() =>
        MetadataSymbol.DecodeAttributeList(typeDefinition.GetCustomAttributes(), this);

    private string BuildName()
    {
        var name = this.MetadataName;
        var backtickIndex = name.IndexOf('`');
        return backtickIndex == -1
            ? name
            : name[..backtickIndex];
    }

    private string BuildMetadataName() => this.MetadataReader.GetString(typeDefinition.Name);

    private ImmutableArray<TypeParameterSymbol> BuildGenericParameters()
    {
        var genericParamsHandle = typeDefinition.GetGenericParameters();
        if (genericParamsHandle.Count == 0) return [];

        var result = ImmutableArray.CreateBuilder<TypeParameterSymbol>();
        foreach (var genericParamHandle in genericParamsHandle)
        {
            var genericParam = this.MetadataReader.GetGenericParameter(genericParamHandle);
            var symbol = new MetadataTypeParameterSymbol(this, genericParam);
            result.Add(symbol);
        }
        return result.ToImmutableArray();
    }

    private ImmutableArray<TypeSymbol> BuildBaseTypes()
    {
        var builder = ImmutableArray.CreateBuilder<TypeSymbol>();
        var typeProvider = this.Assembly.DeclaringCompilation.TypeProvider;
        if (!typeDefinition.BaseType.IsNil)
        {
            builder.Add(MetadataSymbol.GetTypeFromHandle(typeDefinition.BaseType, this));
        }
        foreach (var @interface in typeDefinition.GetInterfaceImplementations())
        {
            var interfaceDef = this.MetadataReader.GetInterfaceImplementation(@interface);
            if (interfaceDef.Interface.IsNil) continue;
            builder.Add(MetadataSymbol.GetTypeFromHandle(interfaceDef.Interface, this));
        }

        return builder.ToImmutable();
    }

    // NOTE: There's a good reason constructors are factored out, the IsEnumType check would cause an infinite recursion
    // with the members flow
    private ImmutableArray<Symbol> BuildMembers()
    {
        var result = ImmutableArray.CreateBuilder<Symbol>();

        // Nested types
        foreach (var typeHandle in typeDefinition.GetNestedTypes())
        {
            var typeDef = this.MetadataReader.GetTypeDefinition(typeHandle);
            // Skip special name
            if (typeDef.Attributes.HasFlag(TypeAttributes.SpecialName)) continue;
            // Turn into a symbol
            var symbol = MetadataSymbol.ToSymbol(this, typeDef);
            result.Add(symbol);
            // Add additional symbols
            result.AddRange(symbol.GetAdditionalSymbols());
        }

        // Methods
        foreach (var methodHandle in typeDefinition.GetMethods())
        {
            var method = this.MetadataReader.GetMethodDefinition(methodHandle);
            // Skip special name, if not an operator
            if (method.Attributes.HasFlag(MethodAttributes.SpecialName))
            {
                var name = this.MetadataReader.GetString(method.Name);
                if (!name.StartsWith(CompilerConstants.OperatorPrefix)) continue;
            }
            // Add it
            var methodSymbol = new MetadataMethodSymbol(
                containingSymbol: this,
                methodDefinition: method);
            result.Add(methodSymbol);
        }

        // Fields
        foreach (var fieldHandle in typeDefinition.GetFields())
        {
            var fieldDef = this.MetadataReader.GetFieldDefinition(fieldHandle);
            // Add it
            var fieldSym = new MetadataFieldSymbol(containingSymbol: this, fieldDefinition: fieldDef);
            result.Add(fieldSym);
        }

        // Properties
        foreach (var propHandle in typeDefinition.GetProperties())
        {
            var propDef = this.MetadataReader.GetPropertyDefinition(propHandle);
            var propSym = new MetadataPropertySymbol(
                containingSymbol: this,
                propertyDefinition: propDef);
            result.Add(propSym);
        }

        // Add constructors separately
        result.AddRange(this.Constructors);

        // For enums we provide equality operators
        // NOTE: We do it here as in the AdditionalSymbols flow the IsEnumType check would cause an infinite recursion
        // as the members of the module are not initialized yet
        if (this.IsEnumType)
        {
            var wellKnownTypes = this.Assembly.DeclaringCompilation.WellKnownTypes;
            result.AddRange(wellKnownTypes.GetEnumEqualityMembers(this));
        }

        // Done
        return result.ToImmutable();
    }

    private ImmutableArray<FunctionSymbol> BuildConstructors()
    {
        var result = ImmutableArray.CreateBuilder<FunctionSymbol>();

        foreach (var methodHandle in typeDefinition.GetMethods())
        {
            var method = this.MetadataReader.GetMethodDefinition(methodHandle);
            // Skip non-constructors
            if (!method.Attributes.HasFlag(MethodAttributes.SpecialName)) continue;

            var name = this.MetadataReader.GetString(method.Name);
            if (name != CompilerConstants.ConstructorName) continue;

            var ctor = new MetadataMethodSymbol(
                containingSymbol: this,
                methodDefinition: method);

            result.Add(ctor);
        }

        return result.ToImmutable();
    }

    private SymbolDocumentation BuildDocumentation() =>
        XmlDocumentationExtractor.Extract(this);

    private string BuildRawDocumentation() =>
        MetadataDocumentation.GetDocumentation(this);
}
