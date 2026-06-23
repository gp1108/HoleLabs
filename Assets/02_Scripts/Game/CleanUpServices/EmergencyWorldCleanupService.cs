using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Provides explicit emergency cleanup actions for physics-heavy runtime objects.
/// This service is designed for menu buttons and debug recovery, not for automatic normal gameplay deletion.
/// </summary>
[DisallowMultipleComponent]
public sealed class EmergencyWorldCleanupService : MonoBehaviour
{
    private enum OreCleanupSortMode
    {
        LowestCreditValueFirst = 0,
        FarthestFromReferenceFirst = 1,
        LowestValueThenFarthest = 2
    }

    private enum PickupDisposalMode
    {
        ReturnToPoolWhenPossible = 0,
        DestroyRuntimeRoot = 1
    }

    private enum MoneyCleanupMode
    {
        CollectIntoWallet = 0,
        RemoveWithoutGrantingCredits = 1
    }

    [Header("References")]
    [Tooltip("Registry used to read active world objects. If empty, the first registry in the scene is used.")]
    [SerializeField] private RuntimeWorldObjectRegistry Registry;

    [Tooltip("Wallet credited when loose money is collected through emergency cleanup.")]
    [SerializeField] private CurrencyWallet CurrencyWallet;

    [Tooltip("Reference transform used by distance-based cleanup sorting and preserve radius checks. Usually the player.")]
    [SerializeField] private Transform ReferenceTransform;

    [Header("Ore Cleanup")]
    [Tooltip("Number of ores removed by the standard cleanup button.")]
    [SerializeField] private int OreCleanupBatchSize = 100;

    [Tooltip("Number of ores removed by the large cleanup button.")]
    [SerializeField] private int LargeOreCleanupBatchSize = 500;

    [Tooltip("Ordering used when selecting ores to remove first.")]
    [SerializeField] private OreCleanupSortMode OreSortMode = OreCleanupSortMode.LowestValueThenFarthest;

    [Tooltip("How active ore pickups are disposed during emergency cleanup.")]
    [SerializeField] private PickupDisposalMode OreDisposalMode = PickupDisposalMode.DestroyRuntimeRoot;

    [Tooltip("If true, ores close to the reference transform are protected from emergency cleanup.")]
    [SerializeField] private bool PreserveOresNearReference = false;

    [Tooltip("Ores within this radius around the reference transform are preserved when Preserve Ores Near Reference is enabled.")]
    [SerializeField] private float PreserveOreRadius = 5f;

    [Header("Money Cleanup")]
    [Tooltip("Number of money pickups processed by the standard money cleanup button.")]
    [SerializeField] private int MoneyCleanupBatchSize = 200;

    [Tooltip("How loose money is processed during emergency cleanup.")]
    [SerializeField] private MoneyCleanupMode LooseMoneyCleanupMode = MoneyCleanupMode.CollectIntoWallet;

    [Tooltip("How money pickups are disposed after being processed.")]
    [SerializeField] private PickupDisposalMode MoneyDisposalMode = PickupDisposalMode.ReturnToPoolWhenPossible;

    [Header("Runtime World Item Cleanup")]
    [Tooltip("If true, explicit runtime world item cleanup buttons are allowed to destroy non-scene world items.")]
    [SerializeField] private bool AllowRuntimeWorldItemCleanup = false;

    [Tooltip("Number of runtime world items removed by the standard cleanup button.")]
    [SerializeField] private int RuntimeWorldItemCleanupBatchSize = 25;

    [Tooltip("If true, scene-placed persistent world items are never destroyed by emergency cleanup.")]
    [SerializeField] private bool PreserveScenePlacedWorldItems = true;

    [Header("Debug")]
    [Tooltip("Logs cleanup results to the console.")]
    [SerializeField] private bool DebugLogs = true;

    private readonly List<OrePickup> OreBuffer = new();
    private readonly List<MoneyPickup> MoneyBuffer = new();
    private readonly List<WorldItem> WorldItemBuffer = new();

    /// <summary>
    /// Resolves missing service references.
    /// </summary>
    private void Awake()
    {
        ResolveReferences();
    }

