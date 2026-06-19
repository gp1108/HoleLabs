using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared laboratory endpoint used by every installed mineral elevator access point.
/// This component owns the global elevator storage, capacity reservations and claimed output ejection.
/// </summary>
[DisallowMultipleComponent]
public sealed class MineralElevatorHub : MonoBehaviour, IPlayerInteractable
{
    [Header("Activation")]
    [Tooltip("Root object enabled when the first mineral elevator access point activates this hub. Keep this component on an always-active parent object.")]
    [SerializeField] private GameObject ActivationRoot;

    [Tooltip("If true, this hub starts active even before any access point has been installed.")]
    [SerializeField] private bool StartActivated = false;

    [Header("References")]
    [Tooltip("Runtime ore service used to spawn claimed physical ore pickups at the laboratory output point.")]
    [SerializeField] private OreRuntimeService OreRuntimeService;

    [Tooltip("Upgrade manager used to resolve global mineral elevator capacity upgrades.")]
    [SerializeField] private UpgradeManager UpgradeManager;

    [Tooltip("Optional feedback emitter used for hub activation, blocking, claiming and output events.")]
    [SerializeField] private GameFeedbackEmitter FeedbackEmitter;

    [Header("Storage")]
    [Tooltip("Base shared amount of ore payloads that can be stored inside this laboratory hub before upgrades.")]
    [SerializeField] private int BaseStoredOreCapacity = 100;

    [Tooltip("If true, the shared capacity is resolved through UpgradeManager.")]
    [SerializeField] private bool UseUpgradeModifiedCapacity = true;

    [Tooltip("Upgrade stat used to modify this hub shared storage capacity.")]
    [SerializeField] private UpgradeStatType StoredOreCapacityStat = UpgradeStatType.MineralElevatorStoredOreCapacity;

    [Header("Claim Output")]
    [Tooltip("World point where claimed ore pickups are spawned. Its forward axis defines optional impulse direction.")]
    [SerializeField] private Transform OutputSpawnPoint;

    [Tooltip("If true, claimed ore pickups receive an impulse in Output Spawn Point forward direction.")]
    [SerializeField] private bool ApplySpawnForwardImpulse = true;

    [Tooltip("Impulse force applied to claimed ore pickups when Apply Spawn Forward Impulse is enabled.")]
    [SerializeField] private float SpawnForwardImpulseForce = 3f;

    [Tooltip("Seconds between each claimed ore spawn. This is applied after clearance checks.")]
    [SerializeField] private float ClaimSpawnInterval = 0.05f;

    [Tooltip("Maximum ore pickups spawned per interaction. Use 0 or a negative value to claim everything currently stored.")]
    [SerializeField] private int MaxClaimedOrePerInteraction = 0;

    [Header("Claim Spawn Clearance")]
    [Tooltip("If true, the next claimed ore is not spawned until the protected output volume is clear from previous ore bodies.")]
    [SerializeField] private bool WaitForSpawnPointClearance = true;

    [Tooltip("Radius around Output Spawn Point that must be clear before another claimed ore is spawned.")]
    [SerializeField] private float ClaimSpawnClearanceRadius = 0.65f;

    [Tooltip("Extra safety radius added to the claim spawn clearance check.")]
    [SerializeField] private float ClaimSpawnClearancePadding = 0.05f;

    [Tooltip("Maximum colliders checked by the non-alloc clearance query.")]
    [SerializeField] private int ClaimSpawnClearanceMaxColliders = 64;

    [Tooltip("Seconds between repeated clearance checks while the output point remains occupied.")]
    [SerializeField] private float ClaimSpawnClearanceCheckInterval = 0.02f;

    [Tooltip("Maximum time to wait for the output point to clear. Use 0 to wait indefinitely.")]
    [SerializeField] private float ClaimSpawnClearanceTimeout = 0f;

    [Tooltip("FixedUpdate steps waited after spawning one ore before the next clearance check starts.")]
    [SerializeField] private int FixedStepsAfterClaimSpawn = 2;

