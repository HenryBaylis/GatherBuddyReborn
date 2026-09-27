using System;
using System.Collections.Generic;
using System.Linq;
using GatherBuddy.Interfaces;

namespace GatherBuddy.AutoGather.Lists;

/// <summary>Managing auto-gather lists by name, for other plugins through IPC.</summary>
public partial class AutoGatherListsManager
{
    public AutoGatherList? FindList(string name)
        => Lists.FirstOrDefault(l => l.Name == name);

    /// <summary>
    /// Creates the list, or replaces the items of an existing one with the same name, keeping its other settings.
    /// Items that aren't gatherables or fish are skipped. While the list is enabled, items failing GatherBuddy's
    /// perception or bait checks are added disabled, as when added by hand.
    /// </summary>
    /// <returns>How many items were added.</returns>
    public int SetListItems(string name, IReadOnlyDictionary<uint, uint> items, string? description)
    {
        var list = FindList(name);
        var isNew = list == null;
        list ??= new AutoGatherList { Name = name };

        while (list.Items.Count > 0)
            list.RemoveAt(0);
        if (description != null)
            list.Description = description;

        var added = 0;
        foreach (var (itemId, quantity) in items)
        {
            IGatherable? item = GatherBuddy.GameData.Gatherables.TryGetValue(itemId, out var gatherable) ? gatherable
                : GatherBuddy.GameData.Fishes.TryGetValue(itemId, out var fish) ? fish : null;
            if (item == null || !list.Add(item, quantity))
                continue;

            added++;
            if (list.Enabled && (!ValidateSingleFishBait(item) || !ValidateSingleGatherablePerception(item)))
                list.SetEnabled(item, false);
        }

        if (isNew)
        {
            AddList(list);
        }
        else
        {
            Save();
            if (list.Enabled)
                SetActiveItems();
        }

        return added;
    }

    /// <returns>
    /// Whether the list now has the requested state. False if there's no such list, or enabling was refused by
    /// GatherBuddy's perception or bait checks.
    /// </returns>
    public bool SetListEnabled(string name, bool enabled)
    {
        if (FindList(name) is not { } list)
            return false;
        if (list.Enabled != enabled)
            ToggleList(list);
        return list.Enabled == enabled;
    }

    public bool DeleteList(string name)
    {
        if (FindList(name) is not { } list)
            return false;
        DeleteList(list);
        return true;
    }
}