    /// <summary>
    /// Rebuilds registry data and logs current active object counts.
    /// </summary>
    [ContextMenu("Debug Runtime Object Counts")]
    public void DebugRuntimeObjectCounts()
    {
        RuntimeWorldObjectRegistry ActiveRegistry = GetRegistry();

        if (ActiveRegistry == null)
        {
            Debug.LogWarning("[EmergencyWorldCleanupService] Missing RuntimeWorldObjectRegistry.", this);
            return;
        }

        ActiveRegistry.RebuildFromScene();
        ActiveRegistry.DebugCounts();
    }

    /// <summary>
    /// Removes the configured ore batch using the selected priority mode.
    /// Intended for a normal emergency menu button.
    /// </summary>
    public void CleanConfiguredOreBatch()
    {
        int RemovedCount = CleanOres(Mathf.Max(0, OreCleanupBatchSize));
        Log("Cleaned configured ore batch. Removed: " + RemovedCount);
    }

    /// <summary>
    /// Removes a larger ore batch using the selected priority mode.
    /// Intended for a stronger emergency menu button.
    /// </summary>
    public void CleanLargeOreBatch()
    {
        int RemovedCount = CleanOres(Mathf.Max(0, LargeOreCleanupBatchSize));
        Log("Cleaned large ore batch. Removed: " + RemovedCount);
    }

    /// <summary>
    /// Removes every currently tracked ore pickup that is not protected by the preserve radius.
    /// Use only for hard emergency recovery.
    /// </summary>
    public void CleanAllEligibleOres()
    {
        int RemovedCount = CleanOres(int.MaxValue);
        Log("Cleaned all eligible ores. Removed: " + RemovedCount);
    }

    /// <summary>
    /// Processes the configured money pickup batch.
    /// When configured to collect into wallet, this grants credits before removing the pickups.
    /// </summary>
    public void CleanConfiguredMoneyBatch()
    {
        int RemovedCount = CleanMoney(Mathf.Max(0, MoneyCleanupBatchSize));
        Log("Cleaned configured money batch. Processed: " + RemovedCount);
    }

    /// <summary>
    /// Processes every currently tracked money pickup.
    /// </summary>
    public void CleanAllMoneyPickups()
    {
        int RemovedCount = CleanMoney(int.MaxValue);
        Log("Cleaned all money pickups. Processed: " + RemovedCount);
    }

    /// <summary>
    /// Removes a configured number of runtime world items when runtime item cleanup is enabled.
    /// Scene-placed persistent items are preserved by default.
    /// </summary>
    public void CleanConfiguredRuntimeWorldItems()
    {
        int RemovedCount = CleanRuntimeWorldItems(Mathf.Max(0, RuntimeWorldItemCleanupBatchSize));
        Log("Cleaned configured runtime world item batch. Removed: " + RemovedCount);
    }

    /// <summary>
    /// Performs a broad emergency cleanup using the configured ore, money and runtime item settings.
    /// </summary>
    public void RunConfiguredEmergencyCleanup()
    {
        int RemovedOres = CleanOres(Mathf.Max(0, OreCleanupBatchSize));
        int ProcessedMoney = CleanMoney(Mathf.Max(0, MoneyCleanupBatchSize));
        int RemovedWorldItems = CleanRuntimeWorldItems(Mathf.Max(0, RuntimeWorldItemCleanupBatchSize));

        Log("Emergency cleanup completed. Ores: " + RemovedOres + " | Money: " + ProcessedMoney + " | Runtime World Items: " + RemovedWorldItems);
    }

    /// <summary>
    /// Removes up to MaxCount eligible ore pickups from the world.
    /// </summary>
    /// <param name="MaxCount">Maximum number of ores to remove.</param>
    /// <returns>Removed ore count.</returns>
    public int CleanOres(int MaxCount)
    {
        if (MaxCount <= 0)
        {
            return 0;
        }

        RuntimeWorldObjectRegistry ActiveRegistry = GetRegistry();

        if (ActiveRegistry == null)
        {
            return 0;
        }

        ActiveRegistry.CopyActiveOrePickups(OreBuffer);
        RemoveProtectedOres(OreBuffer);
        SortOres(OreBuffer);

        int RemovedCount = 0;
        int CountToRemove = Mathf.Min(MaxCount, OreBuffer.Count);

        for (int Index = 0; Index < CountToRemove; Index++)
        {
            OrePickup Pickup = OreBuffer[Index];

            if (Pickup == null)
            {
                continue;
            }

            DisposeOrePickup(Pickup);
            RemovedCount++;
        }

        OreBuffer.Clear();
        return RemovedCount;
    }

