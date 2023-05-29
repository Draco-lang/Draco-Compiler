using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;
using ClrDebug;

namespace Draco.Debugger;

/// <summary>
/// Represents a method.
/// </summary>
public sealed class Method
{
    /// <summary>
    /// The cache for this object.
    /// </summary>
    internal SessionCache SessionCache { get; }

    /// <summary>
    /// The internal handle.
    /// </summary>
    internal CorDebugFunction CorDebugFunction { get; }

    /// <summary>
    /// The method definition handle of this method.
    /// </summary>
    internal MethodDefinitionHandle MethodDefinitionHandle => MetadataTokens.MethodDefinitionHandle(this.CorDebugFunction.Token);

    /// <summary>
    /// The debug info of this method.
    /// </summary>
    internal MethodDebugInformation DebugInfo => this.debugInfo ??= this.BuildDebugInfo();
    private MethodDebugInformation? debugInfo;

    /// <summary>
    /// The module this function lies in.
    /// </summary>
    public Module Module => this.SessionCache.GetModule(this.CorDebugFunction.Module);

    /// <summary>
    /// The name of the method.
    /// </summary>
    public string Name => this.name ??= this.BuildName();
    private string? name;

    /// <summary>
    /// The source file this method lies in.
    /// </summary>
    public SourceFile? SourceFile => this.sourceFile ??= this.BuildSourceFile();
    private SourceFile? sourceFile;

    /// <summary>
    /// The sequence points within this method.
    /// </summary>
    public ImmutableArray<SequencePoint> SequencePoints => this.sequencePoints ??= this.BuildSequencePoints();
    private ImmutableArray<SequencePoint>? sequencePoints;

    internal Method(
        SessionCache sessionCache,
        CorDebugFunction corDebugFunction)
    {
        this.SessionCache = sessionCache;
        this.CorDebugFunction = corDebugFunction;
    }

    private MethodDebugInformation BuildDebugInfo() => this.Module.PdbReader
        .GetMethodDebugInformation(this.MethodDefinitionHandle);

    private string BuildName()
    {
        var import = this.CorDebugFunction.Module.GetMetaDataInterface().MetaDataImport;
        var methodProps = import.GetMethodProps(this.CorDebugFunction.Token);
        return methodProps.szMethod;
    }

    private SourceFile? BuildSourceFile()
    {
        var module = this.Module;
        var docHandle = this.DebugInfo.Document;
        return module.SourceFiles.FirstOrDefault(s => s.DocumentHandle == docHandle);
    }

    private ImmutableArray<SequencePoint> BuildSequencePoints() => this.DebugInfo
        .GetSequencePoints()
        .ToImmutableArray();
}