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

    [Header("Feel Feedbacks")]
    [Tooltip("Feel player triggered every time this vein accepts a mining hit.")]
    [SerializeField] private MMF_Player HitFeedbacks;

    [Tooltip("Feel player triggered when this vein breaks after the final mining hit.")]
    [SerializeField] private MMF_Player BreakFeedbacks;

    [Tooltip("Intensity passed to the hit feedback player.")]
    [SerializeField] private float HitFeedbackIntensity = 1f;

    [Tooltip("Intensity passed to the break feedback player.")]
    [SerializeField] private float BreakFeedbackIntensity = 1f;

    [Tooltip("If true, the regular hit feedback also plays on the final hit that breaks the vein.")]
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
    /// Remaining hits required before the vein breaks.
    /// </summary>
    private int CurrentHitsRemaining;

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
    /// Gets whether this vein is currently regrowing.
    /// </summary>
    public bool GetIsGrowing()
    {
        return CurrentState == VeinState.Growing;
    }

    /// <summary>
    /// Gets the current remaining hit count for this vein.
    /// </summary>
    public int GetCurrentHitsRemaining()
    {
        return Mathf.Max(0, CurrentHitsRemaining);
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
    /// <param name="HitsRemainingValue">Saved remaining hit count for ready veins.</param>
    /// <param name="RespawnTimerRemainingValue">Saved remaining regrowth timer.</param>
    public void ApplySavedRuntimeState(bool IsGrowingValue, int HitsRemainingValue, float RespawnTimerRemainingValue)
    {
        if (OreDefinition == null || OreRuntimeService == null)
        {
            return;
        }

        if (IsGrowingValue)
        {
            CurrentState = VeinState.Growing;
            CurrentRespawnTimer = Mathf.Max(0f, RespawnTimerRemainingValue);
            CurrentHitsRemaining = 0;

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
        CurrentHitsRemaining = Mathf.Clamp(
            HitsRemainingValue,
            1,
            Mathf.Max(1, OreRuntimeService.ResolveHitsRequired(OreDefinition)));

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

        if (VisualRoot == null)
        {
            VisualRoot = transform;
        }

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
    /// Attempts to apply one mining hit through the generic mineable interface.
    /// </summary>
    /// <param name="MiningPower">Power value of the mining hit.</param>
    /// <param name="HitContext">Explicit source context that caused the hit.</param>
    /// <returns>True when the vein accepted the mining hit.</returns>
    public bool TryMine(float MiningPower, MiningHitContext HitContext)
    {
        return ApplyHit(HitContext);
    }

    /// <summary>
    /// Applies one mining hit to this ore vein.
    /// Returns true if the vein was successfully hit.
    /// </summary>
    /// <param name="HitContext">Explicit source context that caused the hit.</param>
    /// <returns>True when the hit was accepted.</returns>
    public bool ApplyHit(MiningHitContext HitContext)
    {
        if (CurrentState != VeinState.Ready || OreDefinition == null || OreRuntimeService == null)
        {
            return false;
        }

        LastMiningHitContext = HitContext;
        CurrentHitsRemaining--;

        bool IsBreakingHit = CurrentHitsRemaining <= 0;

        if (PlayHitFeedbackOnBreakingHit || !IsBreakingHit)
        {
            PlayHitFeedback(HitContext);
        }

        Log("Ore vein hit. Remaining hits: " + CurrentHitsRemaining);

        if (IsBreakingHit)
        {
            BreakVein();
        }

        return true;
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
        PlayBreakFeedback(LastMiningHitContext);

        int DropCount = OreRuntimeService.ResolveDropCount(OreDefinition);
        List<Vector3> ReservedSpawnPositions = new List<Vector3>(DropCount);

        for (int Index = 0; Index < DropCount; Index++)
        {
            OreItemData OreItemData = OreRuntimeService.CreateOreItemData(OreDefinition);

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
        CurrentState = VeinState.Ready;
        CurrentRespawnTimer = 0f;
        CurrentHitsRemaining = OreRuntimeService != null && OreDefinition != null
            ? OreRuntimeService.ResolveHitsRequired(OreDefinition)
            : 1;

        UpdateGrowthVisual(1f);
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