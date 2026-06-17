using System.Collections.Generic;
using MoreMountains.Feedbacks;
using UnityEngine;

/// <summary>
/// Runtime mineable ore vein.
/// This component handles mining hits, robust drop spawning, depletion state and visual regrowth.
/// </summary>
public sealed class OreVein : MonoBehaviour, IMineable
{
    private enum VeinState
    {
        Growing = 0,
        Ready = 1
    }

    [Header("References")]
    [Tooltip("Optional visual root scaled during regrowth. If empty, this transform is used.")]
    [SerializeField] private Transform VisualRoot;

    [Tooltip("Optional explicit world point used as the preferred ore drop origin. If empty, this transform is used.")]
    [SerializeField] private Transform DropOrigin;

    [Header("Game Feedback")]
    [Tooltip("Generic feedback emitter used by this vein to play ore-specific particles, VFX, decals, audio and Feel bindings.")]
    [SerializeField] private GameFeedbackEmitter FeedbackEmitter;

    [Tooltip("If true, this vein emits generic ore feedback events in addition to any direct Feel players still assigned below.")]
    [SerializeField] private bool UseGameFeedback = true;

    [Tooltip("Event played when this vein accepts mining damage but does not break.")]
    [SerializeField] private string OreHitEventId = GameFeedbackEventIds.OreHit;

    [Tooltip("Event played when this vein breaks and starts regrowing.")]
    [SerializeField] private string OreBreakEventId = GameFeedbackEventIds.OreBreak;

    [Tooltip("Event played when a mining hit is rejected because the source tier is too low.")]
    [SerializeField] private string OreInsufficientTierEventId = GameFeedbackEventIds.OreInsufficientTier;

    [Tooltip("Event played when this vein cannot accept mining because it is unavailable or missing runtime data.")]
    [SerializeField] private string OreTargetUnavailableEventId = GameFeedbackEventIds.OreTargetUnavailable;

    [Tooltip("Event played when a mining request reaches the vein but carries no usable damage.")]
    [SerializeField] private string OreNoDamageEventId = GameFeedbackEventIds.OreNoDamage;

    [Tooltip("Event played when this vein finishes regrowing and becomes mineable again.")]
    [SerializeField] private string OreRegrownEventId = GameFeedbackEventIds.OreRegrown;

    [Tooltip("If true, the regular ore hit feedback also plays on the final hit that breaks the vein.")]
    [SerializeField] private bool PlayOreHitFeedbackOnBreakingHit = false;

    [Tooltip("If true, the emitter is resolved from this GameObject when it is not assigned manually.")]
    [SerializeField] private bool AutoResolveFeedbackEmitter = true;

    [Header("Direct Feel Feedbacks")]
    [Tooltip("Feel player triggered every time this vein accepts a mining hit.")]
    [SerializeField] private MMF_Player HitFeedbacks;

    [Tooltip("Feel player triggered when this vein breaks after the final mining hit.")]
    [SerializeField] private MMF_Player BreakFeedbacks;

    [Tooltip("Feel player triggered when a mining hit is rejected because the tool tier is too low.")]
    [SerializeField] private MMF_Player RejectedHitFeedbacks;

    [Tooltip("Intensity passed to the hit feedback player.")]
    [SerializeField] private float HitFeedbackIntensity = 1f;

    [Tooltip("Intensity passed to the break feedback player.")]
    [SerializeField] private float BreakFeedbackIntensity = 1f;

    [Tooltip("Intensity passed to the rejected hit feedback player.")]
    [SerializeField] private float RejectedHitFeedbackIntensity = 1f;

    [Tooltip("If true, the direct Feel hit feedback also plays on the final hit that breaks the vein.")]
    [SerializeField] private bool PlayHitFeedbackOnBreakingHit = true;

    [Header("Regrowth")]
    [Tooltip("Minimum scale used while the ore is regrowing.")]
    [SerializeField] private float MinimumGrowthScale = 0.05f;

    [Tooltip("If true, the ore regrowth is animated by scaling the visual root.")]
    [SerializeField] private bool AnimateGrowth = true;

    [Header("Drops")]
    [Tooltip("Base horizontal radius used to spread multiple drops around the origin.")]
    [SerializeField] private float DropScatterRadius = 0.45f;

    [Tooltip("Base vertical offset applied to dropped ore spawn points.")]
    [SerializeField] private float DropVerticalOffset = 0.2f;

