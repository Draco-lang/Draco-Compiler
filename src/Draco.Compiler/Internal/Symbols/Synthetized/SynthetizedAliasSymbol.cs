namespace Draco.Compiler.Internal.Symbols.Synthetized;

/// <summary>
/// An alias defined by the compiler.
/// </summary>
internal sealed class SynthetizedAliasSymbol(string name, Symbol substitution) : AliasSymbol
{
    public override string MetadataName => this.Substitution.MetadataName;
    public override string Name => name;
    public override Symbol Substitution => substitution;
    public override Api.Semantics.Visibility Visibility => Api.Semantics.Visibility.Public;
}
