using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;

namespace Junexis.Studio.Core.Tests;

public sealed class LaunchProfileRegistryTests
{
    [Theory]
    [InlineData("main.cpp", "cpp")]
    [InlineData("main.go", "go")]
    [InlineData("main.ts", "typescript")]
    [InlineData("component.tsx", "typescript")]
    [InlineData("main.jnx", "junexis")]
    public void FindForPath_ResolvesDefaultLanguageProfiles(string path, string expectedId)
    {
        var registry = new LaunchProfileRegistry();

        var profile = registry.FindForPath(path);

        Assert.Equal(expectedId, profile?.Id);
    }

    [Fact]
    public void Validate_BlocksDebugWhenDebuggerCapabilityIsMissing()
    {
        var registry = new LaunchProfileRegistry();
        var profile = Assert.IsType<LaunchProfile>(registry.FindForPath("main.go"));
        var snapshot = new ToolchainDoctorSnapshot(
            DateTimeOffset.UtcNow,
            [new ToolchainCapability("go", "Go", "Go", "go version", true, "go1.25", "Ready.")]);

        var validation = registry.Validate(profile, LaunchOperation.Debug, snapshot);

        Assert.False(validation.IsValid);
        Assert.Equal("debug.go", validation.CommandId);
        Assert.Equal(["go-dap"], validation.MissingCapabilities);
    }

    [Fact]
    public void Validate_SelectsTypeScriptBuildWhenCompilerIsAvailable()
    {
        var registry = new LaunchProfileRegistry();
        var profile = Assert.IsType<LaunchProfile>(registry.FindForPath("main.ts"));
        var snapshot = new ToolchainDoctorSnapshot(
            DateTimeOffset.UtcNow,
            [new ToolchainCapability("typescript", "TypeScript", "TypeScript", "tsc --version", true, "6.0", "Ready.")]);

        var validation = registry.Validate(profile, LaunchOperation.Build, snapshot);

        Assert.True(validation.IsValid);
        Assert.Equal("build.typescript", validation.CommandId);
    }
}
