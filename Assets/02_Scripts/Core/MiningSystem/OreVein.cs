using System;
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

    private enum OreVeinSizeCategory
    {
        Small = 0,
        Normal = 1,
        Large = 2,
        Custom = 3
    }

    private enum DropCountMode
    {
        DefinitionRandom = 0,
        DefinitionMinimum = 1,
        DefinitionMaximum = 2,
        DefinitionRandomPlusAdditive = 3,
        CustomRange = 4
    }

    private enum RegrowthVisualMode
    {
        ScaleAnimation = 0,
        VisibilityToggle = 1
    }

    private readonly struct DropSpawnBasis
    {
        public readonly Vector3 Origin;
        public readonly Vector3 Forward;
        public readonly Vector3 Right;
        public readonly Vector3 Up;

        public DropSpawnBasis(Vector3 OriginValue, Vector3 ForwardValue, Vector3 RightValue, Vector3 UpValue)
        {
            Origin = OriginValue;
            Forward = ForwardValue.sqrMagnitude > 0.0001f ? ForwardValue.normalized : Vector3.forward;
            Right = RightValue.sqrMagnitude > 0.0001f ? RightValue.normalized : Vector3.right;
            Up = UpValue.sqrMagnitude > 0.0001f ? UpValue.normalized : Vector3.up;
        }
    }

    [Serializable]
    private sealed class VeinSizeDropProfile
    {
        [Tooltip("Readable label used only to identify this profile in the inspector.")]
        [SerializeField] private string ProfileLabel = "Normal";

        [Tooltip("Optional helper scale used by the context menu or validation scale tool. This does not affect dropped ore size.")]
        [SerializeField] private float VeinVisualScaleMultiplier = 1f;

        [Tooltip("How this vein size profile resolves the amount of ore drops produced when the vein breaks.")]
        [SerializeField] private DropCountMode DropMode = DropCountMode.DefinitionRandom;

        [Tooltip("Extra drops added when Drop Mode is Definition Random Plus Additive.")]
        [SerializeField] private int AdditiveDropCount = 0;

        [Tooltip("Minimum drop count used when Drop Mode is Custom Range.")]
        [SerializeField] private int CustomDropCountMin = 1;

        [Tooltip("Maximum drop count used when Drop Mode is Custom Range.")]
        [SerializeField] private int CustomDropCountMax = 1;

        /// <summary>
        /// Creates one vein size drop profile with safe default values.
        /// </summary>
        /// <param name="ProfileLabelValue">Readable profile label.</param>
        /// <param name="VeinVisualScaleMultiplierValue">Optional helper visual scale multiplier.</param>
        /// <param name="DropModeValue">Drop count resolution mode.</param>
        /// <param name="AdditiveDropCountValue">Extra drops added by additive mode.</param>
        /// <param name="CustomDropCountMinValue">Minimum custom drop count.</param>
        /// <param name="CustomDropCountMaxValue">Maximum custom drop count.</param>
        public VeinSizeDropProfile(
            string ProfileLabelValue,
            float VeinVisualScaleMultiplierValue,
            DropCountMode DropModeValue,
            int AdditiveDropCountValue,
            int CustomDropCountMinValue,
            int CustomDropCountMaxValue)
        {
            ProfileLabel = ProfileLabelValue;
            VeinVisualScaleMultiplier = Mathf.Max(0.01f, VeinVisualScaleMultiplierValue);
            DropMode = DropModeValue;
            AdditiveDropCount = Mathf.Max(0, AdditiveDropCountValue);
            CustomDropCountMin = Mathf.Max(0, CustomDropCountMinValue);
            CustomDropCountMax = Mathf.Max(CustomDropCountMin, CustomDropCountMaxValue);
        }

        /// <summary>
        /// Gets the optional helper scale multiplier configured for this profile.
        /// </summary>
        public float GetVeinVisualScaleMultiplier()
        {
            return Mathf.Max(0.01f, VeinVisualScaleMultiplier);
        }

        /// <summary>
        /// Resolves the final drop count for this profile.
        /// </summary>
        /// <param name="OreDefinition">Ore definition used by the vein.</param>
        /// <param name="OreRuntimeService">Runtime service used to resolve upgraded definition ranges.</param>
        /// <returns>Final drop count produced by the vein.</returns>
        public int ResolveDropCount(OreDefinition OreDefinition, OreRuntimeService OreRuntimeService)
        {
            if (OreDefinition == null || OreRuntimeService == null)
            {
                return 0;
            }

            OreRuntimeService.ResolveDropCountRange(OreDefinition, out int DefinitionMin, out int DefinitionMax);

            switch (DropMode)
            {
                case DropCountMode.DefinitionMinimum:
                    return Mathf.Max(0, DefinitionMin);

                case DropCountMode.DefinitionMaximum:
                    return Mathf.Max(0, DefinitionMax);

                case DropCountMode.DefinitionRandomPlusAdditive:
                    return Mathf.Max(0, UnityEngine.Random.Range(DefinitionMin, DefinitionMax + 1) + Mathf.Max(0, AdditiveDropCount));

                case DropCountMode.CustomRange:
                    int SafeCustomMin = Mathf.Max(0, CustomDropCountMin);
                    int SafeCustomMax = Mathf.Max(SafeCustomMin, CustomDropCountMax);
                    return UnityEngine.Random.Range(SafeCustomMin, SafeCustomMax + 1);

                default:
                    return UnityEngine.Random.Range(DefinitionMin, DefinitionMax + 1);
            }
        }
    }

    [Header("Scene Authored Vein")]
    [Tooltip("Ore definition mined by this scene-authored vein. Assign this directly on the visible vein placed in the level.")]
    [SerializeField] private OreDefinition AssignedOreDefinition;

    [Tooltip("Runtime service used by scene-authored veins. If empty, the vein resolves the first OreRuntimeService in the scene.")]
    [SerializeField] private OreRuntimeService AssignedOreRuntimeService;

    [Tooltip("If true, this visible scene vein initializes itself on Start.")]
    [SerializeField] private bool AutoInitializeOnStart = true;

    [Header("Vein Size Profile")]
    [Tooltip("Authoring size category for this vein. It affects vein drop count only, not dropped ore runtime size.")]
    [SerializeField] private OreVeinSizeCategory VeinSizeCategory = OreVeinSizeCategory.Normal;

    [Tooltip("If true, the selected vein size profile modifies the amount of ore drops produced by this vein.")]
    [SerializeField] private bool UseVeinSizeDropProfile = true;

    [Tooltip("Optional root scaled when using the Apply Selected Vein Size Profile Scale context menu. If empty, Visual Root is used.")]
    [SerializeField] private Transform SizeProfileScaleRoot;

    [Tooltip("If true, OnValidate applies the selected profile visual scale automatically. Keep this disabled when scaling veins manually.")]
    [SerializeField] private bool ApplySizeProfileScaleOnValidate = false;

    [Tooltip("Drop and helper-scale settings used when this vein is marked as Small.")]
    [SerializeField] private VeinSizeDropProfile SmallVeinProfile = new VeinSizeDropProfile("Small", 0.5f, DropCountMode.DefinitionMinimum, 0, 1, 1);

    [Tooltip("Drop and helper-scale settings used when this vein is marked as Normal.")]
    [SerializeField] private VeinSizeDropProfile NormalVeinProfile = new VeinSizeDropProfile("Normal", 1f, DropCountMode.DefinitionRandom, 0, 1, 1);

    [Tooltip("Drop and helper-scale settings used when this vein is marked as Large.")]
    [SerializeField] private VeinSizeDropProfile LargeVeinProfile = new VeinSizeDropProfile("Large", 2f, DropCountMode.DefinitionRandomPlusAdditive, 1, 1, 1);

    [Tooltip("Drop and helper-scale settings used when this vein is marked as Custom.")]
    [SerializeField] private VeinSizeDropProfile CustomVeinProfile = new VeinSizeDropProfile("Custom", 1f, DropCountMode.CustomRange, 0, 1, 1);

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
    [Tooltip("Controls whether regrowth is shown as a scale animation or as a destroyed visual that pops back when ready.")]
    [SerializeField] private RegrowthVisualMode VisualRegrowthMode = RegrowthVisualMode.ScaleAnimation;

    [Tooltip("Minimum scale used while the ore is regrowing when Scale Animation mode is active.")]
    [SerializeField] private float MinimumGrowthScale = 0.05f;

    [Header("Drop Spawn Area")]
    [Tooltip("If true, drop spawn offsets use the Drop Origin transform axes. If false, the vein transform axes are used.")]
    [SerializeField] private bool UseDropOriginAxes = true;

    [Tooltip("Forward offset from the drop origin. Rotate the Drop Origin so its forward axis points away from the wall.")]
    [SerializeField] private float DropForwardOffset = 0.25f;

    [Tooltip("Random extra forward offset applied to each drop so they do not spawn in a flat row.")]
    [SerializeField] private float DropForwardJitter = 0.08f;

    [Tooltip("Side radius used to spread multiple drops across the spawn plane.")]
    [SerializeField] private float DropScatterRadius = 0.45f;

    [Tooltip("Base vertical offset applied from the drop origin before random variation.")]
    [SerializeField] private float DropVerticalOffset = 0.15f;

    [Tooltip("Vertical radius used to vary drop height across the spawn plane.")]
    [SerializeField] private float DropVerticalScatterRadius = 0.25f;

    [Tooltip("Additional random vertical variation applied to each drop spawn after the base vertical offset.")]
    [SerializeField] private float DropVerticalJitter = 0.08f;

    [Header("Safe Spawn")]
    [Tooltip("Layers considered solid when validating ore drop spawn clearance. Exclude the vein layer if needed.")]
    [SerializeField] private LayerMask DropSpawnBlockingLayers = ~0;

    [Tooltip("Approximate clearance radius used to keep ore spawns away from walls and from each other.")]
    [SerializeField] private float SpawnClearanceRadius = 0.2f;

    [Tooltip("Maximum amount of candidate positions tested per ore drop before using the last safe fallback.")]
    [SerializeField] private int MaxSpawnAttemptsPerDrop = 16;

    [Tooltip("Side radius added on each retry while searching for a valid spawn point.")]
    [SerializeField] private float SpawnRadiusStep = 0.12f;

    [Tooltip("Forward distance added on each retry while searching for a valid spawn point away from the wall.")]
    [SerializeField] private float SpawnForwardStep = 0.12f;

    [Tooltip("Vertical distance added on each retry while searching for a valid spawn point.")]
    [SerializeField] private float SpawnHeightStep = 0.08f;

    [Tooltip("Random yaw rotation applied to each spawned ore pickup.")]
    [SerializeField] private bool RandomizeYawRotation = true;

    [Tooltip("If true, a subtle random pitch and roll are also applied to the spawned ore pickup.")]
    [SerializeField] private bool RandomizeTiltRotation = true;

    [Tooltip("Maximum absolute random pitch applied when tilt randomization is enabled.")]
    [SerializeField] private float MaxRandomPitch = 12f;

    [Tooltip("Maximum absolute random roll applied when tilt randomization is enabled.")]
    [SerializeField] private float MaxRandomRoll = 12f;

    [Header("Editor Preview")]
    [Tooltip("Draws the drop origin, forward direction, spawn area and preview clearance spheres when the vein is selected.")]
    [SerializeField] private bool DrawDropSpawnGizmos = true;

    [Tooltip("Amount of preview drop points drawn in the editor when the final runtime drop count is not known yet.")]
    [SerializeField] private int DropSpawnPreviewCount = 4;

    [Tooltip("If true, preview points draw clearance spheres using the configured spawn clearance radius.")]
    [SerializeField] private bool DrawDropSpawnClearanceGizmos = true;

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
    /// Minimum flat purity percent bonus captured from the hit that breaks this vein.
    /// This value is added to each generated ore purity roll.
    /// </summary>
    private float LastPurityBonusPercentMin;

    /// <summary>
    /// Maximum flat purity percent bonus captured from the hit that breaks this vein.
    /// This value is added to each generated ore purity roll.
    /// </summary>
    private float LastPurityBonusPercentMax;

    /// <summary>
    /// Soft cached reference to the elevator magnet resolved for recent spawns.
    /// </summary>
    private ElevatorOreSpawnMagnet CachedElevatorOreSpawnMagnet;

    /// <summary>
    /// True after this vein has resolved its authored data and runtime service.
    /// </summary>
    private bool IsInitialized;

    /// <summary>
    /// Base local scale captured from the authored visual before regrowth animation modifies it.
    /// </summary>
    private Vector3 BaseVisualLocalScale = Vector3.one;

    /// <summary>
    /// Whether the base visual scale has already been captured for this runtime instance.
    /// </summary>
    private bool HasCapturedBaseVisualScale;

    /// <summary>
    /// Renderers controlled by visibility-toggle regrowth mode.
    /// </summary>
    private Renderer[] CachedVisualRenderers = Array.Empty<Renderer>();

    /// <summary>
    /// Gets the ore definition currently used by this vein.
    /// This is used by external systems such as the scanner.
    /// </summary>
    public OreDefinition GetOreDefinition()
    {
        return OreDefinition != null ? OreDefinition : AssignedOreDefinition;
    }

    /// <summary>
    /// Gets the ore definition authored directly on this scene vein.
    /// </summary>
    public OreDefinition GetAssignedOreDefinition()
    {
        return AssignedOreDefinition;
    }

    /// <summary>
    /// Gets the mining tier required to damage this vein.
    /// </summary>
    /// <returns>Required mining tier, or TierI when no definition is available.</returns>
    public MiningTier GetRequiredMiningTier()
    {
        OreDefinition CurrentDefinition = GetOreDefinition();
        return CurrentDefinition != null ? CurrentDefinition.GetRequiredMiningTier() : MiningTier.TierI;
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
        EnsureInitialized();

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
    /// Initializes this scene-authored ore vein with its definition and runtime service.
    /// </summary>
    /// <param name="OreDefinitionValue">Definition used by this ore vein.</param>
    /// <param name="OreRuntimeServiceValue">Runtime service used to resolve ore values and drops.</param>
    public void Initialize(OreDefinition OreDefinitionValue, OreRuntimeService OreRuntimeServiceValue)
    {
        OreDefinition = OreDefinitionValue != null ? OreDefinitionValue : AssignedOreDefinition;
        OreRuntimeService = OreRuntimeServiceValue != null ? OreRuntimeServiceValue : ResolveRuntimeService();
        LastMiningHitContext = MiningHitContext.CreateUnknown();
        LastPurityBonusPercentMin = 0f;
        LastPurityBonusPercentMax = 0f;

        ResolveVisualRoot();
        CaptureBaseVisualScale();
        ResolveFeedbackEmitter();

        IsInitialized = true;
        ResetReadyState();
    }

    /// <summary>
    /// Initializes a visible scene-authored vein if no external spawn point initialized it.
    /// </summary>
    private void Start()
    {
        if (!AutoInitializeOnStart || IsInitialized)
        {
            return;
        }

        InitializeSceneAuthoredVein();
    }

    /// <summary>
    /// Initializes this vein using its directly assigned scene authoring data.
    /// </summary>
    public void InitializeSceneAuthoredVein()
    {
        Initialize(AssignedOreDefinition, ResolveRuntimeService());
    }

    /// <summary>
    /// Ensures this vein has resolved its runtime dependencies before external systems query or restore it.
    /// </summary>
    public void EnsureInitialized()
    {
        if (IsInitialized)
        {
            return;
        }

        InitializeSceneAuthoredVein();
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
        EnsureInitialized();

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
        LastPurityBonusPercentMin = MiningRequest.PurityBonusPercentMin;
        LastPurityBonusPercentMax = MiningRequest.PurityBonusPercentMax;
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

        int DropCount = ResolveVeinDropCount();
        List<Vector3> ReservedSpawnPositions = new List<Vector3>(DropCount);

        for (int Index = 0; Index < DropCount; Index++)
        {
            OreItemData OreItemData = OreRuntimeService.CreateOreItemData(OreDefinition, LastPurityBonusPercentMin, LastPurityBonusPercentMax);

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
        LastPurityBonusPercentMin = 0f;
        LastPurityBonusPercentMax = 0f;
        StartRegrowth();
        Log("Ore vein broken and " + DropCount + " drops were spawned.");
    }

    /// <summary>
    /// Resolves the amount of drops produced by this vein after applying the authored vein size profile.
    /// </summary>
    /// <returns>Final amount of ore pickups to spawn.</returns>
    private int ResolveVeinDropCount()
    {
        if (OreRuntimeService == null || OreDefinition == null)
        {
            return 0;
        }

        if (!UseVeinSizeDropProfile)
        {
            return OreRuntimeService.ResolveDropCount(OreDefinition);
        }

        VeinSizeDropProfile Profile = GetSelectedVeinSizeProfile();

        if (Profile == null)
        {
            return OreRuntimeService.ResolveDropCount(OreDefinition);
        }

        return Profile.ResolveDropCount(OreDefinition, OreRuntimeService);
    }

    /// <summary>
    /// Gets the size/drop profile selected by the current vein category.
    /// </summary>
    /// <returns>Configured vein size profile.</returns>
    private VeinSizeDropProfile GetSelectedVeinSizeProfile()
    {
        switch (VeinSizeCategory)
        {
            case OreVeinSizeCategory.Small:
                return SmallVeinProfile;

            case OreVeinSizeCategory.Large:
                return LargeVeinProfile;

            case OreVeinSizeCategory.Custom:
                return CustomVeinProfile;

            default:
                return NormalVeinProfile;
        }
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
        DropSpawnBasis SpawnBasis = ResolveDropSpawnBasis();
        float ForwardJitter = UnityEngine.Random.Range(0f, Mathf.Abs(DropForwardJitter));
        float VerticalJitter = UnityEngine.Random.Range(-Mathf.Abs(DropVerticalJitter), Mathf.Abs(DropVerticalJitter));
        Vector3 BasePosition = SpawnBasis.Origin +
                               (SpawnBasis.Forward * (Mathf.Max(0f, DropForwardOffset) + ForwardJitter)) +
                               (SpawnBasis.Up * (DropVerticalOffset + VerticalJitter));

        float ClearanceRadius = Mathf.Max(0.05f, SpawnClearanceRadius);
        float SeparationDistance = ClearanceRadius * 2f;
        Vector3 LastValidFallback = BasePosition +
                                    (SpawnBasis.Forward * Mathf.Max(0f, SpawnForwardStep)) +
                                    (SpawnBasis.Up * Mathf.Max(0f, SpawnHeightStep));

        for (int AttemptIndex = 0; AttemptIndex < Mathf.Max(1, MaxSpawnAttemptsPerDrop); AttemptIndex++)
        {
            Vector3 CandidateOffset = GetSpawnPatternOffset(DropIndex, TotalDropCount, AttemptIndex, SpawnBasis);
            Vector3 CandidatePosition = BasePosition + CandidateOffset;

            LastValidFallback = CandidatePosition;

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

        Log("Failed to resolve a fully clean drop spawn. Using the last tested fallback position.");
        return LastValidFallback;
    }


    /// <summary>
    /// Builds a subtle random rotation for a spawned ore pickup so repeated drops do not look identical.
    /// </summary>
    private Quaternion GetRandomDropRotation()
    {
        float Yaw = RandomizeYawRotation ? UnityEngine.Random.Range(0f, 360f) : 0f;
        float Pitch = RandomizeTiltRotation ? UnityEngine.Random.Range(-Mathf.Abs(MaxRandomPitch), Mathf.Abs(MaxRandomPitch)) : 0f;
        float Roll = RandomizeTiltRotation ? UnityEngine.Random.Range(-Mathf.Abs(MaxRandomRoll), Mathf.Abs(MaxRandomRoll)) : 0f;

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
    private Vector3 GetSpawnPatternOffset(int DropIndex, int TotalDropCount, int AttemptIndex, DropSpawnBasis SpawnBasis)
    {
        if (TotalDropCount <= 1 && AttemptIndex == 0)
        {
            return Vector3.zero;
        }

        float SafeTotalDropCount = Mathf.Max(1, TotalDropCount);
        float BaseAngle = 360f / SafeTotalDropCount;
        float AttemptAngleOffset = 137.50776f * AttemptIndex;
        float AngleDegrees = (DropIndex * BaseAngle) + AttemptAngleOffset;
        float AngleRadians = AngleDegrees * Mathf.Deg2Rad;

        float SideRadius = Mathf.Max(0f, DropScatterRadius) + (AttemptIndex * Mathf.Max(0f, SpawnRadiusStep));
        float VerticalRadius = Mathf.Max(0f, DropVerticalScatterRadius);
        float SideOffset = Mathf.Cos(AngleRadians) * SideRadius;
        float VerticalOffset = Mathf.Sin(AngleRadians) * VerticalRadius;
        float ForwardOffset = AttemptIndex * Mathf.Max(0f, SpawnForwardStep);
        float RetryHeightOffset = AttemptIndex * Mathf.Max(0f, SpawnHeightStep);

        return (SpawnBasis.Right * SideOffset) +
               (SpawnBasis.Up * (VerticalOffset + RetryHeightOffset)) +
               (SpawnBasis.Forward * ForwardOffset);
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
            DropSpawnBlockingLayers,
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
    /// Resolves the oriented basis used by runtime drop spawning and editor gizmos.
    /// </summary>
    /// <returns>World-space spawn basis.</returns>
    private DropSpawnBasis ResolveDropSpawnBasis()
    {
        Transform BasisTransform = UseDropOriginAxes && DropOrigin != null ? DropOrigin : transform;
        Vector3 Forward = BasisTransform != null ? BasisTransform.forward : Vector3.forward;
        Vector3 Right = BasisTransform != null ? BasisTransform.right : Vector3.right;
        Vector3 Up = BasisTransform != null ? BasisTransform.up : Vector3.up;

        return new DropSpawnBasis(GetDropOriginPosition(), Forward, Right, Up);
    }

    /// <summary>
    /// Spawns one ore pickup and applies optional elevator spawn assist when the hit context allows it.
    /// Player hits allow this by default, while machine hits must explicitly opt in through MiningHitContext.
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

        if (!LastMiningHitContext.CanUseElevatorOreSpawnAssist())
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
        ResolveVisualRoot();

        if (VisualRoot == null)
        {
            return;
        }

        CaptureBaseVisualScale();

        float ClampedProgress = Mathf.Clamp01(NormalizedProgress);

        if (VisualRegrowthMode == RegrowthVisualMode.VisibilityToggle)
        {
            bool ShouldShowVisual = ClampedProgress >= 1f;
            SetVisualRenderersVisible(ShouldShowVisual);
            VisualRoot.localScale = BaseVisualLocalScale;
            return;
        }

        SetVisualRenderersVisible(true);
        float ScaleMultiplier = Mathf.Lerp(MinimumGrowthScale, 1f, ClampedProgress);
        VisualRoot.localScale = BaseVisualLocalScale * ScaleMultiplier;
    }

    /// <summary>
    /// Resolves the visual root used by growth animation and optional size profile helper scaling.
    /// </summary>
    private void ResolveVisualRoot()
    {
        if (VisualRoot == null)
        {
            VisualRoot = transform;
        }
    }

    /// <summary>
    /// Captures the authored base visual scale once so regrowth preserves manually placed vein size.
    /// </summary>
    private void CaptureBaseVisualScale()
    {
        if (HasCapturedBaseVisualScale)
        {
            return;
        }

        ResolveVisualRoot();

        if (VisualRoot == null)
        {
            BaseVisualLocalScale = Vector3.one;
            CachedVisualRenderers = Array.Empty<Renderer>();
            HasCapturedBaseVisualScale = true;
            return;
        }

        BaseVisualLocalScale = VisualRoot.localScale;
        CachedVisualRenderers = VisualRoot.GetComponentsInChildren<Renderer>(true);
        HasCapturedBaseVisualScale = true;
    }

    /// <summary>
    /// Shows or hides the renderers controlled by this vein without disabling the GameObject that owns the runtime script.
    /// </summary>
    /// <param name="IsVisible">True to show the visual renderers.</param>
    private void SetVisualRenderersVisible(bool IsVisible)
    {
        CaptureBaseVisualScale();

        if (CachedVisualRenderers == null || CachedVisualRenderers.Length == 0)
        {
            return;
        }

        for (int Index = 0; Index < CachedVisualRenderers.Length; Index++)
        {
            if (CachedVisualRenderers[Index] != null)
            {
                CachedVisualRenderers[Index].enabled = IsVisible;
            }
        }
    }

    /// <summary>
    /// Resolves the runtime service used by this scene-authored vein.
    /// </summary>
    /// <returns>Assigned or discovered runtime service.</returns>
    private OreRuntimeService ResolveRuntimeService()
    {
        if (AssignedOreRuntimeService != null)
        {
            return AssignedOreRuntimeService;
        }

        return FindFirstObjectByType<OreRuntimeService>();
    }

    /// <summary>
    /// Applies the selected size profile helper scale to the configured scale root.
    /// This is an authoring utility only; it does not affect dropped ore runtime size.
    /// </summary>
    [ContextMenu("Apply Selected Vein Size Profile Scale")]
    public void ApplySelectedVeinSizeProfileScale()
    {
        VeinSizeDropProfile Profile = GetSelectedVeinSizeProfile();

        if (Profile == null)
        {
            return;
        }

        Transform TargetRoot = SizeProfileScaleRoot != null
            ? SizeProfileScaleRoot
            : (VisualRoot != null ? VisualRoot : transform);

        TargetRoot.localScale = Vector3.one * Profile.GetVeinVisualScaleMultiplier();
        HasCapturedBaseVisualScale = false;
    }

    /// <summary>
    /// Validates authoring helper data in the editor.
    /// </summary>
    private void OnValidate()
    {
        if (Application.isPlaying)
        {
            return;
        }

        if (ApplySizeProfileScaleOnValidate)
        {
            ApplySelectedVeinSizeProfileScale();
        }
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
    /// Draws an editor-only preview of the ore drop area so authored veins can be placed without spawning drops inside walls.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!DrawDropSpawnGizmos)
        {
            return;
        }

        DropSpawnBasis SpawnBasis = ResolveDropSpawnBasis();
        Vector3 BasePosition = SpawnBasis.Origin +
                               (SpawnBasis.Forward * Mathf.Max(0f, DropForwardOffset)) +
                               (SpawnBasis.Up * DropVerticalOffset);

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(SpawnBasis.Origin, SpawnBasis.Origin + (SpawnBasis.Forward * Mathf.Max(0.25f, DropForwardOffset + SpawnForwardStep)));
        Gizmos.DrawWireSphere(SpawnBasis.Origin, 0.035f);

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(BasePosition - (SpawnBasis.Right * Mathf.Max(0f, DropScatterRadius)), BasePosition + (SpawnBasis.Right * Mathf.Max(0f, DropScatterRadius)));
        Gizmos.DrawLine(BasePosition - (SpawnBasis.Up * Mathf.Max(0f, DropVerticalScatterRadius)), BasePosition + (SpawnBasis.Up * Mathf.Max(0f, DropVerticalScatterRadius)));

        int PreviewCount = Mathf.Max(1, DropSpawnPreviewCount);

        for (int Index = 0; Index < PreviewCount; Index++)
        {
            Vector3 PreviewPosition = BasePosition + GetSpawnPatternOffset(Index, PreviewCount, 0, SpawnBasis);
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(PreviewPosition, 0.045f);

            if (DrawDropSpawnClearanceGizmos)
            {
                Gizmos.color = new Color(0f, 1f, 0f, 0.35f);
                Gizmos.DrawWireSphere(PreviewPosition, Mathf.Max(0.05f, SpawnClearanceRadius));
            }
        }
    }
}