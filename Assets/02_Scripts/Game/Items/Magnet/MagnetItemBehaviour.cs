using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Equipped ore magnet tool driven by animation events.
/// Primary use keeps the current pulling behaviour, while secondary use manages optional ore-type filters.
/// Filters are intentionally based on OreDefinition ids instead of runtime ore instances so the magnet targets
/// every ore pickup of the selected type and never depends on a specific physical pickup staying alive.
/// </summary>
public sealed class MagnetItemBehaviour : AnimationEventEquippedItemBehaviour
{
    [Header("References")]
    [Tooltip("Camera used to place the attraction area and raycast filter targets in front of the player.")]
    [SerializeField] private Camera PlayerCamera;

    [Tooltip("Optional explicit target point that attracted objects should move towards.")]
    [SerializeField] private Transform MagnetTargetPoint;

    [Tooltip("Optional upgrade manager used to determine whether magnet filters are unlocked.")]
    [SerializeField] private UpgradeManager UpgradeManager;

    [Tooltip("Optional feedback emitter used when magnet pull or filter events happen.")]
    [SerializeField] private GameFeedbackEmitter FeedbackEmitter;

    [Header("Magnet Area")]
    [Tooltip("Forward distance from the camera to the center of the magnet area.")]
    [SerializeField] private float AreaForwardDistance = 3.25f;

    [Tooltip("Radius of the attraction area.")]
    [SerializeField] private float AreaRadius = 2.5f;

    [Tooltip("Layers considered valid for magnetic attraction checks.")]
    [SerializeField] private LayerMask AttractionLayers = ~0;

    [Tooltip("If true, already held carryable ore pickups are ignored.")]
    [SerializeField] private bool IgnoreHeldObjects = true;

    [Header("Behaviour")]
    [Tooltip("If true, the magnet stays active continuously while the primary input remains held after activation.")]
    [SerializeField] private bool ContinuousPullWhileHeld = true;

    [Header("Performance Budget")]
    [Tooltip("Maximum amount of ore pickups that can be actively magnetized at the same time. Higher values move more ores but cost more physics time.")]
    [SerializeField] private int MaxActivePullTargets = 96;

    [Tooltip("Maximum amount of new ore pickups that can be attached during a single target refresh. This prevents waking too many rigidbodies in the same physics step.")]
    [SerializeField] private int MaxNewAttachmentsPerRefresh = 32;

    [Tooltip("Seconds between expensive attraction overlap refreshes while the magnet is active.")]
    [SerializeField] private float TargetRefreshInterval = 0.08f;

    [Tooltip("Reusable collider buffer size used by the non-alloc attraction overlap. Increase this if the debug log reports a saturated candidate buffer.")]
    [SerializeField] private int CandidateBufferSize = 512;

    [Tooltip("If true, new candidates are attached from nearest to farthest. This costs a small sort but keeps the magnet responsive with many ores.")]
    [SerializeField] private bool PreferClosestTargets = true;

    [Tooltip("If true, already attached targets are released when they move far away from the current attraction area. Keep disabled for the classic drag-and-carry magnet feel.")]
    [SerializeField] private bool ReleaseTargetsOutsideAttractionArea = false;

    [Tooltip("Extra distance beyond the attraction radius before outside-area target release is allowed.")]
    [SerializeField] private float ActiveTargetReleasePadding = 1f;

    [Header("Filter Unlocks")]
    [Tooltip("If true, filter controls are enabled without checking upgrade feature flags. Use only for local testing or always-unlocked magnet variants.")]
    [SerializeField] private bool AllowFiltersWithoutUpgradeRequirement = false;

    [Tooltip("Feature flag required to unlock one active ore-type filter.")]
    [SerializeField] private string SingleFilterFeatureFlagId = "Magnet.Unlock.SingleFilter";

    [Tooltip("Feature flag required to unlock several active ore-type filters.")]
    [SerializeField] private string MultiFilterFeatureFlagId = "Magnet.Unlock.MultiFilter";

    [Tooltip("Maximum amount of ore-type filters supported when the multi-filter feature is unlocked.")]
    [SerializeField] private int MultiFilterCapacity = 3;

    [Header("Filter Raycast")]
    [Tooltip("Maximum raycast distance used by secondary click to select an ore type as filter.")]
    [SerializeField] private float FilterRayDistance = 6f;

    [Tooltip("Layers considered by the secondary-click filter raycast.")]
    [SerializeField] private LayerMask FilterRayLayers = ~0;

    [Header("Debug")]
    [Tooltip("Draws the attraction area, target point and filter ray in the Scene view.")]
    [SerializeField] private bool DrawDebug = false;

    /// <summary>
    /// Runtime filter mode resolved from upgrade state.
    /// </summary>
    private enum MagnetFilterMode
    {
        /// <summary>
        /// Filters cannot be edited and are ignored by attraction.
        /// </summary>
        Locked = 0,

        /// <summary>
        /// One active ore type can be filtered.
        /// </summary>
        Single = 1,

