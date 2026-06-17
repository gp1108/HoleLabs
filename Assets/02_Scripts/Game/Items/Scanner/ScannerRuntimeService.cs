using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Global authority for scanner knowledge.
/// It stores discovered ore types independently from any equipped scanner instance and keeps scanned physical ore instance ids while those instances exist.
/// </summary>
[DisallowMultipleComponent]
public sealed class ScannerRuntimeService : MonoBehaviour
{
    /// <summary>
    /// Serializable save entry for one scanned physical ore pickup instance.
    /// </summary>
    [Serializable]
    public sealed class ScannedOrePickupSaveEntry
    {
        [Tooltip("Stable runtime id assigned to the scanned OrePickup instance.")]
        [SerializeField] private string ScannerInstanceId;

        [Tooltip("Ore id carried by the scanned pickup when it was saved.")]
        [SerializeField] private string OreId;

        /// <summary>
        /// Creates an empty save entry for serializers.
        /// </summary>
        public ScannedOrePickupSaveEntry()
        {
        }

        /// <summary>
        /// Creates a save entry from a runtime scanned pickup record.
        /// </summary>
        /// <param name="ScannerInstanceIdValue">Stable pickup scanner instance id.</param>
        /// <param name="OreIdValue">Ore definition id assigned to the pickup.</param>
        public ScannedOrePickupSaveEntry(string ScannerInstanceIdValue, string OreIdValue)
        {
            ScannerInstanceId = ScannerInstanceIdValue;
            OreId = OreIdValue;
        }

        /// <summary>
        /// Gets the saved scanner instance id.
        /// </summary>
        public string GetScannerInstanceId()
        {
            return ScannerInstanceId;
        }

        /// <summary>
        /// Gets the saved ore id.
        /// </summary>
        public string GetOreId()
        {
            return OreId;
        }
    }

    /// <summary>
    /// Serializable save payload for scanner knowledge.
    /// </summary>
    [Serializable]
    public sealed class ScannerRuntimeSaveData
    {
        [Tooltip("Ore ids discovered globally by scanner use.")]
        [SerializeField] private List<string> DiscoveredOreIds = new();

        [Tooltip("Scanned physical ore pickup instances that still existed when the save was captured.")]
        [SerializeField] private List<ScannedOrePickupSaveEntry> ScannedOrePickups = new();

        /// <summary>
        /// Gets the discovered ore ids saved in this payload.
        /// </summary>
        public IReadOnlyList<string> GetDiscoveredOreIds()
        {
            return DiscoveredOreIds;
        }

        /// <summary>
        /// Gets the scanned ore pickup entries saved in this payload.
        /// </summary>
        public IReadOnlyList<ScannedOrePickupSaveEntry> GetScannedOrePickups()
        {
            return ScannedOrePickups;
        }

        /// <summary>
        /// Replaces discovered ore ids with sanitized values.
        /// </summary>
        /// <param name="OreIds">Ore ids to store.</param>
        public void SetDiscoveredOreIds(IEnumerable<string> OreIds)
        {
            DiscoveredOreIds.Clear();

            if (OreIds == null)
            {
                return;
            }

            HashSet<string> UniqueIds = new(StringComparer.Ordinal);

            foreach (string OreId in OreIds)
            {
                if (string.IsNullOrWhiteSpace(OreId) || !UniqueIds.Add(OreId))
                {
                    continue;
                }

                DiscoveredOreIds.Add(OreId);
            }
        }

        /// <summary>
        /// Replaces scanned ore pickup entries with sanitized values.
        /// </summary>
        /// <param name="Entries">Scanned ore pickup entries to store.</param>
        public void SetScannedOrePickups(IEnumerable<ScannedOrePickupSaveEntry> Entries)
        {
            ScannedOrePickups.Clear();

            if (Entries == null)
            {
                return;
            }

            HashSet<string> UniqueIds = new(StringComparer.Ordinal);

            foreach (ScannedOrePickupSaveEntry Entry in Entries)
            {
                if (Entry == null || string.IsNullOrWhiteSpace(Entry.GetScannerInstanceId()) || !UniqueIds.Add(Entry.GetScannerInstanceId()))
                {
                    continue;
                }

                ScannedOrePickups.Add(new ScannedOrePickupSaveEntry(Entry.GetScannerInstanceId(), Entry.GetOreId()));
            }
        }
    }

    /// <summary>
    /// Current singleton-like service instance used as a fallback by ore pickups during cleanup.
    /// </summary>
    private static ScannerRuntimeService CurrentInstance;

