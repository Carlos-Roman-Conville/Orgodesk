using System.Drawing;
using Orgodesk.Core.Fences;
using Orgodesk.Core.Persistence;
using Xunit;

namespace Orgodesk.Core.Tests;

/// <summary>Gap tests for LayoutStore: damaged, empty and huge files, atomic saves, snapshots, normalising.</summary>
public class StoreGapTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orgodesk-gap-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private static Layout OneFence(string id, params string[] items)
    {
        var l = new Layout();
        var f = new Fence { Id = id, Bounds = new Rectangle(10, 10, 300, 200) };
        f.Items.AddRange(items);
        l.ActivePage.Fences.Add(f);
        return l;
    }

    // ---- damaged / odd files ----

    [Fact]
    public void EmptyLayoutFile_IsReportedAsDamaged_AndKept()
    {
        var store = new LayoutStore(_root);
        File.WriteAllText(store.LayoutPath, "");
        var l = store.LoadLayout();
        Assert.Single(l.Pages);
        Assert.Single(store.TakeLoadProblems());
        Assert.Contains(Directory.EnumerateFiles(_root), p => Path.GetFileName(p).StartsWith("layout.json.corrupt-"));
    }

    [Fact]
    public void TruncatedLayoutFile_RestoresLastGoodCopy()
    {
        var store = new LayoutStore(_root);
        store.SaveLayout(OneFence("docs", "a.txt", "b.txt"));
        store.LoadLayout();                                   // clean load refreshes the .bak
        var full = File.ReadAllText(store.LayoutPath);
        File.WriteAllText(store.LayoutPath, full[..(full.Length / 2)]);   // power cut mid-write

        var l = new LayoutStore(_root).LoadLayout();

        Assert.Equal(new[] { "a.txt", "b.txt" }, l.ActivePage.FindFence("docs")!.Items);
    }

    [Fact]
    public void RestoredLayout_IsWrittenBack_SoNextStartIsClean()
    {
        var store = new LayoutStore(_root);
        store.SaveLayout(OneFence("docs", "a.txt"));
        store.LoadLayout();
        File.WriteAllText(store.LayoutPath, "{ nope");
        store.LoadLayout();
        Assert.Single(store.TakeLoadProblems());

        var again = new LayoutStore(_root);
        var l = again.LoadLayout();
        Assert.Empty(again.TakeLoadProblems());
        Assert.Contains("a.txt", l.ActivePage.FindFence("docs")!.Items);
    }

    [Fact]
    public void TakeLoadProblems_ReturnsEachProblemOnce()
    {
        var store = new LayoutStore(_root);
        File.WriteAllText(store.LayoutPath, "{ nope");
        store.LoadLayout();
        Assert.Single(store.TakeLoadProblems());
        Assert.Empty(store.TakeLoadProblems());
    }

    [Fact]
    public void DamagedLayoutAndDamagedBackup_StartsEmpty_WithoutThrowing()
    {
        var store = new LayoutStore(_root);
        File.WriteAllText(store.LayoutPath, "{ nope");
        File.WriteAllText(store.LayoutBackupPath, "also { nope");
        var l = store.LoadLayout();
        Assert.Single(l.Pages);
        Assert.Contains("no backup", Assert.Single(store.TakeLoadProblems()));
    }

    [Theory]
    [InlineData("[1,2,3]")]
    [InlineData("\"hello\"")]
    [InlineData("42")]
    [InlineData("{\"pages\":\"not a list\"}")]
    [InlineData("{\"pages\":[{\"fences\":[{\"bounds\":[1,2,3,4]}]}]}")]
    [InlineData("{\"pages\":[{\"freeItems\":{\"k\":[1,2]}}]}")]
    public void WrongShapedLayoutJson_NeverCrashesTheLoad(string json)
    {
        var store = new LayoutStore(_root);
        File.WriteAllText(store.LayoutPath, json);
        var l = store.LoadLayout();
        Assert.NotEmpty(l.Pages);
    }

    [Theory]
    [InlineData("{\"pages\":[{\"fences\":[{\"bounds\":{\"x\":\"a\",\"y\":1,\"w\":2,\"h\":3}}]}]}")]
    [InlineData("{\"pages\":[{\"fences\":[{\"bounds\":{\"x\":99999999999,\"y\":1,\"w\":2,\"h\":3}}]}]}")]
    [InlineData("{\"pages\":[{\"freeItems\":{\"k\":{\"x\":null,\"y\":1}}}]}")]
    public void BadNumbersInsideRectangleOrPoint_AreTreatedAsDamagedFile(string json)
    {
        var store = new LayoutStore(_root);
        File.WriteAllText(store.LayoutPath, json);
        var l = store.LoadLayout();
        Assert.NotEmpty(l.Pages);
        Assert.Single(store.TakeLoadProblems());
    }

    [Fact]
    public void NullLists_InLayoutJson_AreTreatedAsEmpty()
    {
        var store = new LayoutStore(_root);
        File.WriteAllText(store.LayoutPath, "{\"pages\":null}");
        Assert.NotEmpty(store.LoadLayout().Pages);
    }

    [Fact]
    public void LayoutJsonWithOnlyBraces_LoadsAsDefaultLayout()
    {
        var store = new LayoutStore(_root);
        File.WriteAllText(store.LayoutPath, "{}");
        var l = store.LoadLayout();
        Assert.Single(l.Pages);
        Assert.Empty(store.TakeLoadProblems());
    }

    [Fact]
    public void CommentsAndTrailingCommas_AreTolerated()
    {
        var store = new LayoutStore(_root);
        File.WriteAllText(store.LayoutPath, "{ // hand edited\n \"pages\": [ { \"name\": \"Work\", }, ], }");
        Assert.Equal("Work", store.LoadLayout().Pages[0].Name);
    }

    // ---- normalising ----

    [Fact]
    public void ActivePageIndex_OutOfRange_FallsBackToFirstPage()
    {
        var store = new LayoutStore(_root);
        File.WriteAllText(store.LayoutPath, "{\"activePageIndex\":7,\"pages\":[{\"name\":\"A\"}]}");
        Assert.Equal(0, store.LoadLayout().ActivePageIndex);
        File.WriteAllText(store.LayoutPath, "{\"activePageIndex\":-3,\"pages\":[{\"name\":\"A\"}]}");
        Assert.Equal(0, store.LoadLayout().ActivePageIndex);
    }

    [Fact]
    public void NoPages_GetsOnePage_AndBlankNamesGetNumbered()
    {
        var store = new LayoutStore(_root);
        File.WriteAllText(store.LayoutPath, "{\"pages\":[]}");
        Assert.Single(store.LoadLayout().Pages);

        File.WriteAllText(store.LayoutPath, "{\"pages\":[{\"name\":\"\"},{\"name\":\"  \"},{\"name\":\"Work\"}]}");
        var l = store.LoadLayout();
        Assert.Equal(new[] { "Page 1", "Page 2", "Work" }, l.Pages.Select(p => p.Name));
    }

    [Fact]
    public void DuplicatePresetIds_GetTheirOwnIds_AndKeepTheirContent()
    {
        var store = new LayoutStore(_root);
        var l = new Layout();
        l.Presets.Add(new Page { Id = "same", Name = "One" });
        l.Presets.Add(new Page { Id = "same", Name = "Two" });
        l.Presets.Add(new Page { Id = "same", Name = "Three" });
        store.SaveLayout(l);
        var back = store.LoadLayout();
        Assert.Equal(3, back.Presets.Select(p => p.Id).Distinct().Count());
        Assert.Equal(new[] { "One", "Two", "Three" }, back.Presets.Select(p => p.Name));
    }

    [Fact]
    public void ItemInFenceAndOnAnotherPageAsFreeIcon_IsKeptOnlyOnTheFirstPage()
    {
        var store = new LayoutStore(_root);
        var l = OneFence("a", "C:\\D\\File.TXT");
        var second = new Page();
        second.FreeItems["c:\\d\\file.txt"] = new Point(0, 0);
        l.Pages.Add(second);
        store.SaveLayout(l);
        var back = store.LoadLayout();
        Assert.Single(back.Pages[0].FindFence("a")!.Items);
        Assert.Empty(back.Pages[1].FreeItems);
    }

    [Fact]
    public void ItemListedTwiceInOneFence_IsKeptOnce_InOrder()
    {
        var store = new LayoutStore(_root);
        store.SaveLayout(OneFence("a", "x", "y", "X", "z"));
        Assert.Equal(new[] { "x", "y", "z" }, store.LoadLayout().ActivePage.FindFence("a")!.Items);
    }

    // ---- save atomicity and size ----

    [Fact]
    public void FailedSave_LeavesThePreviousLayoutReadable()
    {
        var store = new LayoutStore(_root);
        store.SaveLayout(OneFence("docs", "a.txt"));
        File.SetAttributes(store.LayoutPath, FileAttributes.ReadOnly);
        try { Assert.ThrowsAny<Exception>(() => store.SaveLayout(OneFence("docs", "other.txt"))); }
        finally { File.SetAttributes(store.LayoutPath, FileAttributes.Normal); }

        var l = new LayoutStore(_root).LoadLayout();
        Assert.Equal(new[] { "a.txt" }, l.ActivePage.FindFence("docs")!.Items);
    }

    [Fact]
    public void StaleTempFileFromACrash_DoesNotBlockSaveOrLoad()
    {
        var store = new LayoutStore(_root);
        File.WriteAllText(store.LayoutPath + ".tmp", "half written garbage");
        store.SaveLayout(OneFence("docs", "a.txt"));
        Assert.False(File.Exists(store.LayoutPath + ".tmp"));
        Assert.Contains("a.txt", store.LoadLayout().ActivePage.FindFence("docs")!.Items);
    }

    [Fact]
    public void SuccessfulSave_LeavesNoTempFile_ForLayoutSettingsAndSnapshot()
    {
        var store = new LayoutStore(_root);
        store.SaveLayout(new Layout());
        store.SaveSettings(new AppSettings());
        store.SaveSnapshot("s", new Layout());
        Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void HugeLayout_RoundTrips_WithEveryItem()
    {
        var store = new LayoutStore(_root);
        var l = new Layout();
        var f = new Fence { Id = "big" };
        for (int i = 0; i < 20000; i++) f.Items.Add($"C:\\Users\\x\\Desktop\\file number {i}.txt");
        l.ActivePage.Fences.Add(f);
        for (int i = 0; i < 5000; i++) l.ActivePage.FreeItems[$"free-{i}"] = new Point(i, i * 2);
        store.SaveLayout(l);
        var back = store.LoadLayout();
        Assert.Equal(20000, back.ActivePage.FindFence("big")!.Items.Count);
        Assert.Equal(5000, back.ActivePage.FreeItems.Count);
        Assert.Equal(new Point(4999, 9998), back.ActivePage.FreeItems["free-4999"]);
    }

    [Fact]
    public void OddItemNames_RoundTrip()
    {
        var store = new LayoutStore(_root);
        var names = new[] { "C:\\d\\caf\u00e9 \u2615 \U0001F600.txt", "C:\\d\\quote\"s & <tag>.txt", "\\\\server\\share\\x", "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", new string('n', 400) };
        store.SaveLayout(OneFence("a", names));
        Assert.Equal(names, store.LoadLayout().ActivePage.FindFence("a")!.Items);
    }

    [Fact]
    public void SaveLayout_ToAMissingDataFolder_RecreatesIt()
    {
        var store = new LayoutStore(_root);
        Directory.Delete(_root, recursive: true);
        store.SaveLayout(OneFence("a", "k"));
        Assert.Contains("k", store.LoadLayout().ActivePage.FindFence("a")!.Items);
    }

    // ---- settings ----

    [Fact]
    public void EmptySettingsFile_IsResetToDefaults_AndReportedOnce()
    {
        var store = new LayoutStore(_root);
        File.WriteAllText(store.SettingsPath, "");
        var s = store.LoadSettings();
        Assert.True(s.StartWithWindows);
        Assert.Single(store.TakeLoadProblems());
        store.LoadSettings();
        Assert.Empty(store.TakeLoadProblems());
    }

    [Fact]
    public void SettingsWithUnknownProperties_AreStillRead()
    {
        var store = new LayoutStore(_root);
        File.WriteAllText(store.SettingsPath, "{\"iconSize\":64,\"fromAFutureVersion\":{\"a\":1}}");
        Assert.Equal(64, store.LoadSettings().IconSize);
        Assert.Empty(store.TakeLoadProblems());
    }

    // ---- snapshots ----

    [Theory]
    [InlineData("..\\..\\evil")]
    [InlineData("../../evil")]
    [InlineData("a:b*c?d")]
    public void SnapshotName_CannotEscapeTheSnapshotsFolder(string name)
    {
        var store = new LayoutStore(_root);
        var info = store.SaveSnapshot(name, OneFence("a", "k"));
        Assert.Equal(Path.GetFullPath(store.SnapshotsDir), Path.GetDirectoryName(Path.GetFullPath(info.Path)));
        Assert.NotNull(store.LoadSnapshot(name));
        Assert.True(store.DeleteSnapshot(name));
    }

    [Fact]
    public void EmptyOrBlankSnapshotName_GetsATimestampName()
    {
        var store = new LayoutStore(_root);
        var info = store.SaveSnapshot("   ", new Layout());
        Assert.NotEmpty(info.Name);
        Assert.True(File.Exists(info.Path));
    }

    [Fact]
    public void SavingASnapshotTwiceWithTheSameName_ReplacesIt()
    {
        var store = new LayoutStore(_root);
        store.SaveSnapshot("day", OneFence("a", "old"));
        store.SaveSnapshot("day", OneFence("a", "new"));
        Assert.Single(store.ListSnapshots());
        Assert.Equal(new[] { "new" }, store.LoadSnapshot("day")!.ActivePage.FindFence("a")!.Items);
    }

    [Fact]
    public void DamagedOrMissingSnapshot_LoadsAsNull_AndDoesNotTouchTheLiveLayout()
    {
        var store = new LayoutStore(_root);
        store.SaveLayout(OneFence("live", "k"));
        File.WriteAllText(Path.Combine(store.SnapshotsDir, "bad.json"), "{ nope");
        Assert.Null(store.LoadSnapshot("bad"));
        Assert.Null(store.LoadSnapshot("never-saved"));
        Assert.False(store.DeleteSnapshot("never-saved"));
        Assert.Contains("k", store.LoadLayout().ActivePage.FindFence("live")!.Items);
    }

    [Fact]
    public void ListSnapshots_NewestFirst()
    {
        var store = new LayoutStore(_root);
        var a = store.SaveSnapshot("a", new Layout());
        var b = store.SaveSnapshot("b", new Layout());
        File.SetLastWriteTime(a.Path, new DateTime(2026, 1, 1));
        File.SetLastWriteTime(b.Path, new DateTime(2026, 6, 1));
        Assert.Equal(new[] { "b", "a" }, store.ListSnapshots().Select(s => s.Name));
    }
}