    /// <summary>
    /// Processes up to MaxCount active money pickups.
    /// </summary>
    /// <param name="MaxCount">Maximum number of money pickups to process.</param>
    /// <returns>Processed money pickup count.</returns>
    public int CleanMoney(int MaxCount)
    {
        if (MaxCount <= 0)
        {
            return 0;
        }

        RuntimeWorldObjectRegistry ActiveRegistry = GetRegistry();

        if (ActiveRegistry == null)
        {
            return 0;
        }

        ResolveReferences();
        ActiveRegistry.CopyActiveMoneyPickups(MoneyBuffer);

        int RemovedCount = 0;
        int CountToRemove = Mathf.Min(MaxCount, MoneyBuffer.Count);

        for (int Index = 0; Index < CountToRemove; Index++)
        {
            MoneyPickup Pickup = MoneyBuffer[Index];

            if (Pickup == null)
            {
                continue;
            }

            if (LooseMoneyCleanupMode == MoneyCleanupMode.CollectIntoWallet && CurrencyWallet != null)
            {
                CurrencyWallet.AddCurrency(Pickup.GetCurrencyType(), Pickup.GetAmount());
            }

            DisposeMoneyPickup(Pickup);
            RemovedCount++;
        }

        MoneyBuffer.Clear();
        return RemovedCount;
    }

    /// <summary>
    /// Removes up to MaxCount runtime world items when runtime world item cleanup is enabled.
    /// </summary>
    /// <param name="MaxCount">Maximum number of runtime world items to remove.</param>
    /// <returns>Removed world item count.</returns>
    public int CleanRuntimeWorldItems(int MaxCount)
    {
        if (!AllowRuntimeWorldItemCleanup || MaxCount <= 0)
        {
            return 0;
        }

        RuntimeWorldObjectRegistry ActiveRegistry = GetRegistry();

        if (ActiveRegistry == null)
        {
            return 0;
        }

        ActiveRegistry.CopyActiveWorldItems(WorldItemBuffer);

        int RemovedCount = 0;

        for (int Index = 0; Index < WorldItemBuffer.Count && RemovedCount < MaxCount; Index++)
        {
            WorldItem Item = WorldItemBuffer[Index];

            if (Item == null)
            {
                continue;
            }

            if (PreserveScenePlacedWorldItems && ActiveRegistry.IsScenePlacedWorldItem(Item))
            {
                continue;
            }

            DisposeWorldItem(Item);
            RemovedCount++;
        }

        WorldItemBuffer.Clear();
        return RemovedCount;
    }

    /// <summary>
    /// Removes protected ores from the candidate buffer.
    /// </summary>
    /// <param name="Candidates">Ore candidate list.</param>
    private void RemoveProtectedOres(List<OrePickup> Candidates)
    {
        if (!PreserveOresNearReference || ReferenceTransform == null || PreserveOreRadius <= 0f)
        {
            return;
        }

        float SqrRadius = PreserveOreRadius * PreserveOreRadius;

        for (int Index = Candidates.Count - 1; Index >= 0; Index--)
        {
            OrePickup Pickup = Candidates[Index];

            if (Pickup == null)
            {
                Candidates.RemoveAt(Index);
                continue;
            }

            Vector3 Delta = Pickup.GetRuntimeRoot().position - ReferenceTransform.position;

            if (Delta.sqrMagnitude <= SqrRadius)
            {
                Candidates.RemoveAt(Index);
            }
        }
    }

    /// <summary>
    /// Sorts ore candidates according to the configured emergency priority.
    /// </summary>
    /// <param name="Candidates">Ore candidates to sort.</param>
    private void SortOres(List<OrePickup> Candidates)
    {
        switch (OreSortMode)
        {
            case OreCleanupSortMode.FarthestFromReferenceFirst:
                Candidates.Sort(CompareOreDistanceDescending);
                break;

            case OreCleanupSortMode.LowestValueThenFarthest:
                Candidates.Sort(CompareOreValueAscendingThenDistanceDescending);
                break;

            default:
                Candidates.Sort(CompareOreValueAscending);
                break;
        }
    }

    /// <summary>
    /// Compares ores by ascending credit value.
    /// </summary>
    private int CompareOreValueAscending(OrePickup A, OrePickup B)
    {
        return GetOreCreditValue(A).CompareTo(GetOreCreditValue(B));
    }