        /// <summary>
        /// Several active ore types can be filtered.
        /// </summary>
        Multi = 2
    }

    /// <summary>
    /// Lightweight candidate generated during one attraction overlap refresh.
    /// </summary>
    private struct MagnetCandidate
    {
        /// <summary>
        /// Ore pickup represented by the candidate.
        /// </summary>
        public OrePickup OrePickup;

        /// <summary>
        /// Carryable component that can be attached to the magnet.
        /// </summary>
        public PhysicsCarryable Carryable;

        /// <summary>
        /// Squared distance from the magnet target point.
        /// </summary>
        public float DistanceSqr;
    }

    /// <summary>
    /// Sorts candidates from nearest to farthest without allocating comparer instances during play.
    /// </summary>
    private sealed class MagnetCandidateDistanceComparer : IComparer<MagnetCandidate>
    {
        /// <summary>
        /// Compares two candidates by squared distance.
        /// </summary>
        /// <param name="Left">First candidate.</param>
        /// <param name="Right">Second candidate.</param>
        /// <returns>Negative when left is closer, positive when right is closer.</returns>
        public int Compare(MagnetCandidate Left, MagnetCandidate Right)
        {
            return Left.DistanceSqr.CompareTo(Right.DistanceSqr);
        }
    }

    /// <summary>
    /// Shared comparer used by the candidate refresh sort.
    /// </summary>
    private static readonly MagnetCandidateDistanceComparer CandidateDistanceComparer = new MagnetCandidateDistanceComparer();

    /// <summary>
    /// Active ore filter ids shared by all runtime instances of the equipped magnet behaviour.
    /// The equipped prefab is destroyed and recreated when changing slots, so storing this statically
    /// prevents the filter from being lost during normal hotbar refreshes in a single gameplay session.
    /// </summary>
    private static readonly List<string> ActiveFilterOreIds = new List<string>();

    /// <summary>
    /// Whether the magnet is currently in its continuous active state.
    /// </summary>
    private bool IsMagnetActive;

    /// <summary>
    /// Player colliders ignored by every carryable attached to this magnet.
    /// </summary>
    [SerializeField] private Collider[] CachedPlayerColliders;

    /// <summary>
    /// List of carryables currently attached to the magnet target.
    /// </summary>
    private readonly List<PhysicsCarryable> ActiveCarryables = new List<PhysicsCarryable>();

    /// <summary>
    /// Reusable non-alloc collider buffer for attraction overlap checks.
    /// </summary>
    private Collider[] CandidateColliders = new Collider[0];

    /// <summary>
    /// Reusable candidate buffer built from the overlap result.
    /// </summary>
    private MagnetCandidate[] CandidateBuffer = new MagnetCandidate[0];

    /// <summary>
    /// Reusable set used to avoid processing the same carryable more than once when it has multiple colliders.
    /// </summary>
    private readonly HashSet<PhysicsCarryable> CandidateCarryables = new HashSet<PhysicsCarryable>();

    /// <summary>
    /// Next realtime moment when the expensive target search can be performed.
    /// </summary>
    private float NextTargetRefreshTime;

    /// <summary>
    /// Last amount of colliders returned by the overlap query, used only for debug information.
    /// </summary>
    private int LastCandidateHitCount;

    /// <summary>
    /// Last amount of candidates that passed ore, filter and carryable validation.
    /// </summary>
    private int LastResolvedCandidateCount;

    /// <summary>
    /// Forces the magnet to stop and releases every currently attached carryable.
    /// This is used by save/load so transient magnet state is never serialized.
    /// </summary>
    public void ForceStopMagnetForSave()
    {
        StopMagnetPull();
    }

    /// <summary>
    /// Gets whether at least one ore-type filter is currently active and usable.
    /// </summary>
    /// <returns>True when attraction should be restricted to configured ore types.</returns>
    public bool GetHasActiveFilters()
    {
        return GetCurrentFilterMode() != MagnetFilterMode.Locked && ActiveFilterOreIds.Count > 0;
    }

    /// <summary>
    /// Gets the amount of active ore-type filters.
    /// </summary>
    /// <returns>Current active filter count.</returns>
    public int GetActiveFilterCount()
    {
        return ActiveFilterOreIds.Count;
    }

    /// <summary>
    /// Clears every active magnet filter and optionally plays feedback.
    /// </summary>
    public void ClearFilters()
    {
        ClearFilters(true, GameFeedbackContext.FromTransform(transform));
    }

    /// <summary>
    /// Initializes the magnet and resolves missing runtime references.
    /// </summary>
    /// <param name="OwnerHotbar">Hotbar that owns this equipped item.</param>
    /// <param name="ItemInstance">Runtime item instance represented by this equipped object.</param>
    public override void Initialize(HotbarController OwnerHotbar, ItemInstance ItemInstance)
    {
        base.Initialize(OwnerHotbar, ItemInstance);

        ResolveReferences();
        ResolvePlayerColliders();
    }

