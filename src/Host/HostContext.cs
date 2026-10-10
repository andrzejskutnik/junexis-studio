using Junexis.Studio.Abstractions;
using System;

namespace Junexis.Studio.Host;

/// <summary>
/// Implementacja <see cref="IHostContext"/> – paczka usług przekazywana rozszerzeniom.
/// </summary>
public sealed class HostContext : IHostContext
{
    /// <summary>
    /// Tworzy kontekst hosta z wymaganymi usługami.
    /// </summary>
    /// <param name="eventBus">Event bus.</param>
    /// <param name="commands">Rejestr komend.</param>
    /// <param name="panels">Rejestr paneli.</param>
    /// <param name="output">Output/log.</param>
    /// <param name="processes">Serwis procesów.</param>
    /// <param name="diagnostics">Diagnostyka.</param>
    /// <param name="activeDocument">Serwis aktywnego dokumentu.</param>
    /// <param name="openDocuments">Repozytorium otwartych dokumentów.</param>
    /// <param name="workspace">Workspace.</param>
    /// <param name="versionControl">Opcjonalny serwis kontroli wersji.</param>
    /// <param name="views">Rejestr widoków (ViewModel → View).</param>
    /// <param name="audit">Serwis audytu.</param>
    /// <param name="capabilities">Rejestr capability.</param>
    /// <param name="preferences">Serwis preferencji.</param>
    /// <param name="keybindings">Opcjonalny rejestr skrótów klawiaturowych.</param>
    public HostContext(
        IEventBus eventBus,
        ICommandRegistry commands,
        IPanelRegistry panels,
        IOutput output,
        IProcessService processes,
        IWorkspace workspace,
        IDiagnosticService diagnostics,
        IActiveDocumentService activeDocument,
        IOpenDocumentRepository openDocuments,
        IVersionControlService? versionControl,
        IViewRegistry views,
        IAuditService? audit = null,
        ICapabilityRegistry? capabilities = null,
        IPreferencesService? preferences = null,
        IKeybindingRegistry? keybindings = null)
    {
        EventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        Commands = commands ?? throw new ArgumentNullException(nameof(commands));
        Panels = panels ?? throw new ArgumentNullException(nameof(panels));
        Output = output ?? throw new ArgumentNullException(nameof(output));
        Processes = processes ?? throw new ArgumentNullException(nameof(processes));
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        ActiveDocument = activeDocument ?? throw new ArgumentNullException(nameof(activeDocument));
        OpenDocuments = openDocuments ?? throw new ArgumentNullException(nameof(openDocuments));
        VersionControl = versionControl;
        Views = views ?? throw new ArgumentNullException(nameof(views));
        Audit = audit ?? new NullAuditService();
        Capabilities = capabilities ?? new NullCapabilityRegistry();
        Preferences = preferences ?? new NullPreferencesService();
        Keybindings = keybindings ?? new NullKeybindingRegistry();
    }

    /// <summary>
    /// Event bus do komunikacji wewnętrznej.
    /// </summary>
    public IEventBus EventBus { get; }

    /// <summary>
    /// Rejestr i wykonanie komend.
    /// </summary>
    public ICommandRegistry Commands { get; }

    /// <summary>
    /// Rejestr paneli UI.
    /// </summary>
    public IPanelRegistry Panels { get; }

    /// <summary>
    /// Output/log.
    /// </summary>
    public IOutput Output { get; }

    /// <summary>
    /// Serwis procesów.
    /// </summary>
    public IProcessService Processes { get; }

    /// <summary>
    /// Diagnostyka.
    /// </summary>
    public IDiagnosticService Diagnostics { get; }

    /// <summary>
    /// Serwis aktywnego dokumentu.
    /// </summary>
    public IActiveDocumentService ActiveDocument { get; }

    /// <summary>
    /// Repozytorium otwartych dokumentów.
    /// </summary>
    public IOpenDocumentRepository OpenDocuments { get; }

    /// <summary>
    /// Opcjonalny serwis kontroli wersji.
    /// </summary>
    public IVersionControlService? VersionControl { get; }

    /// <summary>
    /// Workspace i operacje na plikach.
    /// </summary>
    public IWorkspace Workspace { get; }

    /// <summary>
    /// Rejestr mapowania ViewModel → View.
    /// </summary>
    public IViewRegistry Views { get; }

    /// <summary>
    /// Serwis audytu.
    /// </summary>
    public IAuditService Audit { get; }

    /// <summary>
    /// Rejestr capability.
    /// </summary>
    public ICapabilityRegistry Capabilities { get; }

    /// <summary>
    /// Serwis preferencji użytkownika.
    /// </summary>
    public IPreferencesService Preferences { get; }

    /// <summary>
    /// Rejestr skrótów klawiaturowych (gest → commandId).
    /// </summary>
    public IKeybindingRegistry Keybindings { get; }

    /// <summary>
    /// Implementacja Null Object dla IAuditService — brak audytu nie wywraca systemu.
    /// </summary>
    private sealed class NullAuditService : IAuditService
    {
        event Action<AuditEntry>? IAuditService.EntryRecorded
        {
            add { }
            remove { }
        }

        public void Record(AuditEntry entry) { }
        public IReadOnlyList<AuditEntry> GetEntries() => Array.Empty<AuditEntry>();
    }

    /// <summary>
    /// Implementacja Null Object dla ICapabilityRegistry — brak rejestru nie wywraca systemu.
    /// </summary>
    private sealed class NullCapabilityRegistry : ICapabilityRegistry
    {
        public void Register<T>(T instance) where T : class { }
        public bool TryGet<T>(out T? capability) where T : class { capability = default; return false; }
        public T Get<T>() where T : class => throw new InvalidOperationException($"Capability '{typeof(T).Name}' is not registered.");
        public bool Has<T>() where T : class => false;
    }

    /// <summary>
    /// Implementacja Null Object dla IPreferencesService — zwraca domyślne preferencje.
    /// </summary>
    private sealed class NullPreferencesService : IPreferencesService
    {
        public JunexisPreferences Current { get; } = new();
        public void Reload() { }
        public void SaveGlobal() { }
        public void SaveWorkspace() { }
        public string GlobalFilePath => string.Empty;
        public string? WorkspaceFilePath => null;
    }

    /// <summary>
    /// Implementacja Null Object dla <see cref="IKeybindingRegistry"/> — brak rejestru nie wywraca systemu.
    /// </summary>
    private sealed class NullKeybindingRegistry : IKeybindingRegistry
    {
        public void Bind(KeyGesture gesture, string commandId) { }
        public void Unbind(KeyGesture gesture) { }
        public bool TryGetCommand(KeyGesture gesture, out string? commandId) { commandId = null; return false; }
        public IReadOnlyDictionary<KeyGesture, string> GetBindings() => new Dictionary<KeyGesture, string>();
    }
}
