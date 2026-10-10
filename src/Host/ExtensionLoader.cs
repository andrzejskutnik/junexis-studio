using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>
/// Minimalny loader rozszerzeń (v0.1: statyczna lista instancji).
/// Błędy ładowania/zamykania extensions raportuje jako <see cref="SystemDiagnostic"/>.
/// </summary>
public sealed class ExtensionLoader
{
    private readonly List<IJunexisExtension> _extensions = new();

    /// <summary>
    /// Aktualna lista zarejestrowanych rozszerzeń.
    /// </summary>
    public IReadOnlyList<IJunexisExtension> Extensions => _extensions;

    /// <summary>
    /// Dodaje instancję rozszerzenia do listy ładowania.
    /// </summary>
    /// <param name="extension">Instancja rozszerzenia.</param>
    /// <exception cref="ArgumentNullException">Gdy <paramref name="extension"/> jest <see langword="null"/>.</exception>
    public void Add(IJunexisExtension extension)
    {
        _extensions.Add(extension ?? throw new ArgumentNullException(nameof(extension)));
    }

    /// <summary>
    /// Inicjalizuje wszystkie rozszerzenia.
    /// Błędy pojedynczego rozszerzenia nie przerywają całego procesu.
    /// </summary>
    /// <param name="context">Kontekst hosta.</param>
    public void InitializeAll(IHostContext context)
    {
        foreach (var ext in _extensions)
        {
            try
            {
                ext.Initialize(context);
            }
            catch (Exception ex)
            {
                var diag = new SystemDiagnostic(
                    SystemDiagnosticCategory.ExtensionLoad,
                    $"Extension '{ext.Id}' failed to initialize: {ex.Message}",
                    DateTime.UtcNow,
                    Source: ext.Id,
                    ExceptionText: ex.ToString());

                ReportDiagnostic(context, diag);
            }
        }
    }

    /// <summary>
    /// Zamyka wszystkie rozszerzenia w odwrotnej kolejności niż dodanie.
    /// Błędy pojedynczego rozszerzenia nie przerywają całego procesu.
    /// </summary>
    /// <param name="context">Kontekst hosta.</param>
    public void ShutdownAll(IHostContext context)
    {
        for (var i = _extensions.Count - 1; i >= 0; i--)
        {
            var ext = _extensions[i];
            try
            {
                ext.Shutdown();
            }
            catch (Exception ex)
            {
                var diag = new SystemDiagnostic(
                    SystemDiagnosticCategory.ExtensionException,
                    $"Extension '{ext.Id}' failed to shutdown: {ex.Message}",
                    DateTime.UtcNow,
                    Source: ext.Id,
                    ExceptionText: ex.ToString());

                ReportDiagnostic(context, diag);
            }
        }
    }

    /// <summary>
    /// Raportuje diagnostykę systemową: Output (kanał "Extensions") + EventBus + DiagnosticService.
    /// </summary>
    private static void ReportDiagnostic(IHostContext context, SystemDiagnostic diag)
    {
        context.Output.Write("Extensions", diag.Message, OutputSeverity.Error, diag.Source, diag.ExceptionText);
        context.EventBus.Publish(diag);
        context.Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, diag.Message));
    }
}
