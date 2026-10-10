using System.Text.Json;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>JSON-backed workspace store for concrete debug configurations.</summary>
public sealed class DebugConfigurationService : IDebugConfigurationService
{
    private readonly object _gate = new();
    private readonly IWorkspace _workspace;
    private readonly IOutput? _output;
    private IReadOnlyList<DebugLaunchConfiguration> _configurations = CreateDefaults();
    private string? _selectedId = "launch-cpp";

    /// <summary>Creates a configuration store attached to a workspace.</summary>
    /// <param name="workspace">Owning workspace.</param>
    /// <param name="output">Optional persistence diagnostics.</param>
    public DebugConfigurationService(IWorkspace workspace, IOutput? output = null)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _output = output;
        _workspace.FolderOpened += OnWorkspaceOpened;
        if (!string.IsNullOrWhiteSpace(_workspace.RootPath))
            Load();
    }

    /// <inheritdoc />
    public event Action? ConfigurationsChanged;

    /// <inheritdoc />
    public string? SelectedId
    {
        get { lock (_gate) return _selectedId; }
    }

    /// <inheritdoc />
    public IReadOnlyList<DebugLaunchConfiguration> GetConfigurations()
    {
        lock (_gate)
            return _configurations.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <inheritdoc />
    public void Upsert(DebugLaunchConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration.ProfileId);
        var normalized = configuration with
        {
            Id = configuration.Id.Trim(),
            Name = configuration.Name.Trim(),
            ProfileId = configuration.ProfileId.Trim().ToLowerInvariant(),
            Program = Normalize(configuration.Program),
            WorkingDirectory = Normalize(configuration.WorkingDirectory),
            Arguments = configuration.Arguments?.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray() ?? [],
        };
        lock (_gate)
        {
            var updated = _configurations.Where(item => !item.Id.Equals(normalized.Id, StringComparison.Ordinal)).ToList();
            updated.Add(normalized);
            _configurations = updated;
            _selectedId = normalized.Id;
        }
        SaveAndPublish();
    }

    /// <inheritdoc />
    public void Remove(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        lock (_gate)
        {
            _configurations = _configurations.Where(item => !item.Id.Equals(id, StringComparison.Ordinal)).ToArray();
            if (_selectedId?.Equals(id, StringComparison.Ordinal) == true)
                _selectedId = _configurations.FirstOrDefault()?.Id;
        }
        SaveAndPublish();
    }

    /// <inheritdoc />
    public void Select(string? id)
    {
        lock (_gate)
            _selectedId = id is not null && _configurations.Any(item => item.Id.Equals(id, StringComparison.Ordinal))
                ? id
                : null;
        SaveAndPublish();
    }

    private void OnWorkspaceOpened(string _) => Load();

    private void Load()
    {
        try
        {
            var path = StorePath();
            var state = File.Exists(path)
                ? JsonSerializer.Deserialize<StoredState>(File.ReadAllText(path))
                : null;
            lock (_gate)
            {
                _configurations = state?.Configurations is { Length: > 0 } ? state.Configurations : CreateDefaults();
                _selectedId = state?.SelectedId;
                if (_selectedId is null || !_configurations.Any(item => item.Id.Equals(_selectedId, StringComparison.Ordinal)))
                    _selectedId = _configurations.FirstOrDefault()?.Id;
            }
            ConfigurationsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _output?.Write("Debug", $"Cannot load debug configurations: {ex.Message}", OutputSeverity.Warning);
        }
    }

    private void SaveAndPublish()
    {
        if (!string.IsNullOrWhiteSpace(_workspace.RootPath))
        {
            try
            {
                var path = StorePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temporary = path + ".tmp";
                StoredState state;
                lock (_gate)
                    state = new StoredState(_selectedId, _configurations.ToArray());
                File.WriteAllText(temporary, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporary, path, overwrite: true);
            }
            catch (Exception ex)
            {
                _output?.Write("Debug", $"Cannot save debug configurations: {ex.Message}", OutputSeverity.Warning);
            }
        }
        ConfigurationsChanged?.Invoke();
    }

    private string StorePath() => Path.Combine(
        Path.GetFullPath(_workspace.RootPath ?? throw new InvalidOperationException("No workspace is open.")),
        ".junexis", "studio-debug.json");

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<DebugLaunchConfiguration> CreateDefaults() =>
    [
        new("launch-cpp", "Launch C++", "cpp", DebugConfigurationRequest.Launch),
        new("launch-go", "Launch Go", "go", DebugConfigurationRequest.Launch),
        new("launch-typescript", "Launch TypeScript / Node", "typescript", DebugConfigurationRequest.Launch),
    ];

    private sealed record StoredState(string? SelectedId, DebugLaunchConfiguration[] Configurations);
}
