using System.ComponentModel;
using System.Runtime.CompilerServices;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>View model panelu artefaktów ostatniego builda Junexis.</summary>
public sealed class ArtifactExplorerViewModel : INotifyPropertyChanged
{
    private readonly IArtifactService _artifacts;
    private readonly IOutput _output;
    private readonly SynchronizationContext? _synchronizationContext;
    private IReadOnlyList<ArtifactItem> _items = [];
    private string _statusText = "Build a Junexis project to load artifacts.";
    private string? _buildId;

    /// <summary>Tworzy view model panelu artefaktów.</summary>
    /// <param name="artifacts">Czytnik inventory.</param>
    /// <param name="output">Log Studio.</param>
    public ArtifactExplorerViewModel(IArtifactService artifacts, IOutput output)
    {
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _synchronizationContext = SynchronizationContext.Current;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Artefakty pogrupowane przez target w widoku.</summary>
    public IReadOnlyList<ArtifactItem> Items
    {
        get => _items;
        private set => SetField(ref _items, value);
    }

    /// <summary>Status ostatniego odczytu inventory.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    /// <summary>Identyfikator aktualnie pokazanego builda.</summary>
    public string? BuildId
    {
        get => _buildId;
        private set => SetField(ref _buildId, value);
    }

    /// <summary>Wczytuje inventory opublikowane przez kompilator.</summary>
    /// <param name="manifestPath">Ścieżka manifestu.</param>
    /// <param name="cancellationToken">Token anulowania.</param>
    public async Task LoadAsync(string manifestPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var snapshot = await _artifacts.LoadAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            Update(() =>
            {
                Items = snapshot.Items
                    .OrderBy(item => item.Target, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                BuildId = snapshot.BuildId;
                StatusText = $"{Items.Count} artifact(s) from build {snapshot.BuildId[..Math.Min(12, snapshot.BuildId.Length)]}.";
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Update(() => StatusText = "Artifact loading cancelled.");
        }
        catch (Exception ex)
        {
            Update(() =>
            {
                Items = [];
                StatusText = $"Cannot load artifacts: {ex.Message}";
            });
            _output.Write("Artifacts", StatusText, OutputSeverity.Error);
        }
    }

    private void Update(Action action)
    {
        if (_synchronizationContext is null || SynchronizationContext.Current == _synchronizationContext)
            action();
        else
            _synchronizationContext.Post(_ => action(), null);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

internal sealed class ArtifactExplorerExtension : IJunexisExtension
{
    private IEventBus? _eventBus;
    private ArtifactExplorerViewModel? _viewModel;

    public string Id => "junexis.artifacts.explorer";

    public void Initialize(IHostContext context)
    {
        if (!context.Capabilities.TryGet<IArtifactService>(out var artifacts) || artifacts is null)
        {
            context.Output.Write("Artifacts", "IArtifactService unavailable — Artifact Explorer inactive.");
            return;
        }

        _eventBus = context.EventBus;
        _viewModel = new ArtifactExplorerViewModel(artifacts, context.Output);
        _eventBus.Subscribe<ArtifactsUpdatedEvent>(OnArtifactsUpdated);
        context.Panels.RegisterPanel(
            Id,
            PanelType.Tool,
            () => _viewModel,
            title: "Artifacts",
            dockGroup: DockGroup.Left);
    }

    public void Shutdown()
    {
        _eventBus?.Unsubscribe<ArtifactsUpdatedEvent>(OnArtifactsUpdated);
        _eventBus = null;
        _viewModel = null;
    }

    private void OnArtifactsUpdated(ArtifactsUpdatedEvent update)
    {
        if (_viewModel is not null)
            _ = _viewModel.LoadAsync(update.ArtifactManifestPath);
    }
}
