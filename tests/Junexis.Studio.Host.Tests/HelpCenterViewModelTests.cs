using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class HelpCenterViewModelTests
{
    [Fact]
    public void Constructor_ProvidesTaskOrientedTopicsWithDocumentation()
    {
        var viewModel = new HelpCenterViewModel();

        Assert.Equal(5, viewModel.Topics.Count);
        Assert.Equal("getting-started", viewModel.SelectedTopic.Id);
        Assert.Equal(
            viewModel.Topics.Count,
            viewModel.Topics.Select(topic => topic.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(viewModel.Topics, topic =>
        {
            Assert.False(string.IsNullOrWhiteSpace(topic.Title));
            Assert.False(string.IsNullOrWhiteSpace(topic.Summary));
            Assert.NotEmpty(topic.Steps);
            Assert.NotEmpty(topic.SuccessChecks);
            Assert.StartsWith("docs/", topic.Documentation, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void SelectTopic_ChangesSelectionAndRejectsUnknownTopic()
    {
        var viewModel = new HelpCenterViewModel();
        var changes = 0;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(HelpCenterViewModel.SelectedTopic))
                changes++;
        };

        Assert.True(viewModel.SelectTopic("git-workflow"));
        Assert.Equal("git-workflow", viewModel.SelectedTopic.Id);
        Assert.Equal(1, changes);

        Assert.False(viewModel.SelectTopic("missing"));
        Assert.Equal("git-workflow", viewModel.SelectedTopic.Id);
        Assert.Equal(1, changes);
    }
}
