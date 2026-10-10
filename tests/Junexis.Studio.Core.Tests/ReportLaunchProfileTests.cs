using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;

namespace Junexis.Studio.Core.Tests;

public sealed class ReportLaunchProfileTests
{
    [Fact]
    [Trait("Milestone", "M10.9")]
    public void JunexisProfile_RunUsesReportApplicationCommandAndRequiredToolchains()
    {
        var profile = Assert.Single(
            new LaunchProfileRegistry().GetProfiles(),
            candidate => candidate.Id == "junexis");

        Assert.Equal("run.jnx", profile.RunCommandId);
        Assert.Equal(["junexis", "go", "node", "npm"], profile.RunCapabilities);
    }

    [Fact]
    [Trait("Milestone", "M10.9")]
    public void JunexisProfile_MissingNpmRejectsRunWithActionableCapability()
    {
        var registry = new LaunchProfileRegistry();
        var profile = Assert.Single(
            registry.GetProfiles(),
            candidate => candidate.Id == "junexis");
        var snapshot = new ToolchainDoctorSnapshot(
            DateTimeOffset.UtcNow,
            [
                Available("junexis"),
                Available("go"),
                Available("node"),
                new ToolchainCapability("npm", "npm", "Junexis", "npm --version", false, null, "brak")
            ]);

        var validation = registry.Validate(profile, LaunchOperation.Run, snapshot);

        Assert.False(validation.IsValid);
        Assert.Equal(["npm"], validation.MissingCapabilities);
        Assert.Contains("npm", validation.Message, StringComparison.Ordinal);
    }

    private static ToolchainCapability Available(string id) =>
        new(id, id, "Junexis", $"{id} --version", true, "test", "gotowe");
}
