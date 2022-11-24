using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Draco.Compiler.Api.Diagnostics;

namespace Draco.Compiler.Internal.Semantics;

/// <summary>
/// Holds constants for semantic.
/// </summary>
internal static class SemanticErrors
{
    /// <summary>
    /// An undefined reference.
    /// </summary>
    public static readonly DiagnosticTemplate UndefinedReference = DiagnosticTemplate.Create(
        title: "undefined reference",
        severity: DiagnosticSeverity.Error,
        format: "undefined reference to {0}");

    /// <summary>
    /// The type of something could not be inferred.
    /// </summary>
    public static readonly DiagnosticTemplate CouldNotInferType = DiagnosticTemplate.Create(
        title: "could not infer type",
        severity: DiagnosticSeverity.Error,
        format: "could not infer type of {0}");
}
