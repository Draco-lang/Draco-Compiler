namespace Draco.Compiler.Internal.Symbols.Synthetized;

/// <summary>
/// A label generated by the compiler.
/// </summary>
internal sealed class SynthetizedLabelSymbol(string name) : LabelSymbol
{
    public override string Name { get; } = name;

    public SynthetizedLabelSymbol()
        : this(string.Empty)
    {
    }
}
