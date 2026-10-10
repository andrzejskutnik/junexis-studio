using System.ComponentModel;
using System.Runtime.CompilerServices;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>View model for the Studio toolchain health panel.</summary>
public sealed class ToolchainDoctorViewModel : INotifyPropertyChanged
{
    private readonly IToolchainDoctorService _doctor;
    private readonly IEventBus _eventBus;
    private readonly IOutput _output;
    private readonly SynchronizationContext? _synchronizationContext;
    private IReadOnlyList<ToolchainCapability> _capabilities = [];
    private string _statusText = "Toolchains have not been checked yet.";
    private bool _isBusy;

    /// <summary>Creates the toolchain doctor view model.</summary>
    /// <param name="doctor">Toolchain health service.</param>
    /// <param name="eventBus">Studio event bus.</param>
    /// <param name="output">Studio output log.</param>
    public ToolchainDoctorViewModel(IToolchainDoctorService doctor, IEventBus eventBus, IOutput output)
    {
        _doctor = doctor ?? throw new ArgumentNullException(nameof(doctor));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _synchronizationContext = SynchronizationContext.Current;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Capabilities reported by the latest health check.</summary>
    public IReadOnlyList<ToolchainCapability> Capabilities
    {
        get => _capabilities;
        private set => SetField(ref _capabilities, value);
    }

    /// <summary>Summary of the latest check.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    /// <summary>Whether a health check is currently running.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    /// <summary>Refreshes all toolchain capabilities.</summary>
    /// <param name="cancellationToken">Token used to cancel the refresh.</param>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
            return;

        Update(() =>
        {
            IsBusy = true;
            StatusText = "Checking Junexis and language toolchains…";
        });
        try
        {
            var snapshot = await _doctor.CheckAsync(cancellationToken).ConfigureAwait(false);
            var available = snapshot.Capabilities.Count(item => item.IsAvailable);
            var status = $"{available}/{snapshot.Capabilities.Count} toolchain components available.";
            Update(() =>
            {
                Capabilities = snapshot.Capabilities;
                StatusText = status;
            });
            _eventBus.Publish(new ToolchainCapabilitiesUpdatedEvent(snapshot));
            _output.Write("Toolchains", status,
                available == snapshot.Capabilities.Count ? OutputSeverity.Info : OutputSeverity.Warning);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Update(() => StatusText = "Toolchain check cancelled.");
        }
        catch (Exception ex)
        {
            var status = $"Toolchain check failed: {ex.Message}";
            Update(() => StatusText = status);
            _output.Write("Toolchains", status, OutputSeverity.Error);
        }
        finally
        {
            Update(() => IsBusy = false);
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

internal sealed class ToolchainDoctorExtension : IJunexisExtension
{
    private CancellationTokenSource? _cancellation;

    public string Id => "junexis.toolchains.doctor";

    public void Initialize(IHostContext context)
    {
        if (!context.Capabilities.TryGet<IToolchainDoctorService>(out var doctor) || doctor is null)
        {
            context.Output.Write("Toolchains", "IToolchainDoctorService unavailable — Doctor inactive.");
            return;
        }

        _cancellation = new CancellationTokenSource();
        var viewModel = new ToolchainDoctorViewModel(doctor, context.EventBus, context.Output);
        context.Panels.RegisterPanel(
            Id,
            PanelType.Tool,
            () => viewModel,
            title: "Toolchains",
            dockGroup: DockGroup.Left);
        context.Commands.Register(
            "toolchains.doctor.refresh",
            () => _ = viewModel.RefreshAsync(_cancellation.Token),
            menuPath: "Tools/Check Toolchains",
            displayName: "Check Toolchains",
            category: "Tools");
        _ = viewModel.RefreshAsync(_cancellation.Token);
    }

    public void Shutdown()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
    }
}
