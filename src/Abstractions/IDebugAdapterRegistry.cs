namespace Junexis.Studio.Abstractions;

/// <summary>Transport exposed by a concrete Debug Adapter Protocol implementation.</summary>
public enum DebugAdapterTransport
{
    /// <summary>DAP messages are exchanged through adapter stdin/stdout.</summary>
    Stdio,
    /// <summary>The adapter process listens on a local TCP port.</summary>
    Tcp,
}

/// <summary>Resolved concrete debug adapter for one language profile.</summary>
/// <param name="ProfileId">Launch profile identifier.</param>
/// <param name="DisplayName">Adapter display name.</param>
/// <param name="FileName">Adapter executable.</param>
/// <param name="Arguments">Arguments; TCP adapters may contain a <c>{port}</c> placeholder.</param>
/// <param name="Transport">Adapter transport.</param>
/// <param name="ConfigurationType">DAP launch configuration type.</param>
/// <param name="Source">How the adapter was discovered.</param>
public sealed record DebugAdapterDescriptor(
    string ProfileId,
    string DisplayName,
    string FileName,
    string Arguments,
    DebugAdapterTransport Transport,
    string ConfigurationType,
    string Source);

/// <summary>Discovers concrete debug adapters for Studio launch profiles.</summary>
public interface IDebugAdapterRegistry
{
    /// <summary>Resolves the best available adapter for a profile.</summary>
    /// <param name="profileId">Launch profile identifier.</param>
    /// <returns>The adapter descriptor, or <see langword="null"/>.</returns>
    DebugAdapterDescriptor? Resolve(string profileId);

    /// <summary>Returns adapter resolution results for all supported debug profiles.</summary>
    /// <returns>Available adapter descriptors.</returns>
    IReadOnlyList<DebugAdapterDescriptor> GetAvailableAdapters();
}