    [Header("Runtime Maintenance")]
    [Tooltip("If true, the service periodically removes scanned physical ore instance ids whose pickups no longer exist in the scene.")]
    [SerializeField] private bool PruneMissingInstancesAutomatically = true;

    [Tooltip("Seconds between automatic missing-instance prune passes.")]
    [SerializeField] private float MissingInstancePruneInterval = 5f;

    [Header("Debug")]
    [Tooltip("Logs scanner knowledge changes and save/load operations.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Discovered ore definition ids kept globally across scanner instances.
    /// </summary>
    private readonly HashSet<string> DiscoveredOreIds = new(StringComparer.Ordinal);

    /// <summary>
    /// Scanned physical ore pickup ids mapped to their ore id.
    /// </summary>
    private readonly Dictionary<string, string> ScannedOrePickupOreIdsByInstanceId = new(StringComparer.Ordinal);

    /// <summary>
    /// Timer used for automatic stale instance cleanup.
    /// </summary>
    private float MissingInstancePruneTimer;

    /// <summary>
    /// Raised whenever scanner knowledge changes.
    /// UI systems can subscribe to refresh without polling.
    /// </summary>
    public event Action OnKnowledgeChanged;

    /// <summary>
    /// Gets the currently active scanner runtime service, if any.
    /// </summary>
    public static ScannerRuntimeService Instance
    {
        get { return CurrentInstance; }
    }

    /// <summary>
    /// Registers this service as the current scanner knowledge authority.
    /// </summary>
    private void Awake()
    {
        if (CurrentInstance != null && CurrentInstance != this)
        {
            Debug.LogWarning("[ScannerRuntimeService] Multiple scanner runtime services were found. The newest instance will become the active authority.", this);
        }

        CurrentInstance = this;
        MissingInstancePruneTimer = Mathf.Max(0.1f, MissingInstancePruneInterval);
    }

    /// <summary>
    /// Clears the static reference when this service is destroyed.
    /// </summary>
    private void OnDestroy()
    {
        if (CurrentInstance == this)
        {
            CurrentInstance = null;
        }
    }

    /// <summary>
    /// Periodically removes stale scanned instance ids left by destroyed or pooled pickups.
    /// </summary>
    private void Update()
    {
        if (!PruneMissingInstancesAutomatically)
        {
            return;
        }

        MissingInstancePruneTimer -= Time.deltaTime;

        if (MissingInstancePruneTimer > 0f)
        {
            return;
        }

        MissingInstancePruneTimer = Mathf.Max(0.1f, MissingInstancePruneInterval);
        PruneMissingScannedOrePickups();
    }

    /// <summary>
    /// Marks an ore definition as discovered globally.
    /// </summary>
    /// <param name="OreDefinition">Ore definition to discover.</param>
    /// <returns>True if this call added new knowledge.</returns>
    public bool DiscoverOreDefinition(OreDefinition OreDefinition)
    {
        string OreId = GetOreId(OreDefinition);

        if (string.IsNullOrWhiteSpace(OreId))
        {
            return false;
        }

        if (!DiscoveredOreIds.Add(OreId))
        {
            return false;
        }

        Log("Discovered ore definition: " + OreId);
        NotifyKnowledgeChanged();
        return true;
    }

    /// <summary>
    /// Returns whether the provided ore definition has been discovered globally.
    /// </summary>
    /// <param name="OreDefinition">Ore definition to query.</param>
    public bool IsOreDefinitionDiscovered(OreDefinition OreDefinition)
    {
        string OreId = GetOreId(OreDefinition);
        return !string.IsNullOrWhiteSpace(OreId) && DiscoveredOreIds.Contains(OreId);
    }

    /// <summary>
    /// Returns whether the provided ore id has been discovered globally.
    /// </summary>
    /// <param name="OreId">Stable ore id to query.</param>
    public bool IsOreIdDiscovered(string OreId)
    {
        return !string.IsNullOrWhiteSpace(OreId) && DiscoveredOreIds.Contains(OreId);
    }

    /// <summary>
    /// Marks a physical ore pickup instance as fully scanned and discovers its ore definition globally.
    /// </summary>
    /// <param name="OrePickup">Ore pickup instance that completed scanning.</param>
    /// <returns>True if this call added new knowledge.</returns>
    public bool MarkOrePickupScanned(OrePickup OrePickup)
    {
        if (OrePickup == null || OrePickup.GetOreItemData() == null || OrePickup.GetOreItemData().GetOreDefinition() == null)
        {
            return false;
        }

        OreDefinition OreDefinition = OrePickup.GetOreItemData().GetOreDefinition();
        string OreId = GetOreId(OreDefinition);
        string ScannerInstanceId = OrePickup.GetScannerInstanceId();

        bool Changed = DiscoverOreDefinition(OreDefinition);

        if (!string.IsNullOrWhiteSpace(ScannerInstanceId) && !string.IsNullOrWhiteSpace(OreId))
        {
            if (!ScannedOrePickupOreIdsByInstanceId.TryGetValue(ScannerInstanceId, out string ExistingOreId) ||
                !string.Equals(ExistingOreId, OreId, StringComparison.Ordinal))
            {
                ScannedOrePickupOreIdsByInstanceId[ScannerInstanceId] = OreId;
                Changed = true;
                Log("Scanned ore pickup instance: " + ScannerInstanceId + " | Ore=" + OreId);
            }
        }

        if (Changed)
        {
            NotifyKnowledgeChanged();
        }

        return Changed;
    }

    /// <summary>
    /// Returns whether the provided physical ore pickup instance has been scanned.
    /// </summary>
    /// <param name="OrePickup">Ore pickup instance to query.</param>
    public bool IsOrePickupScanned(OrePickup OrePickup)
    {
        if (OrePickup == null)
        {
            return false;
        }

        string ScannerInstanceId = OrePickup.GetScannerInstanceId();

        if (string.IsNullOrWhiteSpace(ScannerInstanceId))
        {
            return false;
        }

        return ScannedOrePickupOreIdsByInstanceId.ContainsKey(ScannerInstanceId);
    }

    /// <summary>
    /// Removes one scanned pickup instance id when a physical ore pickup is consumed, pooled or destroyed.
    /// </summary>
    /// <param name="OrePickup">Ore pickup whose instance scan should be forgotten.</param>
    public void ForgetOrePickupInstance(OrePickup OrePickup)
    {
        if (OrePickup == null)
        {
            return;
        }

        ForgetOrePickupInstanceId(OrePickup.GetScannerInstanceId());
    }

    /// <summary>
    /// Removes one scanned pickup instance id when a physical ore pickup is consumed, pooled or destroyed.
    /// Discovered ore type knowledge is intentionally preserved.
    /// </summary>
    /// <param name="ScannerInstanceId">Stable scanner instance id to remove.</param>
    public void ForgetOrePickupInstanceId(string ScannerInstanceId)
    {
        if (string.IsNullOrWhiteSpace(ScannerInstanceId))
        {
            return;
        }

        if (!ScannedOrePickupOreIdsByInstanceId.Remove(ScannerInstanceId))
        {
            return;
        }

        Log("Forgot scanned ore pickup instance: " + ScannerInstanceId);
        NotifyKnowledgeChanged();
    }

    /// <summary>
    /// Removes scanned physical pickup entries whose instances are no longer active in the scene.
    /// </summary>
    /// <returns>Number of stale scanned entries removed.</returns>
    [ContextMenu("Prune Missing Scanned Ore Pickups")]
    public int PruneMissingScannedOrePickups()
    {
        if (ScannedOrePickupOreIdsByInstanceId.Count == 0)
        {
            return 0;
        }

        HashSet<string> ActiveInstanceIds = new(StringComparer.Ordinal);
        OrePickup[] ActivePickups = FindObjectsByType<OrePickup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        for (int Index = 0; Index < ActivePickups.Length; Index++)
        {
            OrePickup Pickup = ActivePickups[Index];

            if (Pickup == null || !Pickup.gameObject.activeInHierarchy || Pickup.GetOreItemData() == null)
            {
                continue;
            }

            string ScannerInstanceId = Pickup.GetScannerInstanceId();

            if (!string.IsNullOrWhiteSpace(ScannerInstanceId))
            {
                ActiveInstanceIds.Add(ScannerInstanceId);
            }
        }

        List<string> StaleIds = null;

        foreach (KeyValuePair<string, string> Pair in ScannedOrePickupOreIdsByInstanceId)
        {
            if (ActiveInstanceIds.Contains(Pair.Key))
            {
                continue;
            }

            if (StaleIds == null)
            {
                StaleIds = new List<string>();
            }

            StaleIds.Add(Pair.Key);
        }

        if (StaleIds == null || StaleIds.Count == 0)
        {
            return 0;
        }

        for (int Index = 0; Index < StaleIds.Count; Index++)
        {
            ScannedOrePickupOreIdsByInstanceId.Remove(StaleIds[Index]);
        }

        Log("Pruned missing scanned ore pickup instances: " + StaleIds.Count);
        NotifyKnowledgeChanged();
        return StaleIds.Count;
    }

    /// <summary>
    /// Creates a serializable scanner knowledge snapshot for the save system.
    /// </summary>
    public ScannerRuntimeSaveData CreateSaveSnapshot()
    {
        PruneMissingScannedOrePickups();

        ScannerRuntimeSaveData Result = new ScannerRuntimeSaveData();
        Result.SetDiscoveredOreIds(DiscoveredOreIds);

        List<ScannedOrePickupSaveEntry> Entries = new List<ScannedOrePickupSaveEntry>();

        foreach (KeyValuePair<string, string> Pair in ScannedOrePickupOreIdsByInstanceId)
        {
            Entries.Add(new ScannedOrePickupSaveEntry(Pair.Key, Pair.Value));
        }

        Result.SetScannedOrePickups(Entries);
        return Result;
    }

    /// <summary>
    /// Restores scanner knowledge from a save payload.
    /// Missing payloads intentionally clear runtime scanner knowledge for old saves.
    /// </summary>
    /// <param name="SaveData">Saved scanner state.</param>
    public void ApplySaveState(ScannerRuntimeSaveData SaveData)
    {
        DiscoveredOreIds.Clear();
        ScannedOrePickupOreIdsByInstanceId.Clear();

        if (SaveData == null)
        {
            NotifyKnowledgeChanged();
            return;
        }

        IReadOnlyList<string> SavedDiscoveredOreIds = SaveData.GetDiscoveredOreIds();

        if (SavedDiscoveredOreIds != null)
        {
            for (int Index = 0; Index < SavedDiscoveredOreIds.Count; Index++)
            {
                string OreId = SavedDiscoveredOreIds[Index];

                if (!string.IsNullOrWhiteSpace(OreId))
                {
                    DiscoveredOreIds.Add(OreId);
                }
            }
        }

        IReadOnlyList<ScannedOrePickupSaveEntry> SavedScannedPickups = SaveData.GetScannedOrePickups();

        if (SavedScannedPickups != null)
        {
            for (int Index = 0; Index < SavedScannedPickups.Count; Index++)
            {
                ScannedOrePickupSaveEntry Entry = SavedScannedPickups[Index];

                if (Entry == null || string.IsNullOrWhiteSpace(Entry.GetScannerInstanceId()))
                {
                    continue;
                }

                ScannedOrePickupOreIdsByInstanceId[Entry.GetScannerInstanceId()] = Entry.GetOreId();
            }
        }

        PruneMissingScannedOrePickups();
        Log("Applied scanner save state. Discovered ores=" + DiscoveredOreIds.Count + " | Scanned pickups=" + ScannedOrePickupOreIdsByInstanceId.Count);
        NotifyKnowledgeChanged();
    }

    /// <summary>
    /// Clears all scanner knowledge. Intended for debugging or save migration tests.
    /// </summary>
    [ContextMenu("Clear Scanner Knowledge")]
    public void ClearAllKnowledge()
    {
        DiscoveredOreIds.Clear();
        ScannedOrePickupOreIdsByInstanceId.Clear();
        NotifyKnowledgeChanged();
        Log("Cleared all scanner knowledge.");
    }

    /// <summary>
    /// Gets a copy of discovered ore ids for UI systems such as a future encyclopedia.
    /// </summary>
    public List<string> GetDiscoveredOreIdsCopy()
    {
        return new List<string>(DiscoveredOreIds);
    }

    /// <summary>
    /// Gets a copy of scanned ore pickup instance ids for debugging.
    /// </summary>
    public List<string> GetScannedOrePickupInstanceIdsCopy()
    {
        return new List<string>(ScannedOrePickupOreIdsByInstanceId.Keys);
    }

    /// <summary>
    /// Resolves a stable ore id from an ore definition.
    /// </summary>
    /// <param name="OreDefinition">Ore definition to read.</param>
    private string GetOreId(OreDefinition OreDefinition)
    {
        return OreDefinition != null ? OreDefinition.GetOreId() : string.Empty;
    }

    /// <summary>
    /// Raises the knowledge changed event safely.
    /// </summary>
    private void NotifyKnowledgeChanged()
    {
        OnKnowledgeChanged?.Invoke();
    }

    /// <summary>
    /// Writes debug logs when enabled.
    /// </summary>
    /// <param name="Message">Message to log.</param>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[ScannerRuntimeService] " + Message, this);
    }
}
