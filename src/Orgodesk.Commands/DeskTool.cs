using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Orgo.Commands;
using Orgo.FileCore.FileSystem;
using Orgo.FileCore.Journal;
using Orgo.FileCore.Planning;
using Orgodesk.Core.Fences;
using Orgodesk.Core.Persistence;
using Orgodesk.Core.Rules;

namespace Orgodesk.Commands;

/// <summary>
/// What OrgoAI and scripts may ask of Orgodesk (orgoai/PLAN.md Phase 1, slice 4; ISSUES.md FC-55): list its groups,
/// presets, snapshots and rules, and preview changes to the desktop layout. Orgodesk changes no files, so every preview
/// is a change that is not files (orgo-commands OC-10): <see cref="Describe"/> gives the sentences, and after the
/// person's yes <see cref="ApplyOther"/> makes the same change on layout.json, under the shared lock, only if it is still
/// the change that was shown. Each change first keeps the layout as it was as a snapshot, so it can be undone with
/// <c>snapshot_restore</c> (the library's own undo covers file runs only; reported to the controller).
/// </summary>
public sealed class DeskTool : IOrgoTool
{
    public const string ToolName = "orgodesk";
    /// <summary>The name every snapshot kept before a command's change starts with.</summary>
    public const string BeforePrefix = "Before AI change ";
    private const string CanUndo = "The layout as it is now is kept as a snapshot first, so this can be undone.";
    private const int MaxName = 60;

    private readonly SharedLayout _shared;
    private readonly DeskSignals _signals;
    private readonly string _version;
    private readonly string _shown;

    /// <summary>The primary monitor's work area, for placing a new group.</summary>
    public Func<Rectangle> WorkArea { get; init; } = () => new Rectangle(0, 0, 1920, 1040);
    /// <summary>The items on the desktop now, or null when the desktop could not be read.</summary>
    public Func<IReadOnlyList<ItemFacts>?> Desktop { get; init; } = () => null;
    /// <summary>A known folder's path by its English name ("Downloads"), or null.</summary>
    public Func<string, string?> KnownFolder { get; init; } = _ => null;
    public Func<DateTime> Now { get; init; } = () => DateTime.Now;
    /// <summary>How long an apply waits for the app to finish saving a change of its own.</summary>
    public TimeSpan LockWait { get; init; } = TimeSpan.FromSeconds(10);

    public DeskTool(SharedLayout shared, DeskSignals signals, string version)
    {
        _shared = shared;
        _signals = signals;
        _version = version;
        _shown = Path.Combine(shared.Store.RootDir, "commands", "orgodesk-shown");
        // Orgodesk changes no files: the library gets an empty in-memory disk and journal it never runs a plan on, so its
        // own undo and redo find nothing of Orgodesk's to put back.
        var fs = new MemoryFileSystem();
        Files = new ToolServices(fs, new Executor(fs, new MemoryJournal(), @"C:\orgodesk-no-files\undo"), new ProtectedPaths(Array.Empty<string>()), shared.Store.RootDir);
    }

    public ToolServices Files { get; }

    private static ArgInfo Group(string what) => new("group", "string", true, "The group's name, as list groups shows it. " + what) { Example = "Projects" };