    [Header("Feedback Events")]
    [Tooltip("Feedback event played when this hub becomes active for the first time.")]
    [SerializeField] private string HubActivatedEventId = GameFeedbackEventIds.MineralElevatorHubActivated;

    [Tooltip("Feedback event played when one transferred ore payload reaches this hub storage.")]
    [SerializeField] private string TransferCompletedEventId = GameFeedbackEventIds.MineralElevatorTransferCompleted;

    [Tooltip("Feedback event played when the player starts claiming stored ore from the hub.")]
    [SerializeField] private string ClaimStartedEventId = GameFeedbackEventIds.MineralElevatorClaimStarted;

    [Tooltip("Feedback event played when the player manually stops hub output ejection.")]
    [SerializeField] private string ClaimStoppedEventId = GameFeedbackEventIds.MineralElevatorClaimStopped;

    [Tooltip("Feedback event played every time a physical ore pickup is spawned at the hub output.")]
    [SerializeField] private string OutputSpawnedEventId = GameFeedbackEventIds.MineralElevatorOutputSpawned;

    [Tooltip("Feedback event played when a hub action is blocked.")]
    [SerializeField] private string BlockedEventId = GameFeedbackEventIds.MineralElevatorBlocked;

    [Header("Debug")]
    [Tooltip("Draws the output clearance volume when this hub is selected.")]
    [SerializeField] private bool DrawSpawnClearanceGizmo = true;