    /// <summary>
    /// The magnet should only start one activation action when it is currently inactive.
    /// </summary>
    /// <returns>True when a new primary pull can start.</returns>
    protected override bool CanStartPrimaryAction()
    {
        return !IsMagnetActive;
    }

    /// <summary>
    /// While the magnet is active, the primary action should not automatically repeat.
    /// </summary>
    protected override void ProcessPendingPrimaryRepeat()
    {
        PendingPrimaryRepeat = false;
    }

    /// <summary>
    /// Handles secondary click as a direct filter command instead of an animated secondary action.
    /// </summary>
    public override void OnSecondaryUseStarted()
    {
        IsSecondaryUseActive = true;
        HandleFilterInput();
    }

    /// <summary>
    /// Ends the secondary input state without starting any secondary animation loop.
    /// </summary>
    public override void OnSecondaryUseEnded()
    {
        IsSecondaryUseActive = false;
        PendingSecondaryRepeat = false;
    }

    /// <summary>
    /// Runs the continuous magnet attach loop while the magnet is active and the input remains held.
    /// </summary>
    private void FixedUpdate()
    {
        if (!IsMagnetActive)
        {
            return;
        }

        if (!IsPrimaryUseActive)
        {
            StopMagnetPull();
            return;
        }

        ApplyMagnetPull();
    }

    /// <summary>
    /// Activates the continuous magnetic pull exactly at the animation impact frame.
    /// </summary>
    protected override void OnPrimaryActionImpact()
    {
        IsMagnetActive = true;
        ApplyMagnetPull();
        PlayFeedback(GameFeedbackEventIds.MagnetPullStarted, GameFeedbackContext.FromTransform(transform));
        Log("Magnet pull activated at animation impact time.");
    }

    /// <summary>
    /// Keeps the magnet active after the activation animation finished if the player is still holding.
    /// </summary>
    protected override void OnPrimaryActionFinished()
    {
        base.OnPrimaryActionFinished();

        if (!ContinuousPullWhileHeld)
        {
            StopMagnetPull();
            return;
        }

        if (!IsPrimaryUseActive)
        {
            StopMagnetPull();
        }
    }

    /// <summary>
    /// Releasing the primary input immediately disables the continuous magnetic pull.
    /// </summary>
    public override void OnPrimaryUseEnded()
    {
        base.OnPrimaryUseEnded();
        StopMagnetPull();
    }

    /// <summary>
    /// Stops any remaining magnet pull when the item is forcefully interrupted.
    /// </summary>
    protected override void OnForcedUsageStopped()
    {
        base.OnForcedUsageStopped();
        StopMagnetPull();
    }

    /// <summary>
    /// Ensures no ore remains attached if the equipped magnet prefab is disabled or destroyed by hotbar changes.
    /// </summary>
    private void OnDisable()
    {
        StopMagnetPull();
    }

    /// <summary>
    /// Refreshes the attraction candidate list on a budget and keeps already attached ores magnetized without recreating joints every physics step.
    /// </summary>
    private void ApplyMagnetPull()
    {
        if (PlayerCamera == null)
        {
            ResolveReferences();

            if (PlayerCamera == null)
            {
                return;
            }
        }

        Vector3 AreaCenter = GetAreaCenter();
        Transform TargetTransform = ResolveTargetTransform();

        if (TargetTransform == null)
        {
            return;
        }

        if (DrawDebug)
        {
            Debug.DrawLine(AreaCenter, TargetTransform.position, Color.magenta, Time.fixedDeltaTime);
            Debug.DrawRay(AreaCenter, Vector3.up * 0.25f, Color.yellow, Time.fixedDeltaTime);
        }

        CleanupDetachedCarryables(AreaCenter);

        if (Time.time < NextTargetRefreshTime && ActiveCarryables.Count > 0)
        {
            return;
        }

        RefreshMagnetTargets(AreaCenter, TargetTransform);
        NextTargetRefreshTime = Time.time + Mathf.Max(0.01f, TargetRefreshInterval);
    }