    public ToolInfo Info => new(ToolName, _version, new[]
    {
        new ActionInfo("groups", "list", "Lists the groups (boxes) on the desktop with the icons in each.", Array.Empty<ArgInfo>())
        {
            UseWhen = "The person asks which desktop groups or boxes there are, or what is in one.",
            NotFor = "Finding a file anywhere: orgorecall. Files in a folder: orgofolder.",
            Returns = "Each group: name, page, the folder it shows (if any), rolled up or not, its icons.",
            Example = "list(groups)",
        },
        new ActionInfo("presets", "list", "Lists the saved desktop presets (named arrangements of groups and icons).", Array.Empty<ArgInfo>())
        {
            UseWhen = "The person asks which desktop presets or setups they saved.",
            NotFor = "Saved sets of open apps and windows: orgosession. Snapshots: snapshots.",
            Returns = "Each preset: name, whether it is the one in use, how many groups and icons.",
            Example = "list(presets)",
        },
        new ActionInfo("snapshots", "list", "Lists saved desktop layout snapshots, newest first.", Array.Empty<ArgInfo>())
        {
            UseWhen = "The person wants the desktop back as it was, or asks which snapshots exist.",
            NotFor = "Presets to switch between: presets. Windows settings: nothing yet.",
            Returns = "Each snapshot: name and when it was saved. Names starting \"Before AI change\" were kept before a change made through commands.",
            Example = "list(snapshots)",
        },
        new ActionInfo("rules", "list", "Lists the auto-sort rules that put desktop icons into groups.", Array.Empty<ArgInfo>())
        {
            UseWhen = "The person asks how their desktop icons are sorted or which rules exist.",
            NotFor = "Rules that move files between folders: orgofolder. Adding a rule: add_rule.",
            Returns = "Each rule: name, group, what it matches, on or off.",
            Example = "list(rules)",
        },
        new ActionInfo("new_group", "preview", "Makes a new empty group (box) on the desktop.",
            new[] { new ArgInfo("name", "string", true, "The group's title.") { Example = "School" } })
        {
            UseWhen = "The person asks for a new desktop group, box or fence.",
            NotFor = "A box showing a folder: portal. Filling it: add_rule then sort_icons. A new folder: nothing yet.",
            Returns = "A plan with an id: where the empty group goes.",
            Example = "preview(new_group, name=\"School\")",
        },
        new ActionInfo("add_rule", "preview", "Adds an auto-sort rule that puts matching desktop icons into a group.",
            new[]
            {
                new ArgInfo("match", "object", true, "What to match: {\"ext\": [\"pdf\", \"docx\"]} for file types, {\"name\": \"Report*\"} for names (* any text), {\"type\": \"shortcut\"} (file, folder or shortcut).") { Example = "{\"ext\": [\"pdf\"]}" },
                Group("It must exist (new_group makes one)."),
                new ArgInfo("name", "string", false, "The rule's name; made from the group and match when left out."),
            })
        {
            UseWhen = "The person wants certain icons to always go into a group.",
            NotFor = "Moving files into folders: orgofolder. Sorting now: sort_icons after this.",
            Returns = "A plan with an id: the rule in words. Icons move only with sort_icons or as new ones arrive.",
            Example = "preview(add_rule, match={\"ext\": [\"pdf\"]}, group=\"School\")",
        },
        new ActionInfo("sort_icons", "preview", "Sorts the desktop icons into groups by the person's rules now.", Array.Empty<ArgInfo>())
        {
            UseWhen = "The person asks to tidy, clean up or sort desktop icons without moving files.",
            NotFor = "Moving the files into folders: orgofolder. No rules yet: add_rule first.",
            Returns = "A plan with an id: which icons go into which group. Files stay where they are.",
            Example = "preview(sort_icons)",
        },
        new ActionInfo("quick_hide", "preview", "Hides all desktop icons and groups for now, or shows them again.",
            new[] { new ArgInfo("show", "bool", false, "true shows them again; left out hides them.") })
        {
            UseWhen = "The person wants a clean, empty-looking desktop for a while, or the icons back.",
            NotFor = "Removing files: nothing yet. Rolling up one group: roll_up.",
            Returns = "A plan with an id. Nothing moves; double-clicking the desktop also shows them.",
            Example = "preview(quick_hide)",
        },
        new ActionInfo("preset", "preview", "Switches the desktop to a saved preset arrangement.",
            new[] { new ArgInfo("name", "string", true, "The preset's name, as list presets shows it.") { Example = "Gaming" } })
        {
            UseWhen = "The person asks to switch or set the desktop to a named setup.",
            NotFor = "Opening apps and windows: orgosession. Saving one: save_preset. An old state: snapshot_restore.",
            Returns = "A plan with an id: the groups and icons the preset puts back.",
            Example = "preview(preset, name=\"Gaming\")",
        },
        new ActionInfo("save_preset", "preview", "Saves the desktop's current arrangement as a named preset.",
            new[] { new ArgInfo("name", "string", true, "The preset's name; an existing preset of that name is replaced.") { Example = "Office" } })
        {
            UseWhen = "The person wants to keep this desktop arrangement to switch back to later.",
            NotFor = "Saving open apps and windows: orgosession. A one-off backup: snapshot_save.",
            Returns = "A plan with an id: what the preset will hold, or which preset it replaces.",
            Example = "preview(save_preset, name=\"Office\")",
        },
        new ActionInfo("portal", "preview", "Makes a desktop group that shows a folder's contents, or makes a group show one.",
            new[]
            {
                new ArgInfo("folder", "folder", true, "A full path, or a known folder by its English name with an optional subfolder: Downloads, Documents\\Invoices.") { Example = "Downloads" },
                new ArgInfo("group", "string", false, "An existing group to show the folder in; left out makes a new group."),
            })
        {
            UseWhen = "The person wants a box on the desktop showing what is in a folder.",
            NotFor = "Moving files into the folder: orgofolder. An empty group: new_group.",
            Returns = "A plan with an id: the group and the folder it will show. Files stay where they are.",
            Example = "preview(portal, folder=\"Downloads\")",
        },
        new ActionInfo("roll_up", "preview", "Rolls a desktop group up to its title bar, or opens it again.",
            new[] { Group(""), new ArgInfo("open", "bool", false, "true opens it again; left out rolls it up.") })
        {
            UseWhen = "The person asks to collapse, minimise, fold or expand a desktop group.",
            NotFor = "Hiding everything: quick_hide. Removing a group: nothing yet.",
            Returns = "A plan with an id: the group and its new state.",
            Example = "preview(roll_up, group=\"Projects\")",
        },
        new ActionInfo("rename_group", "preview", "Renames a desktop group.",
            new[] { Group("Its title changes; its icons and rules stay."), new ArgInfo("name", "string", true, "The new title; no other group may have it.") { Example = "Work 2026" } })
        {
            UseWhen = "The person wants a desktop group, box or fence to have another name.",
            NotFor = "Renaming a file or folder: nothing yet. A new group: new_group.",
            Returns = "A plan with an id: the old and new title.",
            Example = "preview(rename_group, group=\"Games\", name=\"Play\")",
        },
        new ActionInfo("remove_group", "preview", "Removes a desktop group; its icons go back onto the desktop.",
            new[] { Group("Its icons stay on the desktop; sort rules aimed at it are removed too.") })
        {
            UseWhen = "The person wants a desktop group, box or fence gone.",
            NotFor = "Deleting files or folders: nothing yet. Hiding all icons: quick_hide. Folding it up: roll_up.",
            Returns = "A plan with an id: the icons that go back and the rules that go. No file is touched.",
            Example = "preview(remove_group, group=\"Games\")",
        },
        new ActionInfo("move_icon", "preview", "Moves one desktop icon into a group, or back onto the free desktop.",
            new[]
            {
                new ArgInfo("icon", "string", true, "The icon's name as the desktop shows it: with or without its ending, as list groups shows it.") { Example = "plan.docx" },
                new ArgInfo("to", "string", true, "The group's name, or the word desktop for the free desktop.") { Example = "Projects" },
            })
        {
            UseWhen = "The person wants one icon in a certain group, or out of its group.",
            NotFor = "Moving the file to another folder: orgofolder. Many icons at once: add_rule then sort_icons.",
            Returns = "A plan with an id: the icon, where it is now, where it goes. The file stays where it is.",
            Example = "preview(move_icon, icon=\"plan.docx\", to=\"Projects\")",
        },
        new ActionInfo("snapshot_save", "preview", "Saves a named snapshot of the whole desktop layout.",
            new[] { new ArgInfo("name", "string", true, "The snapshot's name; it must be new.") { Example = "Before" } })
        {
            UseWhen = "The person wants a backup of the desktop arrangement before changing it.",
            NotFor = "A setup to switch between: save_preset. Backing up files: nothing yet.",
            Returns = "A plan with an id: what the snapshot holds. The desktop does not change.",
            Example = "preview(snapshot_save, name=\"Before\")",
        },
        new ActionInfo("snapshot_restore", "preview", "Puts the desktop layout back as a saved snapshot had it; also undoes a change made through commands.",
            new[] { new ArgInfo("name", "string", true, "The snapshot's name, as list snapshots shows it.") { Example = "Monday" } })
        {
            UseWhen = "The person wants the desktop as it was, or to undo a desktop change.",
            NotFor = "Switching setups: preset. Restoring deleted files: nothing yet.",
            Returns = "A plan with an id: the groups it puts back and those that go away.",
            Example = "preview(snapshot_restore, name=\"Monday\")",
        },
    });

