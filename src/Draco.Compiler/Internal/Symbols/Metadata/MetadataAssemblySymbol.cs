using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Threading;
using System.Xml;
using Draco.Compiler.Api;
using Draco.Compiler.Internal.Utilities;

namespace Draco.Compiler.Internal.Symbols.Metadata;

/// <summary>
/// An assembly imported from metadata.
/// </summary>
internal class MetadataAssemblySymbol(
    Compilation compilation,
    MetadataReader metadataReader,
    XmlDocument? documentation) : ModuleSymbol, IMetadataSymbol
{
    public override ImmutableArray<AttributeInstance> Attributes => InterlockedUtils.InitializeDefault(ref this.attributes, this.BuildAttributes);
    private ImmutableArray<AttributeInstance> attributes;

    public override IEnumerable<Symbol> AllMembers => this.RootNamespace.AllMembers;

    /// <summary>
    /// The version of this assembly.
    /// </summary>
    public Version Version => this.assemblyDefinition.Version;

    /// <summary>
    /// The root namespace of this assembly.
    /// </summary>
    public MetadataNamespaceSymbol RootNamespace =>
        LazyInitializer.EnsureInitialized(ref this.rootNamespace, this.BuildRootNamespace);
    private MetadataNamespaceSymbol? rootNamespace;

    public override string Name => this.MetadataName;
    // NOTE: We don't emit the name of the module in fully qualified names
    public override string FullName => string.Empty;

    /// <summary>
    /// The <see cref="System.Reflection.AssemblyName"/> of this referenced assembly.
    /// </summary>
    public AssemblyName AssemblyName =>
        LazyInitializer.EnsureInitialized(ref this.assemblyName, this.assemblyDefinition.GetAssemblyName);
    private AssemblyName? assemblyName;

    public override string MetadataName => this.MetadataReader.GetString(this.assemblyDefinition.Name);
    public MetadataAssemblySymbol Assembly => this;

    public MetadataReader MetadataReader { get; } = metadataReader;

    /// <summary>
    /// XmlDocument containing documentation for this assembly.
    /// </summary>
    public XmlDocument? AssemblyDocumentation { get; } = documentation;

    public override Compilation DeclaringCompilation { get; } = compilation;

    private readonly ModuleDefinition moduleDefinition = metadataReader.GetModuleDefinition();
    private readonly AssemblyDefinition assemblyDefinition = metadataReader.GetAssemblyDefinition();

    private ImmutableArray<AttributeInstance> BuildAttributes() =>
        MetadataSymbol.DecodeAttributeList(this.assemblyDefinition.GetCustomAttributes(), this);

    private MetadataNamespaceSymbol BuildRootNamespace()
    {
        var rootNamespaceDefinition = this.MetadataReader.GetNamespaceDefinitionRoot();
        return new MetadataNamespaceSymbol(
            containingSymbol: this,
            namespaceDefinition: rootNamespaceDefinition);
    }
}
