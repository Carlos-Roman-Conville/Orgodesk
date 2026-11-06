using System.Drawing;
using Orgodesk.Core.Fences;
using Xunit;

namespace Orgodesk.Core.Tests;

public class PresetsTests
{
    private static Layout Desk()
    {
        var l = new Layout();
        l.ActivePage.Fences.Add(new Fence { Id = "dev", Title = "Dev", Bounds = new Rectangle(0, 0, 300, 200), Items = { "a", "b" } });
        l.ActivePage.FreeItems["c"] = new Point(400, 0);
        return l;
    }

    [Fact]
    public void Save_StoresAnIndependentCopy()
    {
        var l = Desk();
        var p = Presets.Save(l, " Development ");
        Assert.Equal("Development", p.Name);
        Assert.Equal(p.Id, l.ActivePresetId);
        l.ActivePage.Fences[0].Title = "changed";
        l.ActivePage.FreeItems["c"] = new Point(1, 1);
        Assert.Equal("Dev", p.Fences[0].Title);
        Assert.Equal(new Point(400, 0), p.FreeItems["c"]);
    }

    [Fact]
    public void Save_DifferentNamesGetDifferentIds()
    {
        var l = Desk();
        var a = Presets.Save(l, "Test");
        l.ActivePage.Fences[0].Bounds = new Rectangle(10, 10, 300, 200);
        var b = Presets.Save(l, "Test 2");
        Assert.NotEqual(a.Id, b.Id);
        Assert.NotEqual(l.ActivePage.Id, a.Id);
        Assert.True(Presets.Apply(l, b.Id));
        Assert.Equal(new Rectangle(10, 10, 300, 200), l.ActivePage.Fences[0].Bounds);
        Assert.True(Presets.Apply(l, a.Id));
        Assert.Equal(new Rectangle(0, 0, 300, 200), l.ActivePage.Fences[0].Bounds);
    }

    [Fact]
    public void Save_SameNameReplaces()
    {
        var l = Desk();
        var first = Presets.Save(l, "Gaming");
        l.ActivePage.Fences[0].Title = "Games";
        var second = Presets.Save(l, "gaming");
        Assert.Single(l.Presets);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal("Games", l.Presets[0].Fences[0].Title);
    }

    [Fact]
    public void Apply_ReplacesArrangementAndKeepsPresetIntact()
    {
        var l = Desk();
        var p = Presets.Save(l, "Dev");
        l.ActivePage.Fences.Clear();
        l.ActivePage.FreeItems.Clear();
        l.ActivePage.FreeItems["z"] = new Point(9, 9);

        Assert.True(Presets.Apply(l, p.Id));
        Assert.Single(l.ActivePage.Fences);
        Assert.Equal(new[] { "a", "b" }, l.ActivePage.Fences[0].Items);
        Assert.False(l.ActivePage.FreeItems.ContainsKey("z"));
        l.ActivePage.Fences[0].Items.Add("new");
        Assert.Equal(2, p.Fences[0].Items.Count);
        Assert.Equal(p.Id, l.ActivePresetId);
    }

    [Fact]
    public void Update_OverwritesKeepingIdAndName()
    {
        var l = Desk();
        var p = Presets.Save(l, "Dev");
        l.ActivePage.Fences[0].Bounds = new Rectangle(50, 50, 300, 200);
        Assert.True(Presets.Update(l, p.Id));
        Assert.Equal(new Rectangle(50, 50, 300, 200), l.Presets[0].Fences[0].Bounds);
        Assert.Equal("Dev", l.Presets[0].Name);
        Assert.Equal(p.Id, l.Presets[0].Id);
        Assert.False(Presets.Update(l, "missing"));
    }

    [Fact]
    public void Delete_ClearsActiveWhenItWasActive()
    {
        var l = Desk();
        var p = Presets.Save(l, "Dev");
        Assert.True(Presets.Delete(l, p.Id));
        Assert.Empty(l.Presets);
        Assert.Null(l.ActivePresetId);
        Assert.False(Presets.Delete(l, p.Id));
    }
}
