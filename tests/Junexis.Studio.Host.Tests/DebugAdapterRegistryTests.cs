using Junexis.Studio.Abstractions;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class DebugAdapterRegistryTests
{
    [Fact]
    public void DebugToolLocator_UsesFallbackWhenToolIsMissingFromPath()
    {
        var root = Directory.CreateTempSubdirectory("junexis-debug-tools-");
        try
        {
            var fallback = Path.Combine(root.FullName, OperatingSystem.IsWindows() ? "gdb.exe" : "gdb");
            File.WriteAllText(fallback, string.Empty);

            var result = DebugToolLocator.Resolve("JUNEXIS_GDB", "gdb", [fallback], _ => null, string.Empty);

            Assert.Equal(fallback, result);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void DebugToolLocator_EnvironmentOverrideHasPriority()
    {
        var result = DebugToolLocator.Resolve(
            "JUNEXIS_GDB",
            "gdb",
            [],
            key => key == "JUNEXIS_GDB" ? "custom-gdb" : null,
            string.Empty);

        Assert.Equal("custom-gdb", result);
    }

    [Fact]
    public void ResolveCpp_AutoDiscoversOpenDebugAd7FromVsCodeExtension()
    {
        var root = Directory.CreateTempSubdirectory("junexis-cpptools-");
        try
        {
            var adapter = Path.Combine(root.FullName, "ms-vscode.cpptools-1.2.3", "debugAdapters", "bin",
                OperatingSystem.IsWindows() ? "OpenDebugAD7.exe" : "OpenDebugAD7");
            Directory.CreateDirectory(Path.GetDirectoryName(adapter)!);
            File.WriteAllText(adapter, string.Empty);
            var registry = new DebugAdapterRegistry(root.FullName, string.Empty, root.FullName, _ => null);

            var result = registry.Resolve("cpp");

            Assert.NotNull(result);
            Assert.Equal(adapter, result.FileName);
            Assert.Equal(DebugAdapterTransport.Stdio, result.Transport);
            Assert.Equal("cppdbg", result.ConfigurationType);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void ResolveGo_AutoDiscoversDelveAndUsesTcpPortTemplate()
    {
        var root = Directory.CreateTempSubdirectory("junexis-delve-");
        try
        {
            var adapter = Path.Combine(root.FullName, "go", "bin", OperatingSystem.IsWindows() ? "dlv.exe" : "dlv");
            Directory.CreateDirectory(Path.GetDirectoryName(adapter)!);
            File.WriteAllText(adapter, string.Empty);
            var registry = new DebugAdapterRegistry("missing", string.Empty, root.FullName, _ => null);

            var result = registry.Resolve("go");

            Assert.NotNull(result);
            Assert.Equal(DebugAdapterTransport.Tcp, result.Transport);
            Assert.Contains("{port}", result.Arguments, StringComparison.Ordinal);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void ResolveTypeScript_UsesExplicitStandaloneAdapterConfiguration()
    {
        var values = new Dictionary<string, string>
        {
            ["JUNEXIS_TYPESCRIPT_DAP"] = "node",
            ["JUNEXIS_TYPESCRIPT_DAP_ARGS"] = "dapDebugServer.js",
            ["JUNEXIS_TYPESCRIPT_DAP_TYPE"] = "pwa-node",
        };
        var registry = new DebugAdapterRegistry("missing", string.Empty, "missing",
            key => values.GetValueOrDefault(key));

        var result = registry.Resolve("typescript");

        Assert.NotNull(result);
        Assert.Equal("node", result.FileName);
        Assert.Equal("dapDebugServer.js", result.Arguments);
        Assert.Equal(DebugAdapterTransport.Stdio, result.Transport);
    }

    [Fact]
    public void ResolveTypeScript_AutoDiscoversPinnedJunexisToolchain()
    {
        var root = Directory.CreateTempSubdirectory("junexis-js-debug-");
        try
        {
            var server = Path.Combine(root.FullName, "js-debug", "1.117.0", "js-debug", "src",
                "dapDebugServer.js");
            Directory.CreateDirectory(Path.GetDirectoryName(server)!);
            File.WriteAllText(server, string.Empty);
            var registry = new DebugAdapterRegistry(
                "missing", string.Empty, root.FullName, _ => null, root.FullName);

            var result = registry.Resolve("typescript");

            Assert.NotNull(result);
            Assert.Equal(DebugAdapterTransport.Tcp, result.Transport);
            Assert.Equal("pwa-node", result.ConfigurationType);
            Assert.Contains(server, result.Arguments, StringComparison.Ordinal);
            Assert.Contains("{port}", result.Arguments, StringComparison.Ordinal);
            Assert.Equal("junexis-toolchain:js-debug", result.Source);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
