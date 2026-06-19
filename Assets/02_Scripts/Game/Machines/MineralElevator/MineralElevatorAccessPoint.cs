using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Companion component for one mineral elevator access point installed through a generic PlaceableInstallationSpot.
/// Access points have independent input zones but send all accepted ore payloads toward one shared laboratory MineralElevatorHub.
/// </summary>
[DisallowMultipleComponent]
public sealed class MineralElevatorAccessPoint : MonoBehaviour, IPlayerInteractable
{
    [Header("References")]
    [Tooltip("Generic placement spot that owns installation, ghost preview, obstruction checks, hotbar consumption and installed item persistence.")]
    [SerializeField] private PlaceableInstallationSpot InstallationSpot;

    [Tooltip("Shared laboratory hub that receives payloads from every installed access point. If empty, the first hub in the scene is used.")]
    [SerializeField] private MineralElevatorHub Hub;

    [Tooltip("Runtime ore service used only to return a payload to the world if a restored or active transfer is rejected unexpectedly.")]
    [SerializeField] private OreRuntimeService OreRuntimeService;

    [Tooltip("Upgrade manager used to resolve global mineral elevator transfer interval upgrades.")]
    [SerializeField] private UpgradeManager UpgradeManager;

    [Tooltip("Optional feedback emitter used for absorption, transfer and blocked events.")]
    [SerializeField] private GameFeedbackEmitter FeedbackEmitter;

    [Header("Input Zone")]
    [Tooltip("Collider that defines the physical ore deposit area. The access point scans this collider bounds only when the player presses interact.")]
    [SerializeField] private Collider InputZone;

    [Tooltip("Layers considered valid for physical OrePickup absorption.")]
    [SerializeField] private LayerMask InputOreLayers = ~0;

    [Tooltip("Fallback radius used when no Input Zone collider is assigned.")]
    [SerializeField] private float FallbackInputRadius = 1.5f;

    [Tooltip("Maximum colliders scanned inside the input zone per interaction.")]
    [SerializeField] private int MaxInputZoneColliders = 128;

    [Tooltip("Maximum ore pickups absorbed per interaction. Use 0 or a negative value to absorb up to the hub free capacity.")]
    [SerializeField] private int MaxAbsorbedOrePerInteraction = 0;

    [Tooltip("If true, ore currently held by the player or magnetized by the magnet cannot be absorbed.")]
    [SerializeField] private bool IgnorePlayerHeldOrMagnetizedOre = true;

    [Header("Transfer")]
    [Tooltip("Base seconds needed for one absorbed ore payload to visually travel to the laboratory hub.")]
    [SerializeField] private float BaseTransferInterval = 1f;

    [Tooltip("If true, Base Transfer Interval is modified through UpgradeManager.")]
    [SerializeField] private bool UseUpgradeModifiedTransferInterval = true;

    [Tooltip("Upgrade stat used to modify the access point transfer interval.")]
    [SerializeField] private UpgradeStatType TransferIntervalStat = UpgradeStatType.MineralElevatorTransferInterval;

    [Tooltip("Point used to return an ore payload to the world if a transfer is rejected unexpectedly. If empty, this transform is used.")]
    [SerializeField] private Transform RejectedOreReturnPoint;

    [Tooltip("If true, unexpected rejected transfer payloads are respawned at Rejected Ore Return Point instead of being discarded.")]
    [SerializeField] private bool ReturnRejectedTransferOreToWorld = true;

    [Header("Feedback Events")]
    [Tooltip("Feedback event played when this access point absorbs at least one ore pickup.")]
    [SerializeField] private string ItemAcceptedEventId = GameFeedbackEventIds.MineralElevatorItemAccepted;

    [Tooltip("Feedback event played when this access point starts or continues a transfer sequence.")]
    [SerializeField] private string TransferStartedEventId = GameFeedbackEventIds.MineralElevatorTransferStarted;

    [Tooltip("Feedback event played every time one pending ore payload reaches the hub.")]
    [SerializeField] private string TransferCompletedEventId = GameFeedbackEventIds.MineralElevatorTransferCompleted;

