using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fixed scene placement point that can host exactly one drill machine.
/// The spot is the authoritative owner of availability, blocking state,
/// weighted ore configuration and ghost preview visibility.
/// </summary>
[DisallowMultipleComponent]
public sealed class DrillPlacementSpot : MonoBehaviour, IDrillRetrievable
{
    [Serializable]
    public sealed class WeightedOreEntry
    {
        [Tooltip("Ore definition that can be produced by this drill spot.")]
        [SerializeField] private OreDefinition OreDefinition;

        [Tooltip("Relative selection weight used for this ore definition.")]
        [SerializeField] private int Weight = 1;

        /// <summary>
        /// Gets the configured ore definition.
        /// </summary>
        public OreDefinition GetOreDefinition()
        {
            return OreDefinition;
        }

        /// <summary>
        /// Gets the validated non-negative weight.
        /// </summary>
        public int GetWeight()
        {
            return Mathf.Max(0, Weight);
        }
    }

    [Header("Availability")]
    [Tooltip("If true, this spot starts blocked and cannot receive a drill.")]
    [SerializeField] private bool IsBlocked = false;

    [Header("Placement")]
    [Tooltip("Placed world drill prefab instantiated when the item is deployed.")]
    [SerializeField] private GameObject PlacedDrillPrefab;

    [Tooltip("Inventory item definition returned when the placed drill is retrieved later.")]
    [SerializeField] private ItemDefinition DrillItemDefinition;

    [Header("Production")]
    [Tooltip("Possible ore definitions produced by this spot.")]
    [SerializeField] private List<WeightedOreEntry> AvailableOres = new List<WeightedOreEntry>();

    [Tooltip("Maximum ore pickups allowed inside the output zone before production pauses.")]
    [SerializeField] private int MaxBufferedOreCount = 4;

    [Tooltip("Seconds required to produce one ore pickup.")]
    [SerializeField] private float ProductionInterval = 4f;

    [Header("Ghost Preview")]
    [Tooltip("Ghost preview object shown while the drill item is equipped. Its transform is also the final drill placement pose.")]
    [SerializeField] private GameObject GhostVisualRoot;

    [Tooltip("Renderer list tinted with the neutral or highlighted ghost material.")]
    [SerializeField] private Renderer[] GhostRenderers;

    [Tooltip("Material used while the ghost is visible but not currently targeted.")]
    [SerializeField] private Material GhostIdleMaterial;

    [Tooltip("Material used while the player is pointing at a valid placement spot.")]
    [SerializeField] private Material GhostValidMaterial;

    [Header("Debug")]
    [Tooltip("Logs spot placement and retrieval operations.")]
    [SerializeField] private bool DebugLogs = false;

    [Tooltip("Currently placed drill machine hosted by this spot.")]
    [SerializeField] private DrillMachine CurrentDrillMachine;

    /// <summary>
    /// Returns whether this spot is currently occupied by a placed drill.
    /// </summary>
    public bool GetIsOccupied()
    {
        return CurrentDrillMachine != null;
    }

    /// <summary>
    /// Returns whether this spot is currently blocked by design or progression.
    /// </summary>
    public bool GetIsBlocked()
    {
        return IsBlocked;
    }

    /// <summary>
    /// Sets whether this spot is currently blocked from drill placement.
    /// </summary>
    public void SetBlocked(bool IsBlockedValue)
    {
        IsBlocked = IsBlockedValue;

        if (IsBlocked)
        {
            HideGhost();
        }
    }

    /// <summary>
    /// Gets whether this spot can currently accept a new drill placement.
    /// </summary>
    public bool CanAcceptPlacement()
    {
        return !IsBlocked &&
               !GetIsOccupied() &&
               PlacedDrillPrefab != null &&
               GhostVisualRoot != null;
    }

    /// <summary>
    /// Gets the world position used both by the ghost and by the final placed drill.
    /// </summary>
    public Vector3 GetPlacementWorldPosition()
    {
        return GhostVisualRoot != null ? GhostVisualRoot.transform.position : transform.position;
    }

    /// <summary>
    /// Gets the world rotation used both by the ghost and by the final placed drill.
    /// </summary>
    public Quaternion GetPlacementWorldRotation()
    {
        return GhostVisualRoot != null ? GhostVisualRoot.transform.rotation : transform.rotation;
    }

    /// <summary>
    /// Shows or hides the ghost according to the current spot availability.
    /// </summary>
    public void SetGhostVisible(bool IsVisible)
    {
        if (GhostVisualRoot == null)
        {
            return;
        }

        bool FinalVisibility = IsVisible && CanAcceptPlacement();
        GhostVisualRoot.SetActive(FinalVisibility);

        if (FinalVisibility)
        {
            ApplyGhostMaterial(false);
        }
    }

    /// <summary>
    /// Hides the ghost immediately.
    /// </summary>
    public void HideGhost()
    {
        if (GhostVisualRoot != null)
        {
            GhostVisualRoot.SetActive(false);
        }
    }

    /// <summary>
    /// Sets whether this ghost should display the valid targeted material.
    /// </summary>
    public void SetGhostHighlighted(bool IsHighlighted)
    {
        if (GhostVisualRoot == null || !GhostVisualRoot.activeSelf)
        {
            return;
        }

        ApplyGhostMaterial(IsHighlighted);
    }

