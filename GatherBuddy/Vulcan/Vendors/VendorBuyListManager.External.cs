using System.Collections.Generic;
using System.Linq;

namespace GatherBuddy.Vulcan.Vendors;

/// <summary>An NPC offer for an item, for other plugins through IPC.</summary>
/// <param name="Supported">Whether GatherBuddy can buy it automatically.</param>
public sealed record VendorOffer(string CurrencyName, uint CurrencyItemId, uint Cost, List<string> Npcs, bool Supported);

/// <summary>Managing vendor buy lists by name, for other plugins through IPC.</summary>
public sealed partial class VendorBuyListManager
{
    public VendorBuyListDefinition? FindList(string name)
        => GatherBuddy.Config.VendorBuyLists.FirstOrDefault(list => list.Name == name);

    /// <summary>
    /// Creates the list, or replaces its entries: buy each item until the character owns the target (bags and armoury).
    /// Items no supported vendor sells are skipped.
    /// </summary>
    /// <returns>How many items were added, or -1 while vendor data is still loading.</returns>
    public int SetListTargets(string name, IReadOnlyDictionary<uint, uint> targets)
    {
        EnsureListState();
        EnsureVendorCachesAvailable();
        if (!VendorShopResolver.IsInitialized)
            return -1;
        if (IsBusy && FindList(name) is { } running && running.Id == _runningListId)
            return 0;

        var list = FindList(name) ?? CreateList(name, false);
        list.Entries.Clear();
        return TrySetTargets(list, targets.Select(t => new VendorTargetRequest(t.Key, t.Value)).ToList(), false, false, false);
    }

    /// <returns>"Started", or why not (a <see cref="StartResult"/> name, or "NoList").</returns>
    public string StartList(string name)
        => FindList(name) is { } list ? Start(list.Id).ToString() : nameof(StartResult.NoList);

    /// <summary>Every NPC offer for the item (gil, special currencies, the current grand company's seals).</summary>
    public static List<VendorOffer> GetOffers(uint itemId)
    {
        EnsureVendorCachesAvailable();
        return VendorShopResolver.GilShopEntries
            .Concat(VendorShopResolver.SpecialShopEntries)
            .Concat(VendorShopResolver.GcShopEntries.Where(VendorShopResolver.MatchesCurrentGrandCompany))
            .Where(entry => entry.ItemId == itemId)
            .Select(entry => new VendorOffer(entry.CurrencyName, entry.CurrencyItemId, entry.Cost,
                entry.Npcs.Select(npc => npc.Name).Distinct().ToList(),
                entry.Npcs.Any(npc => VendorPurchaseManager.IsPurchaseSupported(entry, npc))))
            .ToList();
    }
}
