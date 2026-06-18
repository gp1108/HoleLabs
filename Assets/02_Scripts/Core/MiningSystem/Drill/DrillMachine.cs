using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime placed wall drill that produces ore internally and ejects physical ore pickups only when the player claims its output.
/// </summary>
[DisallowMultipleComponent]
public sealed class DrillMachine : MonoBehaviour, IDrillOutputClaimable
{
    [Header("Output")]
    [Tooltip("World point where claimed ore pickups are spawned. If empty, this transform is used.")]
    [SerializeField] private Transform OreSpawnPoint;

    [Tooltip("If true, claimed ore pickups receive an impulse using the ore spawn point forward direction.")]
    [SerializeField] private bool ApplySpawnForwardImpulse = false;

    [Tooltip("Impulse force applied along the ore spawn point forward direction when spawn impulse is enabled.")]
    [SerializeField] private float SpawnForwardImpulseForce = 2.5f;

    [Tooltip("Seconds between each spawned ore while claiming stored output.")]
    [SerializeField] private float ClaimSpawnInterval = 0.06f;

    [Tooltip("Maximum ore pickups spawned by one claim interaction. Set to zero or less to eject all stored output.")]
    [SerializeField] private int MaxClaimedOrePerInteraction = 0;

    [Header("Claim Spawn Clearance")]
    [Tooltip("If true, the drill waits until the spawn point is physically clear before spawning the next claimed ore pickup.")]
    [SerializeField] private bool WaitForSpawnPointClearance = true;

    [Tooltip("Radius of the protected spawn volume around Ore Spawn Point. It must cover the full body of the largest ore that can be claimed.")]
    [SerializeField] private float ClaimSpawnClearanceRadius = 0.45f;

    [Tooltip("Extra radius added to the blocker size measured from recently spawned ore colliders.")]
    [SerializeField] private float ClaimSpawnClearancePadding = 0.05f;

    [Tooltip("Maximum colliders stored while checking the claimed ore spawn volume.")]
    [SerializeField] private int ClaimSpawnClearanceMaxColliders = 48;

    [Tooltip("Seconds between spawn clearance checks while waiting. Very small values are responsive but should not be zero.")]
    [SerializeField] private float ClaimSpawnClearanceCheckInterval = 0.02f;

    [Tooltip("Maximum seconds to wait for the spawn point to clear. Set to zero or less to wait indefinitely.")]
    [SerializeField] private float ClaimSpawnClearanceTimeout = 0f;

    [Tooltip("Amount of fixed physics steps waited after each claimed ore spawn before the next clearance check can pass.")]
    [SerializeField] private int FixedStepsAfterClaimSpawn = 1;

    [Tooltip("Draws the protected spawn volume around the ore spawn point in the editor.")]
    [SerializeField] private bool DrawSpawnClearanceGizmo = true;

    [Header("Runtime Capacity")]
    [Tooltip("Fallback capacity used if the owner spot does not provide a valid value.")]
    [SerializeField] private int FallbackStoredOreCapacity = 4;

    [Tooltip("Fallback production interval used if the owner spot does not provide a valid value.")]
    [SerializeField] private float FallbackProductionInterval = 4f;

    [Tooltip("If true, UpgradeManager stat modifiers affect production interval and stored output capacity.")]
    [SerializeField] private bool UseUpgradeModifiedValues = true;

    [Header("Upgrade Stats")]
    [Tooltip("Upgrade stat used to modify the seconds required to produce one stored ore.")]
    [SerializeField] private UpgradeStatType ProductionIntervalStat = UpgradeStatType.WallDrillProductionInterval;

    [Tooltip("Upgrade stat used to modify the amount of ore that can be stored before the drill pauses.")]
    [SerializeField] private UpgradeStatType StoredOreCapacityStat = UpgradeStatType.WallDrillStoredOreCapacity;

    [Header("Feedback")]
    [Tooltip("Optional feedback emitter used for drill runtime events.")]
    [SerializeField] private GameFeedbackEmitter FeedbackEmitter;

    [Tooltip("Feedback event played when the drill stores one newly produced ore.")]
    [SerializeField] private string ProductionTickEventId = GameFeedbackEventIds.WallDrillTick;

    [Tooltip("Feedback event played when the drill starts or resumes active production.")]
    [SerializeField] private string ProductionStartedEventId = GameFeedbackEventIds.WallDrillStarted;

