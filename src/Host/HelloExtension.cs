using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>
/// Minimalne rozszerzenie testowe: zapisuje do Output i publikuje przykładowy event.
/// </summary>
internal sealed class HelloExtension : IJunexisExtension
{
    public string Id => "junexis.hello";

    public void Initialize(IHostContext context)
    {
        context.Output.Write("Core", "Hello Extension Loaded");

        context.EventBus.Subscribe<string>(OnStringEvent);
        context.EventBus.Publish("Hello from EventBus");
    }

    public void Shutdown()
    {
        // no-op
    }

    private static void OnStringEvent(string message)
    {
        // Intentionally empty: this is a smoke-test subscription.
        _ = message;
    }
}
