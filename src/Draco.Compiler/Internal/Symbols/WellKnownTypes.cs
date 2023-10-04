using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Draco.Compiler.Api;
using Draco.Compiler.Internal.Symbols.Generic;
using Draco.Compiler.Internal.Symbols.Metadata;
using Draco.Compiler.Internal.Symbols.Synthetized;
using Draco.Compiler.Internal.Utilities;

namespace Draco.Compiler.Internal.Symbols;

/// <summary>
/// A collection of well-known types that the compiler needs.
/// </summary>
internal sealed partial class WellKnownTypes
{
    /// <summary>
    /// object.ToString().
    /// </summary>
    public MetadataMethodSymbol SystemObject_ToString => InterlockedUtils.InitializeNull(
        ref this.object_ToString,
        () => this.SystemObject
            .Members
            .OfType<MetadataMethodSymbol>()
            .Single(m => m.Name == "ToString"));
    private MetadataMethodSymbol? object_ToString;

    /// <summary>
    /// object.Equals(object? o1, object? o2).
    /// </summary>
    public MetadataMethodSymbol SystemObject_Equals => InterlockedUtils.InitializeNull(
        ref this.object_Equals,
        () => this.SystemObject
            .Members
            .OfType<MetadataMethodSymbol>()
            .Single(m => m.Name == "Equals" && m.Parameters.Length == 2));
    private MetadataMethodSymbol? object_Equals;

    /// <summary>
    /// string.Format(string formatString, object[] args).
    /// </summary>
    public MetadataMethodSymbol SystemString_Format => InterlockedUtils.InitializeNull(
        ref this.systemString_Format,
        () => this.SystemString
            .Members
            .OfType<MetadataMethodSymbol>()
            .First(m =>
                m.Name == "Format"
             && m.Parameters is [_, { Type: TypeInstanceSymbol { GenericDefinition: ArrayTypeSymbol } }]));
    private MetadataMethodSymbol? systemString_Format;

    private readonly Compilation compilation;

    public WellKnownTypes(Compilation compilation)
    {
        this.compilation = compilation;
    }

    public MetadataTypeSymbol GetTypeFromAssembly(AssemblyName name, ImmutableArray<string> path)
    {
        var assembly = this.GetAssemblyWithAssemblyName(name);
        return this.GetTypeFromAssembly(assembly, path);
    }

    public MetadataTypeSymbol GetTypeFromAssembly(MetadataAssemblySymbol assembly, ImmutableArray<string> path) =>
        assembly.Lookup(path).OfType<MetadataTypeSymbol>().Single();

    private MetadataAssemblySymbol GetAssemblyWithAssemblyName(AssemblyName name) =>
        this.compilation.MetadataAssemblies.Values.Single(asm => AssemblyNameComparer.Full.Equals(asm.AssemblyName, name));

    private MetadataAssemblySymbol GetAssemblyWithNameAndToken(string name, byte[] token)
    {
        var assemblyName = new AssemblyName() { Name = name };
        assemblyName.SetPublicKeyToken(token);
        return this.compilation.MetadataAssemblies.Values
            .Single(asm => AssemblyNameComparer.NameAndToken.Equals(asm.AssemblyName, assemblyName));
    }
}
