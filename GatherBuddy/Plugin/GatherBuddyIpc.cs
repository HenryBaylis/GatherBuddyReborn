using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using GatherBuddy.Vulcan.Vendors;
using ENpcResident = Lumina.Excel.Sheets.ENpcResident;

namespace GatherBuddy.Plugin;

public sealed class GatherBuddyIpc : IDisposable
{
    public const int IpcVersion = 5;

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

    // Travel: GatherBuddy's vendor navigator taken anywhere (teleport, aethernet and housing via Lifestream, then
    // walking or flying with vnavmesh). Only one navigation runs at a time, shared with vendor purchases and
    // collectable turn-ins, so a travel is refused while one of those is running.

    private VendorNpcLocation? _travelTarget;

    /// <summary>
    /// Travels to a position in a zone: picks the aetheryte, teleports, takes the aethernet, then walks or flies.
    /// With an NPC id, ends in interaction range of that NPC once it's in sight.
    /// </summary>
    /// <param name="npcId">An ENpcResident id, or 0 for a plain position.</param>
    /// <returns>Empty when started, otherwise why not.</returns>
    [EzIPC]
    public string TravelTo(uint territoryId, Vector3 position, uint npcId)
    {
        if (GatherBuddy.AutoGather.Enabled)
            return "auto-gather is on";
        if (GatherBuddy.VendorPurchaseManager.IsRunning || GatherBuddy.VendorBuyListManager.IsBusy
         || GatherBuddy.CollectableManager.IsRunning)
            return "a vendor purchase or collectable turn-in is running";
        if (GatherBuddy.VendorNavigator.IsActive && GatherBuddy.VendorNavigator.CurrentTarget != _travelTarget)
            return "GatherBuddy is already navigating somewhere";

        var name = npcId != 0 && Dalamud.GameData.GetExcelSheet<ENpcResident>().TryGetRow(npcId, out var npc)
            ? npc.Singular.ExtractText()
            : "destination";
        _travelTarget = new VendorNpcLocation(npcId, name, territoryId, 0, position, VendorNpcLocationSource.Override);
        GatherBuddy.VendorNavigator.StartNavigation(_travelTarget);
        return string.Empty;
    }

    /// <summary>
    /// "Idle" (no travel, or it was stopped or taken over), "Teleporting", "WaitingForTeleport", "WaitingForZoneLoad",
    /// "Navigating", "Arrived", or "Failed: reason".
    /// </summary>
    [EzIPC]
    public string GetTravelState()
    {
        var navigator = GatherBuddy.VendorNavigator;
        if (_travelTarget == null || navigator.CurrentTarget != _travelTarget)
            return "Idle";
        if (navigator.IsReadyToPurchase)
            return "Arrived";
        if (navigator.IsFailed)
            return $"Failed: {navigator.FailureReason ?? "unknown"}";
        return navigator.StateName;
    }

    /// <summary>Stops a travel started with <see cref="TravelTo"/>; leaves other navigation alone.</summary>
    [EzIPC]
    public void StopTravel()
    {
        if (_travelTarget != null && GatherBuddy.VendorNavigator.CurrentTarget == _travelTarget)
            GatherBuddy.VendorNavigator.Stop();
        _travelTarget = null;
    }

    // Vendor buy lists by name: buy items from NPC vendors (any currency GatherBuddy supports) until the character owns
    // the target amount, travelling to each vendor.

    /// <summary>Every NPC offer for the item: currency, cost, NPC names, and whether GatherBuddy can buy it automatically.</summary>
    [EzIPC]
    public List<VendorOffer> GetVendorOffers(uint itemId)
        => VendorBuyListManager.GetOffers(itemId);

    /// <summary>
    /// Creates a vendor buy list, or replaces the entries of the one with this name: item id to the amount to own.
    /// </summary>
    /// <returns>How many items were added (unsupported ones are skipped), 0 if that list is running, -1 while vendor data loads.</returns>
    [EzIPC]
    public int SetVendorBuyList(string listName, Dictionary<uint, uint> targets)
        => GatherBuddy.VendorBuyListManager.SetListTargets(listName, targets);

    /// <returns>"Started", or why not: "AlreadyRunning", "AutomationUnavailable" (needs Allagan Tools or Allagan Item Search),
    /// "NoList", "Empty", "NoPendingEntries" (already owned), "VendorDataLoading", "LocationDataLoading", "AnotherPurchaseRunning"…</returns>
    [EzIPC]
    public string StartVendorBuyList(string listName)
    {
        if (GatherBuddy.AutoGather.Enabled)
            return "AutoGatherOn";
        if (GatherBuddy.VendorNavigator.IsActive && GatherBuddy.VendorNavigator.CurrentTarget == _travelTarget)
            return "Travelling";
        return GatherBuddy.VendorBuyListManager.StartList(listName);
    }

    /// <summary>"Running: status" while a buy list runs, otherwise "Idle: last status".</summary>
    [EzIPC]
    public string GetVendorBuyStatus()
        => (GatherBuddy.VendorBuyListManager.IsBusy ? "Running: " : "Idle: ") + GatherBuddy.VendorBuyListManager.StatusText;

    [EzIPC]
    public void StopVendorBuyList()
        => GatherBuddy.VendorBuyListManager.Stop();

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
