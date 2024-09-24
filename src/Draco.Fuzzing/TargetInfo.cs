using System.Diagnostics;
using Draco.Coverage;

namespace Draco.Fuzzing;

/// <summary>
/// Information about a target to be executed.
/// </summary>
public readonly struct TargetInfo
{
    public static TargetInfo InProcess(InstrumentedAssembly assembly, object? user = null) => new()
    {
        Assembly = assembly,
        User = user,
    };
    public static TargetInfo OutOfProcess(
        InstrumentedAssembly assembly,
        Process process,
        SharedMemory<int> sharedMemory,
        object? user = null) => new()
        {
            Assembly = assembly,
            Process = process,
            SharedMemory = sharedMemory,
            User = user,
        };

    /// <summary>
    /// The assembly that is being observed. Even for out-of-process execution, the assembly is loaded into the host process
    /// for information gathering.
    /// </summary>
    public InstrumentedAssembly Assembly { get; init; }

    /// <summary>
    /// The process to be executed, in case of out-of-process execution.
    /// </summary>
    public Process? Process { get; init; }

    /// <summary>
    /// The shared memory for coverage data.
    /// </summary>
    public SharedMemory<int>? SharedMemory { get; init; }

    /// <summary>
    /// Arbitrary user data.
    /// </summary>
    public object? User { get; init; }

    /// <summary>
    /// The coverage result of the target.
    /// </summary>
    public CoverageResult CoverageResult => this.SharedMemory is null
        ? this.Assembly.CoverageResult
        : CoverageResult.FromSharedMemory(this.SharedMemory);

    /// <summary>
    /// Clears the coverage data.
    /// </summary>
    public void ClearCoverageData()
    {
        if (this.SharedMemory is not null)
        {
            this.SharedMemory.Span.Clear();
        }
        else
        {
            this.Assembly.ClearCoverageData();
        }
    }
}
