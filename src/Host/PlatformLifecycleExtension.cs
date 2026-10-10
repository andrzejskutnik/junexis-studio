using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>Model panelu cyklu życia platformy, delegujący wszystkie działania do publicznego CLI.</summary>
public sealed partial class PlatformLifecycleViewModel : INotifyPropertyChanged, IDisposable
{
    private const string OutputChannel = "Cykl platformy";
    private const int MaximumLogCharacters = 256 * 1024;
    private readonly IProcessService _processes;
    private readonly IOutput _output;
    private readonly string _cli;
    private readonly SynchronizationContext? _synchronizationContext;
    private readonly StringBuilder _log = new();
    private readonly StringBuilder _rawStdOut = new();
    private readonly object _logGate = new();
    private CancellationTokenSource? _operationCts;
    private string _profilePath = string.Empty;
    private string _catalogPath = string.Empty;
    private string _trustStorePath = string.Empty;
    private string _currentManifestPath = string.Empty;
    private string _targetBundlePath = string.Empty;
    private string _repairOfflineSource = string.Empty;
    private string _repairBaseUrl = string.Empty;
    private string _statusText = "Wybierz działanie. Studio uruchomi publiczne polecenie junexis.";
    private string _logText = string.Empty;
    private string _recoveryText = "Nie przejęto nadzoru nad żadną operacją.";
    private string? _observedOperationId;
    private bool _isBusy;
    private bool _canApplyRepair;
    private bool _canApplyRollback;
    private string? _repairApprovalKey;
    private string? _rollbackApprovalKey;