    /// <summary>
    /// Performs the expensive overlap query and attaches a limited number of valid ore pickups.
    /// </summary>
    /// <param name="AreaCenter">World-space center of the attraction area.</param>
    /// <param name="TargetTransform">Transform followed by magnetized carryables.</param>
    private void RefreshMagnetTargets(Vector3 AreaCenter, Transform TargetTransform)
    {
        EnsureCandidateBuffers();

        CandidateCarryables.Clear();
        LastCandidateHitCount = Physics.OverlapSphereNonAlloc(
            AreaCenter,
            AreaRadius,
            CandidateColliders,
            AttractionLayers,
            QueryTriggerInteraction.Ignore);

        int CandidateCount = BuildCandidateBuffer(AreaCenter);
        LastResolvedCandidateCount = CandidateCount;

        if (CandidateCount <= 0)
        {
            LogRefreshStatsIfNeeded();
            return;
        }

        if (PreferClosestTargets && CandidateCount > 1)
        {
            Array.Sort(CandidateBuffer, 0, CandidateCount, CandidateDistanceComparer);
        }

        int Capacity = Mathf.Max(1, MaxActivePullTargets);
        int RemainingAttachmentBudget = Mathf.Max(1, MaxNewAttachmentsPerRefresh);

        for (int CandidateIndex = 0; CandidateIndex < CandidateCount; CandidateIndex++)
        {
            if (ActiveCarryables.Count >= Capacity || RemainingAttachmentBudget <= 0)
            {
                break;
            }

            PhysicsCarryable Carryable = CandidateBuffer[CandidateIndex].Carryable;

            if (Carryable == null || ActiveCarryables.Contains(Carryable))
            {
                continue;
            }

            if (Carryable.GetIsMagnetized())
            {
                ActiveCarryables.Add(Carryable);
                continue;
            }

            Carryable.BeginMagnet(TargetTransform, CachedPlayerColliders);

            if (Carryable.GetIsMagnetized())
            {
                ActiveCarryables.Add(Carryable);
                RemainingAttachmentBudget--;
            }
        }

        LogRefreshStatsIfNeeded();
    }

    /// <summary>
    /// Builds the reusable candidate buffer from the current collider overlap result.
    /// </summary>
    /// <param name="AreaCenter">World-space center of the attraction area.</param>
    /// <returns>Amount of valid candidates written into the buffer.</returns>
    private int BuildCandidateBuffer(Vector3 AreaCenter)
    {
        int CandidateCount = 0;
        int ColliderCount = Mathf.Min(LastCandidateHitCount, CandidateColliders.Length);

        for (int HitIndex = 0; HitIndex < ColliderCount; HitIndex++)
        {
            Collider CurrentCollider = CandidateColliders[HitIndex];
            CandidateColliders[HitIndex] = null;

            if (CurrentCollider == null)
            {
                continue;
            }

            if (!TryResolveOrePickupAndCarryable(CurrentCollider, out OrePickup OrePickup, out PhysicsCarryable Carryable))
            {
                continue;
            }

            if (!CandidateCarryables.Add(Carryable))
            {
                continue;
            }

            if (!IsValidMagnetCandidate(OrePickup, Carryable))
            {
                continue;
            }

            if (CandidateCount >= CandidateBuffer.Length)
            {
                break;
            }

            Vector3 CandidatePosition = Carryable.Rigidbody != null
                ? Carryable.Rigidbody.worldCenterOfMass
                : Carryable.transform.position;

            CandidateBuffer[CandidateCount] = new MagnetCandidate
            {
                OrePickup = OrePickup,
                Carryable = Carryable,
                DistanceSqr = (CandidatePosition - AreaCenter).sqrMagnitude
            };

            CandidateCount++;
        }

        return CandidateCount;
    }

