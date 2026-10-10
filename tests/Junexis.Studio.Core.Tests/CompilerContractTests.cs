using Junexis.Studio.Abstractions;
using Xunit;

namespace Junexis.Studio.Core.Tests;

[Trait("Issue", "53")]
[Trait("Issue", "54")]
public sealed class CompilerContractTests
{
    [Fact]
    public void BuildContext_DefaultProfile_IsDebug()
    {
        var ctx = new BuildContext("/workspace");

        Assert.Equal("Debug", ctx.BuildProfile);
        Assert.Null(ctx.ActiveDocumentPath);
        Assert.Null(ctx.EnvironmentVariables);
    }

    [Fact]
    public void BuildContext_Empty_HasEmptyWorkspace()
    {
        var ctx = BuildContext.Empty;

        Assert.Equal(string.Empty, ctx.WorkspaceRoot);
        Assert.Equal("Debug", ctx.BuildProfile);
    }

    [Fact]
    public void BuildContext_WithEnvVars()
    {
        var env = new Dictionary<string, string> { ["CC"] = "gcc" };
        var ctx = new BuildContext("/ws", BuildProfile: "Release", EnvironmentVariables: env);

        Assert.Equal("Release", ctx.BuildProfile);
        Assert.Equal("gcc", ctx.EnvironmentVariables!["CC"]);
    }

    [Fact]
    public void CompilerResult_Success_NoDiagnostics()
    {
        var result = new CompilerResult(true, Array.Empty<Diagnostic>(), "test-compiler");

        Assert.True(result.Success);
        Assert.Empty(result.Diagnostics);
        Assert.Equal("test-compiler", result.CompilerId);
    }

    [Fact]
    public void CompilerResult_Failure_WithDiagnostics()
    {
        var diag = new Diagnostic(DiagnosticSeverity.Error, "syntax error", "main.cpp", 10, 5);
        var result = new CompilerResult(false, new[] { diag }, "gcc");

        Assert.False(result.Success);
        Assert.Single(result.Diagnostics);
        Assert.Equal("syntax error", result.Diagnostics[0].Message);
    }

    [Fact]
    public void FakeCompiler_SupportsFile_ReturnsExpected()
    {
        ICompilerService compiler = new FakeCompiler("test", ".nx");

        Assert.True(compiler.SupportsFile(".nx"));
        Assert.False(compiler.SupportsFile(".cpp"));
        Assert.Equal("test", compiler.CompilerId);
    }

    [Fact]
    public async Task FakeCompiler_BuildAsync_ReturnsResult()
    {
        ICompilerService compiler = new FakeCompiler("test", ".nx");

        var result = await compiler.BuildAsync(BuildContext.Empty);

        Assert.True(result.Success);
        Assert.Equal("test", result.CompilerId);
    }

    [Fact]
    public async Task FakeCompiler_CleanAsync_ReturnsResult()
    {
        ICompilerService compiler = new FakeCompiler("test", ".nx");

        var result = await compiler.CleanAsync(BuildContext.Empty);

        Assert.True(result.Success);
    }

    [Fact]
    public void FakeCompiler_GetDiagnostics_ReturnsEmpty()
    {
        ICompilerService compiler = new FakeCompiler("test", ".nx");

        Assert.Empty(compiler.GetDiagnostics());
    }

    // --- minimal stub implementing the interface ---

    private sealed class FakeCompiler : ICompilerService
    {
        private readonly string _extension;

        public FakeCompiler(string id, string extension)
        {
            CompilerId = id;
            _extension = extension;
        }

        public string CompilerId { get; }

        public bool SupportsFile(string fileExtension)
            => string.Equals(fileExtension, _extension, StringComparison.OrdinalIgnoreCase);

        public Task<CompilerResult> BuildAsync(BuildContext context)
            => Task.FromResult(new CompilerResult(true, Array.Empty<Diagnostic>(), CompilerId));

        public Task<CompilerResult> CleanAsync(BuildContext context)
            => Task.FromResult(new CompilerResult(true, Array.Empty<Diagnostic>(), CompilerId));

        public IReadOnlyList<Diagnostic> GetDiagnostics() => Array.Empty<Diagnostic>();
    }
}
