using System.Drawing;
using System.Text.Json.Nodes;
using Orgo.Commands;
using Orgodesk.Core.Fences;
using Orgodesk.Core.Persistence;
using Orgodesk.Core.Rules;

namespace Orgodesk.Commands.Tests;

/// <summary>
/// A generated desktop for one test: a temp data folder with a seeded layout.json, a fake desktop listing, known
/// folders in the temp folder, and a lock name of its own. Never the real %APPDATA% or the real desktop.
/// </summary>
public sealed class Desk : IDisposable
{
    public readonly string Root = Path.Combine(Path.GetTempPath(), "orgodesk-cmd-tests", Guid.NewGuid().ToString("N"));
    public readonly string Lock = "orgodesk-t" + Guid.NewGuid().ToString("N")[..10];
    public string Data => Path.Combine(Root, "data");
    public string DesktopDir => Path.Combine(Root, "Desktop");
    public string Downloads => Path.Combine(Root, "Downloads");
    public readonly LayoutStore Store;
    public readonly List<ItemFacts> Items = new();
    public bool DesktopUnreadable;
    public readonly DeskTool Tool;
    public readonly CommandHost Host;
    public readonly DeskSignals Signals;

    public string Key(string name) => Path.Combine(DesktopDir, name);

    public Desk(Action<Layout>? seed = null, TimeSpan? lockWait = null)
    {
        Directory.CreateDirectory(DesktopDir);
        Directory.CreateDirectory(Path.Combine(Downloads, "Documents"));
        Store = new LayoutStore(Data);
        foreach (var n in new[] { "a.pdf", "b.pdf", "plan.docx", "notes.txt", "Report 2026.xlsx" })
            Items.Add(new ItemFacts(Key(n), n, Path.GetExtension(n), false, false, false));
        var layout = new Layout();
        var page = layout.ActivePage;
        page.Fences.Add(new Fence { Id = "projects", Title = "Projects", Bounds = new Rectangle(400, 100, 300, 240), Items = { Key("plan.docx") } });
        page.Fences.Add(new Fence { Id = "work", Title = "Work", Bounds = new Rectangle(800, 100, 300, 240) });
        int y = 0;
        foreach (var n in new[] { "a.pdf", "b.pdf", "notes.txt", "Report 2026.xlsx" }) page.FreeItems[Key(n)] = new Point(0, y++ * 92);
        layout.Presets.Add(new Page { Id = "gaming", Name = "Gaming", Fences = { new Fence { Id = "games", Title = "Games", Bounds = new Rectangle(100, 500, 300, 240) } } });
        seed?.Invoke(layout);
        Store.SaveLayout(layout);
        Signals = new DeskSignals(Data);
        Tool = new DeskTool(new SharedLayout(Store, Lock), Signals, "1.0.1")
        {
            WorkArea = () => new Rectangle(0, 0, 1920, 1040),
            Screen = () => new Rectangle(-1920, 0, 3840, 1080),   // a second monitor left of the main one
            Desktop = () => DesktopUnreadable ? null : Items.ToList(),
            KnownFolder = n => n.Equals("Downloads", StringComparison.OrdinalIgnoreCase) ? Downloads
                             : n.Equals("Desktop", StringComparison.OrdinalIgnoreCase) ? DesktopDir : null,
            LockWait = lockWait ?? TimeSpan.FromMilliseconds(300),
        };
        Host = new CommandHost(Tool);
    }

    public Layout Layout => Store.PeekLayout(out _)!;

    public JsonNode Ask(string verb, string? action = null, JsonObject? args = null, string? previewId = null)
    {
        var req = new JsonObject { ["protocol"] = 1, ["verb"] = verb };
        if (action != null) req["action"] = action;
        if (args != null) req["args"] = args;
        if (previewId != null) req["previewId"] = previewId;
        return JsonNode.Parse(Host.Handle(req.ToJsonString()))!;
    }

    public static string? Error(JsonNode answer) => answer["error"]?["code"]?.GetValue<string>();
    public static string Message(JsonNode answer) => answer["error"]?["message"]?.GetValue<string>() ?? "";

    /// <summary>Preview, then apply that preview id; returns (preview answer, apply answer).</summary>
    public (JsonNode Preview, JsonNode Apply) Do(string action, JsonObject? args = null)
    {
        var p = Ask("preview", action, args);
        Assert.True(p["ok"]!.GetValue<bool>(), p.ToJsonString());
        return (p, Ask("apply", previewId: Id(p)));
    }

    public static string Id(JsonNode preview) => preview["result"]!["previewId"]!.GetValue<string>();

    /// <summary>Every file in the data folder except the library's own command records, with its bytes.</summary>
    public Dictionary<string, string> Files() => Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
        .Where(f => !f.Contains(Path.DirectorySeparatorChar + "commands" + Path.DirectorySeparatorChar))
        .ToDictionary(f => f, f => Convert.ToHexString(File.ReadAllBytes(f)) + "@" + File.GetLastWriteTimeUtc(f).Ticks);

    public void Dispose()
    {
        try { Directory.Delete(Root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

/// <summary>Holds the shared lock on a thread of its own (a mutex belongs to the thread that took it), as another process would.</summary>
public sealed class LockHolder : IDisposable
{
    private readonly ManualResetEventSlim _taken = new(), _release = new();
    private readonly Thread _t;

    public LockHolder(string lockName)
    {
        _t = new Thread(() =>
        {
            using var l = ToolLock.TryTake(lockName, TimeSpan.FromSeconds(5)) ?? throw new InvalidOperationException("lock not taken");
            _taken.Set();
            _release.Wait();
        }) { IsBackground = true };
        _t.Start();
        Assert.True(_taken.Wait(TimeSpan.FromSeconds(5)));
    }

    public void Dispose()
    {
        _release.Set();
        _t.Join();
    }
}