    [Tooltip("Logs hub activation, capacity reservation, transfer and claim flow.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Runtime shared ore storage. Ore payloads become physical only when the player claims them from the hub.
    /// </summary>
    private readonly List<OreItemData> StoredOreItems = new List<OreItemData>();

    /// <summary>
    /// Number of storage slots reserved by access points for ores that are currently travelling visually toward this hub.
    /// </summary>
    private int ReservedIncomingOreSlots;

    /// <summary>
    /// Active claim coroutine that ejects stored ore over time.
    /// </summary>
    private Coroutine ClaimRoutine;

    /// <summary>
    /// Non-alloc buffer used by the output clearance gate.
    /// </summary>
    private Collider[] ClaimSpawnClearanceBuffer;

    /// <summary>
    /// Recently spawned ore roots that must leave the protected output volume before another ore can spawn.
    /// </summary>
    private readonly List<GameObject> ActiveClaimSpawnBlockers = new List<GameObject>();

    /// <summary>
    /// Whether the laboratory hub is active because at least one access point has been installed at some point.
    /// </summary>
    private bool IsActivated;

    /// <summary>
    /// Resolves scene references and applies initial activation state.
    /// </summary>
    private void Awake()
    {
        EnsureReferences();
        SetActivatedInternal(StartActivated, false);
    }

    /// <summary>
    /// Ensures optional references are resolved without requiring manual wiring in every scene.
    /// </summary>
    private void EnsureReferences()
    {
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

        if (OutputSpawnPoint == null)
        {
            OutputSpawnPoint = transform;
        }
    }

    /// <summary>
    /// Activates the hub permanently. This is called by the first installed access point.
    /// </summary>
    public void ActivateHub()
    {
        SetActivatedInternal(true, true);
    }

    /// <summary>
    /// Gets whether this hub has been activated by at least one mineral elevator access point.
    /// </summary>
    public bool GetIsActivated()
    {
        return IsActivated;
    }

    /// <summary>
    /// Gets the current shared capacity after upgrades.
    /// </summary>
    public int GetEffectiveStoredOreCapacity()
    {
        int Capacity = Mathf.Max(1, BaseStoredOreCapacity);

        if (UseUpgradeModifiedCapacity && UpgradeManager != null && StoredOreCapacityStat != UpgradeStatType.None)
        {
            Capacity = UpgradeManager.GetModifiedIntStat(StoredOreCapacityStat, Capacity);
        }

        return Mathf.Max(1, Capacity);
    }

    /// <summary>
    /// Gets the number of ore payloads physically stored in the hub and ready to be claimed.
    /// </summary>
    public int GetStoredOreCount()
    {
        CleanupInvalidStoredOre();
        return StoredOreItems.Count;
    }

    /// <summary>
    /// Gets the number of slots reserved for in-transit ores that have not reached the hub yet.
    /// </summary>
    public int GetReservedIncomingOreCount()
    {
        return Mathf.Max(0, ReservedIncomingOreSlots);
    }

    /// <summary>
    /// Gets how many additional ore payloads can currently be accepted or reserved by access points.
    /// </summary>
    public int GetAvailableIncomingCapacity()
    {
        CleanupInvalidStoredOre();
        return Mathf.Max(0, GetEffectiveStoredOreCapacity() - StoredOreItems.Count - ReservedIncomingOreSlots);
    }

    /// <summary>
    /// Gets a copy of the stored ore payloads for save/load capture.
    /// </summary>
    public List<OreItemData> GetStoredOreItemsSnapshot()
    {
        CleanupInvalidStoredOre();
        return new List<OreItemData>(StoredOreItems);
    }

    /// <summary>
    /// Applies saved hub state.
    /// Access point pending transfer state is restored separately and will rebuild incoming reservations after this method.
    /// </summary>
    /// <param name="WasActivated">Saved activation state.</param>
    /// <param name="StoredOreItemsValue">Saved stored ore payloads.</param>
    public void ApplySavedState(bool WasActivated, IReadOnlyList<OreItemData> StoredOreItemsValue)
    {
        StopClaimOutput(false);
        StoredOreItems.Clear();
        ReservedIncomingOreSlots = 0;

        if (StoredOreItemsValue != null)
        {
            for (int Index = 0; Index < StoredOreItemsValue.Count; Index++)
            {
                OreItemData OreItem = StoredOreItemsValue[Index];

                if (OreItem == null || OreItem.GetOreDefinition() == null)
                {
                    continue;
                }

                StoredOreItems.Add(OreItem);
            }
        }

        TrimStoredOreToCapacity();
        SetActivatedInternal(WasActivated || StoredOreItems.Count > 0, false);
        Log("Restored hub. Active=" + IsActivated + " Stored=" + StoredOreItems.Count);
    }

    /// <summary>
    /// Reserves storage slots for ores that an access point is about to absorb and transfer over time.
    /// </summary>
    /// <param name="RequestedSlots">Requested amount of incoming slots.</param>
    /// <returns>Amount of slots that were successfully reserved.</returns>
    public int ReserveIncomingOreSlots(int RequestedSlots)
    {
        if (!IsActivated || RequestedSlots <= 0)
        {
            return 0;
        }

        int AcceptedSlots = Mathf.Min(RequestedSlots, GetAvailableIncomingCapacity());

        if (AcceptedSlots <= 0)
        {
            return 0;
        }

        ReservedIncomingOreSlots += AcceptedSlots;
        Log("Reserved incoming slots: " + AcceptedSlots + " | Reserved=" + ReservedIncomingOreSlots);
        return AcceptedSlots;
    }

    /// <summary>
    /// Rebuilds reserved storage slots from access point pending transfer state after loading a save file.
    /// </summary>
    /// <param name="RequestedSlots">Amount of pending in-transit ores being restored.</param>
    /// <returns>Amount of pending ores accepted by current hub capacity.</returns>
    public int ReserveRestoredIncomingOreSlots(int RequestedSlots)
    {
        return ReserveIncomingOreSlots(RequestedSlots);
    }

    /// <summary>
    /// Releases previously reserved incoming slots when an access point fails to absorb or discards pending transfer payloads.
    /// </summary>
    /// <param name="ReleasedSlots">Amount of incoming reservations to release.</param>
    public void ReleaseIncomingOreSlots(int ReleasedSlots)
    {
        if (ReleasedSlots <= 0)
        {
            return;
        }

        ReservedIncomingOreSlots = Mathf.Max(0, ReservedIncomingOreSlots - ReleasedSlots);
        Log("Released incoming slots: " + ReleasedSlots + " | Reserved=" + ReservedIncomingOreSlots);
    }

    /// <summary>
    /// Completes one previously reserved transfer and stores the ore payload inside the hub.
    /// </summary>
    /// <param name="OreItem">Ore payload that visually finished travelling to the laboratory hub.</param>
    /// <returns>True when the payload was stored, false when the hub state rejected it.</returns>
    public bool CompleteReservedIncomingOre(OreItemData OreItem)
    {
        if (OreItem == null || OreItem.GetOreDefinition() == null)
        {
            ReleaseIncomingOreSlots(1);
            return false;
        }

        if (!IsActivated)
        {
            ReleaseIncomingOreSlots(1);
            return false;
        }

        if (ReservedIncomingOreSlots <= 0)
        {
            Log("Incoming ore completed without a reservation. Rejecting payload to avoid overfilling the hub.");
            return false;
        }

        ReservedIncomingOreSlots = Mathf.Max(0, ReservedIncomingOreSlots - 1);
        CleanupInvalidStoredOre();

        if (StoredOreItems.Count >= GetEffectiveStoredOreCapacity())
        {
            Log("Hub was full when an incoming ore completed. Rejecting payload to avoid capacity overflow.");
            return false;
        }

        StoredOreItems.Add(OreItem);
        PlayFeedback(TransferCompletedEventId, transform.position);
        Log("Stored transferred ore. Stored=" + StoredOreItems.Count + "/" + GetEffectiveStoredOreCapacity());
        return true;
    }

    /// <summary>
    /// Attempts to consume a player interaction on the hub. Pressing again while ejecting stops the output sequence.
    /// </summary>
    public bool TryInteract()
    {
        return TryClaimOutput();
    }

    /// <summary>
    /// Returns whether this hub currently has claimable ore output.
    /// </summary>
    public bool CanClaimOutput()
    {
        CleanupInvalidStoredOre();
        return IsActivated && StoredOreItems.Count > 0 && OreRuntimeService != null && OutputSpawnPoint != null;
    }

    /// <summary>
    /// Starts claiming stored ore output, or stops the current output sequence if it is already running.
    /// </summary>
    public bool TryClaimOutput()
    {
        if (IsClaimingOutput())
        {
            StopClaimOutput(true);
            return true;
        }

        if (!CanClaimOutput())
        {
            PlayFeedback(BlockedEventId, OutputSpawnPoint != null ? OutputSpawnPoint.position : transform.position);
            Log("Claim rejected. Active=" + IsActivated + " Stored=" + StoredOreItems.Count);
            return false;
        }

        PlayFeedback(ClaimStartedEventId, OutputSpawnPoint.position);
        ClaimRoutine = StartCoroutine(ClaimStoredOutputRoutine());
        Log("Started hub output claim. Stored=" + StoredOreItems.Count);
        return true;
    }

    /// <summary>
    /// Spawns stored ore pickups over time and removes each payload only after successful spawn.
    /// </summary>
    private IEnumerator ClaimStoredOutputRoutine()
    {
        int SpawnLimit = MaxClaimedOrePerInteraction <= 0
            ? int.MaxValue
            : Mathf.Max(1, MaxClaimedOrePerInteraction);

        CleanupInvalidStoredOre();
        int SpawnedCount = 0;

        while (StoredOreItems.Count > 0 && SpawnedCount < SpawnLimit)
        {
            bool SpawnPointReady = true;

            if (WaitForSpawnPointClearance)
            {
                yield return WaitUntilClaimSpawnVolumeIsClear(Result => SpawnPointReady = Result);
            }

            if (!SpawnPointReady)
            {
                Log("Claim paused because the output point did not clear before timeout. Remaining=" + StoredOreItems.Count);
                break;
            }

            OreItemData OreData = StoredOreItems[0];

            if (OreData == null || OreData.GetOreDefinition() == null)
            {
                StoredOreItems.RemoveAt(0);
                continue;
            }

            GameObject SpawnedOreObject = SpawnClaimedOre(OreData);

            if (SpawnedOreObject == null)
            {
                Log("Claim paused because the next stored ore could not be spawned.");
                break;
            }

            StoredOreItems.RemoveAt(0);
            SpawnedCount++;
            RegisterClaimSpawnBlocker(SpawnedOreObject);

            int SafeFixedStepCount = Mathf.Max(0, FixedStepsAfterClaimSpawn);

            for (int FixedStepIndex = 0; FixedStepIndex < SafeFixedStepCount; FixedStepIndex++)
            {
                yield return new WaitForFixedUpdate();
            }

            if (ClaimSpawnInterval > 0f)
            {
                yield return new WaitForSeconds(ClaimSpawnInterval);
            }
            else
            {
                yield return null;
            }
        }

        ClaimRoutine = null;
        Log("Hub claim finished. Spawned=" + SpawnedCount + " Remaining=" + StoredOreItems.Count);
    }

    /// <summary>
    /// Spawns one ore pickup at the configured output point.
    /// </summary>
    private GameObject SpawnClaimedOre(OreItemData OreData)
    {
        if (OreRuntimeService == null || OutputSpawnPoint == null || OreData == null)
        {
            return null;
        }

        Vector3 SpawnPosition = OutputSpawnPoint.position;
        Quaternion SpawnRotation = OutputSpawnPoint.rotation;

        GameObject SpawnedOre = OreRuntimeService.SpawnOrePickup(OreData, SpawnPosition, SpawnRotation);

        if (SpawnedOre == null)
        {
            return null;
        }

        ResetSpawnedOreVelocity(SpawnedOre);
        ApplyOptionalSpawnForwardImpulse(SpawnedOre);
        Physics.SyncTransforms();
        PlayFeedback(OutputSpawnedEventId, SpawnPosition);
        return SpawnedOre;
    }

    /// <summary>
    /// Returns whether the hub is currently ejecting stored output.
    /// </summary>
    private bool IsClaimingOutput()
    {
        return ClaimRoutine != null;
    }

    /// <summary>
    /// Stops the current claim output sequence.
    /// </summary>
    private void StopClaimOutput(bool PlayStoppedFeedback)
    {
        if (ClaimRoutine == null)
        {
            return;
        }

        StopCoroutine(ClaimRoutine);
        ClaimRoutine = null;

        if (PlayStoppedFeedback)
        {
            PlayFeedback(ClaimStoppedEventId, OutputSpawnPoint != null ? OutputSpawnPoint.position : transform.position);
        }

        Log("Hub claim output stopped. Remaining=" + StoredOreItems.Count);
    }

    /// <summary>
    /// Waits until the protected output volume is clear.
    /// </summary>
    private IEnumerator WaitUntilClaimSpawnVolumeIsClear(System.Action<bool> ResultCallback)
    {
        if (!WaitForSpawnPointClearance || OutputSpawnPoint == null)
        {
            ResultCallback?.Invoke(true);
            yield break;
        }

        float Timeout = Mathf.Max(0f, ClaimSpawnClearanceTimeout);
        float ElapsedTime = 0f;
        float CheckInterval = Mathf.Max(0.01f, ClaimSpawnClearanceCheckInterval);

        while (!IsClaimSpawnVolumeClear())
        {
            if (Timeout > 0f && ElapsedTime >= Timeout)
            {
                ResultCallback?.Invoke(false);
                yield break;
            }

            ElapsedTime += CheckInterval;
            yield return new WaitForSeconds(CheckInterval);
        }

        ResultCallback?.Invoke(true);
    }

    /// <summary>
    /// Returns whether the configured output volume is free from ore pickup bodies.
    /// </summary>
    private bool IsClaimSpawnVolumeClear()
    {
        if (OutputSpawnPoint == null)
        {
            return true;
        }

        Physics.SyncTransforms();
        CleanupClaimSpawnBlockers();

        float SafeRadius = GetEffectiveClaimSpawnClearanceRadius();

        if (HasRegisteredClaimBlockerInsideSpawnVolume(SafeRadius))
        {
            return false;
        }

        EnsureClaimSpawnClearanceBuffer();

        int HitCount = Physics.OverlapSphereNonAlloc(
            OutputSpawnPoint.position,
            SafeRadius,
            ClaimSpawnClearanceBuffer,
            ~0,
            QueryTriggerInteraction.Collide);

        for (int Index = 0; Index < HitCount; Index++)
        {
            Collider CandidateCollider = ClaimSpawnClearanceBuffer[Index];
            ClaimSpawnClearanceBuffer[Index] = null;

            if (CandidateCollider == null || !CandidateCollider.enabled)
            {
                continue;
            }

            if (CandidateCollider.transform == transform || CandidateCollider.transform.IsChildOf(transform))
            {
                continue;
            }

            OrePickup CandidateOrePickup = CandidateCollider.GetComponentInParent<OrePickup>();

            if (CandidateOrePickup == null)
            {
                continue;
            }

            GameObject CandidateRoot = CandidateOrePickup.GetRuntimeRoot() != null
                ? CandidateOrePickup.GetRuntimeRoot().gameObject
                : CandidateOrePickup.gameObject;

            if (CandidateRoot == null || !CandidateRoot.activeInHierarchy)
            {
                continue;
            }

            if (IsOreRootIntersectingSpawnVolume(CandidateRoot, SafeRadius))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Registers a spawned ore root as a blocker until its whole body leaves the protected output volume.
    /// </summary>
    private void RegisterClaimSpawnBlocker(GameObject SpawnedOreObject)
    {
        if (SpawnedOreObject == null)
        {
            return;
        }

        if (!ActiveClaimSpawnBlockers.Contains(SpawnedOreObject))
        {
            ActiveClaimSpawnBlockers.Add(SpawnedOreObject);
        }
    }

    /// <summary>
    /// Removes destroyed, pooled or inactive ore roots from the local blocker list.
    /// </summary>
    private void CleanupClaimSpawnBlockers()
    {
        for (int Index = ActiveClaimSpawnBlockers.Count - 1; Index >= 0; Index--)
        {
            GameObject Blocker = ActiveClaimSpawnBlockers[Index];

            if (Blocker == null || !Blocker.activeInHierarchy)
            {
                ActiveClaimSpawnBlockers.RemoveAt(Index);
            }
        }
    }

    /// <summary>
    /// Returns true if any locally registered blocker still intersects the protected output volume.
    /// </summary>
    private bool HasRegisteredClaimBlockerInsideSpawnVolume(float SafeRadius)
    {
        for (int Index = ActiveClaimSpawnBlockers.Count - 1; Index >= 0; Index--)
        {
            GameObject Blocker = ActiveClaimSpawnBlockers[Index];

            if (Blocker == null || !Blocker.activeInHierarchy)
            {
                ActiveClaimSpawnBlockers.RemoveAt(Index);
                continue;
            }

            if (IsOreRootIntersectingSpawnVolume(Blocker, SafeRadius))
            {
                return true;
            }

            ActiveClaimSpawnBlockers.RemoveAt(Index);
        }

        return false;
    }

    /// <summary>
    /// Returns true if any collider in the ore root still intersects the protected output sphere.
    /// </summary>
    private bool IsOreRootIntersectingSpawnVolume(GameObject OreRoot, float SafeRadius)
    {
        if (OreRoot == null || OutputSpawnPoint == null)
        {
            return false;
        }

        Vector3 SpawnCenter = OutputSpawnPoint.position;
        float SafeRadiusSqr = SafeRadius * SafeRadius;
        Collider[] OreColliders = OreRoot.GetComponentsInChildren<Collider>(false);

        for (int Index = 0; Index < OreColliders.Length; Index++)
        {
            Collider OreCollider = OreColliders[Index];

            if (OreCollider == null || !OreCollider.enabled)
            {
                continue;
            }

            if (OreCollider.bounds.SqrDistance(SpawnCenter) <= SafeRadiusSqr)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the safe clearance radius used by the output spawn gate.
    /// </summary>
    private float GetEffectiveClaimSpawnClearanceRadius()
    {
        return Mathf.Max(0.01f, ClaimSpawnClearanceRadius + Mathf.Max(0f, ClaimSpawnClearancePadding));
    }

    /// <summary>
    /// Ensures the non-alloc clearance buffer matches the configured capacity.
    /// </summary>
    private void EnsureClaimSpawnClearanceBuffer()
    {
        int SafeCapacity = Mathf.Max(1, ClaimSpawnClearanceMaxColliders);

        if (ClaimSpawnClearanceBuffer == null || ClaimSpawnClearanceBuffer.Length != SafeCapacity)
        {
            ClaimSpawnClearanceBuffer = new Collider[SafeCapacity];
        }
    }

    /// <summary>
    /// Clears physical motion from a claimed ore pickup immediately after spawning.
    /// </summary>
    private void ResetSpawnedOreVelocity(GameObject SpawnedOre)
    {
        if (SpawnedOre == null)
        {
            return;
        }

        Rigidbody SpawnedRigidbody = SpawnedOre.GetComponent<Rigidbody>() ?? SpawnedOre.GetComponentInChildren<Rigidbody>();

        if (SpawnedRigidbody == null || SpawnedRigidbody.isKinematic)
        {
            return;
        }

        SpawnedRigidbody.linearVelocity = Vector3.zero;
        SpawnedRigidbody.angularVelocity = Vector3.zero;
        SpawnedRigidbody.WakeUp();
    }

    /// <summary>
    /// Applies optional forward impulse to a claimed ore pickup.
    /// </summary>
    private void ApplyOptionalSpawnForwardImpulse(GameObject SpawnedOre)
    {
        if (!ApplySpawnForwardImpulse || SpawnedOre == null || OutputSpawnPoint == null)
        {
            return;
        }

        Rigidbody SpawnedRigidbody = SpawnedOre.GetComponent<Rigidbody>() ?? SpawnedOre.GetComponentInChildren<Rigidbody>();

        if (SpawnedRigidbody == null || SpawnedRigidbody.isKinematic)
        {
            return;
        }

        float SafeImpulseForce = Mathf.Max(0f, SpawnForwardImpulseForce);

        if (SafeImpulseForce <= 0f)
        {
            return;
        }

        Vector3 ImpulseDirection = OutputSpawnPoint.forward.normalized;
        SpawnedRigidbody.WakeUp();
        SpawnedRigidbody.AddForce(ImpulseDirection * SafeImpulseForce, ForceMode.Impulse);
    }

    /// <summary>
    /// Applies activation state to visuals and colliders.
    /// </summary>
    private void SetActivatedInternal(bool ShouldBeActivated, bool PlayActivationFeedback)
    {
        bool WasActivated = IsActivated;
        IsActivated = ShouldBeActivated;

        if (ActivationRoot != null)
        {
            ActivationRoot.SetActive(IsActivated);
        }

        if (IsActivated && !WasActivated && PlayActivationFeedback)
        {
            PlayFeedback(HubActivatedEventId, transform.position);
        }

        if (IsActivated && !WasActivated)
        {
            Log("Mineral elevator hub activated.");
        }
    }

    /// <summary>
    /// Removes invalid payloads from internal storage.
    /// </summary>
    private void CleanupInvalidStoredOre()
    {
        StoredOreItems.RemoveAll(Item => Item == null || Item.GetOreDefinition() == null);
    }

    /// <summary>
    /// Trims stored ore when capacity becomes lower than the saved amount.
    /// </summary>
    private void TrimStoredOreToCapacity()
    {
        int Capacity = GetEffectiveStoredOreCapacity();

        while (StoredOreItems.Count > Capacity)
        {
            StoredOreItems.RemoveAt(StoredOreItems.Count - 1);
        }
    }

    /// <summary>
    /// Plays one feedback event from this hub.
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
    /// Draws the protected output volume in the editor.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!DrawSpawnClearanceGizmo)
        {
            return;
        }

        Transform SpawnTransform = OutputSpawnPoint != null ? OutputSpawnPoint : transform;

        if (SpawnTransform == null)
        {
            return;
        }

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(SpawnTransform.position, Mathf.Max(0.01f, ClaimSpawnClearanceRadius));
        Gizmos.DrawLine(SpawnTransform.position, SpawnTransform.position + SpawnTransform.forward * Mathf.Max(0.25f, ClaimSpawnClearanceRadius));
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

        Debug.Log("[MineralElevatorHub] " + Message, this);
    }
}
