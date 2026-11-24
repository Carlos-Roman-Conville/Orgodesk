using System.Security.Cryptography;
using Orgo.Commands;
using Orgodesk.Core.Fences;
using Orgodesk.Core.Persistence;

namespace Orgodesk.Commands;

/// <summary>
/// layout.json shared by the running app and command mode, under orgo-commands' cross-process lock for "orgodesk"
/// (one writer per tool, orgo-commands DESIGN.md rule 7). The app holds the lock from the moment the person changes the
/// layout until it is saved, so a command never writes between an edit and its save; command mode holds it for every
/// apply. If layout.json changed on disk since the app last read or wrote it, the app takes the file's version instead
/// of writing over it (a command's change is never lost to an older copy in memory).
/// Take, hold and release on one thread: a mutex belongs to the thread that took it (the app's UI thread).
/// </summary>
public sealed class SharedLayout
{
    private readonly LayoutStore _store;
    private readonly string? _lock;
    private string _known = "";
    private IDisposable? _held;

    /// <param name="lockName">The lock's tool name ("orgodesk"); tests pass their own. Null: no lock (never in the app).</param>
    public SharedLayout(LayoutStore store, string? lockName)
    {
        _store = store;
        _lock = lockName;
    }

    public LayoutStore Store => _store;

    /// <summary>The lock, or null when another process holds it after <paramref name="wait"/>.</summary>
    public IDisposable? Take(TimeSpan wait) => _lock == null ? new Free() : ToolLock.TryTake(_lock, wait);

    /// <summary>SHA-256 of layout.json as it is on disk ("" when there is none).</summary>
    public string DiskHash()
    {
        try { return File.Exists(_store.LayoutPath) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(_store.LayoutPath))) : ""; }
        catch (IOException) { return "?"; }
    }

    /// <summary>At start: loads under the lock, since loading can write the backup copy and repair a damaged file.
    /// When a command holds the lock past the wait, it loads anyway (the file is replaced whole, never half written).</summary>
    public Layout Load()
    {
        using var held = Take(TimeSpan.FromSeconds(10));
        var layout = _store.LoadLayout();
        _known = DiskHash();
        return layout;
    }

    /// <summary>The person changed the layout: keep the lock until <see cref="Save"/>, so no command writes in between.
    /// Taken without waiting; when a command holds it now, <see cref="Save"/> waits for it instead.</summary>
    public void Hold() => _held ??= Take(TimeSpan.Zero);

    public bool Holding => _held != null;

    public enum Outcome { Saved, Busy, TookDisk }

    /// <summary>
    /// Saves the app's layout, under the lock. <see cref="Outcome.Busy"/>: a command holds the lock; nothing written, try
    /// again soon. <see cref="Outcome.TookDisk"/>: layout.json changed since this app last read or wrote it (a command's
    /// change), so nothing was written and <paramref name="disk"/> is the file's layout, which the app shows instead.
    /// </summary>
    public Outcome Save(Layout layout, out Layout? disk)
    {
        disk = null;
        var held = _held ?? Take(TimeSpan.FromSeconds(2));
        _held = null;
        if (held == null) return Outcome.Busy;
        using (held)
        {
            if (DiskHash() != _known)
            {
                disk = _store.LoadLayout();
                _known = DiskHash();
                return Outcome.TookDisk;
            }
            _store.SaveLayout(layout);
            _known = DiskHash();
            return Outcome.Saved;
        }
    }

    /// <summary>After a command's signal: the layout on disk when it differs from the one this app last saw, else null.</summary>
    public Layout? Changed()
    {
        using var held = Take(TimeSpan.FromSeconds(2));
        if (held == null || DiskHash() == _known) return null;      // busy: the command signals again after its own write
        var disk = _store.LoadLayout();
        _known = DiskHash();
        return disk;
    }

    /// <summary>Runs one of the app's own writes beside layout.json (a snapshot saved or deleted) under the lock. False
    /// when a command held the lock past the wait: nothing was done.</summary>
    public bool Run(Action write, TimeSpan wait)
    {
        using var held = _held != null ? null : Take(wait);
        if (held == null && _held == null) return false;
        write();
        return true;
    }

    /// <summary>Lets go of a hold without saving (the app closing after a failed save).</summary>
    public void Release()
    {
        _held?.Dispose();
        _held = null;
    }

    private sealed class Free : IDisposable { public void Dispose() { } }
}