    [Header("Safe Spawn")]
    [Tooltip("Approximate clearance radius used to keep ore spawns away from walls and from each other.")]
    [SerializeField] private float SpawnClearanceRadius = 0.2f;

    [Tooltip("Maximum amount of candidate positions tested per ore drop before using the last safe fallback.")]
    [SerializeField] private int MaxSpawnAttemptsPerDrop = 12;

    [Tooltip("Horizontal distance added on each retry while searching for a valid spawn point.")]
    [SerializeField] private float SpawnRadiusStep = 0.18f;

    [Tooltip("Vertical distance added on each retry while searching for a valid spawn point.")]
    [SerializeField] private float SpawnHeightStep = 0.12f;

    [Tooltip("Additional random vertical variation applied to each drop spawn after the base vertical offset.")]
    [SerializeField] private float DropVerticalJitter = 0.08f;

    [Tooltip("Random yaw rotation applied to each spawned ore pickup.")]
    [SerializeField] private bool RandomizeYawRotation = true;

    [Tooltip("If true, a subtle random pitch and roll are also applied to the spawned ore pickup.")]
    [SerializeField] private bool RandomizeTiltRotation = true;

    [Tooltip("Maximum absolute random pitch applied when tilt randomization is enabled.")]
    [SerializeField] private float MaxRandomPitch = 12f;

    [Tooltip("Maximum absolute random roll applied when tilt randomization is enabled.")]
    [SerializeField] private float MaxRandomRoll = 12f;

