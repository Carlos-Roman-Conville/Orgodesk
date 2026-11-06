using System.Drawing;
using Orgodesk.Core.Fences;
using Orgodesk.Core.Persistence;
using Orgodesk.Core.Rules;
using Xunit;

namespace Orgodesk.Core.Tests;

public class LayoutStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orgodesk-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static Layout SampleLayout()
    {
        var l = new Layout();
        var f = new Fence
        {
            Id = "docs", Title = "Documents", Bounds = new Rectangle(10, 20, 300, 200),
            Color = "#80FF0000", RolledUp = true, Sort = SortMode.Name,
        };
        f.Items.AddRange(new[] { "C:\\Users\\x\\Desktop\\a.txt", "::{645FF040-5081-101B-9F08-00AA002F954E}" });
        l.ActivePage.Fences.Add(f);
        l.ActivePage.Fences.Add(new Fence { Id = "portal", Title = "Downloads", PortalPath = "C:\\Users\\x\\Downloads" });
        l.ActivePage.FreeItems["C:\\Users\\x\\Desktop\\b.lnk"] = new Point(88, 0);
        l.Pages.Add(new Page { Name = "Work" });
        l.Rules.Add(new Rule { Name = "Text", Kind = RuleKind.Extension, Pattern = "txt;md", TargetFenceId = "docs" });
        return l;
    }

    [Fact]
    public void Layout_RoundTrips()
    {
        var store = new LayoutStore(_root);
        store.SaveLayout(SampleLayout());
        var back = store.LoadLayout();

        Assert.Equal(2, back.Pages.Count);
        var f = back.ActivePage.FindFence("docs")!;
        Assert.Equal("Documents", f.Title);
        Assert.Equal(new Rectangle(10, 20, 300, 200), f.Bounds);
        Assert.Equal("#80FF0000", f.Color);
        Assert.True(f.RolledUp);
        Assert.Equal(SortMode.Name, f.Sort);
        Assert.Equal(2, f.Items.Count);
        Assert.Equal("C:\\Users\\x\\Downloads", back.ActivePage.FindFence("portal")!.PortalPath);
        Assert.Equal(new Point(88, 0), back.ActivePage.FreeItems["C:\\Users\\x\\Desktop\\b.lnk"]);
        Assert.Single(back.Rules);
        Assert.Equal(RuleKind.Extension, back.Rules[0].Kind);
        Assert.Equal("Work", back.Pages[1].Name);
        Assert.False(File.Exists(store.LayoutPath + ".tmp"));
    }

    [Fact]
    public void FreeItems_KeysAreCaseInsensitiveAfterLoad()
    {
        var store = new LayoutStore(_root);
        store.SaveLayout(SampleLayout());
        var back = store.LoadLayout();
        Assert.True(back.ActivePage.FreeItems.ContainsKey("c:\\users\\x\\desktop\\B.LNK"));
    }

    [Fact]
    public void MissingFile_GivesDefaultLayout()
    {
        var store = new LayoutStore(_root);
        var l = store.LoadLayout();
        Assert.Single(l.Pages);
        Assert.Empty(l.ActivePage.Fences);
    }

    [Fact]
    public void CorruptFile_IsBackedUpAndReplaced()
    {
        var store = new LayoutStore(_root);
        File.WriteAllText(store.LayoutPath, "{ this is not json");
        var l = store.LoadLayout();
        Assert.Single(l.Pages);
        Assert.Contains(Directory.EnumerateFiles(_root), p => Path.GetFileName(p).StartsWith("layout.json.corrupt-"));
    }

    [Fact]
    public void DuplicateKeys_KeepFirstOccurrence()
    {
        var store = new LayoutStore(_root);
        var l = new Layout();
        var a = new Fence { Id = "a" }; a.Items.Add("k");
        var b = new Fence { Id = "b" }; b.Items.Add("k");
        l.ActivePage.Fences.AddRange(new[] { a, b });
        l.ActivePage.FreeItems["K"] = new Point(0, 0);
        store.SaveLayout(l);
        var back = store.LoadLayout();
        Assert.Single(back.ActivePage.FindFence("a")!.Items);
        Assert.Empty(back.ActivePage.FindFence("b")!.Items);
        Assert.Empty(back.ActivePage.FreeItems);
    }

    [Fact]
    public void Snapshots_SaveListRestoreDelete()
    {
        var store = new LayoutStore(_root);
        var info = store.SaveSnapshot("Work: before/after", SampleLayout());
        Assert.Equal("Work_ before_after", info.Name);
        Assert.Single(store.ListSnapshots());
        var back = store.LoadSnapshot(info.Name)!;
        Assert.Equal("Documents", back.ActivePage.Fences[0].Title);
        Assert.True(store.DeleteSnapshot(info.Name));
        Assert.Empty(store.ListSnapshots());
        Assert.Null(store.LoadSnapshot("nope"));
    }

    [Fact]
    public void Settings_RoundTrip()
    {
        var store = new LayoutStore(_root);
        var s = new AppSettings { IconSize = 64, FenceColor = "#11223344", StartWithWindows = true };
        store.SaveSettings(s);
        var back = store.LoadSettings();
        Assert.Equal(64, back.IconSize);
        Assert.Equal("#11223344", back.FenceColor);
        Assert.True(back.StartWithWindows);
    }
}
