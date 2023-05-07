using System;
using System.Collections.Generic;

namespace Draco.SourceGeneration.Lsp.Metamodel;

/// <summary>
/// Includes AndType, OrType, TupleType.
/// </summary>
internal sealed record AggregateType : Type
{
    public override required string Kind { get; set; }

    public required EquatableArray<Type> Items { get; set; }
}
