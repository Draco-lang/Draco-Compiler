namespace Draco.Compiler.Api.CodeCompletion;

/// <summary>
/// Categories for <see cref="CompletionItem"/>s that can be used to categorize the completions.
/// </summary>
public enum CompletionKind
{
    DeclarationKeyword,
    VisibilityKeyword,
    ControlFlowKeyword,

    VariableName,
    ParameterName,
    ModuleName,
    FunctionName,
    PropertyName,
    FieldName,

    ReferenceTypeName,
    ValueTypeName,

    Operator,
}