    [Header("Debug")]
    [Tooltip("Logs mining, spawning and regeneration operations.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Reusable overlap buffer used to validate candidate spawn positions without allocations.
    /// </summary>
    private static readonly Collider[] SpawnOverlapBuffer = new Collider[32];

    /// <summary>
    /// Current ore definition used by this vein.
    /// </summary>
    private OreDefinition OreDefinition;

    /// <summary>
    /// Runtime service used to resolve drops and values.
    /// </summary>
    private OreRuntimeService OreRuntimeService;

    /// <summary>
    /// Spawn point that owns this vein instance.
    /// </summary>
    private OreSpawnPoint OwnerSpawnPoint;

    /// <summary>
    /// Current vein runtime state.
    /// </summary>
    private VeinState CurrentState = VeinState.Ready;

    /// <summary>
    /// Remaining mining durability required before the vein breaks.
    /// </summary>
    private int CurrentMiningDurabilityRemaining;

    /// <summary>
    /// Remaining respawn time while the vein is regrowing.
    /// </summary>
    private float CurrentRespawnTimer;

    /// <summary>
    /// Last valid mining context that affected this vein.
    /// The context that actually causes the break is the one consumed by the drop logic.
    /// </summary>
    private MiningHitContext LastMiningHitContext = default;

    /// <summary>
    /// Extraction quality multiplier captured from the hit that breaks this vein.
    /// This multiplier is applied to generated ore purity when drops are created.
    /// </summary>
    private float LastExtractionQualityMultiplier = 1f;

    /// <summary>
    /// Soft cached reference to the elevator magnet resolved for recent spawns.
    /// </summary>
    private ElevatorOreSpawnMagnet CachedElevatorOreSpawnMagnet;

    /// <summary>
    /// Gets the ore definition currently used by this vein.
    /// This is used by external systems such as the scanner.
    /// </summary>
    public OreDefinition GetOreDefinition()
    {
        return OreDefinition;
    }

    /// <summary>
    /// Gets the mining tier required to damage this vein.
    /// </summary>
    /// <returns>Required mining tier, or TierI when no definition is available.</returns>
    public MiningTier GetRequiredMiningTier()
    {
        return OreDefinition != null ? OreDefinition.GetRequiredMiningTier() : MiningTier.TierI;
    }

    /// <summary>
    /// Gets whether this vein is currently regrowing.
    /// </summary>
    public bool GetIsGrowing()
    {
        return CurrentState == VeinState.Growing;
    }

    /// <summary>
    /// Gets the current remaining mining durability for this vein.
    /// </summary>
    public int GetCurrentMiningDurabilityRemaining()
    {
        return Mathf.Max(0, CurrentMiningDurabilityRemaining);
    }


    /// <summary>
    /// Gets the remaining regrowth timer for this vein.
    /// </summary>
    public float GetCurrentRespawnTimer()
    {
        return Mathf.Max(0f, CurrentRespawnTimer);
    }

    /// <summary>
    /// Restores the runtime state of this vein after it has been spawned from its saved ore definition.
    /// </summary>
    /// <param name="IsGrowingValue">True if the vein should be regrowing.</param>
    /// <param name="MiningDurabilityRemainingValue">Saved remaining mining durability for ready veins.</param>
    /// <param name="RespawnTimerRemainingValue">Saved remaining regrowth timer.</param>
    public void ApplySavedRuntimeState(bool IsGrowingValue, int MiningDurabilityRemainingValue, float RespawnTimerRemainingValue)
    {
        if (OreDefinition == null || OreRuntimeService == null)
        {
            return;
        }

        if (IsGrowingValue)
        {
            CurrentState = VeinState.Growing;
            CurrentRespawnTimer = Mathf.Max(0f, RespawnTimerRemainingValue);
            CurrentMiningDurabilityRemaining = 0;

            float RespawnDuration = Mathf.Max(0.01f, OreRuntimeService.ResolveRespawnTime(OreDefinition));
            float NormalizedProgress = 1f - Mathf.Clamp01(CurrentRespawnTimer / RespawnDuration);
            UpdateGrowthVisual(NormalizedProgress);

            if (CurrentRespawnTimer <= 0f)
            {
                ResetReadyState();
            }

            return;
        }

        CurrentState = VeinState.Ready;
        CurrentRespawnTimer = 0f;
        CurrentMiningDurabilityRemaining = Mathf.Clamp(
            MiningDurabilityRemainingValue,
            1,
            Mathf.Max(1, OreRuntimeService.ResolveMiningDurability(OreDefinition)));

        UpdateGrowthVisual(1f);
    }

    /// <summary>
    /// Initializes this ore vein with its definition, runtime service and owner point.
    /// </summary>
    /// <param name="OreDefinitionValue">Definition used by this ore vein.</param>
    /// <param name="OreRuntimeServiceValue">Runtime service used to resolve ore values and drops.</param>
    /// <param name="OwnerSpawnPointValue">Spawn point that owns this vein instance.</param>
    public void Initialize(OreDefinition OreDefinitionValue, OreRuntimeService OreRuntimeServiceValue, OreSpawnPoint OwnerSpawnPointValue)
    {
        OreDefinition = OreDefinitionValue;
        OreRuntimeService = OreRuntimeServiceValue;
        OwnerSpawnPoint = OwnerSpawnPointValue;
        LastMiningHitContext = MiningHitContext.CreateUnknown();
        LastExtractionQualityMultiplier = 1f;

        if (VisualRoot == null)
        {
            VisualRoot = transform;
        }

        ResolveFeedbackEmitter();
        ResetReadyState();
    }

    /// <summary>
    /// Updates regrowth if the vein is currently regenerating.
    /// </summary>
    private void Update()
    {
        if (CurrentState != VeinState.Growing)
        {
            return;
        }

        if (OreRuntimeService == null || OreDefinition == null)
        {
            return;
        }

        CurrentRespawnTimer -= Time.deltaTime;

        float RespawnDuration = Mathf.Max(0.01f, OreRuntimeService.ResolveRespawnTime(OreDefinition));
        float NormalizedProgress = 1f - Mathf.Clamp01(CurrentRespawnTimer / RespawnDuration);

        UpdateGrowthVisual(NormalizedProgress);

        if (CurrentRespawnTimer <= 0f)
        {
            ResetReadyState();
        }
    }


    /// <summary>
    /// Attempts to apply one complete mining request to this ore vein.
    /// This validates state, tier and damage before modifying the vein.
    /// </summary>
    /// <param name="MiningRequest">Complete mining request containing damage, tier, extraction quality and hit context.</param>
    /// <returns>Detailed mining result.</returns>
    public MiningHitResult TryMine(MiningHitRequest MiningRequest)
    {
        if (CurrentState != VeinState.Ready || OreDefinition == null || OreRuntimeService == null)
        {
            PlayOreGameFeedback(OreTargetUnavailableEventId, MiningRequest.HitContext, 1f);
            return MiningHitResult.TargetUnavailable();
        }

        MiningTier RequiredTier = OreDefinition.GetRequiredMiningTier();

        if ((int)MiningRequest.MiningTier < (int)RequiredTier)
        {
            PlayOreGameFeedback(OreInsufficientTierEventId, MiningRequest.HitContext, 1f);
            PlayRejectedHitFeedback(MiningRequest.HitContext);
            Log("Mining hit rejected. Required tier: " + RequiredTier + " | Source tier: " + MiningRequest.MiningTier);
            return MiningHitResult.InsufficientTier(RequiredTier, MiningRequest.MiningTier, CurrentMiningDurabilityRemaining);
        }

        int DamageToApply = Mathf.CeilToInt(MiningRequest.MiningDamage);

        if (DamageToApply <= 0)
        {
            PlayOreGameFeedback(OreNoDamageEventId, MiningRequest.HitContext, 1f);
            return MiningHitResult.NoDamage(CurrentMiningDurabilityRemaining);
        }

        LastMiningHitContext = MiningRequest.HitContext;
        LastExtractionQualityMultiplier = Mathf.Max(0.01f, MiningRequest.ExtractionQualityMultiplier);
        CurrentMiningDurabilityRemaining -= DamageToApply;

        bool IsBreakingHit = CurrentMiningDurabilityRemaining <= 0;

        if (PlayOreHitFeedbackOnBreakingHit || !IsBreakingHit)
        {
            PlayOreGameFeedback(OreHitEventId, MiningRequest.HitContext, 1f);
        }

        if (PlayHitFeedbackOnBreakingHit || !IsBreakingHit)
        {
            PlayHitFeedback(MiningRequest.HitContext);
        }

        Log("Ore vein hit. Damage: " + DamageToApply + " | Remaining mining durability: " + CurrentMiningDurabilityRemaining);

        if (IsBreakingHit)
        {
            BreakVein();
        }

        return MiningHitResult.Accepted(DamageToApply, Mathf.Max(0, CurrentMiningDurabilityRemaining));
    }


    /// <summary>
    /// Gets whether this vein is currently mineable.
    /// </summary>
    /// <returns>True when the vein is ready to receive mining hits.</returns>
    public bool GetIsReady()
    {
        return CurrentState == VeinState.Ready;
    }

    /// <summary>
    /// Breaks the vein, spawns ore drops and starts regrowth.
    /// </summary>
    private void BreakVein()
    {
        PlayOreGameFeedback(OreBreakEventId, LastMiningHitContext, 1f);
        PlayBreakFeedback(LastMiningHitContext);

        int DropCount = OreRuntimeService.ResolveDropCount(OreDefinition);
        List<Vector3> ReservedSpawnPositions = new List<Vector3>(DropCount);

        for (int Index = 0; Index < DropCount; Index++)
        {
            OreItemData OreItemData = OreRuntimeService.CreateOreItemData(OreDefinition, LastExtractionQualityMultiplier);

            if (OreItemData == null)
            {
                continue;
            }

            Vector3 DropPosition = ResolveRobustDropSpawnPosition(Index, DropCount, ReservedSpawnPositions);
            Quaternion DropRotation = GetRandomDropRotation();

            ReservedSpawnPositions.Add(DropPosition);
            SpawnDropWithOptionalPlayerElevatorAssist(OreItemData, DropPosition, DropRotation);
        }

        LastMiningHitContext = MiningHitContext.CreateUnknown();
        LastExtractionQualityMultiplier = 1f;
        StartRegrowth();
        Log("Ore vein broken and " + DropCount + " drops were spawned.");
    }

    /// <summary>
    /// Resolves a robust world spawn position for one drop.
    /// The position is separated from already reserved drops and must be free from blocking geometry.
    /// </summary>
    /// <param name="DropIndex">Index of the drop being spawned in this break event.</param>
    /// <param name="TotalDropCount">Total amount of drops being spawned in this break event.</param>
    /// <param name="ReservedSpawnPositions">Already accepted spawn positions for previous drops.</param>
    /// <returns>Resolved world spawn position.</returns>
    private Vector3 ResolveRobustDropSpawnPosition(int DropIndex, int TotalDropCount, List<Vector3> ReservedSpawnPositions)
    {
        float BaseJitteredHeight = DropVerticalOffset + Random.Range(-Mathf.Abs(DropVerticalJitter), Mathf.Abs(DropVerticalJitter));
        Vector3 BasePosition = GetDropOriginPosition() + (Vector3.up * BaseJitteredHeight);

        float ClearanceRadius = Mathf.Max(0.05f, SpawnClearanceRadius);
        float SeparationDistance = ClearanceRadius * 2f;

        Vector3 LastValidFallback = BasePosition + (Vector3.up * Mathf.Max(0f, SpawnHeightStep));

        for (int AttemptIndex = 0; AttemptIndex < Mathf.Max(1, MaxSpawnAttemptsPerDrop); AttemptIndex++)
        {
            Vector3 CandidateOffset = GetSpawnPatternOffset(DropIndex, TotalDropCount, AttemptIndex);
            Vector3 CandidatePosition = BasePosition + CandidateOffset;

            if (!IsFarEnoughFromReservedSpawns(CandidatePosition, ReservedSpawnPositions, SeparationDistance))
            {
                continue;
            }

            if (!IsWorldPositionFree(CandidatePosition, ClearanceRadius))
            {
                continue;
            }

            return CandidatePosition;
        }

        Log("Failed to resolve a fully clean drop spawn. Using elevated fallback.");
        return LastValidFallback;
    }


    /// <summary>
    /// Builds a subtle random rotation for a spawned ore pickup so repeated drops do not look identical.
    /// </summary>
    private Quaternion GetRandomDropRotation()
    {
        float Yaw = RandomizeYawRotation ? Random.Range(0f, 360f) : 0f;
        float Pitch = RandomizeTiltRotation ? Random.Range(-Mathf.Abs(MaxRandomPitch), Mathf.Abs(MaxRandomPitch)) : 0f;
        float Roll = RandomizeTiltRotation ? Random.Range(-Mathf.Abs(MaxRandomRoll), Mathf.Abs(MaxRandomRoll)) : 0f;

        return Quaternion.Euler(Pitch, Yaw, Roll);
    }

    /// <summary>
    /// Builds a deterministic spread pattern so multiple drops do not spawn on top of each other.
    /// It expands horizontally and vertically across retries.
    /// </summary>
    /// <param name="DropIndex">Index of the drop being spawned.</param>
    /// <param name="TotalDropCount">Total amount of drops spawned in the current break.</param>
    /// <param name="AttemptIndex">Current retry index for this drop.</param>
    /// <returns>Offset from the base drop origin.</returns>
    private Vector3 GetSpawnPatternOffset(int DropIndex, int TotalDropCount, int AttemptIndex)
    {
        if (TotalDropCount <= 1 && AttemptIndex == 0)
        {
            return Vector3.zero;
        }

        float BaseAngle = 360f / Mathf.Max(1, TotalDropCount);
        float AttemptAngleOffset = 41f * AttemptIndex;
        float AngleDegrees = (DropIndex * BaseAngle) + AttemptAngleOffset;
        float AngleRadians = AngleDegrees * Mathf.Deg2Rad;

        float Radius = Mathf.Max(0f, DropScatterRadius) + (AttemptIndex * Mathf.Max(0f, SpawnRadiusStep));
        float Height = AttemptIndex * Mathf.Max(0f, SpawnHeightStep);

        return new Vector3(
            Mathf.Cos(AngleRadians) * Radius,
            Height,
            Mathf.Sin(AngleRadians) * Radius);
    }

    /// <summary>
    /// Returns whether the candidate spawn position is far enough from already reserved ore spawns.
    /// This prevents multiple drops from appearing inside each other during the same break event.
    /// </summary>
    /// <param name="CandidatePosition">Candidate spawn position being evaluated.</param>
    /// <param name="ReservedSpawnPositions">Already accepted spawn positions.</param>
    /// <param name="MinimumDistance">Minimum allowed distance between drops.</param>
    /// <returns>True when the candidate is sufficiently separated.</returns>
    private bool IsFarEnoughFromReservedSpawns(Vector3 CandidatePosition, List<Vector3> ReservedSpawnPositions, float MinimumDistance)
    {
        if (ReservedSpawnPositions == null || ReservedSpawnPositions.Count == 0)
        {
            return true;
        }

        float MinimumDistanceSqr = MinimumDistance * MinimumDistance;

        for (int Index = 0; Index < ReservedSpawnPositions.Count; Index++)
        {
            if ((ReservedSpawnPositions[Index] - CandidatePosition).sqrMagnitude < MinimumDistanceSqr)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns whether the candidate world position is free of blocking geometry.
    /// The check is automatic, ignores triggers and ignores the vein's own hierarchy.
    /// </summary>
    /// <param name="CandidatePosition">World spawn point to validate.</param>
    /// <param name="ClearanceRadius">Approximate ore clearance radius.</param>
    /// <returns>True when the candidate position is free enough to use.</returns>
    private bool IsWorldPositionFree(Vector3 CandidatePosition, float ClearanceRadius)
    {
        int HitCount = Physics.OverlapSphereNonAlloc(
            CandidatePosition,
            ClearanceRadius,
            SpawnOverlapBuffer,
            ~0,
            QueryTriggerInteraction.Ignore);

        for (int Index = 0; Index < HitCount; Index++)
        {
            Collider HitCollider = SpawnOverlapBuffer[Index];

            if (HitCollider == null)
            {
                continue;
            }

            if (HitCollider.transform.IsChildOf(transform))
            {
                continue;
            }

            if (OwnerSpawnPoint != null && HitCollider.transform.IsChildOf(OwnerSpawnPoint.transform))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns the preferred world drop origin.
    /// Uses the explicit drop origin when assigned, otherwise falls back to the vein transform.
    /// </summary>
    /// <returns>World position used as the base drop origin.</returns>
    private Vector3 GetDropOriginPosition()
    {
        return DropOrigin != null ? DropOrigin.position : transform.position;
    }

    /// <summary>
    /// Spawns one ore pickup and applies the optional elevator spawn assist only
    /// when the breaking hit was explicitly caused by the player.
    /// </summary>
    /// <param name="OreItemData">Runtime ore payload to spawn.</param>
    /// <param name="DropPosition">World spawn position.</param>
    private void SpawnDropWithOptionalPlayerElevatorAssist(OreItemData OreItemData, Vector3 DropPosition, Quaternion DropRotation)
    {
        if (OreRuntimeService == null || OreItemData == null)
        {
            return;
        }

        GameObject SpawnedOreObject = OreRuntimeService.SpawnOrePickup(
            OreItemData,
            DropPosition,
            DropRotation);

        if (SpawnedOreObject == null)
        {
            return;
        }

        if (!LastMiningHitContext.IsPlayerSource())
        {
            return;
        }

        ElevatorOreSpawnMagnet ElevatorOreSpawnMagnet = ElevatorOreSpawnMagnet.FindBestForPoint(DropPosition);

        if (ElevatorOreSpawnMagnet == null)
        {
            return;
        }

        CachedElevatorOreSpawnMagnet = ElevatorOreSpawnMagnet;
        ElevatorOreSpawnMagnet.TryAssistSpawnedOre(SpawnedOreObject, DropPosition);
    }

    /// <summary>
    /// Starts the regrowth process after the vein has been mined.
    /// </summary>
    private void StartRegrowth()
    {
        CurrentState = VeinState.Growing;
        CurrentRespawnTimer = OreRuntimeService != null && OreDefinition != null
            ? OreRuntimeService.ResolveRespawnTime(OreDefinition)
            : 0f;

        UpdateGrowthVisual(0f);
    }

    /// <summary>
    /// Resets the vein to a fully grown, mineable state.
    /// </summary>
    private void ResetReadyState()
    {
        bool WasGrowing = CurrentState == VeinState.Growing;

        CurrentState = VeinState.Ready;
        CurrentRespawnTimer = 0f;
        CurrentMiningDurabilityRemaining = OreRuntimeService != null && OreDefinition != null
            ? OreRuntimeService.ResolveMiningDurability(OreDefinition)
            : 1;

        UpdateGrowthVisual(1f);

        if (WasGrowing)
        {
            PlayOreGameFeedback(OreRegrownEventId, MiningHitContext.CreateUnknown(), 1f);
        }

        Log("Ore vein is ready again.");
    }

    /// <summary>
    /// Updates the visual scale of the vein according to a normalized regrowth progress.
    /// </summary>
    /// <param name="NormalizedProgress">Normalized growth progress in the [0, 1] range.</param>
    private void UpdateGrowthVisual(float NormalizedProgress)
    {
        if (!AnimateGrowth || VisualRoot == null)
        {
            return;
        }

        float ClampedProgress = Mathf.Clamp01(NormalizedProgress);
        float ScaleMultiplier = Mathf.Lerp(MinimumGrowthScale, 1f, ClampedProgress);
        VisualRoot.localScale = Vector3.one * ScaleMultiplier;
    }

    /// <summary>
    /// Resolves the generic feedback emitter used by this ore vein.
    /// </summary>
    private void ResolveFeedbackEmitter()
    {
        if (FeedbackEmitter != null || !AutoResolveFeedbackEmitter)
        {
            return;
        }

        FeedbackEmitter = GetComponent<GameFeedbackEmitter>();

        if (FeedbackEmitter == null)
        {
            FeedbackEmitter = GetComponentInChildren<GameFeedbackEmitter>(true);
        }
    }

    /// <summary>
    /// Plays one generic ore feedback event using the mining context as world placement data.
    /// </summary>
    /// <param name="EventId">Stable feedback event id to play.</param>
    /// <param name="HitContext">Mining context that caused the feedback.</param>
    /// <param name="Intensity">Feedback intensity multiplier.</param>
    private void PlayOreGameFeedback(string EventId, MiningHitContext HitContext, float Intensity)
    {
        if (!UseGameFeedback || string.IsNullOrWhiteSpace(EventId))
        {
            return;
        }

        ResolveFeedbackEmitter();

        if (FeedbackEmitter == null)
        {
            return;
        }

        FeedbackEmitter.Play(EventId, CreateOreFeedbackContext(HitContext, Intensity));
    }

    /// <summary>
    /// Creates a generic feedback context from a mining hit context.
    /// </summary>
    /// <param name="HitContext">Mining context that may contain hit point and normal.</param>
    /// <param name="Intensity">Feedback intensity multiplier.</param>
    /// <returns>Generic feedback context for this ore vein.</returns>
    private GameFeedbackContext CreateOreFeedbackContext(MiningHitContext HitContext, float Intensity)
    {
        Transform SourceTransform = HitContext.SourceObject != null ? HitContext.SourceObject.transform : null;
        Vector3 FeedbackPosition = HitContext.GetFeedbackPosition(transform.position);
        bool HasPosition = HitContext.HasWorldPoint || transform != null;
        bool HasNormal = HitContext.HasWorldPoint && HitContext.WorldNormal.sqrMagnitude > 0.0001f;
        Vector3 FeedbackNormal = HasNormal ? HitContext.WorldNormal : transform.up;

        return new GameFeedbackContext(
            HasPosition,
            FeedbackPosition,
            HasNormal,
            FeedbackNormal,
            SourceTransform,
            transform,
            transform,
            Mathf.Max(0f, Intensity));
    }

    /// <summary>
    /// Plays the configured Feel hit feedback at the mining impact position.
    /// </summary>
    /// <param name="HitContext">Context that contains source and optional impact data.</param>
    private void PlayHitFeedback(MiningHitContext HitContext)
    {
        if (HitFeedbacks == null)
        {
            return;
        }

        Vector3 FeedbackPosition = HitContext.GetFeedbackPosition(transform.position);
        HitFeedbacks.PlayFeedbacks(FeedbackPosition, Mathf.Max(0f, HitFeedbackIntensity));
    }

    /// <summary>
    /// Plays the configured Feel break feedback at the mining impact position.
    /// </summary>
    /// <param name="HitContext">Context that contains source and optional impact data.</param>
    private void PlayBreakFeedback(MiningHitContext HitContext)
    {
        if (BreakFeedbacks == null)
        {
            return;
        }

        Vector3 FeedbackPosition = HitContext.GetFeedbackPosition(transform.position);
        BreakFeedbacks.PlayFeedbacks(FeedbackPosition, Mathf.Max(0f, BreakFeedbackIntensity));
    }

    /// <summary>
    /// Plays the configured rejected hit feedback at the mining impact position.
    /// </summary>
    /// <param name="HitContext">Context that contains source and optional impact data.</param>
    private void PlayRejectedHitFeedback(MiningHitContext HitContext)
    {
        if (RejectedHitFeedbacks == null)
        {
            return;
        }

        Vector3 FeedbackPosition = HitContext.GetFeedbackPosition(transform.position);
        RejectedHitFeedbacks.PlayFeedbacks(FeedbackPosition, Mathf.Max(0f, RejectedHitFeedbackIntensity));
    }

    /// <summary>
    /// Logs ore vein messages if debug logging is enabled.
    /// </summary>
    /// <param name="Message">Message to log.</param>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[OreVein] " + Message, this);
    }

    /// <summary>
    /// Releases ownership from its spawn point when destroyed.
    /// </summary>
    private void OnDestroy()
    {
        if (OwnerSpawnPoint != null)
        {
            OwnerSpawnPoint.NotifyVeinReleased(this);
        }
    }
}