    /// <summary>Tworzy model panelu cyklu życia platformy.</summary>
    /// <param name="processes">Usługa uruchamiająca publiczne procesy.</param>
    /// <param name="tools">Resolver publicznego polecenia <c>junexis</c>.</param>
    /// <param name="output">Wspólny dziennik Studio.</param>
    public PlatformLifecycleViewModel(IProcessService processes, IPlatformToolResolver tools, IOutput output)
    {
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
        ArgumentNullException.ThrowIfNull(tools);
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _cli = tools.ResolveCommand("junexis", "JUNEXIS_CLI");
        _synchronizationContext = SynchronizationContext.Current;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Opcjonalny profil instalacji; pusty oznacza profil bieżącego użytkownika.</summary>
    public string ProfilePath
    {
        get => _profilePath;
        set { if (SetField(ref _profilePath, value ?? string.Empty)) InvalidateApprovals(); }
    }

    /// <summary>Ścieżka podpisanego katalogu wydań.</summary>
    public string CatalogPath
    {
        get => _catalogPath;
        set { if (SetField(ref _catalogPath, value ?? string.Empty)) InvalidateRollback(); }
    }

    /// <summary>Ścieżka magazynu zaufania.</summary>
    public string TrustStorePath
    {
        get => _trustStorePath;
        set { if (SetField(ref _trustStorePath, value ?? string.Empty)) InvalidateRollback(); }
    }

    /// <summary>Manifest obecnie aktywnego wydania używany do planowania aktualizacji.</summary>
    public string CurrentManifestPath
    {
        get => _currentManifestPath;
        set => SetField(ref _currentManifestPath, value ?? string.Empty);
    }

    /// <summary>Lokalne archiwum manifestu docelowego wydania.</summary>
    public string TargetBundlePath
    {
        get => _targetBundlePath;
        set => SetField(ref _targetBundlePath, value ?? string.Empty);
    }

    /// <summary>Katalog lustrzany używany przez naprawę offline.</summary>
    public string RepairOfflineSource
    {
        get => _repairOfflineSource;
        set { if (SetField(ref _repairOfflineSource, value ?? string.Empty)) InvalidateRepair(); }
    }

    /// <summary>Bazowy adres HTTPS używany przez naprawę online.</summary>
    public string RepairBaseUrl
    {
        get => _repairBaseUrl;
        set { if (SetField(ref _repairBaseUrl, value ?? string.Empty)) InvalidateRepair(); }
    }

    /// <summary>Bieżący, bezpieczny opis stanu działania.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    /// <summary>Zredagowany strumień wyjścia publicznego CLI.</summary>
    public string LogText
    {
        get => _logText;
        private set => SetField(ref _logText, value);
    }

    /// <summary>Opis przerwanej operacji obserwowanej przez bieżącą sesję Studio.</summary>
    public string RecoveryText
    {
        get => _recoveryText;
        private set => SetField(ref _recoveryText, value);
    }

    /// <summary>Bezpieczny identyfikator operacji, nad którą Studio przejęło nadzór.</summary>
    public string? ObservedOperationId
    {
        get => _observedOperationId;
        private set => SetField(ref _observedOperationId, value);
    }

    /// <summary>Informuje, czy publiczne CLI nadal wykonuje działanie.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(CanCancel));
        }
    }

    /// <summary>Informuje, czy można anulować bieżący proces CLI.</summary>
    public bool CanCancel => IsBusy;

    /// <summary>Informuje, czy naprawę poprzedził udany plan dla tych samych parametrów.</summary>
    public bool CanApplyRepair
    {
        get => _canApplyRepair && !IsBusy;
        private set
        {
            if (SetField(ref _canApplyRepair, value, nameof(CanApplyRepair)))
                OnPropertyChanged(nameof(CanApplyRepair));
        }
    }

    /// <summary>Informuje, czy powrót poprzedził udany plan dla tych samych parametrów.</summary>
    public bool CanApplyRollback
    {
        get => _canApplyRollback && !IsBusy;
        private set
        {
            if (SetField(ref _canApplyRollback, value, nameof(CanApplyRollback)))
                OnPropertyChanged(nameof(CanApplyRollback));
        }
    }

    /// <summary>Wyświetla plan aktualizacji bez wykonywania zmian.</summary>
    public Task PlanUpdateAsync() => RequireAndRunAsync(
        [CatalogPath, TrustStorePath, CurrentManifestPath, TargetBundlePath],
        "Uzupełnij katalog, magazyn zaufania, bieżący manifest i archiwum docelowe.",
        BuildArguments("update", "plan", "--catalog", CatalogPath, "--trust-store", TrustStorePath,
            "--current-manifest", CurrentManifestPath, "--target-bundle", TargetBundlePath, "--json"),
        "Plan aktualizacji został odczytany.");

    /// <summary>Odświeża historię i postęp operacji aktualizacji.</summary>
    public Task RefreshHistoryAsync() => RunAsync(
        BuildArguments("update", "history", "--json"), "Historia i postęp zostały odświeżone.");

    /// <summary>
    /// Przejmuje nadzór nad najnowszą przerwaną operacją widoczną w publicznej historii.
    /// Nie wznawia ani nie modyfikuje operacji.
    /// </summary>
    public async Task TakeOverInterruptedAsync()
    {
        var succeeded = await RunAsync(BuildArguments("update", "history", "--json"),
            "Historia została odczytana; Studio sprawdza przerwane operacje.").ConfigureAwait(false);
        if (!succeeded) return;
        try
        {
            var operation = ParseInterruptedOperation(RawStdOutSnapshot());
            Update(() =>
            {
                ObservedOperationId = operation?.OperationId;
                RecoveryText = operation is null
                    ? "Nie znaleziono operacji wymagającej przejęcia nadzoru."
                    : operation.Result == "recovery-required"
                        ? $"Operacja {operation.OperationId} wymaga naprawy; ostatnia faza: {operation.LastPhase}."
                        : $"Studio nadzoruje operację {operation.OperationId}; ostatnia faza: {operation.LastPhase}.";
            });
        }
        catch (InvalidDataException exception)
        {
            Update(() =>
            {
                ObservedOperationId = null;
                RecoveryText = "Historia nie ma bezpiecznego formatu; nadzór nie został przejęty.";
            });
            AppendLog(exception.Message, OutputSeverity.Error);
        }
    }

    /// <summary>Sprawdza instalację i przygotowuje zgodę na naprawę dla bieżących parametrów.</summary>
    public async Task CheckRepairAsync()
    {
        if (!HasExactlyOneRepairSource())
        {
            SetStatus("Przed sprawdzeniem wskaż dokładnie jedno źródło naprawy: katalog offline albo adres HTTPS.");
            return;
        }
        var succeeded = await RunAsync(BuildArguments("repair", "check", "--json"),
            "Plan naprawy został odczytany.").ConfigureAwait(false);
        if (succeeded)
        {
            _repairApprovalKey = RepairKey();
            Update(() => CanApplyRepair = true);
        }
    }

    /// <summary>Wykonuje naprawę przez CLI po udanym sprawdzeniu tych samych parametrów.</summary>
    public async Task ApplyRepairAsync()
    {
        if (!CanApplyRepair || _repairApprovalKey != RepairKey())
        {
            SetStatus("Najpierw sprawdź naprawę dla niezmienionego profilu i źródła.");
            return;
        }
        var args = BuildArguments("repair", "apply");
        if (!string.IsNullOrWhiteSpace(RepairOfflineSource))
            Add(args, "--offline-source", RepairOfflineSource);
        else
            Add(args, "--base-url", RepairBaseUrl);
        var succeeded = await RunAsync(args, "Naprawa została zakończona.").ConfigureAwait(false);
        if (succeeded) InvalidateRepair();
    }

    /// <summary>Wyświetla plan powrotu do zachowanego wydania.</summary>
    public async Task PlanRollbackAsync()
    {
        if (string.IsNullOrWhiteSpace(CatalogPath) || string.IsNullOrWhiteSpace(TrustStorePath))
        {
            SetStatus("Uzupełnij katalog wydań i magazyn zaufania.");
            return;
        }
        var succeeded = await RunAsync(BuildArguments("update", "rollback", "--catalog", CatalogPath,
            "--trust-store", TrustStorePath, "--json"), "Plan powrotu został odczytany.")
            .ConfigureAwait(false);
        if (succeeded)
        {
            _rollbackApprovalKey = RollbackKey();
            Update(() => CanApplyRollback = true);
        }
    }

    /// <summary>Wykonuje powrót przez CLI po udanym planie tych samych parametrów.</summary>
    public async Task ApplyRollbackAsync()
    {
        if (!CanApplyRollback || _rollbackApprovalKey != RollbackKey())
        {
            SetStatus("Najpierw przygotuj plan powrotu dla niezmienionych materiałów.");
            return;
        }
        var succeeded = await RunAsync(BuildArguments("update", "rollback", "--catalog", CatalogPath,
            "--trust-store", TrustStorePath, "--apply", "--json"), "Powrót został zakończony.")
            .ConfigureAwait(false);
        if (succeeded) InvalidateRollback();
    }

    /// <summary>Żąda anulowania bieżącego procesu CLI wraz z jego procesami potomnymi.</summary>
    public void Cancel()
    {
        var cts = Volatile.Read(ref _operationCts);
        if (cts is null) return;
        SetStatus("Żądanie anulowania przekazane do publicznego CLI…");
        cts.Cancel();
    }

    private Task RequireAndRunAsync(IReadOnlyList<string> required, string message,
        IReadOnlyList<string> arguments, string success)
    {
        if (required.Any(string.IsNullOrWhiteSpace))
        {
            SetStatus(message);
            return Task.CompletedTask;
        }
        return RunAsync(arguments, success);
    }

    private async Task<bool> RunAsync(IReadOnlyList<string> arguments, string success)
    {
        var cts = new CancellationTokenSource();
        if (Interlocked.CompareExchange(ref _operationCts, cts, null) is not null)
        {
            cts.Dispose();
            SetStatus("Inna operacja cyklu platformy nadal trwa.");
            return false;
        }
        Update(() =>
        {
            IsBusy = true;
            StatusText = "Publiczne CLI wykonuje działanie…";
            OnPropertyChanged(nameof(CanApplyRepair));
            OnPropertyChanged(nameof(CanApplyRollback));
        });
        ClearLog();
        try
        {
            var result = await _processes.RunAsync(new ProcessRequest(_cli, arguments),
                line =>
                {
                    if (line.Kind == ProcessOutputKind.StdOut)
                    {
                        lock (_logGate)
                        {
                            if (_rawStdOut.Length > 0) _rawStdOut.AppendLine();
                            if (_rawStdOut.Length + line.Text.Length <= MaximumLogCharacters)
                                _rawStdOut.Append(line.Text);
                        }
                    }
                    AppendLog(line.Text, line.Kind == ProcessOutputKind.StdErr
                        ? OutputSeverity.Error : OutputSeverity.Info);
                }, cts.Token).ConfigureAwait(false);
            var status = result.ExitCode switch
            {
                0 => success,
                3 => "CLI zakończyło działanie bez zmiany: plan jest zablokowany lub wymaga uwagi.",
                _ => $"CLI zakończyło działanie kodem {result.ExitCode}."
            };
            SetStatus(status);
            return result.ExitCode == 0;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            SetStatus("Działanie zostało anulowane. Stan sprawdź przez historię.");
            return false;
        }
        catch (Exception exception)
        {
            AppendLog("Nie można uruchomić publicznego CLI: " + Sanitize(exception.Message),
                OutputSeverity.Error);
            SetStatus("Nie udało się uruchomić publicznego CLI.");
            return false;
        }
        finally
        {
            Interlocked.CompareExchange(ref _operationCts, null, cts);
            cts.Dispose();
            Update(() =>
            {
                IsBusy = false;
                OnPropertyChanged(nameof(CanApplyRepair));
                OnPropertyChanged(nameof(CanApplyRollback));
            });
        }
    }

    private List<string> BuildArguments(params string[] values)
    {
        var result = values.ToList();
        if (!string.IsNullOrWhiteSpace(ProfilePath)) Add(result, "--profile", ProfilePath);
        return result;
    }

    private static void Add(ICollection<string> arguments, string name, string value)
    {
        arguments.Add(name);
        arguments.Add(value);
    }

    private bool HasExactlyOneRepairSource() =>
        string.IsNullOrWhiteSpace(RepairOfflineSource) != string.IsNullOrWhiteSpace(RepairBaseUrl);

    private string RepairKey() => string.Join('\n', ProfilePath, RepairOfflineSource, RepairBaseUrl);
    private string RollbackKey() => string.Join('\n', ProfilePath, CatalogPath, TrustStorePath);

    private void InvalidateApprovals()
    {
        InvalidateRepair();
        InvalidateRollback();
    }

    private void InvalidateRepair()
    {
        _repairApprovalKey = null;
        CanApplyRepair = false;
    }

    private void InvalidateRollback()
    {
        _rollbackApprovalKey = null;
        CanApplyRollback = false;
    }

    private void ClearLog()
    {
        lock (_logGate)
        {
            _log.Clear();
            _rawStdOut.Clear();
        }
        Update(() => LogText = string.Empty);
    }

    private string RawStdOutSnapshot()
    {
        lock (_logGate) return _rawStdOut.ToString();
    }

    private void AppendLog(string value, OutputSeverity severity)
    {
        var safe = Sanitize(value);
        lock (_logGate)
        {
            if (_log.Length + safe.Length + Environment.NewLine.Length > MaximumLogCharacters)
            {
                _log.Clear();
                _log.AppendLine("[starszy wynik pominięty]");
            }
            if (_log.Length > 0) _log.AppendLine();
            _log.Append(safe);
            var snapshot = _log.ToString();
            Update(() => LogText = snapshot);
        }
        _output.Write(OutputChannel, safe, severity);
    }

    private void SetStatus(string value) => Update(() => StatusText = value);

    private void Update(Action action)
    {
        if (_synchronizationContext is null || SynchronizationContext.Current == _synchronizationContext)
            action();
        else
            _synchronizationContext.Post(_ => action(), null);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static string Sanitize(string value)
    {
        var result = NamedSecret().Replace(value, match => match.Groups[1].Value + "=[ukryto]");
        result = BearerToken().Replace(result, "Bearer [ukryto]");
        result = QuerySecret().Replace(result, match => match.Groups[1].Value + "[ukryto]");
        result = UriUserInfo().Replace(result, match => match.Groups[1].Value + "[ukryto]@");
        result = AbsoluteWindowsPath().Replace(result, "[ścieżka ukryta]");
        return AbsoluteUnixPath().Replace(result, "[ścieżka ukryta]");
    }

    [GeneratedRegex("(?i)\\b(password|token|secret|api[-_]?key|authorization)\\s*[:=]\\s*(?:Bearer\\s+)?[^\\s,;]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex NamedSecret();

    [GeneratedRegex("(?i)\\bBearer\\s+[A-Za-z0-9._~+/-]+=*", RegexOptions.CultureInvariant)]
    private static partial Regex BearerToken();

    [GeneratedRegex("(?i)([?&](?:password|token|secret|api[-_]?key|authorization)=)[^&#\\s]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex QuerySecret();

    [GeneratedRegex("(?i)(https?://)[^/@\\s]+@", RegexOptions.CultureInvariant)]
    private static partial Regex UriUserInfo();

    [GeneratedRegex("(?i)(?:[a-z]:\\\\|\\\\\\\\)[^\\r\\n\\t\"'<>|]+", RegexOptions.CultureInvariant)]
    private static partial Regex AbsoluteWindowsPath();

    [GeneratedRegex("(?<![:/A-Za-z0-9._-])/(?!/)[^\\s\"'<>]+", RegexOptions.CultureInvariant)]
    private static partial Regex AbsoluteUnixPath();

    private static InterruptedOperation? ParseInterruptedOperation(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            var root = document.RootElement;
            RejectDuplicateProperties(root);
            if (root.ValueKind != JsonValueKind.Object
                || root.GetProperty("schemaVersion").GetString() != "junexis.update-history/0.1"
                || root.GetProperty("operations").ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Publiczna historia ma nieobsługiwany kontrakt.");
            foreach (var item in root.GetProperty("operations").EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var result = item.GetProperty("result").GetString();
                if (result is not ("in-progress" or "recovery-required")) continue;
                var operationId = item.GetProperty("operationId").GetString();
                var phase = item.GetProperty("lastPhase").GetString();
                if (operationId is null || phase is null || !SafeCode().IsMatch(operationId)
                    || !SafeCode().IsMatch(phase))
                    throw new InvalidDataException("Historia zawiera niebezpieczną tożsamość operacji.");
                return new InterruptedOperation(operationId, result, phase);
            }
            return null;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Publiczna historia nie jest poprawnym JSON.", exception);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidDataException("Publiczna historia nie odpowiada kontraktowi.", exception);
        }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException("Publiczna historia powtarza pole JSON.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{1,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeCode();

    private sealed record InterruptedOperation(string OperationId, string Result, string LastPhase);

    /// <inheritdoc />
    public void Dispose()
    {
        var cts = Interlocked.Exchange(ref _operationCts, null);
        cts?.Cancel();
        cts?.Dispose();
    }
}

internal sealed class PlatformLifecycleExtension : IJunexisExtension
{
    private PlatformLifecycleViewModel? _viewModel;

    public string Id => "junexis.platform.lifecycle";

    public void Initialize(IHostContext context)
    {
        if (!context.Capabilities.TryGet<IPlatformToolResolver>(out var tools) || tools is null)
        {
            context.Output.Write("Cykl platformy", "Brak resolvera publicznego CLI; panel jest nieaktywny.",
                OutputSeverity.Error);
            return;
        }
        _viewModel = new PlatformLifecycleViewModel(context.Processes, tools, context.Output);
        context.Panels.RegisterPanel(Id, PanelType.Tool, () => _viewModel,
            title: "Cykl platformy", dockGroup: DockGroup.Right);
        context.Commands.Register("platform.lifecycle.open",
            () => context.EventBus.Publish(new ActivatePanelRequested(Id)),
            menuPath: "Tools/Cykl platformy", displayName: "Cykl platformy", category: "Tools");
    }

    public void Shutdown()
    {
        _viewModel?.Dispose();
        _viewModel = null;
    }
}
