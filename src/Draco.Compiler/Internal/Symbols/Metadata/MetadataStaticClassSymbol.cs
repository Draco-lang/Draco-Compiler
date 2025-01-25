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
/// A static class read up from metadata that we handle as a module.
/// </summary>
internal sealed class MetadataStaticClassSymbol(
    Symbol containingSymbol,
    TypeDefinition typeDefinition) : ModuleSymbol, IMetadataSymbol
{
    public override Compilation DeclaringCompilation => this.Assembly.DeclaringCompilation;

    public override ImmutableArray<AttributeInstance> Attributes => InterlockedUtils.InitializeDefault(ref this.attributes, this.BuildAttributes);
    private ImmutableArray<AttributeInstance> attributes;

    public override IEnumerable<Symbol> AllMembers =>
        InterlockedUtils.InitializeDefault(ref this.members, this.BuildMembers);
    private ImmutableArray<Symbol> members;

    public override string Name => this.MetadataName;
    public override string MetadataName => this.MetadataReader.GetString(typeDefinition.Name);
    public MetadataReader MetadataReader => this.Assembly.MetadataReader;
    public override Symbol ContainingSymbol { get; } = containingSymbol;

    public override Visibility Visibility => typeDefinition.Attributes switch
    {
        var attr when attr.HasFlag(TypeAttributes.Public)
                   || attr.HasFlag(TypeAttributes.NestedPublic) => Visibility.Public,
        _ => Visibility.Internal,
    };

    public override SymbolDocumentation Documentation => LazyInitializer.EnsureInitialized(ref this.documentation, this.BuildDocumentation);
    private SymbolDocumentation? documentation;

    internal override string RawDocumentation => LazyInitializer.EnsureInitialized(ref this.rawDocumentation, this.BuildRawDocumentation);
    private string? rawDocumentation;

    // NOTE: thread-safety does not matter, same instance
    public MetadataAssemblySymbol Assembly => this.assembly ??= this.AncestorChain.OfType<MetadataAssemblySymbol>().First();
    private MetadataAssemblySymbol? assembly;

    private ImmutableArray<AttributeInstance> BuildAttributes() =>
        MetadataSymbol.DecodeAttributeList(typeDefinition.GetCustomAttributes(), this);

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
            var methodDef = this.MetadataReader.GetMethodDefinition(methodHandle);
            // Skip methods with special name
            if (methodDef.Attributes.HasFlag(MethodAttributes.SpecialName)) continue;
            // Skip non-static methods
            // TODO: What's Invoke in System.Console?
            if (!methodDef.Attributes.HasFlag(MethodAttributes.Static)) continue;
            var methodSym = new MetadataMethodSymbol(
                containingSymbol: this,
                methodDefinition: methodDef);
            result.Add(methodSym);
        }

        // Fields
        foreach (var fieldHandle in typeDefinition.GetFields())
        {
            var fieldDef = this.MetadataReader.GetFieldDefinition(fieldHandle);
            // Skip fields with special name
            if (fieldDef.Attributes.HasFlag(FieldAttributes.SpecialName)) continue;
            // Skip non-static fields
            if (!fieldDef.Attributes.HasFlag(FieldAttributes.Static)) continue;
            var fieldSym = new MetadataFieldSymbol(
                containingSymbol: this,
                fieldDefinition: fieldDef);
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

        // Done
        return result.ToImmutable();
    }

    private SymbolDocumentation BuildDocumentation() =>
        XmlDocumentationExtractor.Extract(this);

    private string BuildRawDocumentation() =>
        MetadataDocumentation.GetDocumentation(this);
}
