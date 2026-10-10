using Junexis.Studio.Core;
using Junexis.Studio.Host;
using System;
using System.IO;
using Xunit;

namespace Junexis.Studio.Host.Tests;

/// <summary>
/// Testy funkcji wyszukiwania w <see cref="DocumentTabViewModel"/> (#126).
/// </summary>
[Trait("Issue", "126")]
public sealed class DocumentTabFindTests
{
    private static DocumentTabViewModel CreateTab(string content)
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_find_tests_");
        var filePath = Path.Combine(tmp.FullName, "test.txt");
        File.WriteAllText(filePath, content);

        var workspace = new WorkspaceService();
        workspace.OpenFolder(tmp.FullName);
        var bus = new EventBus();
        var output = new OutputService();

        return new DocumentTabViewModel(filePath, content, workspace, bus, output);
    }

    // ─── ToggleSearch ────────────────────────────────────────────────────────

    [Fact]
    public void ToggleSearch_Initially_False()
    {
        var sut = CreateTab("hello");

        Assert.False(sut.IsSearchVisible);
    }

    [Fact]
    public void ToggleSearch_TogglesVisibility_TwiceRestores()
    {
        var sut = CreateTab("hello");

        sut.ToggleSearch();
        Assert.True(sut.IsSearchVisible);

        sut.ToggleSearch();
        Assert.False(sut.IsSearchVisible);
    }

    [Fact]
    public void OpenReplace_ShowsPanel()
    {
        var sut = CreateTab("hello");

        sut.OpenReplace();

        Assert.True(sut.IsSearchVisible);
    }

    [Fact]
    public void OpenReplace_DoesNotHideAlreadyVisiblePanel()
    {
        var sut = CreateTab("hello");
        sut.IsSearchVisible = true;

        sut.OpenReplace();

        Assert.True(sut.IsSearchVisible);
    }

    // ─── SearchQuery / SearchMatchCount ─────────────────────────────────────

    [Fact]
    public void SearchQuery_Default_IsEmpty()
    {
        var sut = CreateTab("hello world");

        Assert.Equal(string.Empty, sut.SearchQuery);
        Assert.Equal(0, sut.SearchMatchCount);
    }

    [Fact]
    public void SearchQuery_SetValue_UpdatesMatchCount()
    {
        var sut = CreateTab("hello world hello");

        sut.SearchQuery = "hello";

        Assert.Equal(2, sut.SearchMatchCount);
    }

    [Fact]
    public void SearchQuery_CaseInsensitive_CountsAll()
    {
        var sut = CreateTab("HELLO hello Hello");

        sut.SearchQuery = "hello";

        Assert.Equal(3, sut.SearchMatchCount);
    }

    [Fact]
    public void SearchMatchCase_True_CountsOnlyExactCase()
    {
        var sut = CreateTab("HELLO hello Hello");

        sut.SearchQuery = "hello";
        sut.SearchMatchCase = true;

        Assert.Equal(1, sut.SearchMatchCount);
    }

    [Fact]
    public void SearchMatchCount_UpdatesWhenContentChanges()
    {
        var sut = CreateTab("hello world");
        sut.SearchQuery = "hello";

        Assert.Equal(1, sut.SearchMatchCount);

        sut.Content = "hello hello world";

        Assert.Equal(2, sut.SearchMatchCount);
    }

    // ─── FindNext ────────────────────────────────────────────────────────────

    [Fact]
    public void FindNext_EmptyQuery_DoesNotNavigate()
    {
        var sut = CreateTab("hello world");
        sut.SearchQuery = string.Empty;

        sut.FindNext();

        Assert.Null(sut.RequestedLine);
    }

    [Fact]
    public void FindNext_FindsFirstOccurrenceFromStart()
    {
        // CaretLine=1, CaretColumn=1 (defaults) → startOffset=1 → skips offset 0
        // but wraps → finds "hello" at offset 0
        var sut = CreateTab("hello world");
        sut.SearchQuery = "hello";

        sut.FindNext();

        // single match → wraps back to offset 0 → (1, 1)
        Assert.Equal(1, sut.RequestedLine);
        Assert.Equal(1, sut.RequestedColumn);
    }

    [Fact]
    public void FindNext_AdvancesPastCurrentCaret()
    {
        // "hello world\nhello again" — caret at line 1 col 1 (at first "hello")
        // FindNext should go to second "hello" (line 2 col 1)
        var sut = CreateTab("hello world\nhello again");
        sut.SearchQuery = "hello";
        // caret starts at (1,1)

        sut.FindNext();

        // advances past offset 0, finds "hello" at offset 12 => line 2, col 1
        Assert.Equal(2, sut.RequestedLine);
        Assert.Equal(1, sut.RequestedColumn);
    }

    [Fact]
    public void FindNext_WrapsAround_WhenPastLastMatch()
    {
        // "aaa bbb aaa" — move caret to start of second "aaa" (1, 9)
        var sut = CreateTab("aaa bbb aaa");
        sut.SearchQuery = "aaa";
        sut.CaretLine = 1;
        sut.CaretColumn = 9; // at second "aaa" (offset 8)

        sut.FindNext();

        // startOffset = 9 → no match after 9 → wrap → finds "aaa" at offset 0 → (1,1)
        Assert.Equal(1, sut.RequestedLine);
        Assert.Equal(1, sut.RequestedColumn);
    }

    [Fact]
    public void FindNext_SetsIsSearchVisible_True()
    {
        var sut = CreateTab("hello");
        sut.SearchQuery = "hello";

        sut.FindNext();

        Assert.True(sut.IsSearchVisible);
    }

    // ─── FindPrev ────────────────────────────────────────────────────────────

    [Fact]
    public void FindPrev_EmptyQuery_DoesNotNavigate()
    {
        var sut = CreateTab("hello world");
        sut.SearchQuery = string.Empty;

        sut.FindPrev();

        Assert.Null(sut.RequestedLine);
    }

    [Fact]
    public void FindPrev_FindsPreviousOccurrence()
    {
        // "hello world\nhello again" — caret at line 2 col 1 (second "hello")
        var sut = CreateTab("hello world\nhello again");
        sut.SearchQuery = "hello";
        sut.CaretLine = 2;
        sut.CaretColumn = 1; // at second "hello" (offset 12)

        sut.FindPrev();

        // caretOffset=12, startFrom=11, LastIndexOf("hello", 11) → 0 → (1,1)
        Assert.Equal(1, sut.RequestedLine);
        Assert.Equal(1, sut.RequestedColumn);
    }

    [Fact]
    public void FindPrev_WrapsAround_WhenAtStart()
    {
        // "aaa bbb aaa" — caret at start (1,1) → startFrom = -1 → wrap from end
        var sut = CreateTab("aaa bbb aaa");
        sut.SearchQuery = "aaa";
        sut.CaretLine = 1;
        sut.CaretColumn = 1; // at first "aaa" (offset 0)

        sut.FindPrev();

        // wrap from end → LastIndexOf("aaa", 10) → offset 8 → (1, 9)
        Assert.Equal(1, sut.RequestedLine);
        Assert.Equal(9, sut.RequestedColumn);
    }

    [Fact]
    public void FindPrev_SetsIsSearchVisible_True()
    {
        var sut = CreateTab("hello");
        sut.SearchQuery = "hello";

        sut.FindPrev();

        Assert.True(sut.IsSearchVisible);
    }

    // ─── OpenSearch ──────────────────────────────────────────────────────────

    [Fact]
    public void OpenSearch_SetsQueryAndShowsPanel()
    {
        var sut = CreateTab("hello world hello");
        var version0 = sut.NavigationRequestVersion;

        sut.OpenSearch("hello");

        Assert.Equal("hello", sut.SearchQuery);
        Assert.True(sut.IsSearchVisible);
        Assert.True(sut.NavigationRequestVersion > version0, "OpenSearch should call FindNext, incrementing NavigationRequestVersion.");
    }

    [Fact]
    public void OpenSearch_EmptyQuery_ShowsPanelButDoesNotNavigate()
    {
        var sut = CreateTab("hello world");
        var version0 = sut.NavigationRequestVersion;

        sut.OpenSearch(string.Empty);

        Assert.True(sut.IsSearchVisible);
        Assert.Equal(version0, sut.NavigationRequestVersion);
    }
}