    // ---------- list and status ----------

    public JsonNode? Read(string action, JsonObject args)
    {
        var layout = Current();
        return action switch
        {
            "groups" => new JsonArray(layout.Pages.SelectMany(p => p.Fences.Select(f => (JsonNode)GroupJson(layout, p, f))).ToArray()),
            "presets" => new JsonArray(layout.Presets.Select(p => (JsonNode)new JsonObject
            {
                ["name"] = p.Name, ["inUse"] = p.Id == layout.ActivePresetId, ["groups"] = p.Fences.Count, ["icons"] = Icons(p),
            }).ToArray()),
            "snapshots" => new JsonArray(_shared.Store.ListSnapshots().Select(s => (JsonNode)new JsonObject
            {
                ["name"] = s.Name, ["saved"] = s.SavedAt.ToString("yyyy-MM-dd HH:mm"),
            }).ToArray()),
            "rules" => new JsonArray(layout.Rules.Select(r => (JsonNode)new JsonObject
            {
                ["name"] = r.Name, ["group"] = layout.FindFence(r.TargetFenceId)?.Title, ["matches"] = Matches(r), ["on"] = r.Enabled,
            }).ToArray()),
            _ => throw new CommandException(Codes.UnknownAction, "Orgodesk has no list action \"" + action + "\""),
        };
    }

    private static JsonObject GroupJson(Layout layout, Page page, Fence f)
    {
        var o = new JsonObject { ["name"] = f.Title };
        if (layout.Pages.Count > 1) o["page"] = page.Name;
        if (f.IsPortal) o["folder"] = f.PortalPath;
        o["rolledUp"] = f.RolledUp;
        if (!f.IsPortal) o["icons"] = new JsonArray(f.Items.Select(k => (JsonNode)ItemName(k)).ToArray());
        return o;
    }

    public JsonObject Status()
    {
        var layout = _shared.Store.PeekLayout(out bool damaged);
        var o = new JsonObject { ["running"] = _signals.AppRunning(), ["layout"] = damaged ? "damaged" : "ok" };
        if (layout == null) return o;
        o["page"] = layout.ActivePage.Name;
        o["groups"] = layout.AllFences.Count();
        o["presets"] = layout.Presets.Count;
        o["rules"] = layout.Rules.Count;
        o["snapshots"] = _shared.Store.ListSnapshots().Count;
        return o;
    }

    // ---------- preview and apply ----------

    public Plan Preview(string action, JsonObject args) =>
        throw new CommandException(Codes.Failed, "Orgodesk changes no files; its previews are described changes");

    public IReadOnlyList<string>? Describe(string action, JsonObject args)
    {
        var change = Plan(action, args, Current());
        Remember(action, args, change.Says);
        return change.Says;
    }

    /// <summary>Waits for the app to finish saving a change of its own before the preview is used up, so a busy answer
    /// leaves the same preview to apply later.</summary>
    public string? ApplyRefused(string action)
    {
        using var free = _shared.Take(LockWait) ?? throw Busy();
        return null;
    }

    public JsonObject? ApplyOther(string action, JsonObject args)
    {
        using var held = _shared.Take(LockWait) ?? throw Busy();
        var layout = Current();
        var change = Plan(action, args, layout);
        if (!WasShown(action, args, change.Says))
            throw new CommandException(Codes.Refused, "The desktop changed since the preview, so nothing was done; preview again to see the change as it is now");
        var result = new JsonObject();
        if (change.After != null)
        {
            var before = BeforeName();
            Saving(() => _shared.Store.SaveSnapshot(before, layout));
            Saving(() => _shared.Store.SaveLayout(change.After));
            result["undo"] = "preview(snapshot_restore, name=\"" + before + "\")";
            result["shownNow"] = _signals.Send(_signals.Reload);        // false: Orgodesk is not running; it shows the change at its next start
        }
        if (change.SnapshotAs != null)
        {
            Saving(() => _shared.Store.SaveSnapshot(change.SnapshotAs, layout));
            result["snapshot"] = change.SnapshotAs;
        }
        if (change.QuickHide is bool hide)
        {
            if (!_signals.Send(hide ? _signals.Hide : _signals.Show))
                throw new CommandException(Codes.Failed, "Orgodesk closed after the preview, so its icons were not " + (hide ? "hidden" : "shown"));
            result["undo"] = hide ? "preview(quick_hide, show=true)" : "preview(quick_hide)";
        }
        return result;
    }

