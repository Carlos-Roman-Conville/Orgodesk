namespace Orgodesk.Core.Rules;

public enum RuleKind
{
    /// <summary>Pattern is a list of extensions, e.g. "jpg;png;gif" (dots optional).</summary>
    Extension,
    /// <summary>Pattern is a glob on the display name, e.g. "Report*".</summary>
    NamePattern,
    /// <summary>Matches on the item type only.</summary>
    ItemType,
}

public enum ItemType
{
    Any,
    File,
    Folder,
    Shortcut,
    /// <summary>Non-filesystem items: Recycle Bin, This PC, and so on.</summary>
    System,
}

/// <summary>An auto-sort rule: items that match go to the target fence.</summary>
public sealed class Rule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New rule";
    public bool Enabled { get; set; } = true;
    public RuleKind Kind { get; set; } = RuleKind.Extension;
    public string Pattern { get; set; } = "";
    public ItemType ItemType { get; set; } = ItemType.Any;
    public string TargetFenceId { get; set; } = "";

    public Rule Clone() => (Rule)MemberwiseClone();
}

/// <summary>What the rules engine needs to know about one shell item.</summary>
public readonly record struct ItemFacts(
    string Key,
    string Name,
    string? Extension,
    bool IsFolder,
    bool IsShortcut,
    bool IsSystem)
{
    public ItemType Type =>
        IsSystem ? ItemType.System :
        IsShortcut ? ItemType.Shortcut :
        IsFolder ? ItemType.Folder : ItemType.File;
}
