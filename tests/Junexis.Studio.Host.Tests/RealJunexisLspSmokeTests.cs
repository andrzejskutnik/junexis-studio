using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class RealJunexisLspSmokeTests
{
    [Fact]
    [Trait("Category", "RealLanguageServer")]
    public async Task JunexisServer_InitializeOpenSemanticFeaturesAndClose()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("JUNEXIS_RUN_REAL_LSP_SMOKE"),
                "1",
                StringComparison.Ordinal))
            return;

        var platformRoot = FindPlatformRoot();
        var lspDll = Path.Combine(
            platformRoot,
            "junexis-lang",
            "compiler",
            "JunexisLang.Compiler.LspHost",
            "bin",
            "Debug",
            "net10.0",
            "JunexisLang.Compiler.LspHost.dll");
        Assert.True(File.Exists(lspDll), $"Build the Junexis LSP host first: {lspDll}");

        var sourcePath = Path.Combine(platformRoot, "junexis-lang", "examples", "01-temperature-monitor.jnx");
        var source = await File.ReadAllTextAsync(sourcePath);
        var taskOffset = source.IndexOf("task", StringComparison.Ordinal);
        Assert.True(taskOffset >= 0, "Smoke source must contain the 'task' keyword.");
        var (line, character) = ToPosition(source, taskOffset + 1);

        var eventBus = new EventBus();
        using var workspace = new WorkspaceService();
        workspace.OpenFolder(platformRoot);
        var output = new OutputService();
        var diagnostics = new DiagnosticService();
        var profile = new LanguageServerProfile(
            "junexis",
            "Junexis Language Server",
            "junexis",
            [".jnx"],
            "dotnet",
            [lspDll]);
        await using var service = new LanguageService(
            eventBus,
            workspace,
            diagnostics,
            output,
            [profile]);
        var ready = new TaskCompletionSource<LanguageServerStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.StatusChanged += status =>
        {
            if (status.State is LanguageServerState.Running or LanguageServerState.Failed)
                ready.TrySetResult(status);
        };

        eventBus.Publish(new DocumentOpened(sourcePath, source));
        var started = await Task.WhenAny(ready.Task, Task.Delay(TimeSpan.FromSeconds(15)));
        Assert.True(
            ReferenceEquals(started, ready.Task),
            "LSP did not become ready. Output:" + Environment.NewLine
            + string.Join(Environment.NewLine, output.GetSnapshot().Select(entry => entry.Message)));
        Assert.Equal(LanguageServerState.Running, (await ready.Task).State);

        LanguageHover? hover = null;
        for (var attempt = 0; attempt < 20 && hover is null; attempt++)
        {
            await Task.Delay(50);
            hover = await service.HoverAsync(sourcePath, new LanguagePosition(line, character));
        }
        Assert.NotNull(hover);
        Assert.Contains("task", hover.Content, StringComparison.OrdinalIgnoreCase);

        LanguageReferencesPublished? publishedReferences = null;
        service.ReferencesPublished += result => publishedReferences = result;
        var references = await service.ReferencesAsync(
            sourcePath,
            new LanguagePosition(line, character));
        Assert.NotEmpty(references);
        Assert.Contains(references, location =>
            location.Path.Equals(sourcePath, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(publishedReferences);
        Assert.Equal("task", publishedReferences.Symbol);
        Assert.Equal(references.Count, publishedReferences.Locations.Count);

        var semanticTokens = await service.SemanticTokensAsync(sourcePath);
        Assert.NotEmpty(semanticTokens);
        Assert.Contains(semanticTokens, token =>
            token.Type.Equals("keyword", StringComparison.OrdinalIgnoreCase));

        var refactorPath = Path.Combine(Path.GetTempPath(), "junexis-studio-lsp-refactor.jnx");
        const string refactorSource =
            "task sample() {\n"
            + "    let sensorValue = true && false\n"
            + "    return sensorValue\n"
            + "}\n";
        var replacements = new List<ReplaceOpenDocumentContentRequested>();
        eventBus.Subscribe<ReplaceOpenDocumentContentRequested>(replacements.Add);
        eventBus.Publish(new DocumentOpened(refactorPath, refactorSource));
        await Task.Delay(100);

        var renameCount = await service.RenameAsync(
            refactorPath,
            new LanguagePosition(1, 10),
            "isActive");
        Assert.Equal(2, renameCount);
        Assert.Contains(replacements, replacement =>
            replacement.Path.Equals(refactorPath, StringComparison.OrdinalIgnoreCase)
            && replacement.Content.Contains("let isActive", StringComparison.Ordinal)
            && replacement.Content.Contains("return isActive", StringComparison.Ordinal));

        var codeActions = await service.CodeActionsAsync(
            refactorPath,
            new LanguagePosition(1, 28));
        var narrativeAction = Assert.Single(codeActions);
        Assert.Contains("'and'", narrativeAction.Title, StringComparison.Ordinal);
        var actionCount = await service.ApplyCodeActionAsync(narrativeAction);
        Assert.Equal(1, actionCount);
        Assert.Contains(replacements, replacement =>
            replacement.Path.Equals(refactorPath, StringComparison.OrdinalIgnoreCase)
            && replacement.Content.Contains("true and false", StringComparison.Ordinal));

        eventBus.Publish(new DocumentClosed(refactorPath));
        eventBus.Publish(new DocumentClosed(sourcePath));
    }

    private static string FindPlatformRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "junexis-lang"))
                && Directory.Exists(Path.Combine(current.FullName, "junexis-studio")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate Junexis-platform from the test output.");
    }

    private static (int Line, int Character) ToPosition(string source, int offset)
    {
        var line = 0;
        var character = 0;
        for (var index = 0; index < offset; index++)
        {
            if (source[index] == '\n')
            {
                line++;
                character = 0;
            }
            else if (source[index] != '\r')
            {
                character++;
            }
        }
        return (line, character);
    }
}
