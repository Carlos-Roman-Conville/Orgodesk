using Orgodesk.Core.Rules;

namespace Orgodesk.Windows.Shell;

/// <summary>
/// One item from a shell folder (desktop or portal). Owns its PIDLs.
/// Key is the desktop-absolute parsing name, which is stable across sessions.
/// </summary>
public sealed class ShellItemInfo : IDisposable
{
    public string Key { get; }
    public string DisplayName { get; }
    public string? Path { get; }
    public bool IsFolder { get; }
    public bool IsLink { get; }
    public bool IsFileSystem { get; }
    public bool CanRename { get; }
    public bool CanDelete { get; }
    public DateTime? Modified { get; }

    /// <summary>The folder this item was enumerated from.</summary>
    public IShellFolder Parent { get; }
    /// <summary>PIDL relative to <see cref="Parent"/>.</summary>
    public IntPtr ChildPidl { get; private set; }
    /// <summary>PIDL from the desktop root.</summary>
    public IntPtr AbsolutePidl { get; private set; }

    public bool IsSystem => !IsFileSystem;
    public string? Extension => Path != null && !IsFolder ? System.IO.Path.GetExtension(Path) : null;

    public ShellItemInfo(IShellFolder parent, IntPtr childPidl, IntPtr absolutePidl, string key, string displayName, string? path,
        uint attributes, DateTime? modified)
    {
        Parent = parent;
        ChildPidl = childPidl;
        AbsolutePidl = absolutePidl;
        Key = key;
        DisplayName = displayName;
        Path = path;
        bool stream = (attributes & Const.SFGAO_STREAM) != 0;
        IsLink = (attributes & Const.SFGAO_LINK) != 0;
        IsFolder = (attributes & Const.SFGAO_FOLDER) != 0 && !stream && !IsLink;
        IsFileSystem = (attributes & Const.SFGAO_FILESYSTEM) != 0;
        CanRename = (attributes & Const.SFGAO_CANRENAME) != 0;
        CanDelete = (attributes & Const.SFGAO_CANDELETE) != 0;
        Modified = modified;
    }

    public ItemFacts ToFacts() => new(Key, DisplayName, Extension, IsFolder, IsLink, IsSystem);

    /// <summary>Sort key for "by type": folders first, then by extension, then name.</summary>
    public string TypeSortKey => (IsFolder ? "0" : IsLink ? "1" : "2") + (Extension ?? "") + "\u0001" + DisplayName;

    public void Dispose()
    {
        if (ChildPidl != IntPtr.Zero) { Shell32.ILFree(ChildPidl); ChildPidl = IntPtr.Zero; }
        if (AbsolutePidl != IntPtr.Zero) { Shell32.ILFree(AbsolutePidl); AbsolutePidl = IntPtr.Zero; }
    }

    public override string ToString() => DisplayName;
}
