using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>
/// In-memory implementacja <see cref="IAuditService"/>.
/// Rejestruje wpisy i opcjonalnie wypisuje je do <see cref="IOutput"/> (kanał "Audit").
/// </summary>
public sealed class AuditService : IAuditService
{
    private readonly object _gate = new();
    private readonly List<AuditEntry> _entries = new();
    private readonly IOutput? _output;

    /// <summary>
    /// Zdarzenie emitowane po nowym wpisie audytu.
    /// </summary>
    public event Action<AuditEntry>? EntryRecorded;

    /// <summary>
    /// Tworzy serwis audytu bez powiązania z Output.
    /// </summary>
    public AuditService() : this(null) { }

    /// <summary>
    /// Tworzy serwis audytu z opcjonalnym <see cref="IOutput"/> do którego wpisy trafiają na kanał "Audit".
    /// </summary>
    /// <param name="output">Opcjonalny serwis Output.</param>
    public AuditService(IOutput? output)
    {
        _output = output;
        _output?.RegisterChannel("Audit");
    }

    /// <inheritdoc />
    public void Record(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_gate)
        {
            _entries.Add(entry);
        }

        _output?.Write("Audit", $"[{entry.Action}] {entry.Detail}", OutputSeverity.Info, entry.Source);

        EntryRecorded?.Invoke(entry);
    }

    /// <inheritdoc />
    public IReadOnlyList<AuditEntry> GetEntries()
    {
        lock (_gate)
        {
            return _entries.ToArray();
        }
    }

    /// <summary>
    /// Maskuje wartość wrażliwą — zostawia pierwsze 4 znaki i zastępuje resztę gwiazdkami.
    /// Gdy wartość jest krótsza niż 5 znaków, maskuje całość.
    /// </summary>
    /// <param name="value">Wartość do zamaskowania.</param>
    /// <returns>Zamaskowana wartość.</returns>
    public static string MaskSensitive(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "****";
        }

        if (value.Length <= 4)
        {
            return new string('*', value.Length);
        }

        return string.Concat(value.AsSpan(0, 4), new string('*', value.Length - 4));
    }
}
