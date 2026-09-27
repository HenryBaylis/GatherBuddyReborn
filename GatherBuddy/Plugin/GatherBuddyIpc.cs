using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace GatherBuddy.Plugin;

public sealed class GatherBuddyIpc : IDisposable
{
    public const int IpcVersion = 3;

    private readonly GatherBuddy _plugin;

    public GatherBuddyIpc(GatherBuddy plugin)
    {
        _plugin = plugin;
        EzIPC.Init(this, GatherBuddy.InternalName);
        Debug.Assert(AutoGatherWaiting != null);
        Debug.Assert(AutoGatherEnabledChanged != null);
    }

#pragma warning disable CA1822 // Mark members as static
    [EzIPC]
    public int Version()
        => IpcVersion;

    [EzIPC]
    public uint Identify(string text)
        => _plugin.Executor.Identificator.IdentifyGatherable(text)?.ItemId
         ?? _plugin.Executor.Identificator.IdentifyFish(text)?.ItemId ?? 0;

    [EzIPC]
    public bool IsAutoGatherEnabled()
        => GatherBuddy.AutoGather.Enabled;

    [EzIPC]
    public string GetAutoGatherStatusText()
        => GatherBuddy.AutoGather.AutoStatus;

    [EzIPC]
    public void SetAutoGatherEnabled(bool enabled)
        => GatherBuddy.AutoGather.Enabled = enabled;

    [EzIPC]
    public bool IsAutoGatherWaiting()
        => GatherBuddy.AutoGather.Waiting;

    /// <summary>Auto-gather lists by name, and whether each is enabled.</summary>
    [EzIPC]
    public Dictionary<string, bool> GetAutoGatherLists()
        => _plugin.AutoGatherListsManager.Lists
            .GroupBy(l => l.Name)
            .ToDictionary(g => g.Key, g => g.First().Enabled);

    /// <summary>Item ids and quantities in a list; empty if there's no such list.</summary>
    [EzIPC]
    public Dictionary<uint, uint> GetAutoGatherListItems(string listName)
        => _plugin.AutoGatherListsManager.FindList(listName)?.Quantities.ToDictionary(q => q.Key.ItemId, q => q.Value)
         ?? [];

    /// <summary>
    /// Creates a list, or replaces the items of the list with this name (keeping its other settings). New lists
    /// start disabled. Unknown item ids are skipped.
    /// </summary>
    /// <param name="items">Item id to quantity.</param>
    /// <param name="description">Null keeps an existing list's description.</param>
    /// <returns>How many items were added.</returns>
    [EzIPC]
    public int SetAutoGatherList(string listName, Dictionary<uint, uint> items, string? description)
        => _plugin.AutoGatherListsManager.SetListItems(listName, items, description);

    /// <returns>Whether the list now has that state; false if not found or enabling failed GatherBuddy's checks.</returns>
    [EzIPC]
    public bool SetAutoGatherListEnabled(string listName, bool enabled)
        => _plugin.AutoGatherListsManager.SetListEnabled(listName, enabled);

    /// <returns>False if there's no such list.</returns>
    [EzIPC]
    public bool DeleteAutoGatherList(string listName)
        => _plugin.AutoGatherListsManager.DeleteList(listName);

    [EzIPCEvent]
    public Action AutoGatherWaiting;

    [EzIPCEvent]
    public Action<bool> AutoGatherEnabledChanged;

#pragma warning restore CA1822 // Mark members as static

    public void Dispose()
    {
        // EzIPC disposal is handled in GatherBuddy.cs Dispose method
    }
}
