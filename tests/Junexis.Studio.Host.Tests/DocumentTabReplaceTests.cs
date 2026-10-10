using Junexis.Studio.Core;
using Junexis.Studio.Host;
using System.IO;
using Xunit;

namespace Junexis.Studio.Host.Tests;

/// <summary>
/// Testy funkcji Replace w <see cref="DocumentTabViewModel"/> (#127).
/// </summary>
[Trait("Issue", "127")]
public sealed class DocumentTabReplaceTests
{
    private static DocumentTabViewModel CreateTab(string content)
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_replace_tests_");
        var filePath = Path.Combine(tmp.FullName, "test.txt");
        File.WriteAllText(filePath, content);

        var workspace = new WorkspaceService();
        workspace.OpenFolder(tmp.FullName);
        var bus = new EventBus();
        var output = new OutputService();

        return new DocumentTabViewModel(filePath, content, workspace, bus, output);
    }

    // ─── ReplaceQuery ────────────────────────────────────────────────────────

    [Fact]
    public void ReplaceQuery_Default_IsEmpty()
    {
        var sut = CreateTab("hello world");
        Assert.Equal(string.Empty, sut.ReplaceQuery);
    }

    [Fact]
    public void ReplaceQuery_SetValue_RaisesPropertyChanged()
    {
        var sut = CreateTab("hello world");
        var changed = false;
        sut.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DocumentTabViewModel.ReplaceQuery))
                changed = true;
        };

        sut.ReplaceQuery = "goodbye";

        Assert.True(changed);
        Assert.Equal("goodbye", sut.ReplaceQuery);
    }

    // ─── ReplaceNext ─────────────────────────────────────────────────────────

    [Fact]
    public void ReplaceNext_EmptyQuery_ReturnsZero_DoesNotChangeContent()
    {
        var sut = CreateTab("hello world");
        sut.SearchQuery = string.Empty;
        sut.ReplaceQuery = "bye";

        var result = sut.ReplaceNext();

        Assert.Equal(0, result);
        Assert.Equal("hello world", sut.Content);
    }

    [Fact]
    public void ReplaceNext_NoMatch_ReturnsZero()
    {
        var sut = CreateTab("hello world");
        sut.SearchQuery = "xyz";
        sut.ReplaceQuery = "abc";

        var result = sut.ReplaceNext();

        Assert.Equal(0, result);
        Assert.Equal("hello world", sut.Content);
    }

    [Fact]
    public void ReplaceNext_SingleMatch_ReplacesOnce()
    {
        var sut = CreateTab("hello world");
        sut.SearchQuery = "world";
        sut.ReplaceQuery = "universe";

        var result = sut.ReplaceNext();

        Assert.Equal(1, result);
        Assert.Equal("hello universe", sut.Content);
    }

    [Fact]
    public void ReplaceNext_TwoMatches_ReplacesBothSequentially()
    {
        var sut = CreateTab("foo foo");
        sut.SearchQuery = "foo";
        sut.ReplaceQuery = "bar";

        sut.ReplaceNext();
        sut.ReplaceNext();

        Assert.Equal("bar bar", sut.Content);
    }

    [Fact]
    public void ReplaceNext_SetsDirty()
    {
        var sut = CreateTab("hello");
        sut.SearchQuery = "hello";
        sut.ReplaceQuery = "hi";

        sut.ReplaceNext();

        Assert.True(sut.IsDirty);
    }

    [Fact]
    public void ReplaceNext_CaseInsensitive_ByDefault()
    {
        var sut = CreateTab("Hello World");
        sut.SearchQuery = "hello";
        sut.ReplaceQuery = "hi";

        var result = sut.ReplaceNext();

        Assert.Equal(1, result);
        Assert.Equal("hi World", sut.Content);
    }

    [Fact]
    public void ReplaceNext_WithMatchCase_RespectsCase()
    {
        var sut = CreateTab("Hello hello");
        sut.SearchQuery = "hello";
        sut.SearchMatchCase = true;
        sut.ReplaceQuery = "hi";

        var result = sut.ReplaceNext();

        Assert.Equal(1, result);
        // tylko lowercase "hello" zostaje zastąpione
        Assert.Equal("Hello hi", sut.Content);
    }

    [Fact]
    public void ReplaceNext_EmptyReplaceQuery_RemovesMatch()
    {
        var sut = CreateTab("hello world");
        sut.SearchQuery = "hello ";
        sut.ReplaceQuery = string.Empty;

        sut.ReplaceNext();

        Assert.Equal("world", sut.Content);
    }

    // ─── ReplaceAll ──────────────────────────────────────────────────────────

    [Fact]
    public void ReplaceAll_EmptyQuery_ReturnsZero_DoesNotChangeContent()
    {
        var sut = CreateTab("hello hello");
        sut.SearchQuery = string.Empty;
        sut.ReplaceQuery = "bye";

        var result = sut.ReplaceAll();

        Assert.Equal(0, result);
        Assert.Equal("hello hello", sut.Content);
    }

    [Fact]
    public void ReplaceAll_NoMatch_ReturnsZero()
    {
        var sut = CreateTab("hello world");
        sut.SearchQuery = "xyz";
        sut.ReplaceQuery = "abc";

        var result = sut.ReplaceAll();

        Assert.Equal(0, result);
        Assert.Equal("hello world", sut.Content);
    }

    [Fact]
    public void ReplaceAll_ReplacesAllOccurrences()
    {
        var sut = CreateTab("foo bar foo baz foo");
        sut.SearchQuery = "foo";
        sut.ReplaceQuery = "qux";

        var result = sut.ReplaceAll();

        Assert.Equal(3, result);
        Assert.Equal("qux bar qux baz qux", sut.Content);
    }

    [Fact]
    public void ReplaceAll_CaseInsensitive_ReplacesAllVariants()
    {
        var sut = CreateTab("FOO foo Foo");
        sut.SearchQuery = "foo";
        sut.ReplaceQuery = "bar";

        var result = sut.ReplaceAll();

        Assert.Equal(3, result);
        Assert.Equal("bar bar bar", sut.Content);
    }

    [Fact]
    public void ReplaceAll_WithMatchCase_ReplacesOnlyExactCase()
    {
        var sut = CreateTab("FOO foo Foo");
        sut.SearchQuery = "foo";
        sut.SearchMatchCase = true;
        sut.ReplaceQuery = "bar";

        var result = sut.ReplaceAll();

        Assert.Equal(1, result);
        Assert.Equal("FOO bar Foo", sut.Content);
    }

    [Fact]
    public void ReplaceAll_SetsDirty_WhenMatchFound()
    {
        var sut = CreateTab("hello hello");
        sut.SearchQuery = "hello";
        sut.ReplaceQuery = "hi";

        sut.ReplaceAll();

        Assert.True(sut.IsDirty);
    }

    [Fact]
    public void ReplaceAll_UpdatesSearchMatchCount_AfterReplace()
    {
        var sut = CreateTab("hello hello");
        sut.SearchQuery = "hello";

        Assert.Equal(2, sut.SearchMatchCount);

        sut.ReplaceQuery = "hi";
        sut.ReplaceAll();

        Assert.Equal(0, sut.SearchMatchCount);
    }
}
