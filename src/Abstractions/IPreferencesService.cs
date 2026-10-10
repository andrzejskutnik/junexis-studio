namespace Junexis.Studio.Abstractions;

/// <summary>
/// Serwis preferencji użytkownika – ładuje i udostępnia konfigurację IDE z pliku YAML.
/// Obsługuje dwa poziomy: globalny (~/.junexis-studio/preferences.yml) i workspace (.junexis-studio/preferences.yml).
/// Workspace nadpisuje wartości globalne (merge).
/// </summary>
public interface IPreferencesService
{
    /// <summary>
    /// Bieżące, zmergowane preferencje (globalny + workspace override).
    /// Nigdy nie zwraca null — zawsze zawiera domyślne wartości.
    /// </summary>
    JunexisPreferences Current { get; }

    /// <summary>
    /// Przeładowuje preferencje z dysku (globalny + workspace).
    /// Wywoływane np. przy zmiencie workspace.
    /// </summary>
    void Reload();

    /// <summary>
    /// Zapisuje bieżące preferencje do pliku globalnego.
    /// </summary>
    void SaveGlobal();

    /// <summary>
    /// Zapisuje bieżące preferencje do pliku aktywnego workspace.
    /// </summary>
    void SaveWorkspace();

    /// <summary>
    /// Ścieżka do globalnego pliku preferencji (np. ~/.junexis-studio/preferences.yml).
    /// </summary>
    string GlobalFilePath { get; }

    /// <summary>
    /// Ścieżka do pliku preferencji workspace (np. {workspace}/.junexis-studio/preferences.yml).
    /// Null, gdy workspace nie jest otwarty.
    /// </summary>
    string? WorkspaceFilePath { get; }
}
