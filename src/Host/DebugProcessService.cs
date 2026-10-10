using System.Diagnostics;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>Enumerates operating-system processes for debugger attach configurations.</summary>
public sealed class DebugProcessService : IDebugProcessService
{
    private readonly Func<IEnumerable<DebugProcessInfo>> _provider;

    /// <summary>Creates a process service backed by <see cref="Process.GetProcesses()"/>.</summary>
    public DebugProcessService()
        : this(ReadProcesses)
    {
    }

    internal DebugProcessService(Func<IEnumerable<DebugProcessInfo>> provider) =>
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));

    /// <inheritdoc />
    public IReadOnlyList<DebugProcessInfo> GetProcesses() => _provider()
        .Where(item => item.Id > 0)
        .DistinctBy(item => item.Id)
        .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
        .ThenBy(item => item.Id)
        .ToArray();

    private static IEnumerable<DebugProcessInfo> ReadProcesses()
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                string? path = null;
                try { path = process.MainModule?.FileName; }
                catch (Exception) { }
                string name;
                try { name = process.ProcessName; }
                catch (Exception) { continue; }
                yield return new DebugProcessInfo(process.Id, name, path);
            }
        }
    }
}