    [Tooltip("Feedback event played when absorption is requested but the shared hub has no available capacity.")]
    [SerializeField] private string FullEventId = GameFeedbackEventIds.MineralElevatorFull;

    [Tooltip("Feedback event played when this access point cannot handle the interaction.")]
    [SerializeField] private string BlockedEventId = GameFeedbackEventIds.MineralElevatorBlocked;

    [Header("Debug")]
    [Tooltip("Draws the input zone bounds or fallback sphere when this access point is selected.")]
    [SerializeField] private bool DrawInputZoneGizmo = true;

    [Tooltip("Logs access point installation, absorption, reservation and transfer flow.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Ore payloads physically absorbed by this access point but not yet delivered to the shared hub.
    /// </summary>
    private readonly List<OreItemData> PendingTransferOreItems = new List<OreItemData>();

    /// <summary>
    /// Reusable collider buffer for input-zone scans.
    /// </summary>
    private Collider[] InputZoneResults;

    /// <summary>
    /// Reusable unique ore pickup list built from input-zone scan results.
    /// </summary>
    private readonly List<OrePickup> CandidateOrePickups = new List<OrePickup>();

    /// <summary>
    /// Active transfer coroutine that moves pending payloads to the hub over time.
    /// </summary>
    private Coroutine TransferRoutine;

    /// <summary>
    /// Remaining time until the next pending payload reaches the hub.
    /// </summary>
    private float RemainingTransferTimer = -1f;

    /// <summary>
    /// Whether this access point has an installed mineral elevator visual through the generic placeable spot.
    /// </summary>
    private bool HasInstalledElevator;

    /// <summary>
    /// Resolves references.
    /// </summary>
    private void Awake()
    {
        EnsureReferences();
        EnsureInputZoneBuffer();
    }

    /// <summary>
    /// Subscribes to generic placeable installation events.
    /// </summary>
    private void OnEnable()
    {
        EnsureReferences();

        if (InstallationSpot != null)
        {
            InstallationSpot.InstallationChanged += HandleInstallationChanged;
            InstallationSpot.InstallationCleared += HandleInstallationCleared;
        }
    }

    /// <summary>
    /// Unsubscribes from generic placeable installation events.
    /// </summary>
    private void OnDisable()
    {
        if (InstallationSpot != null)
        {
            InstallationSpot.InstallationChanged -= HandleInstallationChanged;
            InstallationSpot.InstallationCleared -= HandleInstallationCleared;
        }
    }

    /// <summary>
    /// Refreshes installation state when the scene starts with a pre-restored or manually installed visual.
    /// </summary>
    private void Start()
    {
        RefreshInstalledStateFromSpot();
        EnsureHubReservationsForPendingTransfers();
        RestartTransferRoutineIfNeeded();
    }

    /// <summary>
    /// Resolves optional references from the scene when not assigned manually.
    /// </summary>
    private void EnsureReferences()
    {
        if (InstallationSpot == null)
        {
            InstallationSpot = GetComponent<PlaceableInstallationSpot>();
        }

        if (Hub == null)
        {
            MineralElevatorHub[] Hubs = FindObjectsByType<MineralElevatorHub>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Hub = Hubs != null && Hubs.Length > 0 ? Hubs[0] : null;
        }

        if (OreRuntimeService == null)
        {
            OreRuntimeService = FindFirstObjectByType<OreRuntimeService>();
        }

        if (UpgradeManager == null)
        {
            UpgradeManager = FindFirstObjectByType<UpgradeManager>();
        }

        if (FeedbackEmitter == null)
        {
            FeedbackEmitter = GetComponent<GameFeedbackEmitter>() ?? GetComponentInChildren<GameFeedbackEmitter>(true);
        }

        if (RejectedOreReturnPoint == null)
        {
            RejectedOreReturnPoint = transform;
        }
    }

    /// <summary>
    /// Ensures the non-alloc input-zone buffer matches the configured capacity.
    /// </summary>
    private void EnsureInputZoneBuffer()
    {
        int SafeCapacity = Mathf.Max(1, MaxInputZoneColliders);

        if (InputZoneResults == null || InputZoneResults.Length != SafeCapacity)
        {
            InputZoneResults = new Collider[SafeCapacity];
        }
    }

    /// <summary>
    /// Attempts to consume the player interaction by absorbing physical ores from the configured input area.
    /// </summary>
    public bool TryInteract()
    {
        return TryAbsorbInputOre();
    }

    /// <summary>
    /// Gets whether this access point currently has an installed mineral elevator visual.
    /// </summary>
    public bool GetHasInstalledElevator()
    {
        return HasInstalledElevator;
    }

    /// <summary>
    /// Gets the remaining transfer timer for save/load capture.
    /// </summary>
    public float GetRemainingTransferTimer()
    {
        return PendingTransferOreItems.Count > 0 ? Mathf.Max(0f, RemainingTransferTimer) : 0f;
    }

    /// <summary>
    /// Gets a copy of the pending in-transit payloads for save/load capture.
    /// </summary>
    public List<OreItemData> GetPendingTransferOreItemsSnapshot()
    {
        CleanupInvalidPendingOre();
        return new List<OreItemData>(PendingTransferOreItems);
    }

    /// <summary>
    /// Applies saved pending transfer state after generic placeable installation and hub state have been restored.
    /// </summary>
    /// <param name="RemainingTransferTimerValue">Saved remaining transfer timer.</param>
    /// <param name="PendingOreItems">Saved pending in-transit payloads.</param>
    public void ApplySavedState(float RemainingTransferTimerValue, IReadOnlyList<OreItemData> PendingOreItems)
    {
        StopTransferRoutine();
        ReleaseCurrentPendingReservations();
        PendingTransferOreItems.Clear();

        if (PendingOreItems != null)
        {
            for (int Index = 0; Index < PendingOreItems.Count; Index++)
            {
                OreItemData OreItem = PendingOreItems[Index];

                if (OreItem == null || OreItem.GetOreDefinition() == null)
                {
                    continue;
                }

                PendingTransferOreItems.Add(OreItem);
            }
        }

        RemainingTransferTimer = RemainingTransferTimerValue > 0f
            ? RemainingTransferTimerValue
            : GetEffectiveTransferInterval();

        EnsureHubReservationsForPendingTransfers();
        RestartTransferRoutineIfNeeded();
        Log("Restored access point. Pending=" + PendingTransferOreItems.Count);
    }

    /// <summary>
    /// Attempts to absorb physical ore pickups from the input zone and queue them for timed transfer to the shared hub.
    /// </summary>
    private bool TryAbsorbInputOre()
    {
        EnsureReferences();

        if (!HasInstalledElevator)
        {
            PlayFeedback(BlockedEventId, transform.position);
            Log("Interaction rejected because no mineral elevator is installed on this access point.");
            return false;
        }

        if (Hub == null)
        {
            PlayFeedback(BlockedEventId, transform.position);
            Log("Interaction rejected because no mineral elevator hub was found.");
            return false;
        }

        Hub.ActivateHub();

        int AvailableHubCapacity = Hub.GetAvailableIncomingCapacity();

        if (AvailableHubCapacity <= 0)
        {
            PlayFeedback(FullEventId, transform.position);
            Log("Interaction rejected because the shared hub is full or fully reserved.");
            return true;
        }

        BuildCandidateOrePickupList();

        if (CandidateOrePickups.Count <= 0)
        {
            PlayFeedback(BlockedEventId, transform.position);
            Log("Interaction found no valid OrePickup inside the input zone.");
            return false;
        }

        int RequestedAbsorbCount = CandidateOrePickups.Count;

        if (MaxAbsorbedOrePerInteraction > 0)
        {
            RequestedAbsorbCount = Mathf.Min(RequestedAbsorbCount, MaxAbsorbedOrePerInteraction);
        }

        RequestedAbsorbCount = Mathf.Min(RequestedAbsorbCount, AvailableHubCapacity);

        int ReservedSlots = Hub.ReserveIncomingOreSlots(RequestedAbsorbCount);

        if (ReservedSlots <= 0)
        {
            PlayFeedback(FullEventId, transform.position);
            Log("Interaction rejected because the hub did not reserve any incoming slot.");
            return true;
        }

        int AbsorbedCount = 0;

        for (int Index = 0; Index < CandidateOrePickups.Count && AbsorbedCount < ReservedSlots; Index++)
        {
            OrePickup Pickup = CandidateOrePickups[Index];

            if (!CanAbsorbOrePickup(Pickup))
            {
                continue;
            }

            OreItemData RuntimeOreData = Pickup.GetOreItemData();

            if (RuntimeOreData == null || RuntimeOreData.GetOreDefinition() == null)
            {
                continue;
            }

            if (!ConsumePhysicalOrePickup(Pickup))
            {
                continue;
            }

            PendingTransferOreItems.Add(RuntimeOreData);
            AbsorbedCount++;
        }

        if (AbsorbedCount < ReservedSlots)
        {
            Hub.ReleaseIncomingOreSlots(ReservedSlots - AbsorbedCount);
        }

        if (AbsorbedCount <= 0)
        {
            PlayFeedback(BlockedEventId, transform.position);
            Log("Interaction reserved capacity but no ore could be consumed. Released reservations.");
            return false;
        }

        PlayFeedback(ItemAcceptedEventId, transform.position);
        PlayFeedback(TransferStartedEventId, transform.position);
        RestartTransferRoutineIfNeeded();
        Log("Absorbed ores: " + AbsorbedCount + " Pending=" + PendingTransferOreItems.Count);
        return true;
    }

    /// <summary>
    /// Builds a unique candidate OrePickup list from the configured input area.
    /// </summary>
    private void BuildCandidateOrePickupList()
    {
        CandidateOrePickups.Clear();
        EnsureInputZoneBuffer();

        int HitCount;

        if (InputZone != null)
        {
            Bounds InputBounds = InputZone.bounds;
            HitCount = Physics.OverlapBoxNonAlloc(
                InputBounds.center,
                InputBounds.extents,
                InputZoneResults,
                Quaternion.identity,
                InputOreLayers,
                QueryTriggerInteraction.Collide);
        }
        else
        {
            HitCount = Physics.OverlapSphereNonAlloc(
                transform.position,
                Mathf.Max(0.01f, FallbackInputRadius),
                InputZoneResults,
                InputOreLayers,
                QueryTriggerInteraction.Collide);
        }

        for (int Index = 0; Index < HitCount; Index++)
        {
            Collider CandidateCollider = InputZoneResults[Index];
            InputZoneResults[Index] = null;

            if (CandidateCollider == null || !CandidateCollider.enabled)
            {
                continue;
            }

            OrePickup Pickup = CandidateCollider.GetComponentInParent<OrePickup>();

            if (!CanAbsorbOrePickup(Pickup))
            {
                continue;
            }

            if (!CandidateOrePickups.Contains(Pickup))
            {
                CandidateOrePickups.Add(Pickup);
            }
        }
    }

    /// <summary>
    /// Returns whether the provided physical ore pickup can be absorbed by this access point.
    /// </summary>
    private bool CanAbsorbOrePickup(OrePickup Pickup)
    {
        if (Pickup == null || Pickup.GetOreItemData() == null || Pickup.GetOreItemData().GetOreDefinition() == null)
        {
            return false;
        }

        GameObject RuntimeRoot = Pickup.GetRuntimeRoot() != null
            ? Pickup.GetRuntimeRoot().gameObject
            : Pickup.gameObject;

        if (RuntimeRoot == null || !RuntimeRoot.activeInHierarchy)
        {
            return false;
        }

        if (!IgnorePlayerHeldOrMagnetizedOre)
        {
            return true;
        }

        PhysicsCarryable Carryable = RuntimeRoot.GetComponent<PhysicsCarryable>() ?? RuntimeRoot.GetComponentInChildren<PhysicsCarryable>();

        if (Carryable == null)
        {
            return true;
        }

        return !Carryable.GetIsHeld() && !Carryable.GetIsMagnetized();
    }

    /// <summary>
    /// Removes a physical ore pickup from the world after its runtime payload has been queued for transfer.
    /// </summary>
    private bool ConsumePhysicalOrePickup(OrePickup Pickup)
    {
        if (Pickup == null)
        {
            return false;
        }

        if (Pickup.ReturnToPool())
        {
            return true;
        }

        GameObject RuntimeRoot = Pickup.GetRuntimeRoot() != null
            ? Pickup.GetRuntimeRoot().gameObject
            : Pickup.gameObject;

        if (RuntimeRoot != null)
        {
            Destroy(RuntimeRoot);
            return true;
        }

        Destroy(Pickup.gameObject);
        return true;
    }

    /// <summary>
    /// Restarts transfer processing when pending payloads exist and no routine is currently running.
    /// </summary>
    private void RestartTransferRoutineIfNeeded()
    {
        if (!HasInstalledElevator || PendingTransferOreItems.Count <= 0 || TransferRoutine != null)
        {
            return;
        }

        TransferRoutine = StartCoroutine(ProcessPendingTransferRoutine());
    }

    /// <summary>
    /// Stops the active transfer routine without clearing pending payloads.
    /// </summary>
    private void StopTransferRoutine()
    {
        if (TransferRoutine == null)
        {
            return;
        }

        StopCoroutine(TransferRoutine);
        TransferRoutine = null;
    }

    /// <summary>
    /// Processes one pending ore payload every transfer interval and stores it inside the shared hub only when the timer completes.
    /// </summary>
    private IEnumerator ProcessPendingTransferRoutine()
    {
        if (RemainingTransferTimer <= 0f)
        {
            RemainingTransferTimer = GetEffectiveTransferInterval();
        }

        while (HasInstalledElevator && PendingTransferOreItems.Count > 0)
        {
            while (RemainingTransferTimer > 0f)
            {
                RemainingTransferTimer -= Time.deltaTime;
                yield return null;
            }

            CompleteOnePendingTransfer();
            RemainingTransferTimer = GetEffectiveTransferInterval();
        }

        RemainingTransferTimer = -1f;
        TransferRoutine = null;
    }

    /// <summary>
    /// Completes one pending transfer and moves its payload into the shared hub storage.
    /// If hub state rejects the payload unexpectedly, the payload is returned to the world when configured.
    /// </summary>
    private void CompleteOnePendingTransfer()
    {
        CleanupInvalidPendingOre();

        if (PendingTransferOreItems.Count <= 0)
        {
            return;
        }

        OreItemData OreItem = PendingTransferOreItems[0];
        PendingTransferOreItems.RemoveAt(0);

        if (Hub != null && Hub.CompleteReservedIncomingOre(OreItem))
        {
            PlayFeedback(TransferCompletedEventId, transform.position);
            Log("Completed one transfer. Pending=" + PendingTransferOreItems.Count);
            return;
        }

        ReturnRejectedOreToWorld(OreItem);
        PlayFeedback(BlockedEventId, transform.position);
        Log("Transfer was rejected by hub and was returned to the input side when possible.");
    }

    /// <summary>
    /// Returns an unexpectedly rejected payload back to the world near this access point.
    /// </summary>
    private void ReturnRejectedOreToWorld(OreItemData OreItem)
    {
        if (!ReturnRejectedTransferOreToWorld || OreRuntimeService == null || OreItem == null || OreItem.GetOreDefinition() == null)
        {
            return;
        }

        Transform ReturnPoint = RejectedOreReturnPoint != null ? RejectedOreReturnPoint : transform;
        OreRuntimeService.SpawnOrePickup(OreItem, ReturnPoint.position, ReturnPoint.rotation);
    }

    /// <summary>
    /// Releases hub reservations for all currently pending payloads.
    /// </summary>
    private void ReleaseCurrentPendingReservations()
    {
        if (Hub == null || PendingTransferOreItems.Count <= 0)
        {
            return;
        }

        Hub.ReleaseIncomingOreSlots(PendingTransferOreItems.Count);
    }

    /// <summary>
    /// Rebuilds hub capacity reservations for pending payloads after save/load restoration.
    /// Extra pending payloads beyond current capacity are returned to the world when possible.
    /// </summary>
    private void EnsureHubReservationsForPendingTransfers()
    {
        EnsureReferences();

        if (Hub == null || PendingTransferOreItems.Count <= 0)
        {
            return;
        }

        Hub.ActivateHub();
        int AcceptedReservations = Hub.ReserveRestoredIncomingOreSlots(PendingTransferOreItems.Count);

        if (AcceptedReservations >= PendingTransferOreItems.Count)
        {
            return;
        }

        int RejectedCount = PendingTransferOreItems.Count - AcceptedReservations;

        for (int Index = PendingTransferOreItems.Count - 1; Index >= AcceptedReservations; Index--)
        {
            OreItemData RejectedOre = PendingTransferOreItems[Index];
            PendingTransferOreItems.RemoveAt(Index);
            ReturnRejectedOreToWorld(RejectedOre);
        }

        Log("Pending transfer state exceeded hub free capacity after restore. Returned overflow payloads=" + RejectedCount);
    }

    /// <summary>
    /// Gets the effective transfer interval after upgrades.
    /// </summary>
    private float GetEffectiveTransferInterval()
    {
        float Interval = Mathf.Max(0.01f, BaseTransferInterval);

        if (UseUpgradeModifiedTransferInterval && UpgradeManager != null && TransferIntervalStat != UpgradeStatType.None)
        {
            Interval = UpgradeManager.GetModifiedFloatStat(TransferIntervalStat, Interval);
        }

        return Mathf.Max(0.01f, Interval);
    }

    /// <summary>
    /// Reacts to generic placeable installation changes.
    /// </summary>
    private void HandleInstallationChanged(PlaceableInstallationSpot Spot, ItemInstance InstalledItem, GameObject InstalledVisual)
    {
        RefreshInstalledStateFromSpot();

        if (HasInstalledElevator && Hub != null)
        {
            Hub.ActivateHub();
        }

        RestartTransferRoutineIfNeeded();
    }

    /// <summary>
    /// Clears transient transfer state when the generic placement spot is cleared or replaced.
    /// </summary>
    private void HandleInstallationCleared(PlaceableInstallationSpot Spot)
    {
        StopTransferRoutine();
        ReleaseCurrentPendingReservations();
        PendingTransferOreItems.Clear();
        RemainingTransferTimer = -1f;
        HasInstalledElevator = false;
        Log("Generic installation was cleared. Mineral elevator access point state was reset.");
    }

    /// <summary>
    /// Refreshes installation state from the generic placeable spot.
    /// </summary>
    private void RefreshInstalledStateFromSpot()
    {
        HasInstalledElevator = InstallationSpot != null && InstallationSpot.GetIsOccupied();
    }

    /// <summary>
    /// Removes invalid pending payloads.
    /// </summary>
    private void CleanupInvalidPendingOre()
    {
        PendingTransferOreItems.RemoveAll(Item => Item == null || Item.GetOreDefinition() == null);
    }

    /// <summary>
    /// Plays one feedback event from this access point.
    /// </summary>
    private void PlayFeedback(string EventId, Vector3 Position)
    {
        if (FeedbackEmitter == null || string.IsNullOrWhiteSpace(EventId))
        {
            return;
        }

        FeedbackEmitter.Play(EventId, GameFeedbackContext.FromPosition(Position, transform));
    }

    /// <summary>
    /// Draws the configured input area in the editor.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!DrawInputZoneGizmo)
        {
            return;
        }

        Gizmos.color = Color.cyan;

        if (InputZone != null)
        {
            Bounds InputBounds = InputZone.bounds;
            Gizmos.DrawWireCube(InputBounds.center, InputBounds.size);
        }
        else
        {
            Gizmos.DrawWireSphere(transform.position, Mathf.Max(0.01f, FallbackInputRadius));
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

        Debug.Log("[MineralElevatorAccessPoint] " + Message, this);
    }
}