    [Tooltip("Feedback event played when active production is paused because the drill is ejecting output or cannot currently produce.")]
    [SerializeField] private string ProductionPausedEventId = GameFeedbackEventIds.WallDrillPaused;

    [Tooltip("Feedback event played when the drill reaches storage capacity.")]
    [SerializeField] private string FullEventId = GameFeedbackEventIds.WallDrillFull;

    [Tooltip("Feedback event played when the player starts claiming stored output.")]
    [SerializeField] private string ClaimedEventId = GameFeedbackEventIds.WallDrillClaimed;

    [Tooltip("Feedback event played when the player manually stops output ejection before all stored ore has been spawned.")]
    [SerializeField] private string EjectionStoppedEventId = GameFeedbackEventIds.WallDrillEjectionStopped;

    [Tooltip("Feedback event played when production can resume after output ejection finishes or is stopped.")]
    [SerializeField] private string ProductionResumedEventId = GameFeedbackEventIds.WallDrillResumed;

    [Tooltip("Feedback event played for every ore pickup spawned during claim output.")]
    [SerializeField] private string OutputSpawnedEventId = GameFeedbackEventIds.WallDrillOutputSpawned;

    [Tooltip("Feedback event played when claim is requested but no stored output can be ejected.")]
    [SerializeField] private string BlockedEventId = GameFeedbackEventIds.WallDrillBlocked;

    [Header("Optional Animator")]
    [Tooltip("Optional animator driven by the drill runtime state.")]
    [SerializeField] private Animator DrillAnimator;

    [Tooltip("Optional animator trigger fired when one ore is produced internally.")]
    [SerializeField] private string ProduceTriggerName = "Produce";

    [Tooltip("Optional animator trigger fired when a stored ore pickup is ejected.")]
    [SerializeField] private string EjectTriggerName = "Eject";

    [Tooltip("Optional animator bool enabled while the machine is allowed to produce.")]
    [SerializeField] private string IsWorkingBoolName = "IsWorking";

    [Tooltip("Optional animator bool enabled while the storage is full.")]
    [SerializeField] private string IsFullBoolName = "IsFull";

    [Tooltip("Optional animator bool enabled while the drill is ejecting claimed output.")]
    [SerializeField] private string IsEjectingBoolName = "IsEjecting";

