using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>
/// Minimalny serwis Output: przechowuje wpisy i publikuje zdarzenie przy zapisie.
/// </summary>
public sealed class OutputService : IOutput
{
    private readonly List<OutputEntry> _entries = new();
    private readonly HashSet<string> _channels = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly int _maxEntries;

    private static string NormalizeChannel(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            return "General";
        }

        channel = channel.Trim();
        return string.IsNullOrWhiteSpace(channel) ? "General" : channel;
    }

    /// <summary>
    /// Zdarzenie wywoływane po zapisaniu wpisu.
    /// </summary>
    public event Action<OutputEntry>? EntryWritten;

    /// <summary>
    /// Tworzy serwis Output z limitem historii (ring buffer).
    /// </summary>
    /// <param name="maxEntries">Maksymalna liczba wpisów przechowywanych w pamięci.</param>
    /// <exception cref="ArgumentOutOfRangeException">Gdy <paramref name="maxEntries"/> jest mniejsze niż 1.</exception>
    public OutputService(int maxEntries = 1000)
    {
        if (maxEntries < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEntries), maxEntries, "maxEntries must be >= 1.");
        }

        _maxEntries = maxEntries;

        // "General" is the default/fallback channel.
        _channels.Add("General");
    }

    /// <inheritdoc />
    public string RegisterChannel(string channel)
    {
        var normalized = NormalizeChannel(channel);

        lock (_gate)
        {
            _channels.Add(normalized);
        }

        return normalized;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetChannelsSnapshot()
    {
        lock (_gate)
        {
            return _channels.OrderBy(c => c, StringComparer.Ordinal).ToArray();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<OutputEntry> GetSnapshot()
    {
        lock (_gate)
        {
            return _entries.ToArray();
        }
    }

    /// <summary>
    /// Zapisuje wiadomość w danym kanale.
    /// </summary>
    /// <param name="channel">Kanał (gdy pusty, używa "General").</param>
    /// <param name="message">Treść komunikatu (gdy <see langword="null"/>, używa pustego stringa).</param>
    public void Write(string channel, string message)
    {
        Write(channel, message, OutputSeverity.Info);
    }

    /// <summary>
    /// Zapisuje wiadomość w danym kanale z określonym poziomem istotności.
    /// </summary>
    /// <param name="channel">Kanał (gdy pusty, używa "General").</param>
    /// <param name="message">Treść komunikatu (gdy <see langword="null"/>, używa pustego stringa).</param>
    /// <param name="severity">Poziom istotności wpisu.</param>
    public void Write(string channel, string message, OutputSeverity severity)
    {
        Write(channel, message, severity, null, null);
    }

    /// <summary>
    /// Zapisuje wiadomość z pełnymi metadanymi.
    /// </summary>
    /// <param name="channel">Kanał (gdy pusty, używa "General").</param>
    /// <param name="message">Treść komunikatu.</param>
    /// <param name="severity">Poziom istotności wpisu.</param>
    /// <param name="source">Identyfikator źródła (extension id / subsystem).</param>
    /// <param name="exceptionText">Bezpieczna tekstowa reprezentacja wyjątku.</param>
    public void Write(string channel, string message, OutputSeverity severity, string? source, string? exceptionText = null)
    {
        channel = RegisterChannel(channel);

        message ??= string.Empty;

        var entry = new OutputEntry(channel, message, DateTime.UtcNow, severity, source, exceptionText);
        Action<OutputEntry>? handler;

        lock (_gate)
        {
            _entries.Add(entry);
            if (_entries.Count > _maxEntries)
            {
                _entries.RemoveRange(0, _entries.Count - _maxEntries);
            }

            handler = EntryWritten;
        }

        handler?.Invoke(entry);
    }
}
