using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime placed drill machine that periodically produces ore pickups while its local output
/// trigger remains below the configured capacity limit.
/// </summary>
[DisallowMultipleComponent]
public sealed class DrillMachine : MonoBehaviour
{
    [Header("Output")]
    [Tooltip("World point where produced ore pickups will spawn. If empty, this transform is used.")]
    [SerializeField] private Transform OreSpawnPoint;

    [Tooltip("Trigger collider used to count ore pickups buffered by this drill. If empty, one will be searched in children.")]
    [SerializeField] private Collider OutputTrigger;

    [Tooltip("If true, only ore pickups are counted as buffered output.")]
    [SerializeField] private bool CountOnlyOrePickups = true;

    [Header("Optional Feedback")]
    [Tooltip("Optional animator triggered when a production cycle completes.")]
    [SerializeField] private Animator DrillAnimator;

    [Tooltip("Optional animator trigger fired when one ore is produced.")]
    [SerializeField] private string ProduceTriggerName = "Produce";

    [Tooltip("Optional animator bool enabled while the machine is allowed to work.")]
    [SerializeField] private string IsWorkingBoolName = "IsWorking";

    [Header("Debug")]
    [Tooltip("Logs drill production flow.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Spot that owns this machine.
    /// </summary>
    private DrillPlacementSpot OwnerSpot;

    /// <summary>
    /// Runtime service used to produce ore payloads and pickups.
    /// </summary>
    private OreRuntimeService OreRuntimeService;

    /// <summary>
    /// Inventory item definition represented by this placed drill.
    /// </summary>
    private ItemDefinition DrillItemDefinition;

    /// <summary>
    /// Seconds needed to produce one ore.
    /// </summary>
    private float ProductionInterval;

    /// <summary>
    /// Maximum ore pickups allowed in the output zone.
    /// </summary>
    private int MaxBufferedOreCount;

    /// <summary>
    /// Remaining time until the next ore is produced.
    /// </summary>
    private float RemainingProductionTimer;

    /// <summary>
    /// Runtime set of ore pickups currently inside the output trigger.
    /// </summary>
    private readonly HashSet<OrePickup> BufferedOrePickups = new HashSet<OrePickup>();

    /// <summary>
    /// Gets the drill item definition represented by this placed machine.
    /// </summary>
    public ItemDefinition GetDrillItemDefinition()
    {
        return DrillItemDefinition;
    }

    /// <summary>
    /// Gets the current remaining production timer.
    /// </summary>
    public float GetRemainingProductionTimer()
    {
        return Mathf.Max(0f, RemainingProductionTimer);
    }

    /// <summary>
    /// Initializes the placed drill runtime.
    /// </summary>
    public void Initialize(
        DrillPlacementSpot OwnerSpotValue,
        OreRuntimeService OreRuntimeServiceValue,
        ItemDefinition DrillItemDefinitionValue,
        float ProductionIntervalValue,
        int MaxBufferedOreCountValue,
        float RemainingProductionTimerValue = -1f)
    {
        OwnerSpot = OwnerSpotValue;
        OreRuntimeService = OreRuntimeServiceValue;
        DrillItemDefinition = DrillItemDefinitionValue;
        ProductionInterval = Mathf.Max(0.01f, ProductionIntervalValue);
        MaxBufferedOreCount = Mathf.Max(1, MaxBufferedOreCountValue);
        RemainingProductionTimer = RemainingProductionTimerValue >= 0f
            ? Mathf.Clamp(RemainingProductionTimerValue, 0f, ProductionInterval)
            : ProductionInterval;

        if (OreSpawnPoint == null)
        {
            OreSpawnPoint = transform;
        }

        if (DrillAnimator == null)
        {
            DrillAnimator = GetComponentInChildren<Animator>();
        }

        if (OutputTrigger == null)
        {
            OutputTrigger = GetComponentInChildren<Collider>(true);
        }

        if (OutputTrigger != null)
        {
            OutputTrigger.isTrigger = true;
        }

        RefreshBufferedOreState();
        SetAnimatorWorkingState(CanProduce());
    }

    /// <summary>
    /// Updates the production cycle.
    /// </summary>
    private void Update()
    {
        bool CanWork = CanProduce();
        SetAnimatorWorkingState(CanWork);

        if (!CanWork)
        {
            return;
        }

        RemainingProductionTimer -= Time.deltaTime;

        if (RemainingProductionTimer > 0f)
        {
            return;
        }

        ProduceOneOre();
        RemainingProductionTimer = ProductionInterval;
    }

    /// <summary>
    /// Tracks ore pickups entering the drill output trigger.
    /// </summary>
    private void OnTriggerEnter(Collider Other)
    {
        TryTrackOrePickup(Other);
    }

    /// <summary>
    /// Untracks ore pickups leaving the drill output trigger.
    /// </summary>
    private void OnTriggerExit(Collider Other)
    {
        OrePickup OrePickup = ResolveOrePickup(Other);

        if (OrePickup == null)
        {
            return;
        }

        if (BufferedOrePickups.Remove(OrePickup))
        {
            Log("Ore pickup left output trigger. Count=" + BufferedOrePickups.Count);
        }
    }

    /// <summary>
    /// Returns whether the drill can currently produce another ore.
    /// </summary>
    private bool CanProduce()
    {
        if (OwnerSpot == null || OreRuntimeService == null || OreSpawnPoint == null)
        {
            return false;
        }

        CleanupNullBufferedEntries();
        return BufferedOrePickups.Count < MaxBufferedOreCount;
    }

    /// <summary>
    /// Produces exactly one ore pickup according to the owner spot ore table.
    /// </summary>
    private void ProduceOneOre()
    {
        if (OwnerSpot == null || OreRuntimeService == null)
        {
            return;
        }

        OreDefinition OreDefinition = OwnerSpot.ResolveRandomOreDefinition();

        if (OreDefinition == null)
        {
            Log("Production skipped because no valid ore definition was resolved.");
            return;
        }

        OreItemData RuntimeOreData = OreRuntimeService.CreateOreItemData(OreDefinition);

        if (RuntimeOreData == null)
        {
            Log("Production skipped because ore runtime data could not be created.");
            return;
        }

        GameObject SpawnedOre = OreRuntimeService.SpawnOrePickup(
            RuntimeOreData,
            OreSpawnPoint.position,
            OreSpawnPoint.rotation);

        if (SpawnedOre == null)
        {
            Log("Production skipped because the ore pickup could not be spawned.");
            return;
        }

        TryTriggerProduceFeedback();
        RefreshBufferedOreState();

        Log("Produced ore " + OreDefinition.GetDisplayName() + " at spot " + OwnerSpot.name);
    }

    /// <summary>
    /// Rebuilds the buffered ore state from the current overlaps.
    /// This is important after load because trigger enter events are not guaranteed to replay.
    /// </summary>
    private void RefreshBufferedOreState()
    {
        BufferedOrePickups.Clear();

        if (OutputTrigger == null)
        {
            return;
        }

        Bounds TriggerBounds = OutputTrigger.bounds;
        Collider[] Hits = Physics.OverlapBox(
            TriggerBounds.center,
            TriggerBounds.extents,
            OutputTrigger.transform.rotation,
            ~0,
            QueryTriggerInteraction.Ignore);

        for (int Index = 0; Index < Hits.Length; Index++)
        {
            TryTrackOrePickup(Hits[Index]);
        }

        Log("Buffered ore state refreshed. Count=" + BufferedOrePickups.Count);
    }

    /// <summary>
    /// Attempts to track one ore pickup from the provided collider.
    /// </summary>
    private void TryTrackOrePickup(Collider Other)
    {
        OrePickup OrePickup = ResolveOrePickup(Other);

        if (OrePickup == null)
        {
            return;
        }

        if (BufferedOrePickups.Add(OrePickup))
        {
            Log("Ore pickup entered output trigger. Count=" + BufferedOrePickups.Count);
        }
    }

    /// <summary>
    /// Resolves an ore pickup from a collider or its hierarchy.
    /// </summary>
    private OrePickup ResolveOrePickup(Collider Other)
    {
        if (Other == null)
        {
            return null;
        }

        OrePickup OrePickup = Other.GetComponent<OrePickup>() ?? Other.GetComponentInParent<OrePickup>();

        if (CountOnlyOrePickups)
        {
            return OrePickup;
        }

        return OrePickup;
    }

    /// <summary>
    /// Removes null or inactive entries caused by pooling or destruction.
    /// </summary>
    private void CleanupNullBufferedEntries()
    {
        BufferedOrePickups.RemoveWhere(Item => Item == null || !Item.gameObject.activeInHierarchy);
    }

    /// <summary>
    /// Triggers the optional production animation hook.
    /// </summary>
    private void TryTriggerProduceFeedback()
    {
        if (DrillAnimator == null || string.IsNullOrWhiteSpace(ProduceTriggerName))
        {
            return;
        }

        DrillAnimator.SetTrigger(ProduceTriggerName);
    }

    /// <summary>
    /// Sets the optional animator bool that indicates whether the drill is actively allowed to work.
    /// </summary>
    private void SetAnimatorWorkingState(bool IsWorking)
    {
        if (DrillAnimator == null || string.IsNullOrWhiteSpace(IsWorkingBoolName))
        {
            return;
        }

        DrillAnimator.SetBool(IsWorkingBoolName, IsWorking);
    }

    /// <summary>
    /// Releases ownership from the spot when the drill is destroyed.
    /// </summary>
    private void OnDestroy()
    {
        if (OwnerSpot != null)
        {
            OwnerSpot.NotifyDrillReleased(this);
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

        Debug.Log("[DrillMachine] " + Message, this);
    }
}