    [Header("Debug")]
    [Tooltip("Logs drill production and claim flow.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Spot that owns this placed drill.
    /// </summary>
    private WallDrillInstallationSpot OwnerSpot;

    /// <summary>
    /// Runtime service used to create ore payloads and spawn claimed ore pickups.
    /// </summary>
    private OreRuntimeService OreRuntimeService;

    /// <summary>
    /// Upgrade manager used to apply global drill production upgrades.
    /// </summary>
    private UpgradeManager UpgradeManager;

    /// <summary>
    /// Inventory item definition represented by this placed drill.
    /// </summary>
    private ItemDefinition DrillItemDefinition;

    /// <summary>
    /// Base seconds needed to produce one stored ore before upgrade modifiers.
    /// </summary>
    private float BaseProductionInterval;

    /// <summary>
    /// Base stored ore capacity before upgrade modifiers.
    /// </summary>
    private int BaseStoredOreCapacity;

    /// <summary>
    /// Remaining time until the next ore is stored internally.
    /// </summary>
    private float RemainingProductionTimer;

    /// <summary>
    /// Runtime output storage. These payloads are not physical until the player claims them.
    /// </summary>
    private readonly List<OreItemData> StoredOreItems = new List<OreItemData>();

    /// <summary>
    /// Active claim coroutine that ejects stored ore over time.
    /// </summary>
    private Coroutine ClaimRoutine;

    /// <summary>
    /// Non-alloc buffer used to check whether the claimed ore spawn volume is occupied.
    /// </summary>
    private Collider[] ClaimSpawnClearanceBuffer;

    /// <summary>
    /// Recently spawned ore roots that must leave the protected output volume before the next ore can spawn.
    /// </summary>
    private readonly List<GameObject> ActiveClaimSpawnBlockers = new List<GameObject>();

    /// <summary>
    /// Previous full-state cache used to fire full feedback only once per transition.
    /// </summary>
    private bool WasFullLastFrame;

    /// <summary>
    /// Previous production-state cache used to fire production start and pause feedback only once per transition.
    /// </summary>
    private bool WasProductionActiveLastFrame;

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
    /// Gets a copy of the stored ore payloads for save/load capture.
    /// </summary>
    public List<OreItemData> GetStoredOreItemsSnapshot()
    {
        return new List<OreItemData>(StoredOreItems);
    }

    /// <summary>
    /// Gets the amount of internally stored ore waiting to be claimed.
    /// </summary>
    public int GetStoredOreCount()
    {
        CleanupInvalidStoredOre();
        return StoredOreItems.Count;
    }

    /// <summary>
    /// Gets the current effective stored output capacity.
    /// </summary>
    public int GetEffectiveStoredOreCapacity()
    {
        int Capacity = Mathf.Max(1, BaseStoredOreCapacity > 0 ? BaseStoredOreCapacity : FallbackStoredOreCapacity);

        if (UseUpgradeModifiedValues && UpgradeManager != null && StoredOreCapacityStat != UpgradeStatType.None)
        {
            Capacity = UpgradeManager.GetModifiedIntStat(StoredOreCapacityStat, Capacity);
        }

        return Mathf.Max(1, Capacity);
    }

    /// <summary>
    /// Initializes the placed drill runtime.
    /// </summary>
    /// <param name="OwnerSpotValue">Spot that owns this drill.</param>
    /// <param name="OreRuntimeServiceValue">Ore runtime service used to generate and spawn ore.</param>
    /// <param name="UpgradeManagerValue">Upgrade manager used for global drill upgrades.</param>
    /// <param name="DrillItemDefinitionValue">Item definition represented by this placed drill.</param>
    /// <param name="ProductionIntervalValue">Base seconds required to produce one stored ore.</param>
    /// <param name="MaxStoredOreCountValue">Base stored ore capacity.</param>
    /// <param name="RemainingProductionTimerValue">Saved remaining timer, or negative to reset.</param>
    /// <param name="StoredOreItemsValue">Saved stored ore payloads, or null for empty storage.</param>
    public void Initialize(
        WallDrillInstallationSpot OwnerSpotValue,
        OreRuntimeService OreRuntimeServiceValue,
        UpgradeManager UpgradeManagerValue,
        ItemDefinition DrillItemDefinitionValue,
        float ProductionIntervalValue,
        int MaxStoredOreCountValue,
        float RemainingProductionTimerValue = -1f,
        IReadOnlyList<OreItemData> StoredOreItemsValue = null)
    {
        OwnerSpot = OwnerSpotValue;
        OreRuntimeService = OreRuntimeServiceValue;
        UpgradeManager = UpgradeManagerValue;
        DrillItemDefinition = DrillItemDefinitionValue;
        BaseProductionInterval = Mathf.Max(0.01f, ProductionIntervalValue > 0f ? ProductionIntervalValue : FallbackProductionInterval);
        BaseStoredOreCapacity = Mathf.Max(1, MaxStoredOreCountValue > 0 ? MaxStoredOreCountValue : FallbackStoredOreCapacity);
        RemainingProductionTimer = RemainingProductionTimerValue >= 0f
            ? Mathf.Clamp(RemainingProductionTimerValue, 0f, GetEffectiveProductionInterval())
            : GetEffectiveProductionInterval();

        if (OreSpawnPoint == null)
        {
            OreSpawnPoint = transform;
        }

        if (DrillAnimator == null)
        {
            DrillAnimator = GetComponentInChildren<Animator>();
        }

        if (FeedbackEmitter == null)
        {
            FeedbackEmitter = GetComponent<GameFeedbackEmitter>() ?? GetComponentInChildren<GameFeedbackEmitter>(true);
        }

        StoredOreItems.Clear();

        if (StoredOreItemsValue != null)
        {
            for (int Index = 0; Index < StoredOreItemsValue.Count; Index++)
            {
                if (StoredOreItemsValue[Index] == null || StoredOreItemsValue[Index].GetOreDefinition() == null)
                {
                    continue;
                }

                StoredOreItems.Add(StoredOreItemsValue[Index]);
            }
        }

        TrimStoredOreToCapacity();
        RefreshAnimatorState();
        WasFullLastFrame = IsStorageFull();
        WasProductionActiveLastFrame = CanProduce();
    }

    /// <summary>
    /// Updates internal production while the drill has storage space.
    /// </summary>
    private void Update()
    {
        bool CanProduceNow = CanProduce();
        RefreshAnimatorState();
        HandleProductionStateTransition(CanProduceNow);

        if (!CanProduceNow)
        {
            HandleFullStateTransition();
            return;
        }

        float EffectiveInterval = GetEffectiveProductionInterval();
        RemainingProductionTimer -= Time.deltaTime;

        if (RemainingProductionTimer > 0f)
        {
            HandleFullStateTransition();
            return;
        }

        ProduceOneStoredOre();
        RemainingProductionTimer = EffectiveInterval;
        HandleFullStateTransition();
    }

    /// <summary>
    /// Returns whether this object currently has claimable output.
    /// </summary>
    public bool CanClaimOutput()
    {
        if (IsClaimingOutput())
        {
            return true;
        }

        CleanupInvalidStoredOre();
        return StoredOreItems.Count > 0 && OreRuntimeService != null && OreSpawnPoint != null;
    }

    /// <summary>
    /// Tries to claim the currently buffered output.
    /// </summary>
    public bool TryClaimOutput()
    {
        if (IsClaimingOutput())
        {
            StopClaimOutputByInteraction();
            return true;
        }

        if (!CanClaimOutput())
        {
            PlayFeedback(BlockedEventId, transform.position);
            Log("Claim rejected because no stored output is available.");
            return false;
        }

        bool WasProducingBeforeClaim = CanProduce();

        if (WasProducingBeforeClaim)
        {
            PlayFeedback(ProductionPausedEventId, transform.position);
        }

        PlayFeedback(ClaimedEventId, OreSpawnPoint.position);
        ClaimRoutine = StartCoroutine(ClaimStoredOutputRoutine());
        WasProductionActiveLastFrame = false;
        RefreshAnimatorState();
        Log("Started claiming stored output. Count=" + StoredOreItems.Count);
        return true;
    }

    /// <summary>
    /// Returns whether the drill can currently produce and store another ore payload.
    /// </summary>
    private bool CanProduce()
    {
        if (IsClaimingOutput())
        {
            return false;
        }

        if (OwnerSpot == null || OreRuntimeService == null)
        {
            return false;
        }

        CleanupInvalidStoredOre();
        return StoredOreItems.Count < GetEffectiveStoredOreCapacity();
    }

    /// <summary>
    /// Produces one ore payload and stores it internally until claimed by the player.
    /// </summary>
    private void ProduceOneStoredOre()
    {
        if (OwnerSpot == null || OreRuntimeService == null)
        {
            return;
        }

        if (IsStorageFull())
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

        StoredOreItems.Add(RuntimeOreData);
        TryTriggerAnimator(ProduceTriggerName);
        PlayFeedback(ProductionTickEventId, transform.position);

        Log("Stored ore " + OreDefinition.GetDisplayName() + ". Stored=" + StoredOreItems.Count + "/" + GetEffectiveStoredOreCapacity());
    }

    /// <summary>
    /// Spawns stored ore pickups over time and removes each payload from internal storage only after it has been spawned.
    /// The next pickup is not spawned until the protected spawn volume is free.
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
                Log("Claim paused because the spawn point did not clear before timeout. Remaining=" + StoredOreItems.Count);
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
        RefreshAnimatorState();
        PlayProductionResumedFeedbackIfReady();
        Log("Claim finished. Spawned=" + SpawnedCount + " Remaining=" + StoredOreItems.Count);
    }

    /// <summary>
    /// Returns whether the drill is currently ejecting claimed output.
    /// </summary>
    private bool IsClaimingOutput()
    {
        return ClaimRoutine != null;
    }

    /// <summary>
    /// Stops the current output ejection sequence because the player interacted with the drill again.
    /// Stored ore that has not been spawned remains in internal storage.
    /// </summary>
    private void StopClaimOutputByInteraction()
    {
        if (ClaimRoutine == null)
        {
            return;
        }

        StopCoroutine(ClaimRoutine);
        ClaimRoutine = null;
        RefreshAnimatorState();
        PlayFeedback(EjectionStoppedEventId, OreSpawnPoint != null ? OreSpawnPoint.position : transform.position);
        PlayProductionResumedFeedbackIfReady();
        Log("Claim output stopped by player interaction. Remaining=" + StoredOreItems.Count);
    }

    /// <summary>
    /// Plays production resumed feedback only if the drill is able to produce after ejection stops.
    /// </summary>
    private void PlayProductionResumedFeedbackIfReady()
    {
        if (!CanProduce())
        {
            WasProductionActiveLastFrame = false;
            return;
        }

        PlayFeedback(ProductionResumedEventId, transform.position);
        WasProductionActiveLastFrame = true;
    }

    /// <summary>
    /// Spawns one claimed ore pickup at the configured output point.
    /// The output point position is the only spawn position and its forward axis is the only impulse direction.
    /// </summary>
    /// <param name="OreData">Runtime ore payload to spawn.</param>
    /// <returns>Spawned ore root object, or null if spawning failed.</returns>
    private GameObject SpawnClaimedOre(OreItemData OreData)
    {
        if (OreRuntimeService == null || OreSpawnPoint == null || OreData == null)
        {
            return null;
        }

        Vector3 SpawnPosition = OreSpawnPoint.position;
        Quaternion SpawnRotation = OreSpawnPoint.rotation;

        GameObject SpawnedOre = OreRuntimeService.SpawnOrePickup(
            OreData,
            SpawnPosition,
            SpawnRotation);

        if (SpawnedOre == null)
        {
            return null;
        }

        ResetSpawnedOreVelocity(SpawnedOre);
        ApplyOptionalSpawnForwardImpulse(SpawnedOre);
        Physics.SyncTransforms();

        TryTriggerAnimator(EjectTriggerName);
        PlayFeedback(OutputSpawnedEventId, SpawnPosition);
        return SpawnedOre;
    }

    /// <summary>
    /// Waits until the protected claimed ore spawn volume is clear.
    /// </summary>
    /// <param name="ResultCallback">Callback receiving whether the spawn volume became clear.</param>
    private IEnumerator WaitUntilClaimSpawnVolumeIsClear(System.Action<bool> ResultCallback)
    {
        if (!WaitForSpawnPointClearance || OreSpawnPoint == null)
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
    /// Returns whether the configured claimed ore spawn volume is free from ore pickup bodies.
    /// This check intentionally scans all physics layers and then filters by OrePickup so layer setup cannot make the gate miss a spawned ore.
    /// </summary>
    private bool IsClaimSpawnVolumeClear()
    {
        if (OreSpawnPoint == null)
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
            OreSpawnPoint.position,
            SafeRadius,
            ClaimSpawnClearanceBuffer,
            ~0,
            QueryTriggerInteraction.Collide);

        for (int Index = 0; Index < HitCount; Index++)
        {
            Collider CandidateCollider = ClaimSpawnClearanceBuffer[Index];

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
    /// Registers a recently spawned ore as a blocker until its whole body leaves the protected spawn volume.
    /// </summary>
    /// <param name="SpawnedOreObject">Spawned ore root object.</param>
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
    /// Returns true if any locally registered blocker still intersects the protected spawn volume.
    /// </summary>
    /// <param name="SafeRadius">Radius of the protected volume.</param>
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
    /// Returns true if any collider in the provided ore root still intersects the protected output sphere.
    /// Bounds are used deliberately because they catch the whole body leaving the spawn volume, not just the object's origin.
    /// </summary>
    /// <param name="OreRoot">Ore root to test.</param>
    /// <param name="SafeRadius">Radius of the protected volume.</param>
    private bool IsOreRootIntersectingSpawnVolume(GameObject OreRoot, float SafeRadius)
    {
        if (OreRoot == null || OreSpawnPoint == null)
        {
            return false;
        }

        Vector3 SpawnCenter = OreSpawnPoint.position;
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
    /// Gets the effective clearance radius used by the claim spawn gate.
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
    /// <param name="SpawnedOre">Spawned ore root object.</param>
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
    /// Applies an optional impulse to a claimed ore pickup using the ore spawn point forward axis.
    /// </summary>
    /// <param name="SpawnedOre">Spawned ore root object.</param>
    private void ApplyOptionalSpawnForwardImpulse(GameObject SpawnedOre)
    {
        if (!ApplySpawnForwardImpulse || SpawnedOre == null || OreSpawnPoint == null)
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

        Vector3 ImpulseDirection = OreSpawnPoint.forward.normalized;
        SpawnedRigidbody.WakeUp();
        SpawnedRigidbody.AddForce(ImpulseDirection * SafeImpulseForce, ForceMode.Impulse);
    }

    /// <summary>
    /// Gets the current effective production interval after upgrades.
    /// </summary>
    private float GetEffectiveProductionInterval()
    {
        float Interval = Mathf.Max(0.01f, BaseProductionInterval > 0f ? BaseProductionInterval : FallbackProductionInterval);

        if (UseUpgradeModifiedValues && UpgradeManager != null && ProductionIntervalStat != UpgradeStatType.None)
        {
            Interval = UpgradeManager.GetModifiedFloatStat(ProductionIntervalStat, Interval);
        }

        return Mathf.Max(0.01f, Interval);
    }

    /// <summary>
    /// Returns whether the internal storage is currently full.
    /// </summary>
    private bool IsStorageFull()
    {
        CleanupInvalidStoredOre();
        return StoredOreItems.Count >= GetEffectiveStoredOreCapacity();
    }

    /// <summary>
    /// Fires production start and pause feedback when production availability changes.
    /// </summary>
    /// <param name="IsProductionActiveNow">Whether production is currently active.</param>
    private void HandleProductionStateTransition(bool IsProductionActiveNow)
    {
        if (IsProductionActiveNow && !WasProductionActiveLastFrame)
        {
            PlayFeedback(ProductionStartedEventId, transform.position);
            Log("Drill production active.");
        }
        else if (!IsProductionActiveNow && WasProductionActiveLastFrame && !IsStorageFull())
        {
            PlayFeedback(ProductionPausedEventId, transform.position);
            Log("Drill production paused.");
        }

        WasProductionActiveLastFrame = IsProductionActiveNow;
    }

    /// <summary>
    /// Fires a full feedback event only when the drill transitions into the full state.
    /// </summary>
    private void HandleFullStateTransition()
    {
        bool IsFullNow = IsStorageFull();

        if (IsFullNow && !WasFullLastFrame)
        {
            PlayFeedback(FullEventId, transform.position);
            Log("Drill storage is full.");
        }

        WasFullLastFrame = IsFullNow;
    }

    /// <summary>
    /// Removes invalid ore payloads from storage.
    /// </summary>
    private void CleanupInvalidStoredOre()
    {
        StoredOreItems.RemoveAll(Item => Item == null || Item.GetOreDefinition() == null);
    }

    /// <summary>
    /// Trims restored output if the saved storage exceeds the current effective capacity.
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
    /// Refreshes animator bools from current runtime state.
    /// </summary>
    private void RefreshAnimatorState()
    {
        SetAnimatorBool(IsWorkingBoolName, CanProduce());
        SetAnimatorBool(IsFullBoolName, IsStorageFull());
        SetAnimatorBool(IsEjectingBoolName, IsClaimingOutput());
    }

    /// <summary>
    /// Triggers an animator parameter if it is configured.
    /// </summary>
    private void TryTriggerAnimator(string TriggerName)
    {
        if (DrillAnimator == null || string.IsNullOrWhiteSpace(TriggerName))
        {
            return;
        }

        DrillAnimator.SetTrigger(TriggerName);
    }

    /// <summary>
    /// Sets an animator bool if it is configured.
    /// </summary>
    private void SetAnimatorBool(string BoolName, bool Value)
    {
        if (DrillAnimator == null || string.IsNullOrWhiteSpace(BoolName))
        {
            return;
        }

        DrillAnimator.SetBool(BoolName, Value);
    }

    /// <summary>
    /// Plays a feedback event at the provided world position.
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
    /// Draws the claimed ore protected spawn volume in the editor.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!DrawSpawnClearanceGizmo)
        {
            return;
        }

        Transform SpawnTransform = OreSpawnPoint != null ? OreSpawnPoint : transform;

        if (SpawnTransform == null)
        {
            return;
        }

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(SpawnTransform.position, Mathf.Max(0.01f, ClaimSpawnClearanceRadius));
        Gizmos.DrawLine(SpawnTransform.position, SpawnTransform.position + SpawnTransform.forward * Mathf.Max(0.25f, ClaimSpawnClearanceRadius));
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
