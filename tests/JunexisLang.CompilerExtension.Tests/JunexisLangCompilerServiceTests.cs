using JunexisLang.Studio.CompilerExtension;
using Junexis.Studio.Abstractions;

namespace JunexisLang.Studio.CompilerExtension.Tests;

/// <summary>
/// Testy jednostkowe dla <see cref="JunexisLangCompilerService"/>.
/// </summary>
public sealed class JunexisLangCompilerServiceTests
{
    [Fact]
    [Trait("Issue", "CONT-07")]
    public void SupportsFile_Jnx_ReturnsTrue()
    {
        var svc = new JunexisLangCompilerService(new JunexisCompilerOptions());
        Assert.True(svc.SupportsFile(".jnx"));
    }

    [Fact]
    [Trait("Issue", "CONT-07")]
    public void SupportsFile_JnxUppercase_ReturnsTrue()
    {
        var svc = new JunexisLangCompilerService(new JunexisCompilerOptions());
        Assert.True(svc.SupportsFile(".JNX"));
    }

    [Fact]
    [Trait("Issue", "CONT-07")]
    public void SupportsFile_Cs_ReturnsFalse()
    {
        var svc = new JunexisLangCompilerService(new JunexisCompilerOptions());
        Assert.False(svc.SupportsFile(".cs"));
    }

    [Fact]
    [Trait("Issue", "CONT-07")]
    public async Task BuildAsync_NoJnxFile_EmptyWorkspace_ReturnsFalse_WithWarning()
    {
        // Arrange: temp katalog bez .jnx
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var svc = new JunexisLangCompilerService(new JunexisCompilerOptions());
            var ctx = new BuildContext(dir, null);

            // Act
            var result = await svc.BuildAsync(ctx);

            // Assert
            Assert.False(result.Success);
            Assert.NotEmpty(result.Diagnostics);
            Assert.Equal(DiagnosticSeverity.Warning, result.Diagnostics[0].Severity);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    [Trait("Issue", "CONT-07")]
    public async Task CleanAsync_EmptyWorkspace_ReturnsSuccess()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var svc = new JunexisLangCompilerService(new JunexisCompilerOptions());
            var ctx = new BuildContext(dir, null);

            var result = await svc.CleanAsync(ctx);

            Assert.True(result.Success);
            Assert.Empty(result.Diagnostics);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    [Trait("Issue", "CONT-07")]
    public void CompilerId_IsJunexisLang()
    {
        var svc = new JunexisLangCompilerService(new JunexisCompilerOptions());
        Assert.Equal("junexis-lang", svc.CompilerId);
    }

    // ── WorkingDirectory tests (R-01 hardening) ────────────────────────────

    [Fact]
    [Trait("Issue", "CONT-07-R01")]
    public void ResolveWorkingDirectory_ExistingDirectory_ReturnsFileDirectory()
    {
        // Arrange: temp dir + fake .jnx file path inside it
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "test.jnx");

            // Act
            var result = JunexisLangCompilerService.ResolveWorkingDirectory(filePath);

            // Assert: returns the directory containing the file
            Assert.Equal(dir, result, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    [Trait("Issue", "CONT-07-R01")]
    public void ResolveWorkingDirectory_NonExistentDirectory_ReturnsFallback()
    {
        // Arrange: path whose parent directory does NOT exist
        var filePath = Path.Combine(
            Path.GetTempPath(),
            "definitely_does_not_exist_" + Path.GetRandomFileName(),
            "subdir",
            "file.jnx");

        // Act
        var result = JunexisLangCompilerService.ResolveWorkingDirectory(filePath);

        // Assert: fallback to Environment.CurrentDirectory
        Assert.Equal(Environment.CurrentDirectory, result);
    }

    [Fact]
    [Trait("Issue", "CONT-07-R01")]
    public void ResolveWorkingDirectory_PathWithSpaces_ReturnsCorrectDirectory()
    {
        // Arrange: directory name with embedded spaces
        var dir = Path.Combine(Path.GetTempPath(), "junexis test dir with spaces " + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "my project.jnx");

            // Act
            var result = JunexisLangCompilerService.ResolveWorkingDirectory(filePath);

            // Assert: spaces preserved, path resolved correctly
            Assert.Equal(dir, result, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ResolveWorkingDirectory_ProjectDirectory_ReturnsSameDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Equal(
                dir,
                JunexisLangCompilerService.ResolveWorkingDirectory(dir),
                StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ResolveBuildInput_ProjectManifest_WinsOverActiveGeneratedFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(dir, "out", "server"));
        File.WriteAllText(Path.Combine(dir, "junexis.project.toml"), "name = \"test\"");
        try
        {
            var context = new BuildContext(
                dir,
                Path.Combine(dir, "out", "server", "main.go"));

            Assert.Equal(
                dir,
                JunexisLangCompilerService.ResolveBuildInput(context),
                StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    [Trait("Issue", "CONT-07-R01")]
    public async Task BuildAsync_NonExistentWorkingDirectory_ReturnsControlledDiagnostic()
    {
        // Arrange: active document in a path whose directory does not exist.
        // ResolveWorkingDirectory falls back to Environment.CurrentDirectory.
        // CLI itself doesn't exist → "not found" diagnostic (no unhandled exception).
        var ctx = new BuildContext(
            WorkspaceRoot: Path.GetTempPath(),
            ActiveDocumentPath: Path.Combine(
                Path.GetTempPath(),
                "nonexistent_" + Path.GetRandomFileName(),
                "file.jnx"));

        var svc = new JunexisLangCompilerService(
            new JunexisCompilerOptions { CliPath = "__junexis_not_installed__" });

        // Act — must not throw
        var result = await svc.BuildAsync(ctx);

        // Assert: controlled failure with Error diagnostic
        Assert.False(result.Success);
        Assert.NotEmpty(result.Diagnostics);
        Assert.Equal(DiagnosticSeverity.Error, result.Diagnostics[0].Severity);
    }

    [Fact]
    [Trait("Issue", "25")]
    public void CreateStartInfo_LocalCompilerDllUsesDotnetWithoutFlatteningArguments()
    {
        var dll = Path.GetFullPath(Path.Combine("tools", "JunexisLang.Compiler.CLI.dll"));

        var startInfo = JunexisLangCompilerService.CreateStartInfo(
            dll,
            ["build", "project with spaces", "--json"],
            Environment.CurrentDirectory);

        Assert.Equal("dotnet", startInfo.FileName);
        Assert.Equal([dll, "build", "project with spaces", "--json"], startInfo.ArgumentList);
        Assert.False(startInfo.UseShellExecute);
    }
}
