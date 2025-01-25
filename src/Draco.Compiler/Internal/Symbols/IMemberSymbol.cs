namespace Draco.Compiler.Internal.Symbols;

/// <summary>
/// Represents any member symbol.
/// </summary>
internal interface IMemberSymbol
{
    /// <summary>
    /// Specifying if the given symbol is static.
    /// </summary>
    public bool IsStatic { get; }

    /// <summary>
    /// True if this member is an explicit implementation of an interface member.
    /// </summary>
    public bool IsExplicitImplementation { get; }
}