    /// <summary>
    /// Compares ores by descending distance from the configured reference transform.
    /// </summary>
    private int CompareOreDistanceDescending(OrePickup A, OrePickup B)
    {
        return GetDistanceScore(B).CompareTo(GetDistanceScore(A));
    }

    /// <summary>
    /// Compares ores by ascending value, then descending distance when values are similar.
    /// </summary>
    private int CompareOreValueAscendingThenDistanceDescending(OrePickup A, OrePickup B)
    {
        int ValueComparison = GetOreCreditValue(A).CompareTo(GetOreCreditValue(B));

        if (ValueComparison != 0)
        {
            return ValueComparison;
        }

        return GetDistanceScore(B).CompareTo(GetDistanceScore(A));
    }

    /// <summary>
    /// Gets the credit value of an ore pickup for emergency sorting.
    /// </summary>
    private float GetOreCreditValue(OrePickup Pickup)
    {
        if (Pickup == null || Pickup.GetOreItemData() == null)
        {
            return 0f;
        }

        return Pickup.GetOreItemData().GetCreditValue();
    }

    /// <summary>
    /// Gets squared distance from the reference transform for sorting.
    /// </summary>
    private float GetDistanceScore(OrePickup Pickup)
    {
        if (Pickup == null || ReferenceTransform == null)
        {
            return 0f;
        }

        return (Pickup.GetRuntimeRoot().position - ReferenceTransform.position).sqrMagnitude;
    }

    /// <summary>
    /// Disposes an ore pickup using the configured emergency mode.
    /// </summary>
    private void DisposeOrePickup(OrePickup Pickup)
    {
        if (Pickup == null)
        {
            return;
        }

        RuntimeWorldObjectRegistry.UnregisterOrePickup(Pickup);

        if (OreDisposalMode == PickupDisposalMode.ReturnToPoolWhenPossible && Pickup.ReturnToPool())
        {
            return;
        }

        Destroy(Pickup.GetRuntimeRoot().gameObject);
    }

    /// <summary>
    /// Disposes a money pickup using the configured emergency mode.
    /// </summary>
    private void DisposeMoneyPickup(MoneyPickup Pickup)
    {
        if (Pickup == null)
        {
            return;
        }

        RuntimeWorldObjectRegistry.UnregisterMoneyPickup(Pickup);

        if (MoneyDisposalMode == PickupDisposalMode.ReturnToPoolWhenPossible && Pickup.ReturnToPool())
        {
            return;
        }

        Destroy(Pickup.GetRuntimeRoot().gameObject);
    }

    /// <summary>
    /// Disposes a runtime world item root without affecting valid scene-placed persistent anchors.
    /// </summary>
    private void DisposeWorldItem(WorldItem Item)
    {
        if (Item == null)
        {
            return;
        }

        RuntimeWorldObjectRegistry.UnregisterWorldItem(Item);

        ScenePlacedWorldItemPersistence Persistence = Item.GetComponentInParent<ScenePlacedWorldItemPersistence>(true);

        if (Persistence != null && !Persistence.ShouldPreserveAsScenePlacedItem())
        {
            Destroy(Persistence.gameObject);
            return;
        }

        Destroy(Item.gameObject);
    }

    /// <summary>
    /// Resolves service references that are allowed to be auto-bound.
    /// </summary>
    private void ResolveReferences()
    {
        if (Registry == null)
        {
            Registry = RuntimeWorldObjectRegistry.Instance;

            if (Registry == null)
            {
                Registry = FindFirstObjectByType<RuntimeWorldObjectRegistry>();
            }
        }

        if (CurrencyWallet == null)
        {
            CurrencyWallet = FindFirstObjectByType<CurrencyWallet>();
        }
    }

    /// <summary>
    /// Gets the current runtime registry and rebuilds it once for safety before cleanup operations.
    /// </summary>
    private RuntimeWorldObjectRegistry GetRegistry()
    {
        ResolveReferences();

        if (Registry != null)
        {
            Registry.RebuildFromScene();
        }

        return Registry;
    }

    /// <summary>
    /// Logs cleanup messages if debug logging is enabled.
    /// </summary>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[EmergencyWorldCleanupService] " + Message, this);
    }
}