    /// <summary>A write that cannot be made (read-only file, disk full, antivirus) is a plain refusal: the layout is as it
    /// was (the store writes whole files, never half), and no path or exception text reaches the answer.</summary>
    private static void Saving(Action write)
    {
        try { write(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CommandException(Codes.Failed, "Orgodesk could not save its layout files (is the folder read-only or locked?), so the desktop layout was not changed");
        }
    }

    private static CommandException Busy() =>
        new(Codes.Busy, "Orgodesk is saving a change of its own right now; try again in a moment");

    /// <summary>What a change does, in sentences, and its result: the layout after it, a snapshot to save, or icons to hide.</summary>
    private sealed record Change(IReadOnlyList<string> Says, Layout? After = null, string? SnapshotAs = null, bool? QuickHide = null);

    private Change Plan(string action, JsonObject args, Layout layout) => action switch
    {
        "new_group" => NewGroup(layout, Name(args, "name")),
        "add_rule" => AddRule(layout, (JsonObject)args["match"]!, FindGroup(layout, Str(args, "group")!), Str(args, "name")),
        "sort_icons" => SortIcons(layout),
        "quick_hide" => QuickHide(Bool(args, "show")),
        "preset" => SwitchPreset(layout, Str(args, "name")!),
        "save_preset" => SavePreset(layout, Name(args, "name")),
        "portal" => Portal(layout, Folder(Str(args, "folder")!), Str(args, "group") is string g ? FindGroup(layout, g) : null),
        "roll_up" => RollUp(layout, FindGroup(layout, Str(args, "group")!), Bool(args, "open")),
        "rename_group" => RenameGroup(layout, FindGroup(layout, Str(args, "group")!), Name(args, "name")),
        "remove_group" => RemoveGroup(layout, FindGroup(layout, Str(args, "group")!)),
        "move_icon" => MoveIcon(layout, Str(args, "icon")!, Str(args, "to")!),
        "snapshot_save" => SnapshotSave(layout, Name(args, "name")),
        "snapshot_restore" => SnapshotRestore(layout, Str(args, "name")!),
        _ => throw new CommandException(Codes.UnknownAction, "Orgodesk has no preview action \"" + action + "\""),
    };

    private static Change Layouts(Layout after, params string[] says) => new(says.Append(CanUndo).ToList(), after);

    /// <summary>"desktop" names the free desktop in move_icon, so no group may have that name.</summary>
    private static void NotReserved(string name)
    {
        if (Same(name, FreeDesktop)) throw new CommandException(Codes.BadArgs, "\"" + FreeDesktop + "\" means the free desktop in move_icon, so no group can have that name; pick another");
    }
    private const string FreeDesktop = "desktop";

    private Change RenameGroup(Layout layout, Fence group, string name)
    {
        NotReserved(name);
        if (layout.AllFences.FirstOrDefault(f => f.Id != group.Id && Same(f.Title, name)) is { } taken)
            throw new CommandException(Codes.Refused, "A group called \"" + taken.Title + "\" already exists; pick another name");
        if (group.Title == name) throw new CommandException(Codes.Refused, "The group is already called \"" + name + "\"");
        var after = layout.Clone();
        after.FindFence(group.Id)!.Title = name;
        return Layouts(after, "Renames the group \"" + group.Title + "\" to \"" + name + "\"; its icons and rules stay.");
    }

    private Change RemoveGroup(Layout layout, Fence group)
    {
        var after = layout.Clone();
        var page = after.PageOfFence(group.Id)!;
        LayoutReconciler.ReleaseFence(page, page.FindFence(group.Id)!, WorkArea(), Metrics(after));
        int rules = after.Rules.RemoveAll(r => r.TargetFenceId == group.Id);
        var says = new List<string> { "Removes the group \"" + group.Title + "\"" + (group.IsPortal ? " (it showed " + group.PortalPath + ")" : "") + "." };
        if (group.Items.Count > 0) says.Add("Its " + Count(group.Items.Count, "icon") + " go back onto the desktop: " + Names(group.Items) + ".");
        if (rules > 0) says.Add("Its " + Count(rules, "sort rule") + " go with it.");
        says.Add("No file is deleted or moved.");
        return Layouts(after, says.ToArray());
    }

    private Change MoveIcon(Layout layout, string icon, string to)
    {
        icon = icon.Trim();
        if (icon.Length == 0 || Unsafe(icon)) throw new CommandException(Codes.BadArgs, "icon is the name of an icon on the desktop, such as plan.docx");
        var page = layout.ActivePage;
        var keys = page.FreeItems.Keys.Concat(page.Fences.Where(f => !f.IsPortal).SelectMany(f => f.Items)).Where(k => !k.StartsWith("::", StringComparison.Ordinal)).ToList();
        var hits = keys.Where(k => Same(Path.GetFileName(k), icon)).ToList();
        if (hits.Count == 0) hits = keys.Where(k => Same(Path.GetFileNameWithoutExtension(k), icon)).ToList();
        if (hits.Count == 0) throw new CommandException(Codes.NotFound, "There is no icon called \"" + icon + "\" on the desktop page; list groups shows the icons in groups");
        if (hits.Count > 1) throw new CommandException(Codes.BadArgs, Count(hits.Count, "icon") + " match \"" + icon + "\" (" + Names(hits) + "); give the whole name with its ending");
        string key = hits[0];
        var from = page.FenceContaining(key);
        to = to.Trim();
        var after = layout.Clone();
        var apage = after.ActivePage;
        string name = ItemName(key);
        if (Same(to, FreeDesktop))
        {
            if (from == null) throw new CommandException(Codes.Refused, "\"" + name + "\" is already on the free desktop");
            apage.Remove(key);
            apage.FreeItems[key] = LayoutReconciler.NextFreeSlot(apage, WorkArea(), Metrics(after));
            return Layouts(after, "Takes \"" + name + "\" out of the group \"" + from.Title + "\" and puts it back on the free desktop. The file stays where it is.");
        }
        var target = FindGroup(layout, to);
        if (target.IsPortal) throw new CommandException(Codes.Refused, "The group \"" + target.Title + "\" shows a folder, so icons cannot be put into it");
        if (page.FindFence(target.Id) == null) throw new CommandException(Codes.Refused, "The group \"" + target.Title + "\" is on another page than the icon");
        if (from?.Id == target.Id) throw new CommandException(Codes.Refused, "\"" + name + "\" is already in \"" + target.Title + "\"");
        apage.Remove(key);
        apage.FindFence(target.Id)!.Items.Add(key);
        return Layouts(after, "Moves \"" + name + "\" " + (from == null ? "from the free desktop" : "out of \"" + from.Title + "\"") + " into the group \"" + target.Title + "\". The file stays where it is.");
    }

    private Change NewGroup(Layout layout, string name)
    {
        NotReserved(name);
        if (layout.AllFences.FirstOrDefault(f => Same(f.Title, name)) is { } taken)
            throw new CommandException(Codes.Refused, "A group called \"" + taken.Title + "\" already exists; pick another name, or use it with add_rule or portal");
        var after = layout.Clone();
        var (spot, covers) = FreeSpot(after.ActivePage, after);
        after.ActivePage.Fences.Add(new Fence { Title = name, Bounds = spot });
        return Layouts(after, "Makes a new empty group \"" + name + "\" on the desktop" + PageWords(after) + ", in a free spot."
            + (covers ? " There is no clear spot, so the icons under it move aside." : ""));
    }

    private Change AddRule(Layout layout, JsonObject match, Fence group, string? name)
    {
        if (group.IsPortal)
            throw new CommandException(Codes.Refused, "The group \"" + group.Title + "\" shows a folder, so icons cannot be sorted into it; make a group with new_group");
        foreach (var (key, _) in match)
            if (key is not ("ext" or "name" or "type")) throw new CommandException(Codes.BadArgs, "match takes ext, name and type, not \"" + key + "\"");
        var rule = new Rule { TargetFenceId = group.Id };
        string what;
        var type = Str(match, "type")?.Trim().ToLowerInvariant();
        if (type != null)
        {
            rule.ItemType = type switch
            {
                "file" => ItemType.File, "folder" => ItemType.Folder, "shortcut" => ItemType.Shortcut,
                _ => throw new CommandException(Codes.BadArgs, "match type is file, folder or shortcut, not \"" + type + "\""),
            };
            rule.Kind = RuleKind.ItemType;
        }
        string typeWord = type == null ? "items" : type + "s";
        if (match["ext"] != null)
        {
            if (type != null) throw new CommandException(Codes.BadArgs, "match ext already means files; leave type out");
            var exts = (match["ext"] is JsonArray a ? a.Select(x => x?.ToString() ?? "") : new[] { match["ext"]!.ToString() })
                .Select(e => e.Trim().TrimStart('.').ToLowerInvariant()).Where(e => e.Length > 0).Distinct().ToList();
            if (exts.Count == 0 || exts.Any(e => e.Length > 15 || !e.All(c => char.IsLetterOrDigit(c) || c is '_' or '-')))
                throw new CommandException(Codes.BadArgs, "match ext is a list of file endings such as [\"pdf\", \"docx\"]");
            rule.Kind = RuleKind.Extension;
            rule.Pattern = string.Join(";", exts);
            what = "files ending in " + Or(exts.Select(e => "." + e).ToList());
        }
        else if (Str(match, "name") is string glob)
        {
            glob = glob.Trim();
            if (glob.Length == 0 || glob.Length > 100 || Unsafe(glob)) throw new CommandException(Codes.BadArgs, "match name is a name such as \"Report*\" (* stands for any text)");
            rule.Kind = RuleKind.NamePattern;
            rule.Pattern = glob;
            what = typeWord + " named like \"" + glob + "\"";
        }
        else if (type != null) what = typeWord;
        else throw new CommandException(Codes.BadArgs, "match needs ext, name or type, such as {\"ext\": [\"pdf\"]}");
        if (layout.Rules.Any(r => r.TargetFenceId == group.Id && r.Kind == rule.Kind && r.ItemType == rule.ItemType && Same(r.Pattern, rule.Pattern)))
            throw new CommandException(Codes.Refused, "That rule already exists for \"" + group.Title + "\"; list rules shows it");
        rule.Name = name != null ? CheckName("name", name) : group.Title + ": " + what;
        var after = layout.Clone();
        after.Rules.Add(rule);
        return Layouts(after, "Adds the rule \"" + rule.Name + "\": desktop " + what + " go into the group \"" + group.Title + "\".",
            "Icons already on the desktop move only when they are sorted (sort_icons); new ones go there as they arrive.");
    }

    private Change SortIcons(Layout layout)
    {
        if (!layout.Rules.Any(r => r.Enabled && layout.FindFence(r.TargetFenceId) is { IsPortal: false }))
            throw new CommandException(Codes.NotFound, "There are no sort rules yet, so there is nothing to sort by; add one with add_rule");
        var facts = Desktop();
        bool placed = layout.Pages.Any(p => p.FreeItems.Count > 0 || p.Fences.Any(f => !f.IsPortal && f.Items.Count > 0));
        if (facts == null || facts.Count == 0 && placed)
            throw new CommandException(Codes.Failed, "Orgodesk could not read the desktop, so it cannot tell which icons to sort; try again");
        var after = layout.Clone();
        LayoutReconciler.ApplyRules(after, facts.OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase).ToList());
        var says = new List<string>();
        foreach (var f in after.AllFences.Where(f => !f.IsPortal))
        {
            var was = layout.FindFence(f.Id)?.Items ?? new List<string>();
            var came = f.Items.Where(k => !was.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList();
            if (came.Count > 0) says.Add("Moves " + Count(came.Count, "icon") + " into \"" + f.Title + "\": " + Names(came) + ".");
        }
        if (says.Count == 0) throw new CommandException(Codes.NotFound, "Every icon is already where the rules put it");
        says.Add("Files stay where they are; only the icons move.");
        return Layouts(after, says.ToArray());
    }

    private Change QuickHide(bool show)
    {
        if (!_signals.AppRunning())
            throw new CommandException(Codes.Refused, "Orgodesk is not running, so it has no icons on the desktop to hide or show; start Orgodesk first");
        return new Change(new[]
        {
            show ? "Shows the desktop icons and groups again."
                 : "Hides every icon and group on the desktop until they are shown again (double-click the desktop, or ask). Nothing is moved or deleted.",
        }, QuickHide: !show);
    }

    private Change SwitchPreset(Layout layout, string name)
    {
        var preset = Presets.Find(layout, name.Trim()) ?? throw new CommandException(Codes.NotFound,
            "There is no preset called \"" + name + "\"; " + (layout.Presets.Count == 0 ? "none is saved yet (save_preset saves one)" : "the presets are " + Names(layout.Presets.Select(p => p.Name).ToList())));
        var after = layout.Clone();
        Presets.Apply(after, preset.Id);
        LayoutReconciler.ClampToArea(after, WorkArea(), Metrics(after));
        return Layouts(after, "Switches the desktop" + PageWords(after) + " to the preset \"" + preset.Name + "\": " + Holds(preset) + " in their saved places.",
            "Icons the preset does not know get a free spot.");
    }

    private Change SavePreset(Layout layout, string name)
    {
        var old = Presets.Find(layout, name);
        var after = layout.Clone();
        Presets.Save(after, name);
        return Layouts(after, (old != null ? "Replaces the preset \"" + old.Name + "\" with" : "Saves") + " the desktop as it is now" + PageWords(after)
            + (old != null ? "" : " as the preset \"" + name + "\"") + ": " + Holds(layout.ActivePage) + ". The desktop itself does not change.");
    }

    private Change Portal(Layout layout, string folder, Fence? group)
    {
        var after = layout.Clone();
        if (group == null)
        {
            if (layout.AllFences.FirstOrDefault(f => f.IsPortal && Same(f.PortalPath, folder)) is { } shows)
                throw new CommandException(Codes.Refused, "The group \"" + shows.Title + "\" already shows " + folder);
            string title = Path.GetFileName(folder.TrimEnd('\\'));
            if (title.Length == 0) title = folder;
            for (int n = 2; after.AllFences.Any(f => Same(f.Title, title)); n++) title = Path.GetFileName(folder.TrimEnd('\\')) + " " + n;
            var (spot, covers) = FreeSpot(after.ActivePage, after);
            after.ActivePage.Fences.Add(new Fence { Title = title, Bounds = spot, PortalPath = folder });
            return Layouts(after, "Makes a new group \"" + title + "\" on the desktop" + PageWords(after) + " that shows what is in " + folder + ".",
                "Files stay where they are; the group is a view of the folder." + (covers ? " There is no clear spot, so the icons under it move aside." : ""));
        }
        if (group.IsPortal && Same(group.PortalPath, folder))
            throw new CommandException(Codes.Refused, "The group \"" + group.Title + "\" already shows " + folder);
        var page = after.PageOfFence(group.Id)!;
        var f = page.FindFence(group.Id)!;
        var released = f.Items.ToList();
        f.Items.Clear();
        f.PortalPath = folder;
        var m = Metrics(after);
        foreach (var key in released) page.FreeItems[key] = LayoutReconciler.NextFreeSlot(page, WorkArea(), m);
        int rules = layout.Rules.Count(r => r.TargetFenceId == group.Id);
        return Layouts(after, "Makes the group \"" + group.Title + "\" show what is in " + folder + (group.IsPortal ? " instead of " + group.PortalPath : "") + ".",
            (released.Count > 0 ? "Its " + Count(released.Count, "icon") + " go back onto the desktop: " + Names(released) + ". " : "")
            + (rules > 0 ? "Its " + Count(rules, "sort rule") + " no longer put icons into it. " : "") + "Files stay where they are.");
    }

    private static Change RollUp(Layout layout, Fence group, bool open)
    {
        if (group.RolledUp != open)
            throw new CommandException(Codes.Refused, "The group \"" + group.Title + "\" is already " + (open ? "open" : "rolled up"));
        var after = layout.Clone();
        after.FindFence(group.Id)!.RolledUp = !open;
        return Layouts(after, open ? "Opens the group \"" + group.Title + "\" so its icons show again."
            : "Rolls up the group \"" + group.Title + "\" so only its title bar shows; its icons stay in it.");
    }

    private Change SnapshotSave(Layout layout, string name)
    {
        var safe = LayoutStore.SanitizeName(name);
        if (safe.StartsWith(BeforePrefix.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new CommandException(Codes.BadArgs, "Names starting \"" + BeforePrefix.Trim() + "\" are kept for Orgodesk's own undo snapshots; pick another");
        if (_shared.Store.FindSnapshot(safe) is { } there)
            throw new CommandException(Codes.Refused, "There is already a snapshot called \"" + there.Name + "\"; pick another name (a snapshot is never replaced through commands)");
        return new Change(new[] { "Saves the desktop layout as it is now as the snapshot \"" + safe + "\": " + Holds(layout.ActivePage) + ". The desktop itself does not change." }, SnapshotAs: safe);
    }

    private Change SnapshotRestore(Layout layout, string name)
    {
        var all = _shared.Store.ListSnapshots();
        var info = _shared.Store.FindSnapshot(name.Trim()) ?? throw new CommandException(Codes.NotFound,
            "There is no snapshot called \"" + name + "\"; " + (all.Count == 0 ? "none is saved yet" : "the newest are " + Names(all.Select(s => s.Name).ToList(), 8)));
        var snap = _shared.Store.LoadSnapshot(info.Name)
                   ?? throw new CommandException(Codes.Failed, "The snapshot \"" + info.Name + "\" is damaged, so it cannot be put back; pick another");
        var says = new List<string> { "Puts the desktop back as the snapshot \"" + info.Name + "\" (saved " + info.SavedAt.ToString("yyyy-MM-dd HH:mm") + ") had it: " + Holds(snap.ActivePage) + "." };
        var gone = layout.AllFences.Where(f => !snap.AllFences.Any(s => s.Id == f.Id)).Select(f => f.Title).ToList();
        if (gone.Count > 0) says.Add("Groups not in it go away, and their icons go back onto the desktop: " + Names(gone) + ".");
        if (snap.Rules.Count != layout.Rules.Count || snap.Presets.Count != layout.Presets.Count)
            says.Add("Its " + Count(snap.Rules.Count, "sort rule") + " and " + Count(snap.Presets.Count, "preset") + " replace the " + layout.Rules.Count + " and " + layout.Presets.Count + " there are now.");
        return Layouts(snap, says.ToArray());
    }

    // ---------- helpers ----------

    /// <summary>layout.json as it is now, read without writing anything.</summary>
    private Layout Current() => _shared.Store.PeekLayout(out _) ?? throw new CommandException(Codes.Failed,
        "Orgodesk's layout file (layout.json) is damaged, so nothing can be read or planned; start Orgodesk, which restores it from its backup");

    private Fence FindGroup(Layout layout, string name)
    {
        var hits = layout.AllFences.Where(f => Same(f.Title, name.Trim())).ToList();
        if (hits.Count == 1) return hits[0];
        if (hits.Count > 1)
            throw new CommandException(Codes.BadArgs, Count(hits.Count, "group") + " are called \"" + name + "\"; rename one in Orgodesk first");
        var names = layout.AllFences.Select(f => f.Title).ToList();
        throw new CommandException(Codes.NotFound, "There is no group called \"" + name + "\"; " + (names.Count == 0 ? "there are no groups yet (new_group makes one)" : "the groups are " + Names(names, 20)));
    }

    private string Folder(string text)
    {
        text = text.Trim().Trim('"').Replace('/', '\\');
        if (text.Length == 0 || Unsafe(text)) throw new CommandException(Codes.BadArgs, "folder is a full path or a known folder such as Downloads");
        string full;
        if (Path.IsPathFullyQualified(text)) full = text;
        else
        {
            var parts = text.Split('\\', 2);
            var root = KnownFolder(parts[0]) ?? throw new CommandException(Codes.NotFound,
                "\"" + text + "\" is not a full path or a known folder (Desktop, Documents, Downloads, Music, Pictures, Videos)");
            full = parts.Length > 1 ? Path.Combine(root, parts[1]) : root;
        }
        try { full = Path.GetFullPath(full).TrimEnd('\\'); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { throw new CommandException(Codes.BadArgs, "\"" + text + "\" is not a folder path Windows can use"); }
        if (full.EndsWith(':')) full += "\\";
        if (!Directory.Exists(full)) throw new CommandException(Codes.NotFound, "There is no folder " + full);
        if (KnownFolder("Desktop") is string desk && Same(Path.GetFullPath(desk).TrimEnd('\\'), full.TrimEnd('\\')))
            throw new CommandException(Codes.Refused, "The desktop's own icons are on the desktop already; pick another folder");
        return full;
    }

    /// <summary>A spot for a new group on the page: near the top right, clear of other groups and of icons. When there is
    /// none, the first spot clear of groups (<c>covers</c>: icons there move aside).</summary>
    private (Rectangle Spot, bool Covers) FreeSpot(Page page, Layout layout)
    {
        var area = WorkArea();
        var m = Metrics(layout);
        double k = m.CellWidth / 88.0;
        var size = new Size((int)(300 * k), (int)(240 * k));
        var groups = page.Fences.Select(f => Rectangle.Inflate(f.Bounds, 8, 8)).ToList();
        var icons = page.FreeItems.Values.Select(p => FreeGrid.CellAt(p, m)).ToList();
        int step = Math.Max(8, m.CellWidth / 2);
        Rectangle? fallback = null;
        for (int y = area.Y + 16; y + size.Height <= area.Bottom; y += step)
            for (int x = area.Right - size.Width - 16; x >= area.X; x -= step)
            {
                var r = new Rectangle(new Point(x, y), size);
                if (groups.Any(g => g.IntersectsWith(r))) continue;
                if (!icons.Any(i => i.IntersectsWith(r))) return (r, false);
                fallback ??= r;
            }
        return (fallback ?? new Rectangle(new Point(Math.Max(area.X, area.Right - size.Width - 16), area.Y + 16), size), true);
    }

    private LayoutMetrics Metrics(Layout layout)
    {
        var s = _shared.Store.PeekSettings();
        var m = new LayoutMetrics(s.CellWidth, s.CellHeight, s.IconSize, s.TitleBarHeight, s.FencePadding);
        return layout.GridScale > 0 ? m.Scale(layout.GridScale) : m;
    }

    private string BeforeName()
    {
        string name = BeforePrefix + Now().ToString("yyyy-MM-dd HH-mm-ss");
        for (int n = 2; _shared.Store.FindSnapshot(name) != null; n++) name = BeforePrefix + Now().ToString("yyyy-MM-dd HH-mm-ss") + " (" + n + ")";
        return name;
    }

    // What was shown: one empty file per preview, named by a hash of the action, its arguments and its sentences. Apply
    // carries out a change only when the change it would make now is word for word one that was shown (library rule
    // "apply only what was shown"). Kept a little longer than a preview lives (24 h).
    private string ShownKey(string action, JsonObject args, IReadOnlyList<string> says) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(action + "\n" + args.ToJsonString() + "\n" + string.Join("\n", says))));

    private void Remember(string action, JsonObject args, IReadOnlyList<string> says)
    {
        Directory.CreateDirectory(_shown);
        File.WriteAllText(Path.Combine(_shown, ShownKey(action, args, says)), "");
        foreach (var old in Directory.EnumerateFiles(_shown).Where(f => File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddHours(-25)).ToList())
            try { File.Delete(old); } catch (IOException) { }               // only this folder's own empty marker files
    }

    private bool WasShown(string action, JsonObject args, IReadOnlyList<string> says)
    {
        var path = Path.Combine(_shown, ShownKey(action, args, says));
        return File.Exists(path) && File.GetLastWriteTimeUtc(path) > DateTime.UtcNow.AddHours(-25);
    }

    private static string Name(JsonObject args, string key) => CheckName(key, Str(args, key)!);

    private static string CheckName(string key, string name)
    {
        name = name.Trim();
        if (name.Length == 0) throw new CommandException(Codes.BadArgs, key + " is empty");
        if (name.Length > MaxName) throw new CommandException(Codes.BadArgs, key + " is longer than " + MaxName + " characters");
        if (Unsafe(name)) throw new CommandException(Codes.BadArgs, key + " holds a control or text-direction character, which can make it read as something else");
        return name;
    }

    /// <summary>Control characters and the text-direction marks that make a name read as another one.</summary>
    private static bool Unsafe(string s) => s.Any(c => char.IsControl(c) || c is '\u200E' or '\u200F' or >= '\u202A' and <= '\u202E' or >= '\u2066' and <= '\u2069');

    private static string? Str(JsonObject args, string key) => args[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    private static bool Bool(JsonObject args, string key) => args[key] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
    private static bool Same(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string ItemName(string key) => key.StartsWith("::", StringComparison.Ordinal) ? "a Windows item (" + key + ")" : Path.GetFileName(key.TrimEnd('\\'));
    private static int Icons(Page p) => p.FreeItems.Count + p.Fences.Where(f => !f.IsPortal).Sum(f => f.Items.Count);
    private static string Count(int n, string what) => n + " " + what + (n == 1 ? "" : "s");
    private static string PageWords(Layout l) => l.Pages.Count > 1 ? " (page \"" + l.ActivePage.Name + "\")" : "";

    private static string Holds(Page p) =>
        (p.Fences.Count == 0 ? "no groups" : Count(p.Fences.Count, "group") + " (" + Names(p.Fences.Select(f => f.Title).ToList()) + ")")
        + " and " + Count(p.FreeItems.Count, "loose icon");

    private static string Names(IReadOnlyList<string> keysOrNames, int max = 10)
    {
        var names = keysOrNames.Select(k => k.Contains('\\') || k.StartsWith("::") ? ItemName(k) : k).ToList();
        return string.Join(", ", names.Take(max).Select(n => "\"" + n + "\"")) + (names.Count > max ? " and " + (names.Count - max) + " more" : "");
    }

    private static string Or(IReadOnlyList<string> items) => items.Count == 1 ? items[0] : string.Join(", ", items.Take(items.Count - 1)) + " or " + items[^1];

    private static string Matches(Rule r) => r.Kind switch
    {
        RuleKind.Extension => "files ending in " + string.Join(", ", r.Pattern.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(e => "." + e.TrimStart('.'))),
        RuleKind.NamePattern => (r.ItemType == ItemType.Any ? "items" : r.ItemType.ToString().ToLowerInvariant() + "s") + " named like \"" + r.Pattern + "\"",
        _ => r.ItemType.ToString().ToLowerInvariant() + "s",
    };
}
