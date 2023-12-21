using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Draco.Compiler.Internal.Symbols;

namespace Draco.Compiler.Internal.OptimizingIr.Model;

/// <summary>
/// Read-only interface of a class.
/// </summary>
internal interface IClass
{
    /// <summary>
    /// The symbol that corresponds to this class.
    /// </summary>
    public TypeSymbol Symbol { get; }

    /// <summary>
    /// The name of this class.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The parent class this class is defined in, if any.
    /// </summary>
    public IClass? DeclaringClass { get; }

    /// <summary>
    /// The module this class is defined in.
    /// </summary>
    public IModule DeclaringModule { get; }

    /// <summary>
    /// The assembly this class is defined in.
    /// </summary>
    public IAssembly Assembly { get; }

    /// <summary>
    /// The generic parameters on this class.
    /// </summary>
    public IReadOnlyList<TypeParameterSymbol> Generics { get; }

    // TODO: Base class
    // TODO: Interfaces? (we might wanna keep them external)
    // TODO: Nested classes

    /// <summary>
    /// The fields defined on this class.
    /// </summary>
    public IReadOnlyList<FieldSymbol> Fields { get; }

    /// <summary>
    /// The properties defined on this class.
    /// </summary>
    public IReadOnlyList<PropertySymbol> Properties { get; }

    /// <summary>
    /// The procedures defined on this class.
    /// </summary>
    public IReadOnlyDictionary<FunctionSymbol, IProcedure> Procedures { get; }
}
