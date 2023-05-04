using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Metadata;
using Draco.Compiler.Api;

namespace Draco.Compiler.Internal.Symbols.Metadata;

/// <summary>
/// An assembly imported from metadata.
/// </summary>
internal class MetadataAssemblySymbol : ModuleSymbol, IMetadataSymbol
{
    public override IEnumerable<Symbol> Members => this.RootNamespace.Members;

    /// <summary>
    /// The root namespace of this assembly.
    /// </summary>
    public MetadataNamespaceSymbol RootNamespace => this.rootNamespace ??= this.BuildRootNamespace();
    private MetadataNamespaceSymbol? rootNamespace;

    public override string Name => this.MetadataName;
    // NOTE: We don't emit the name of the module in fully qualified names
    public override string FullName => string.Empty;
    public override Symbol ContainingSymbol { get; }

    /// <summary>
    /// The <see cref="System.Reflection.AssemblyName"/> of this referenced assembly.
    /// </summary>
    public AssemblyName AssemblyName => this.assemblyName ??= this.assemblyDefinition.GetAssemblyName();
    private AssemblyName? assemblyName;

    public string MetadataName => this.MetadataReader.GetString(this.assemblyDefinition.Name);
    public MetadataAssemblySymbol Assembly => this;

    public MetadataReader MetadataReader { get; }

    /// <summary>
    /// The compilation this assembly belongs to.
    /// </summary>
    public Compilation Compilation { get; }

    private readonly ModuleDefinition moduleDefinition;
    private readonly AssemblyDefinition assemblyDefinition;

    public MetadataAssemblySymbol(
        Symbol containingSymbol,
        Compilation compilation,
        MetadataReader metadataReader)
    {
        this.ContainingSymbol = containingSymbol;
        this.Compilation = compilation;
        this.MetadataReader = metadataReader;
        this.moduleDefinition = metadataReader.GetModuleDefinition();
        this.assemblyDefinition = metadataReader.GetAssemblyDefinition();
    }

    private MetadataNamespaceSymbol BuildRootNamespace()
    {
        var rootNamespaceDefinition = this.MetadataReader.GetNamespaceDefinitionRoot();
        return new MetadataNamespaceSymbol(
            containingSymbol: this,
            namespaceDefinition: rootNamespaceDefinition);
    }
}