    /// <summary>
    /// Places a drill on this spot and initializes its production runtime.
    /// </summary>
    public bool TryPlaceDrill(OreRuntimeService OreRuntimeService, ItemDefinition PlacedDrillItemDefinition, float RemainingProductionTimer = -1f)
    {
        if (!CanAcceptPlacement() || OreRuntimeService == null)
        {
            return false;
        }

        GameObject Instance = Instantiate(
            PlacedDrillPrefab,
            GetPlacementWorldPosition(),
            GetPlacementWorldRotation(),
            transform);

        DrillMachine DrillMachine = Instance.GetComponent<DrillMachine>();

        if (DrillMachine == null)
        {
            DrillMachine = Instance.GetComponentInChildren<DrillMachine>(true);
        }

        if (DrillMachine == null)
        {
            Destroy(Instance);
            return false;
        }

        ItemDefinition RuntimeDrillItemDefinition = PlacedDrillItemDefinition != null
            ? PlacedDrillItemDefinition
            : DrillItemDefinition;

        DrillMachine.Initialize(
            this,
            OreRuntimeService,
            RuntimeDrillItemDefinition,
            ProductionInterval,
            MaxBufferedOreCount,
            RemainingProductionTimer);

        CurrentDrillMachine = DrillMachine;
        HideGhost();

        Log("Drill placed successfully on spot: " + name);
        return true;
    }

    /// <summary>
    /// Tries to retrieve the currently placed drill as an inventory item instance.
    /// This is intentionally exposed now so a future retrieval tool can call it.
    /// </summary>
    public bool TryRetrieveDrill(out ItemInstance RetrievedItemInstance)
    {
        RetrievedItemInstance = null;

        if (CurrentDrillMachine == null)
        {
            return false;
        }

        ItemDefinition RuntimeDefinition = CurrentDrillMachine.GetDrillItemDefinition();

        if (RuntimeDefinition == null)
        {
            RuntimeDefinition = DrillItemDefinition;
        }

        if (RuntimeDefinition == null)
        {
            return false;
        }

        RetrievedItemInstance = RuntimeDefinition.CreateRuntimeInstance(1);
        Destroy(CurrentDrillMachine.gameObject);
        CurrentDrillMachine = null;

        Log("Drill retrieved from spot: " + name);
        return true;
    }

    /// <summary>
    /// Returns the current saved production timer for the hosted drill.
    /// </summary>
    public float GetCurrentRemainingProductionTimer()
    {
        return CurrentDrillMachine != null ? CurrentDrillMachine.GetRemainingProductionTimer() : 0f;
    }

    /// <summary>
    /// Returns the current placed drill item definition, if any.
    /// </summary>
    public ItemDefinition GetCurrentPlacedDrillItemDefinition()
    {
        return CurrentDrillMachine != null ? CurrentDrillMachine.GetDrillItemDefinition() : null;
    }

    /// <summary>
    /// Resolves the next ore definition using this spot weighted configuration.
    /// </summary>
    public OreDefinition ResolveRandomOreDefinition()
    {
        int TotalWeight = 0;

        for (int Index = 0; Index < AvailableOres.Count; Index++)
        {
            WeightedOreEntry Entry = AvailableOres[Index];

            if (Entry == null || Entry.GetOreDefinition() == null)
            {
                continue;
            }

            TotalWeight += Entry.GetWeight();
        }

        if (TotalWeight <= 0)
        {
            return null;
        }

        int RandomRoll = UnityEngine.Random.Range(0, TotalWeight);
        int RunningWeight = 0;

        for (int Index = 0; Index < AvailableOres.Count; Index++)
        {
            WeightedOreEntry Entry = AvailableOres[Index];

            if (Entry == null || Entry.GetOreDefinition() == null)
            {
                continue;
            }

            RunningWeight += Entry.GetWeight();

            if (RandomRoll < RunningWeight)
            {
                return Entry.GetOreDefinition();
            }
        }

        return null;
    }

    /// <summary>
    /// Called by the hosted drill when it gets destroyed or removed.
    /// </summary>
    public void NotifyDrillReleased(DrillMachine DrillMachine)
    {
        if (CurrentDrillMachine == DrillMachine)
        {
            CurrentDrillMachine = null;
        }
    }

    /// <summary>
    /// Applies the saved runtime state of this spot.
    /// </summary>
    public void ApplySavedState(bool IsBlockedValue, bool IsOccupiedValue, ItemDefinition SavedDrillItemDefinition, OreRuntimeService OreRuntimeService, float RemainingProductionTimer)
    {
        IsBlocked = IsBlockedValue;

        if (!IsOccupiedValue)
        {
            if (CurrentDrillMachine != null)
            {
                Destroy(CurrentDrillMachine.gameObject);
                CurrentDrillMachine = null;
            }

            HideGhost();
            return;
        }

        if (CurrentDrillMachine != null)
        {
            Destroy(CurrentDrillMachine.gameObject);
            CurrentDrillMachine = null;
        }

        TryPlaceDrill(
            OreRuntimeService,
            SavedDrillItemDefinition != null ? SavedDrillItemDefinition : DrillItemDefinition,
            RemainingProductionTimer);
    }

    /// <summary>
    /// Resolves missing references and initializes ghost visibility.
    /// </summary>
    private void Awake()
    {
        HideGhost();
    }

    /// <summary>
    /// Applies the configured idle or valid material to the ghost renderers.
    /// </summary>
    private void ApplyGhostMaterial(bool IsHighlighted)
    {
        if (GhostRenderers == null || GhostRenderers.Length == 0)
        {
            return;
        }

        Material TargetMaterial = IsHighlighted && GhostValidMaterial != null
            ? GhostValidMaterial
            : GhostIdleMaterial;

        if (TargetMaterial == null)
        {
            return;
        }

        for (int Index = 0; Index < GhostRenderers.Length; Index++)
        {
            if (GhostRenderers[Index] == null)
            {
                continue;
            }

            GhostRenderers[Index].sharedMaterial = TargetMaterial;
        }
    }

    /// <summary>
    /// Writes a debug log when enabled.
    /// </summary>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[DrillPlacementSpot] " + Message, this);
    }
}