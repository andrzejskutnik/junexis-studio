using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>Default launch-profile registry for languages supported by Studio.</summary>
public sealed class LaunchProfileRegistry : ILaunchProfileRegistry
{
    private readonly IReadOnlyList<LaunchProfile> _profiles;

    /// <summary>Creates the default registry for Junexis, C#, C++, Go, and TypeScript.</summary>
    public LaunchProfileRegistry()
        : this(CreateDefaults())
    {
    }

    /// <summary>Creates a registry with an explicit ordered profile collection.</summary>
    /// <param name="profiles">Profiles to register.</param>
    public LaunchProfileRegistry(IReadOnlyList<LaunchProfile> profiles)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
    }

    /// <inheritdoc />
    public IReadOnlyList<LaunchProfile> GetProfiles() => _profiles;

    /// <inheritdoc />
    public LaunchProfile? FindForPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return _profiles.FirstOrDefault(profile =>
            profile.FileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public LaunchProfileValidation Validate(
        LaunchProfile profile,
        LaunchOperation operation,
        ToolchainDoctorSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var (command, requirements) = operation switch
        {
            LaunchOperation.Build => (profile.BuildCommandId, profile.BuildCapabilities),
            LaunchOperation.Run => (profile.RunCommandId, profile.RunCapabilities),
            LaunchOperation.Debug => (profile.DebugCommandId, profile.DebugCapabilities),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        if (command is null)
            return new LaunchProfileValidation(false, null, [],
                $"{profile.DisplayName} does not define a {operation.ToString().ToLowerInvariant()} command.");

        // The initial startup scan is asynchronous. Preserve command availability until its
        // first snapshot arrives; every subsequent launch is validated against real probes.
        if (snapshot is null)
            return new LaunchProfileValidation(true, command, [], "Toolchain scan is still in progress.");

        var available = snapshot.Capabilities
            .Where(capability => capability.IsAvailable)
            .Select(capability => capability.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = requirements.Where(id => !available.Contains(id)).ToArray();
        return missing.Length == 0
            ? new LaunchProfileValidation(true, command, [], "Profile is ready.")
            : new LaunchProfileValidation(false, command, missing,
                $"Missing toolchain capability: {string.Join(", ", missing)}.");
    }

    private static IReadOnlyList<LaunchProfile> CreateDefaults() =>
    [
        new("junexis", "Junexis", [".jnx"], "build.jnx", "run.jnx", null,
            ["junexis"], ["junexis", "go", "node", "npm"], []),
        new("csharp", "C# / .NET", [".cs", ".csproj", ".sln"], "build.csharp", "run.csharp", null,
            ["dotnet"], ["dotnet"], []),
        new("cpp", "C / C++", [".c", ".cc", ".cxx", ".cpp"], "build.gpp", "run.gpp", "debug.gpp",
            ["cpp"], [], ["cpp-dap", "gdb"]),
        new("go", "Go", [".go"], "build.go", "run.go", "debug.go",
            ["go"], ["go"], ["go-dap"]),
        new("typescript", "TypeScript", [".ts", ".tsx"], "build.typescript", "run.typescript", "debug.typescript",
            ["typescript"], ["node"], ["typescript-dap"]),
    ];
}
