using System.Collections.Generic;
using Draco.Compiler.Internal.Symbols;

namespace Draco.Compiler.Internal.OptimizingIr.Model;

/// <summary>
/// Interface of a module.
/// </summary>
internal interface IModule
{
    /// <summary>
    /// The symbol of this module.
    /// </summary>
    public ModuleSymbol Symbol { get; }

    /// <summary>
    /// The name of this module.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The submodules of this module.
    /// </summary>
    public IReadOnlyDictionary<ModuleSymbol, IModule> Submodules { get; }

    /// <summary>
    /// The fields within this module.
    /// </summary>
    public IReadOnlySet<FieldSymbol> Fields { get; }

    /// <summary>
    /// The properties within this module.
    /// </summary>
    public IReadOnlySet<PropertySymbol> Properties { get; }

    /// <summary>
    /// The compiled procedures within this module.
    /// </summary>
    public IReadOnlyDictionary<FunctionSymbol, IProcedure> Procedures { get; }

    /// <summary>
    /// The compiled classes within this module.
    /// </summary>
    public IReadOnlyDictionary<TypeSymbol, IClass> Classes { get; }

    /// <summary>
    /// The procedure performing global initialization.
    /// </summary>
    public IProcedure GlobalInitializer { get; }
}
