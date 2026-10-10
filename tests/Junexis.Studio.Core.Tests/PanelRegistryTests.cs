using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

public sealed class PanelRegistryTests
{
    [Fact]
    [Trait("Issue", "5")]
    public void RegisterToolPanel_WithEmptyId_Throws()
    {
        var sut = new PanelRegistry();
        Assert.Throws<ArgumentException>(() => sut.RegisterToolPanel(" ", () => new object()));
    }

    [Fact]
    [Trait("Issue", "5")]
    public void RegisterDocumentPanel_WithNullFactory_Throws()
    {
        var sut = new PanelRegistry();
        Assert.Throws<ArgumentNullException>(() => sut.RegisterDocumentPanel("doc", null!));
    }

    [Fact]
    [Trait("Issue", "5")]
    public void GetRegisteredPanels_ReturnsSnapshotWithTypes()
    {
        var sut = new PanelRegistry();
        sut.RegisterToolPanel("explorer", () => "tool");
        sut.RegisterDocumentPanel("editor", () => "doc");

        var panels = sut.GetRegisteredPanels();

        Assert.Contains(panels, p => p.Id == "explorer" && p.Type == PanelType.Tool);
        Assert.Contains(panels, p => p.Id == "editor" && p.Type == PanelType.Document);
    }

    // --- #36 ---

    [Fact]
    [Trait("Issue", "36")]
    public void RegisterPanel_WithFullMetadata_ReturnsAllFields()
    {
        var sut = new PanelRegistry();
        sut.RegisterPanel("output", PanelType.Tool, () => "vm", title: "Output", dockGroup: DockGroup.Bottom);

        var panels = sut.GetRegisteredPanels();

        var panel = Assert.Single(panels);
        Assert.Equal("output", panel.Id);
        Assert.Equal(PanelType.Tool, panel.Type);
        Assert.Equal("Output", panel.Title);
        Assert.Equal(DockGroup.Bottom, panel.DockGroup);
    }

    [Fact]
    [Trait("Issue", "36")]
    public void RegisterPanel_Defaults_TitleNullAndDockGroupLeft()
    {
        var sut = new PanelRegistry();
        sut.RegisterPanel("props", PanelType.Document, () => "vm");

        var panel = Assert.Single(sut.GetRegisteredPanels());
        Assert.Null(panel.Title);
        Assert.Equal(DockGroup.Left, panel.DockGroup);
    }

    [Fact]
    [Trait("Issue", "36")]
    public void RegisterPanel_Temporary_ReturnsTemporaryType()
    {
        var sut = new PanelRegistry();
        sut.RegisterPanel("preview", PanelType.Temporary, () => "vm", title: "Preview", dockGroup: DockGroup.Center);

        var panel = Assert.Single(sut.GetRegisteredPanels());
        Assert.Equal(PanelType.Temporary, panel.Type);
        Assert.Equal(DockGroup.Center, panel.DockGroup);
    }

    [Fact]
    [Trait("Issue", "36")]
    public void RegisterToolPanel_Legacy_DefaultsDockGroupAndNullTitle()
    {
        var sut = new PanelRegistry();
        sut.RegisterToolPanel("explorer", () => "tool");

        var panel = Assert.Single(sut.GetRegisteredPanels());
        Assert.Equal(PanelType.Tool, panel.Type);
        Assert.Null(panel.Title);
        Assert.Equal(DockGroup.Left, panel.DockGroup);
    }

    [Fact]
    [Trait("Issue", "36")]
    public void RegisterPanel_WithEmptyId_Throws()
    {
        var sut = new PanelRegistry();
        Assert.Throws<ArgumentException>(() => sut.RegisterPanel("", PanelType.Tool, () => "vm"));
    }
}
