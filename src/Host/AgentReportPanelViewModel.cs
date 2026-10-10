using Junexis.Studio.Abstractions;
using Junexis.Mcp.Studio;
using System.ComponentModel;
using System.Text.Json;

namespace Junexis.Studio.Host;

/// <summary>Current status of a file reported by the latest Agent session.</summary>
public enum AgentFileStatus
{
    /// <summary>The file exists after the Agent change.</summary>
    Changed,

    /// <summary>The file content hash is unchanged from the Agent baseline.</summary>
    Unchanged,

    /// <summary>The Agent reported the file, but it no longer exists.</summary>
    Missing,
}

/// <summary>Read-only file entry shown by the Agent Report panel.</summary>
public sealed record AgentFileItem(string Path, AgentFileStatus Status, string? Diff);

/// <summary>
/// Read-only summary of the latest Agent session published by the Host.
/// </summary>
public sealed class AgentReportPanelViewModel : IDisposable, INotifyPropertyChanged
{
    private readonly IOutput _output;
    private readonly StudioMcpBridge _bridge;
    private readonly IWorkspace _workspace;

    /// <summary>Gets the latest session status.</summary>
    public string Status { get; private set; } = "No session";

    /// <summary>Gets the latest session goal.</summary>
    public string Goal { get; private set; } = string.Empty;

    /// <summary>Gets the number of changed files in the latest session.</summary>
    public int ChangedFiles { get; private set; }

    /// <summary>Gets repository-relative files changed by the latest session.</summary>
    public IReadOnlyList<AgentFileItem> ChangedFileItems { get; private set; } = [];

    /// <summary>Raised when the displayed report changes.</summary>
    public event Action? ReportChanged;

    /// <summary>Raised when a displayed value changes.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Initializes the panel and subscribes to Agent Output entries.</summary>
    public AgentReportPanelViewModel(IOutput output, StudioMcpBridge bridge, IWorkspace workspace)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _output.EntryWritten += OnEntryWritten;
        var latest = _output.GetSnapshot().LastOrDefault(entry => entry.Channel == "Agent");
        if (latest is not null)
        {
            ApplyMessage(latest.Message);
        }

        LoadLatestChangedFiles();
    }

    /// <summary>Activates a changed file in the current Studio workspace.</summary>
    public bool OpenChangedFile(string relativePath) => _bridge.OpenLocation(relativePath).Succeeded;

    /// <inheritdoc />
    public void Dispose() => _output.EntryWritten -= OnEntryWritten;

    private void OnEntryWritten(OutputEntry entry)
    {
        if (entry.Channel != "Agent")
        {
            return;
        }

        ApplyMessage(entry.Message);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Goal)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ChangedFiles)));
        LoadLatestChangedFiles();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ChangedFileItems)));
        ReportChanged?.Invoke();
    }

    private void ApplyMessage(string message)
    {
        const string prefix = "Latest session: ";
        if (!message.StartsWith(prefix, StringComparison.Ordinal))
        {
            return;
        }

        var payload = message[prefix.Length..];
        var changedMarker = "; changed files: ";
        var changedIndex = payload.LastIndexOf(changedMarker, StringComparison.Ordinal);
        if (changedIndex < 0)
        {
            Status = payload;
            return;
        }

        var details = payload[..changedIndex];
        if (int.TryParse(payload[(changedIndex + changedMarker.Length)..], out var count))
        {
            ChangedFiles = count;
        }

        var goalSeparator = details.IndexOf(" — ", StringComparison.Ordinal);
        if (goalSeparator < 0)
        {
            Status = details;
            Goal = string.Empty;
            return;
        }

        Status = details[..goalSeparator];
        Goal = details[(goalSeparator + 3)..];
    }

    private void LoadLatestChangedFiles()
    {
        try
        {
            using var document = JsonDocument.Parse(_bridge.ReadLatestSession());
            if (document.RootElement.TryGetProperty("fileStates", out var states)
                && states.ValueKind == JsonValueKind.Array)
            {
                ChangedFileItems = states.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.Object)
                    .Select(item =>
                    {
                        var path = item.GetProperty("path").GetString()!;
                        var existsAfter = item.GetProperty("existsAfter").GetBoolean();
                        var before = item.GetProperty("beforeSha256").GetString();
                        var after = item.GetProperty("afterSha256").GetString();
                        var diff = item.TryGetProperty("diff", out var diffValue)
                            ? diffValue.GetString()
                            : null;
                        var status = !existsAfter
                            ? AgentFileStatus.Missing
                            : string.Equals(before, after, StringComparison.OrdinalIgnoreCase)
                                ? AgentFileStatus.Unchanged
                                : AgentFileStatus.Changed;
                        return new AgentFileItem(path, status, diff);
                    })
                    .ToArray();
            }
            else if (document.RootElement.TryGetProperty("changedFiles", out var files)
                     && files.ValueKind == JsonValueKind.Array)
            {
                ChangedFileItems = files.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString()!)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => new AgentFileItem(
                        path,
                        File.Exists(Path.Combine(_workspace.RootPath ?? string.Empty, path))
                            ? AgentFileStatus.Changed
                            : AgentFileStatus.Missing,
                        null))
                    .ToArray();
            }
        }
        catch (FileNotFoundException)
        {
            ChangedFileItems = [];
        }
        catch (ArgumentException)
        {
            ChangedFileItems = [];
        }
        catch (JsonException)
        {
            ChangedFileItems = [];
        }
    }

}