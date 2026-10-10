using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

[Trait("Issue", "55")]
public sealed class CompilerSelectorTests
{
    [Fact]
    public void NoCompilers_SelectReturnsNull()
    {
        var selector = new CompilerSelector();

        Assert.Null(selector.Select(".cpp"));
    }

    [Fact]
    public void SingleCompiler_SupportsFile_Selected()
    {
        var selector = new CompilerSelector();
        var gcc = new FakeCompiler("gcc", ".cpp", ".c");
        selector.Register(gcc);

        Assert.Same(gcc, selector.Select(".cpp"));
    }

    [Fact]
    public void MultipleCompilers_SelectByFile()
    {
        var selector = new CompilerSelector();
        var gcc = new FakeCompiler("gcc", ".cpp", ".c");
        var compiler1 = new FakeCompiler("junexis-lang", ".nx");
        selector.Register(gcc);
        selector.Register(compiler1);

        Assert.Same(gcc, selector.Select(".cpp"));
        Assert.Same(compiler1, selector.Select(".nx"));
    }

    [Fact]
    public void ExplicitOverride_WinsOverFileMapping()
    {
        var selector = new CompilerSelector();
        var gcc = new FakeCompiler("gcc", ".cpp");
        var clang = new FakeCompiler("clang", ".cpp");
        selector.Register(gcc);
        selector.Register(clang);

        selector.SetExplicit("clang");

        Assert.Same(clang, selector.Select(".cpp"));
    }

    [Fact]
    public void ExplicitOverride_Null_ResumesAutoSelect()
    {
        var selector = new CompilerSelector();
        var gcc = new FakeCompiler("gcc", ".cpp");
        selector.Register(gcc);

        selector.SetExplicit("clang");
        selector.SetExplicit(null);

        Assert.Same(gcc, selector.Select(".cpp"));
    }

    [Fact]
    public void ExplicitNotFound_FallsBack()
    {
        var output = new OutputService();
        var selector = new CompilerSelector(output);
        var gcc = new FakeCompiler("gcc", ".cpp");
        selector.Register(gcc);

        selector.SetExplicit("nonexistent");

        // Falls back to file-based selection.
        Assert.Same(gcc, selector.Select(".cpp"));
        Assert.Contains(output.GetSnapshot(), e => e.Message.Contains("not found"));
    }

    [Fact]
    public void NoMatchingFile_FallsToDefault()
    {
        var selector = new CompilerSelector();
        var gcc = new FakeCompiler("gcc", ".cpp");
        selector.Register(gcc);

        // ".xyz" not supported by gcc, but gcc is default (first registered).
        Assert.Same(gcc, selector.Select(".xyz"));
    }

    [Fact]
    public void NoCompilers_LogsToOutput()
    {
        var output = new OutputService();
        var selector = new CompilerSelector(output);

        selector.Select(".cpp");

        Assert.Contains(output.GetSnapshot(), e => e.Message.Contains("No compiler available"));
    }

    [Fact]
    public void GetRegistered_ReturnsAll()
    {
        var selector = new CompilerSelector();
        selector.Register(new FakeCompiler("gcc", ".cpp"));
        selector.Register(new FakeCompiler("junexis-lang", ".nx"));

        Assert.Equal(2, selector.GetRegistered().Count);
    }

    // --- stub ---

    private sealed class FakeCompiler : ICompilerService
    {
        private readonly HashSet<string> _extensions;

        public FakeCompiler(string id, params string[] extensions)
        {
            CompilerId = id;
            _extensions = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
        }

        public string CompilerId { get; }

        public bool SupportsFile(string fileExtension) => _extensions.Contains(fileExtension);

        public Task<CompilerResult> BuildAsync(BuildContext context)
            => Task.FromResult(new CompilerResult(true, Array.Empty<Diagnostic>(), CompilerId));

        public Task<CompilerResult> CleanAsync(BuildContext context)
            => Task.FromResult(new CompilerResult(true, Array.Empty<Diagnostic>(), CompilerId));

        public IReadOnlyList<Diagnostic> GetDiagnostics() => Array.Empty<Diagnostic>();
    }
}
