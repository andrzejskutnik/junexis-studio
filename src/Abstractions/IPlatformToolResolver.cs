namespace Junexis.Studio.Abstractions;

/// <summary>Rozpoznaje publiczne polecenia platformy z konfiguracji, profilu użytkownika lub PATH.</summary>
public interface IPlatformToolResolver
{
    /// <summary>Wskazuje polecenie używane do uruchomienia składnika platformy.</summary>
    /// <param name="commandName">Publiczna nazwa polecenia bez ścieżki.</param>
    /// <param name="overrideEnvironmentVariable">Opcjonalna zmienna środowiskowa z jawną ścieżką.</param>
    /// <returns>Jawna ścieżka albo nazwa polecenia rozwiązywana przez PATH.</returns>
    string ResolveCommand(string commandName, string? overrideEnvironmentVariable = null);
}