    /// <summary>
    /// Returns whether an ore pickup and carryable pair can be controlled by the magnet.
    /// </summary>
    /// <param name="OrePickup">Ore pickup being evaluated.</param>
    /// <param name="Carryable">Carryable being evaluated.</param>
    /// <returns>True when the candidate can be considered for active magnet attachment.</returns>
    private bool IsValidMagnetCandidate(OrePickup OrePickup, PhysicsCarryable Carryable)
    {
        if (OrePickup == null || Carryable == null)
        {
            return false;
        }

        if (!PassesActiveFilters(OrePickup))
        {
            return false;
        }

        if (IgnoreHeldObjects && Carryable.GetIsHeld())
        {
            return false;
        }

        if (!Carryable.CanAttachToMagnet() && !Carryable.GetIsMagnetized())
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Handles secondary click filter behaviour using the currently looked-at ore pickup.
    /// </summary>
    private void HandleFilterInput()
    {
        MagnetFilterMode FilterMode = GetCurrentFilterMode();

        if (FilterMode == MagnetFilterMode.Locked)
        {
            PlayFeedback(GameFeedbackEventIds.MagnetFilterRejected, GameFeedbackContext.FromTransform(transform));
            Log("Magnet filter input rejected because filters are locked.");
            return;
        }

        if (!TryRaycastOrePickup(out OrePickup TargetOrePickup, out RaycastHit HitInfo))
        {
            ClearFilters(true, BuildFilterFallbackContext());
            Log("Magnet filters cleared because secondary click did not hit an ore pickup.");
            return;
        }

        OreDefinition TargetOreDefinition = ResolveOreDefinition(TargetOrePickup);
        string TargetOreId = ResolveOreFilterId(TargetOreDefinition);

        if (string.IsNullOrWhiteSpace(TargetOreId))
        {
            PlayFeedback(GameFeedbackEventIds.MagnetFilterRejected, GameFeedbackContext.FromRaycastHit(HitInfo, transform));
            Log("Magnet filter input rejected because the looked-at ore pickup has no valid ore definition id.");
            return;
        }

        GameFeedbackContext FeedbackContext = GameFeedbackContext.FromRaycastHit(HitInfo, transform);

        if (FilterMode == MagnetFilterMode.Single)
        {
            ApplySingleFilter(TargetOreId, TargetOreDefinition, FeedbackContext);
            return;
        }

        ApplyMultiFilter(TargetOreId, TargetOreDefinition, FeedbackContext);
    }

    /// <summary>
    /// Applies the one-filter behaviour. Looking at the same ore type clears the filter; looking at another replaces it.
    /// </summary>
    /// <param name="TargetOreId">Ore id selected by the raycast.</param>
    /// <param name="TargetOreDefinition">Ore definition selected by the raycast.</param>
    /// <param name="FeedbackContext">Feedback context at the raycast hit point.</param>
    private void ApplySingleFilter(string TargetOreId, OreDefinition TargetOreDefinition, GameFeedbackContext FeedbackContext)
    {
        if (ActiveFilterOreIds.Count == 1 && string.Equals(ActiveFilterOreIds[0], TargetOreId, StringComparison.Ordinal))
        {
            ClearFilters(true, FeedbackContext);
            Log("Single magnet filter cleared by clicking the already-filtered ore type: " + ResolveOreDisplayName(TargetOreDefinition, TargetOreId));
            return;
        }

        ActiveFilterOreIds.Clear();
        ActiveFilterOreIds.Add(TargetOreId);
        PlayFeedback(GameFeedbackEventIds.MagnetFilterSet, FeedbackContext);
        ReleaseActiveCarryablesBlockedByFilters();
        ForceImmediateTargetRefresh();
        Log("Single magnet filter set to: " + ResolveOreDisplayName(TargetOreDefinition, TargetOreId));
    }

    /// <summary>
    /// Applies the multi-filter behaviour. Existing filters are toggled off, new filters are added until capacity is reached.
    /// </summary>
    /// <param name="TargetOreId">Ore id selected by the raycast.</param>
    /// <param name="TargetOreDefinition">Ore definition selected by the raycast.</param>
    /// <param name="FeedbackContext">Feedback context at the raycast hit point.</param>
    private void ApplyMultiFilter(string TargetOreId, OreDefinition TargetOreDefinition, GameFeedbackContext FeedbackContext)
    {
        int ExistingIndex = ActiveFilterOreIds.FindIndex(ExistingOreId => string.Equals(ExistingOreId, TargetOreId, StringComparison.Ordinal));

        if (ExistingIndex >= 0)
        {
            ActiveFilterOreIds.RemoveAt(ExistingIndex);
            PlayFeedback(GameFeedbackEventIds.MagnetFilterRemoved, FeedbackContext);
            ReleaseActiveCarryablesBlockedByFilters();
            ForceImmediateTargetRefresh();
            Log("Magnet filter removed: " + ResolveOreDisplayName(TargetOreDefinition, TargetOreId));
            return;
        }

        int Capacity = GetCurrentMultiFilterCapacity();

        if (ActiveFilterOreIds.Count >= Capacity)
        {
            PlayFeedback(GameFeedbackEventIds.MagnetFilterRejected, FeedbackContext);
            Log("Magnet filter rejected because capacity is full. Capacity: " + Capacity);
            return;
        }

        ActiveFilterOreIds.Add(TargetOreId);
        PlayFeedback(GameFeedbackEventIds.MagnetFilterAdded, FeedbackContext);
        ReleaseActiveCarryablesBlockedByFilters();
        ForceImmediateTargetRefresh();
        Log("Magnet filter added: " + ResolveOreDisplayName(TargetOreDefinition, TargetOreId));
    }

    /// <summary>
    /// Clears filters and plays feedback if requested.
    /// </summary>
    /// <param name="ShouldPlayFeedback">Whether a cleared feedback event should be played.</param>
    /// <param name="FeedbackContext">Feedback context for the clear event.</param>
    private void ClearFilters(bool ShouldPlayFeedback, GameFeedbackContext FeedbackContext)
    {
        bool HadFilters = ActiveFilterOreIds.Count > 0;
        ActiveFilterOreIds.Clear();

        if (ShouldPlayFeedback && HadFilters)
        {
            PlayFeedback(GameFeedbackEventIds.MagnetFilterCleared, FeedbackContext);
        }

        if (HadFilters)
        {
            ForceImmediateTargetRefresh();
        }
    }

    /// <summary>
    /// Detaches every carryable currently attached to this magnet.
    /// </summary>
    private void StopMagnetPull()
    {
        bool WasActive = IsMagnetActive;
        IsMagnetActive = false;

        for (int CarryableIndex = 0; CarryableIndex < ActiveCarryables.Count; CarryableIndex++)
        {
            PhysicsCarryable Carryable = ActiveCarryables[CarryableIndex];
            if (Carryable == null)
            {
                continue;
            }

            Carryable.EndMagnet();
        }

        ActiveCarryables.Clear();

        if (WasActive)
        {
            PlayFeedback(GameFeedbackEventIds.MagnetPullStopped, GameFeedbackContext.FromTransform(transform));
        }
    }

    /// <summary>
    /// Removes null, released or invalid entries from the runtime carryable list.
    /// </summary>
    /// <param name="AreaCenter">Current attraction area center used by optional outside-area release.</param>
    private void CleanupDetachedCarryables(Vector3 AreaCenter)
    {
        float ReleaseDistance = AreaRadius + Mathf.Max(0f, ActiveTargetReleasePadding);
        float ReleaseDistanceSqr = ReleaseDistance * ReleaseDistance;

        for (int CarryableIndex = ActiveCarryables.Count - 1; CarryableIndex >= 0; CarryableIndex--)
        {
            PhysicsCarryable Carryable = ActiveCarryables[CarryableIndex];
            if (Carryable == null || !Carryable.GetIsMagnetized())
            {
                ActiveCarryables.RemoveAt(CarryableIndex);
                continue;
            }

            if (ReleaseTargetsOutsideAttractionArea)
            {
                Vector3 CarryablePosition = Carryable.Rigidbody != null ? Carryable.Rigidbody.worldCenterOfMass : Carryable.transform.position;
                if ((CarryablePosition - AreaCenter).sqrMagnitude > ReleaseDistanceSqr)
                {
                    Carryable.EndMagnet();
                    ActiveCarryables.RemoveAt(CarryableIndex);
                }
            }
        }
    }

    /// <summary>
    /// Ensures reusable overlap and candidate buffers match the configured size.
    /// </summary>
    private void EnsureCandidateBuffers()
    {
        int ClampedSize = Mathf.Max(8, CandidateBufferSize);

        if (CandidateColliders == null || CandidateColliders.Length != ClampedSize)
        {
            CandidateColliders = new Collider[ClampedSize];
        }

        if (CandidateBuffer == null || CandidateBuffer.Length != ClampedSize)
        {
            CandidateBuffer = new MagnetCandidate[ClampedSize];
        }
    }

    /// <summary>
    /// Forces the next fixed update to rebuild target candidates immediately.
    /// </summary>
    private void ForceImmediateTargetRefresh()
    {
        NextTargetRefreshTime = 0f;
    }

    /// <summary>
    /// Releases currently attached ores that no longer pass the active ore-type filters.
    /// </summary>
    private void ReleaseActiveCarryablesBlockedByFilters()
    {
        if (GetCurrentFilterMode() == MagnetFilterMode.Locked || ActiveFilterOreIds.Count <= 0)
        {
            return;
        }

        for (int CarryableIndex = ActiveCarryables.Count - 1; CarryableIndex >= 0; CarryableIndex--)
        {
            PhysicsCarryable Carryable = ActiveCarryables[CarryableIndex];

            if (Carryable == null)
            {
                ActiveCarryables.RemoveAt(CarryableIndex);
                continue;
            }

            OrePickup OrePickup = ResolveOrePickupFromCarryable(Carryable);
            if (OrePickup == null || !PassesActiveFilters(OrePickup))
            {
                Carryable.EndMagnet();
                ActiveCarryables.RemoveAt(CarryableIndex);
            }
        }
    }

    /// <summary>
    /// Resolves an ore pickup from an active carryable entry.
    /// </summary>
    /// <param name="Carryable">Carryable to inspect.</param>
    /// <returns>Resolved ore pickup, or null when the carryable is no longer an ore pickup.</returns>
    private OrePickup ResolveOrePickupFromCarryable(PhysicsCarryable Carryable)
    {
        if (Carryable == null)
        {
            return null;
        }

        OrePickup OrePickup = Carryable.GetComponent<OrePickup>();

        if (OrePickup == null)
        {
            OrePickup = Carryable.GetComponentInParent<OrePickup>();
        }

        if (OrePickup == null)
        {
            OrePickup = Carryable.GetComponentInChildren<OrePickup>(true);
        }

        return OrePickup;
    }

    /// <summary>
    /// Logs budget refresh details when magnet debug logs are enabled.
    /// </summary>
    private void LogRefreshStatsIfNeeded()
    {
        if (!DebugLogs)
        {
            return;
        }

        if (LastCandidateHitCount >= CandidateColliders.Length)
        {
            Debug.LogWarning(
                "[MagnetItemBehaviour] Candidate buffer saturated. Increase Candidate Buffer Size. Size: " + CandidateColliders.Length,
                this);
        }

        Debug.Log(
            "[MagnetItemBehaviour] Refresh | Hits: " + LastCandidateHitCount +
            " | Valid: " + LastResolvedCandidateCount +
            " | Active: " + ActiveCarryables.Count +
            " | MaxActive: " + Mathf.Max(1, MaxActivePullTargets),
            this);
    }

    /// <summary>
    /// Returns whether the provided ore pickup is allowed by the currently active filters.
    /// </summary>
    /// <param name="OrePickup">Ore pickup evaluated by the magnet.</param>
    /// <returns>True when the pickup can be attracted.</returns>
    private bool PassesActiveFilters(OrePickup OrePickup)
    {
        if (OrePickup == null)
        {
            return false;
        }

        if (GetCurrentFilterMode() == MagnetFilterMode.Locked || ActiveFilterOreIds.Count <= 0)
        {
            return true;
        }

        OreDefinition OreDefinition = ResolveOreDefinition(OrePickup);
        string OreId = ResolveOreFilterId(OreDefinition);

        if (string.IsNullOrWhiteSpace(OreId))
        {
            return false;
        }

        for (int FilterIndex = 0; FilterIndex < ActiveFilterOreIds.Count; FilterIndex++)
        {
            if (string.Equals(ActiveFilterOreIds[FilterIndex], OreId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves an ore pickup and its carryable from an arbitrary collider hit by the magnet area.
    /// </summary>
    /// <param name="HitCollider">Collider found inside the magnet area.</param>
    /// <param name="OrePickup">Resolved ore pickup.</param>
    /// <param name="Carryable">Resolved carryable used for physical magnet attachment.</param>
    /// <returns>True when both components exist.</returns>
    private bool TryResolveOrePickupAndCarryable(Collider HitCollider, out OrePickup OrePickup, out PhysicsCarryable Carryable)
    {
        OrePickup = null;
        Carryable = null;

        if (HitCollider == null)
        {
            return false;
        }

        OrePickup = HitCollider.GetComponent<OrePickup>();

        if (OrePickup == null)
        {
            OrePickup = HitCollider.GetComponentInParent<OrePickup>();
        }

        if (OrePickup == null)
        {
            return false;
        }

        Carryable = HitCollider.GetComponent<PhysicsCarryable>();

        if (Carryable == null)
        {
            Carryable = HitCollider.GetComponentInParent<PhysicsCarryable>();
        }

        if (Carryable == null)
        {
            Carryable = OrePickup.GetComponentInChildren<PhysicsCarryable>(true);
        }

        return Carryable != null;
    }

    /// <summary>
    /// Raycasts from the player camera and resolves an ore pickup if the player is looking at one.
    /// </summary>
    /// <param name="OrePickup">Resolved ore pickup.</param>
    /// <param name="HitInfo">Raycast hit information.</param>
    /// <returns>True when the raycast hits an ore pickup.</returns>
    private bool TryRaycastOrePickup(out OrePickup OrePickup, out RaycastHit HitInfo)
    {
        OrePickup = null;
        HitInfo = default(RaycastHit);

        if (PlayerCamera == null)
        {
            ResolveReferences();

            if (PlayerCamera == null)
            {
                return false;
            }
        }

        Ray FilterRay = new Ray(PlayerCamera.transform.position, PlayerCamera.transform.forward);

        if (DrawDebug)
        {
            Debug.DrawRay(FilterRay.origin, FilterRay.direction * FilterRayDistance, Color.cyan, 0.2f);
        }

        if (!Physics.Raycast(FilterRay, out HitInfo, FilterRayDistance, FilterRayLayers, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        OrePickup = HitInfo.collider != null ? HitInfo.collider.GetComponent<OrePickup>() : null;

        if (OrePickup == null && HitInfo.collider != null)
        {
            OrePickup = HitInfo.collider.GetComponentInParent<OrePickup>();
        }

        return OrePickup != null;
    }

    /// <summary>
    /// Resolves the active ore definition from a pickup.
    /// </summary>
    /// <param name="OrePickup">Pickup to inspect.</param>
    /// <returns>Ore definition, or null when the pickup has no runtime payload.</returns>
    private OreDefinition ResolveOreDefinition(OrePickup OrePickup)
    {
        if (OrePickup == null || OrePickup.GetOreItemData() == null)
        {
            return null;
        }

        return OrePickup.GetOreItemData().GetOreDefinition();
    }

    /// <summary>
    /// Resolves a stable ore filter id from an ore definition.
    /// </summary>
    /// <param name="OreDefinition">Ore definition to identify.</param>
    /// <returns>Stable ore id, or a fallback asset name when the ore id is empty.</returns>
    private string ResolveOreFilterId(OreDefinition OreDefinition)
    {
        if (OreDefinition == null)
        {
            return string.Empty;
        }

        string OreId = OreDefinition.GetOreId();

        if (!string.IsNullOrWhiteSpace(OreId))
        {
            return OreId;
        }

        return OreDefinition.name;
    }

    /// <summary>
    /// Resolves a readable ore name for debug logs.
    /// </summary>
    /// <param name="OreDefinition">Ore definition to display.</param>
    /// <param name="FallbackId">Fallback id when the definition has no display name.</param>
    /// <returns>Readable ore name.</returns>
    private string ResolveOreDisplayName(OreDefinition OreDefinition, string FallbackId)
    {
        if (OreDefinition == null)
        {
            return FallbackId;
        }

        string DisplayName = OreDefinition.GetDisplayName();

        if (!string.IsNullOrWhiteSpace(DisplayName))
        {
            return DisplayName;
        }

        return !string.IsNullOrWhiteSpace(FallbackId) ? FallbackId : OreDefinition.name;
    }

    /// <summary>
    /// Resolves the currently available filter mode from upgrade state.
    /// </summary>
    /// <returns>Current filter mode.</returns>
    private MagnetFilterMode GetCurrentFilterMode()
    {
        if (AllowFiltersWithoutUpgradeRequirement)
        {
            return MagnetFilterMode.Multi;
        }

        if (UpgradeManager == null)
        {
            ResolveReferences();
        }

        if (UpgradeManager == null)
        {
            return MagnetFilterMode.Locked;
        }

        if (UpgradeManager.IsFeatureUnlocked(MultiFilterFeatureFlagId))
        {
            return MagnetFilterMode.Multi;
        }

        if (UpgradeManager.IsFeatureUnlocked(SingleFilterFeatureFlagId))
        {
            return MagnetFilterMode.Single;
        }

        return MagnetFilterMode.Locked;
    }

    /// <summary>
    /// Gets the current multi-filter capacity.
    /// </summary>
    /// <returns>Clamped multi-filter capacity.</returns>
    private int GetCurrentMultiFilterCapacity()
    {
        return Mathf.Max(2, MultiFilterCapacity);
    }

    /// <summary>
    /// Computes the world-space center of the spherical attraction area.
    /// </summary>
    /// <returns>World-space area center.</returns>
    private Vector3 GetAreaCenter()
    {
        return PlayerCamera.transform.position + PlayerCamera.transform.forward * AreaForwardDistance;
    }

    /// <summary>
    /// Resolves the transform used as the runtime spring-anchor target.
    /// </summary>
    /// <returns>Target transform used by magnetized objects.</returns>
    private Transform ResolveTargetTransform()
    {
        return MagnetTargetPoint != null ? MagnetTargetPoint : transform;
    }

    /// <summary>
    /// Builds a fallback feedback context in front of the camera when no surface was hit.
    /// </summary>
    /// <returns>Fallback feedback context.</returns>
    private GameFeedbackContext BuildFilterFallbackContext()
    {
        if (PlayerCamera != null)
        {
            return GameFeedbackContext.FromPosition(
                PlayerCamera.transform.position + PlayerCamera.transform.forward * Mathf.Min(FilterRayDistance, 2f),
                transform);
        }

        return GameFeedbackContext.FromTransform(transform);
    }

    /// <summary>
    /// Plays one feedback event if a feedback emitter is available.
    /// </summary>
    /// <param name="EventId">Feedback event id.</param>
    /// <param name="Context">Runtime feedback context.</param>
    private void PlayFeedback(string EventId, GameFeedbackContext Context)
    {
        if (FeedbackEmitter == null)
        {
            ResolveReferences();
        }

        if (FeedbackEmitter == null)
        {
            return;
        }

        FeedbackEmitter.Play(EventId, Context);
    }

    /// <summary>
    /// Resolves optional scene and prefab references.
    /// </summary>
    private void ResolveReferences()
    {
        if (PlayerCamera == null && OwnerHotbar != null)
        {
            PlayerCamera = OwnerHotbar.GetComponentInChildren<Camera>();
        }

        if (PlayerCamera == null)
        {
            PlayerCamera = Camera.main;
        }

        if (UpgradeManager == null)
        {
            UpgradeManager = FindFirstObjectByType<UpgradeManager>();
        }

        if (FeedbackEmitter == null)
        {
            FeedbackEmitter = GetComponent<GameFeedbackEmitter>();
        }

        if (FeedbackEmitter == null)
        {
            FeedbackEmitter = GetComponentInChildren<GameFeedbackEmitter>(true);
        }

        if (FeedbackEmitter == null)
        {
            FeedbackEmitter = GetComponentInParent<GameFeedbackEmitter>();
        }
    }

    /// <summary>
    /// Resolves the player colliders ignored by every magnet attachment.
    /// </summary>
    private void ResolvePlayerColliders()
    {
        if (OwnerHotbar == null)
        {
            return;
        }

        PlayerInteractionController InteractionController = OwnerHotbar.GetComponent<PlayerInteractionController>();
        if (InteractionController == null)
        {
            InteractionController = OwnerHotbar.GetComponentInParent<PlayerInteractionController>(true);
        }

        if (InteractionController != null)
        {
            CachedPlayerColliders = InteractionController.GetPlayerColliders();
        }

        if (CachedPlayerColliders == null || CachedPlayerColliders.Length == 0)
        {
            CachedPlayerColliders = PhysicsUtils.GetHierarchyColliders(OwnerHotbar.gameObject, true);
        }
    }

    /// <summary>
    /// Writes a magnet-specific debug message when logging is enabled.
    /// </summary>
    /// <param name="Message">Message to log.</param>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[MagnetItemBehaviour] " + Message, this);
    }
}
