namespace Junexis.Studio.Abstractions;

/// <summary>
/// Zestaw usług udostępnianych rozszerzeniom przez host.
/// </summary>
public interface IHostContext
{
    /// <summary>
    /// Event bus do komunikacji między modułami i rozszerzeniami.
    /// </summary>
    IEventBus EventBus { get; }

    /// <summary>
    /// Rejestr komend dostępnych w aplikacji.
    /// </summary>
    ICommandRegistry Commands { get; }

    /// <summary>
    /// Rejestr paneli UI (narzędziowych i dokumentów).
    /// </summary>
    IPanelRegistry Panels { get; }

    /// <summary>
    /// Serwis output/logów.
    /// </summary>
    IOutput Output { get; }

    /// <summary>
    /// Serwis do uruchamiania procesów zewnętrznych (async + streaming output).
    /// </summary>
    IProcessService Processes { get; }

    /// <summary>
    /// Serwis diagnostyki (błędy/ostrzeżenia) niezależny od output.
    /// </summary>
    IDiagnosticService Diagnostics { get; }

    /// <summary>
    /// Serwis aktywnego dokumentu (np. bieżący plik w edytorze).
    /// </summary>
    IActiveDocumentService ActiveDocument { get; }

    /// <summary>
    /// Repozytorium aktualnie otwartych dokumentów.
    /// </summary>
    IOpenDocumentRepository OpenDocuments { get; }

    /// <summary>
    /// Opcjonalny serwis kontroli wersji (np. Git).
    /// </summary>
    IVersionControlService? VersionControl { get; }

    /// <summary>
    /// Serwis workspace (folder roboczy + operacje na plikach).
    /// </summary>
    IWorkspace Workspace { get; }

    /// <summary>
    /// Rejestr mapowania ViewModel → View używany przez Shell.
    /// </summary>
    IViewRegistry Views { get; }

    /// <summary>
    /// Serwis audytu — rejestruje operacje wrażliwe/administracyjne.
    /// </summary>
    IAuditService Audit { get; }

    /// <summary>
    /// Rejestr capability — lekki lookup usług bez twardych zależności.
    /// </summary>
    ICapabilityRegistry Capabilities { get; }

    /// <summary>
    /// Serwis preferencji użytkownika (preferences.yml).
    /// </summary>
    IPreferencesService Preferences { get; }

    /// <summary>
    /// Rejestr skrótów klawiaturowych (gest → commandId).
    /// </summary>
    IKeybindingRegistry Keybindings { get; }
}
