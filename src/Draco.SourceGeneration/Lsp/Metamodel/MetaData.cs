using System;
using System.Collections.Generic;
using System.Text;

namespace Draco.SourceGeneration.Lsp.Metamodel;

internal sealed class MetaData
{
    /// <summary>
    /// The protocol version.
    /// </summary>
    public string Version { get; set; } = string.Empty;
}
