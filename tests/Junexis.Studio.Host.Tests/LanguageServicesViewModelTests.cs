using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class LanguageServicesViewModelTests
{
    [Fact]
    [Trait("Category", "LanguageServices")]
    public void Host_RegistersPanelAndOpenCommand()
    {
        var host = new JunexisStudioHost();
        host.Start();
        try
        {
            var panel = Assert.Single(
                host.Context.Panels.GetRegisteredPanels(),
                item => item.Id == "junexis.language-services");
            Assert.Equal("Language Services", panel.Title);
            Assert.True(host.Context.Commands.CanExecute("language.services.open"));

            ActivatePanelRequested? activation = null;
            host.Context.EventBus.Subscribe<ActivatePanelRequested>(request => activation = request);
            host.Context.Commands.Execute("language.services.open");

            Assert.Equal("junexis.language-services", activation?.PanelId);
        }
        finally
        {
            host.Shutdown();
        }
    }

    [Fact]
    [Trait("Category", "LanguageServices")]
    public void ReferencesEvent_PopulatesPanelAndNavigatesSelectedLocation()
    {
        var service = new StubLanguageService();
        var eventBus = new EventBus();
        using var workspace = new WorkspaceService();
        var output = new OutputService();
        using var viewModel = new LanguageServicesViewModel(service, eventBus, workspace, output);
        NavigateToLocationRequested? navigation = null;
        eventBus.Subscribe<NavigateToLocationRequested>(request => navigation = request);
        var location = new LanguageLocation(
            Path.Combine(Path.GetTempPath(), "sample.jnx"),
            new LanguagePosition(6, 3));

        service.PublishReferences(new LanguageReferencesPublished(
            "junexis",
            location.Path,
            "temperature",
            [location]));

        var item = Assert.Single(viewModel.References);
        Assert.Equal(7, item.Line);
        Assert.Equal(4, item.Column);
        Assert.Contains("temperature", viewModel.ReferencesTitle);
        Assert.True(viewModel.HasReferences);

        viewModel.NavigateToSelectedReference();

        Assert.NotNull(navigation);
        Assert.Equal(location.Path, navigation.Path);
        Assert.Equal(7, navigation.Line);
        Assert.Equal(4, navigation.Column);
    }

    [Fact]
    [Trait("Category", "LanguageServices")]
    public async Task RestartSelectedAsync_RestartsProfileAndRefreshesStatus()
    {
        var service = new StubLanguageService();
        var eventBus = new EventBus();
        using var workspace = new WorkspaceService();
        using var viewModel = new LanguageServicesViewModel(
            service,
            eventBus,
            workspace,
            new OutputService());
        viewModel.SelectedStatus = viewModel.Statuses.Single(status => status.ProfileId == "go");

        await viewModel.RestartSelectedAsync();

        Assert.Equal("go", service.RestartedProfileId);
        Assert.Contains("restarted", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "LanguageServices")]
    public void Dispose_UnsubscribesFromServiceEvents()
    {
        var service = new StubLanguageService();
        var viewModel = new LanguageServicesViewModel(
            service,
            new EventBus(),
            new WorkspaceService(),
            new OutputService());
        viewModel.Dispose();

        service.PublishReferences(new LanguageReferencesPublished(
            "junexis",
            "sample.jnx",
            "task",
            [new LanguageLocation("sample.jnx", new LanguagePosition(0, 0))]));

        Assert.Empty(viewModel.References);
    }

    private sealed class StubLanguageService : ILanguageService
    {
        private IReadOnlyList<LanguageServerStatus> _statuses =
        [
            new("junexis", "Junexis Language Server", LanguageServerState.Running, "Ready."),
            new("go", "Go Language Server", LanguageServerState.Stopped, "Not started."),
        ];

        public event Action<LanguageServerStatus>? StatusChanged;
        public event Action<LanguageDiagnosticsPublished>? DiagnosticsPublished
        {
            add { }
            remove { }
        }
        public event Action<LanguageReferencesPublished>? ReferencesPublished;

        public string? RestartedProfileId { get; private set; }

        public IReadOnlyList<LanguageServerStatus> GetStatuses() => _statuses;

        public bool Supports(string path) => true;

        public Task<LanguageHover?> HoverAsync(
            string path,
            LanguagePosition position,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<LanguageHover?>(null);

        public Task<IReadOnlyList<LanguageCompletionItem>> CompletionAsync(
            string path,
            LanguagePosition position,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LanguageCompletionItem>>([]);

        public Task<LanguageLocation?> DefinitionAsync(
            string path,
            LanguagePosition position,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<LanguageLocation?>(null);

        public Task<IReadOnlyList<LanguageLocation>> ReferencesAsync(
            string path,
            LanguagePosition position,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LanguageLocation>>([]);

        public Task<IReadOnlyList<LanguageSemanticToken>> SemanticTokensAsync(
            string path,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LanguageSemanticToken>>([]);

        public Task<int> RenameAsync(
            string path,
            LanguagePosition position,
            string newName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<IReadOnlyList<LanguageCodeAction>> CodeActionsAsync(
            string path,
            LanguagePosition position,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LanguageCodeAction>>([]);

        public Task<int> ApplyCodeActionAsync(
            LanguageCodeAction action,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task RestartAsync(string path, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RestartProfileAsync(string profileId, CancellationToken cancellationToken = default)
        {
            RestartedProfileId = profileId;
            _statuses = _statuses.Select(status => status.ProfileId == profileId
                    ? status with { State = LanguageServerState.Running, Message = "Ready.", RestartCount = 1 }
                    : status)
                .ToArray();
            StatusChanged?.Invoke(_statuses.Single(status => status.ProfileId == profileId));
            return Task.CompletedTask;
        }

        public void PublishReferences(LanguageReferencesPublished result) =>
            ReferencesPublished?.Invoke(result);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
