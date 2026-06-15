using UnityEngine;

/// <summary>
/// Equipped pickaxe behaviour built on top of the animation-event item action system.
/// The mining hit happens only when the animation clip explicitly sends the impact event,
/// keeping the visible swing, gameplay hit and feedback dispatch synchronized.
/// </summary>
public sealed class PickaxeItemBehaviour : AnimationEventEquippedItemBehaviour
{
    [Header("References")]
    [Tooltip("Camera used to cast mining rays. If empty, the item looks for one on the owner.")]
    [SerializeField] private Camera PlayerCamera;

    [Header("Mining Fallbacks")]
    [Tooltip("Fallback mining damage used when the equipped item definition is not a PickaxeItemDefinition.")]
    [SerializeField] private float FallbackMiningDamage = 1f;

    [Tooltip("Fallback mining tier used when the equipped item definition is not a PickaxeItemDefinition.")]
    [SerializeField] private MiningTier FallbackMiningTier = MiningTier.TierI;

    [Tooltip("Fallback extraction quality used when the equipped item definition is not a PickaxeItemDefinition.")]
    [SerializeField] private float FallbackExtractionQualityMultiplier = 1f;

    [Tooltip("Fallback durability cost used when the equipped item definition is not a PickaxeItemDefinition.")]
    [SerializeField] private float FallbackDurabilityCostPerAcceptedHit = 1f;

    [Tooltip("If true, fallback pickaxes consume durability on accepted mining hits.")]
    [SerializeField] private bool FallbackUsesDurability = true;

    [Tooltip("If true, fallback pickaxes are removed from the hotbar when durability reaches zero.")]
    [SerializeField] private bool FallbackBreaksAtZeroDurability = true;

    [Header("Raycast")]
    [Tooltip("Maximum distance used to detect mineable targets.")]
    [SerializeField] private float MiningDistance = 4f;

    [Tooltip("Layers considered valid mining targets.")]
    [SerializeField] private LayerMask MiningLayers = ~0;

    [Header("Timing")]
    [Tooltip("Fallback minimum seconds between primary action starts when the item definition does not provide one.")]
    [SerializeField] private float FallbackMinimumUseInterval = 0f;

    [Header("Feedback")]
    [Tooltip("Generic feedback emitter used to play particles, audio and Feel feedbacks when mining results happen. If empty, one is found or created on this item root.")]
    [SerializeField] private GameFeedbackEmitter FeedbackEmitter;

    [Tooltip("Fallback feedback profile used when the equipped item definition does not provide one.")]
    [SerializeField] private GameFeedbackProfile FallbackFeedbackProfile;

    [Tooltip("If true, a PickaxeItemDefinition feedback profile overrides the profile currently assigned to the emitter.")]
    [SerializeField] private bool UseItemDefinitionFeedbackProfile = true;

    [Tooltip("If true, a feedback event is played when the mining ray hits nothing.")]
    [SerializeField] private bool PlayMissFeedbackWhenRayHitsNothing = false;

    [Tooltip("If true, a feedback event is played when the mining ray hits a collider that is not mineable.")]
    [SerializeField] private bool PlayNonMineableHitFeedback = true;

    [Tooltip("If true, regular accepted hit feedback is also played on the same hit that breaks the vein.")]
    [SerializeField] private bool PlayAcceptedFeedbackOnBreakingHit = true;

    [Header("Debug")]
    [Tooltip("Draws the mining ray in the Scene view when attempting a hit.")]
    [SerializeField] private bool DrawDebugRay = false;

    /// <summary>
    /// Last time a primary pickaxe action was started.
    /// </summary>
    private float LastPrimaryActionStartTime = -999f;

    /// <summary>
    /// Initializes the pickaxe and resolves missing owner references.
    /// </summary>
    /// <param name="OwnerHotbar">Hotbar that owns this equipped item.</param>
    /// <param name="ItemInstance">Runtime item instance attached to this behaviour.</param>
    public override void Initialize(HotbarController OwnerHotbar, ItemInstance ItemInstance)
    {
        base.Initialize(OwnerHotbar, ItemInstance);
        ResolvePlayerCamera();
        ResolveFeedbackEmitter();
    }

    /// <summary>
    /// Resolves the gameplay camera used by mining raycasts.
    /// This intentionally supports nested equipped item hierarchies where the pickaxe behaviour may no longer live directly under the player camera socket.
    /// </summary>
    private void ResolvePlayerCamera()
    {
        if (PlayerCamera != null)
        {
            return;
        }

        if (OwnerHotbar != null)
        {
            PlayerController PlayerController = OwnerHotbar.GetComponentInParent<PlayerController>();

            if (PlayerController != null && PlayerController.PlayerCamera != null)
            {
                PlayerCamera = PlayerController.PlayerCamera;
                return;
            }

            PlayerCamera = OwnerHotbar.GetComponentInChildren<Camera>(true);

            if (PlayerCamera != null)
            {
                return;
            }
        }

        PlayerCamera = Camera.main;

        if (PlayerCamera != null)
        {
            return;
        }

        PlayerCamera = FindFirstObjectByType<Camera>();
    }

    /// <summary>
    /// Resolves or creates the generic feedback emitter used by this pickaxe.
    /// </summary>
    private void ResolveFeedbackEmitter()
    {
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
            FeedbackEmitter = gameObject.AddComponent<GameFeedbackEmitter>();
        }

        GameFeedbackProfile ResolvedProfile = GetResolvedFeedbackProfile();

        if (ResolvedProfile != null)
        {
            FeedbackEmitter.SetFeedbackProfile(ResolvedProfile);
        }
    }

    /// <summary>
    /// Gets the feedback profile that should be used by this equipped pickaxe.
    /// </summary>
    /// <returns>Resolved feedback profile, or null when none is configured.</returns>
    private GameFeedbackProfile GetResolvedFeedbackProfile()
    {
        PickaxeItemDefinition PickaxeDefinition = GetPickaxeDefinition();

        if (UseItemDefinitionFeedbackProfile && PickaxeDefinition != null && PickaxeDefinition.GetFeedbackProfile() != null)
        {
            return PickaxeDefinition.GetFeedbackProfile();
        }

        return FallbackFeedbackProfile;
    }

    /// <summary>
    /// Checks whether the pickaxe can start a new primary action.
    /// This prevents broken tools from swinging and supports an optional minimum use interval.
    /// </summary>
    /// <returns>True when a primary action can start.</returns>
    protected override bool CanStartPrimaryAction()
    {
        if (!base.CanStartPrimaryAction())
        {
            return false;
        }

        if (IsBroken())
        {
            Log("Primary action blocked because the pickaxe is broken.");
            return false;
        }

        float MinimumUseInterval = GetResolvedMinimumUseInterval();

        if (MinimumUseInterval > 0f && Time.time - LastPrimaryActionStartTime < MinimumUseInterval)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Records action start time after the animation trigger has been accepted.
    /// </summary>
    protected override void OnPrimaryActionStarted()
    {
        base.OnPrimaryActionStarted();
        LastPrimaryActionStartTime = Time.time;
    }

    /// <summary>
    /// Applies the mining effect exactly when the animation impact event is fired.
    /// </summary>
    protected override void OnPrimaryActionImpact()
    {
        ResolvePlayerCamera();
        ResolveFeedbackEmitter();

        if (PlayerCamera == null)
        {
            Log("No camera was found for the pickaxe mining ray.");
            return;
        }

        Ray MiningRay = PlayerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (DrawDebugRay)
        {
            Debug.DrawRay(MiningRay.origin, MiningRay.direction * MiningDistance, Color.yellow, 0.5f);
        }

        if (!Physics.Raycast(MiningRay, out RaycastHit HitInfo, MiningDistance, MiningLayers, QueryTriggerInteraction.Ignore))
        {
            if (PlayMissFeedbackWhenRayHitsNothing)
            {
                Vector3 MissPosition = MiningRay.origin + (MiningRay.direction * Mathf.Max(0f, MiningDistance));
                PlayFeedback(GameFeedbackEventIds.MiningMiss, GameFeedbackContext.FromPosition(MissPosition, transform));
            }

            Log("Mining ray hit nothing.");
            return;
        }

        GameFeedbackContext FeedbackContext = GameFeedbackContext.FromRaycastHit(HitInfo, transform);
        IMineable Mineable = ResolveMineable(HitInfo);

        if (Mineable == null)
        {
            if (PlayNonMineableHitFeedback)
            {
                PlayFeedback(GameFeedbackEventIds.MiningNonMineableHit, FeedbackContext);
            }

            Log("Mining ray hit a non-mineable target.");
            return;
        }

        MiningHitContext HitContext = new MiningHitContext(
            MiningHitContext.HitSourceType.Player,
            this.OwnerHotbar != null ? this.OwnerHotbar.gameObject : gameObject,
            HitInfo.point,
            HitInfo.normal);

        MiningHitRequest MiningRequest = new MiningHitRequest(
            GetResolvedMiningDamage(),
            GetResolvedMiningTier(),
            GetResolvedExtractionQualityMultiplier(),
            GetResolvedDurabilityCostPerAcceptedHit(),
            HitContext);

        MiningHitResult MiningResult = Mineable.TryMine(MiningRequest);

        if (!MiningResult.WasAccepted)
        {
            PlayRejectedMiningFeedback(MiningResult, FeedbackContext);
            LogRejectedMiningResult(MiningResult);
            return;
        }

        PlayAcceptedMiningFeedback(MiningResult, FeedbackContext);
        ApplyAcceptedHitDurabilityCost(MiningRequest.DurabilityCost);
        Log("Mineable target accepted pickaxe hit. Damage: " + MiningResult.DamageApplied + " | Remaining target durability: " + MiningResult.RemainingDurability);
    }

    /// <summary>
    /// Resolves a mineable target from the current raycast hit.
    /// </summary>
    /// <param name="HitInfo">Raycast hit returned by the mining ray.</param>
    /// <returns>Mineable target if found, otherwise null.</returns>
    private IMineable ResolveMineable(RaycastHit HitInfo)
    {
        if (HitInfo.collider == null)
        {
            return null;
        }

        IMineable Mineable = HitInfo.collider.GetComponent<IMineable>();

        if (Mineable != null)
        {
            return Mineable;
        }

        Mineable = HitInfo.collider.GetComponentInParent<IMineable>();

        if (Mineable != null)
        {
            return Mineable;
        }

        if (HitInfo.rigidbody != null)
        {
            Mineable = HitInfo.rigidbody.GetComponent<IMineable>();

            if (Mineable != null)
            {
                return Mineable;
            }

            Mineable = HitInfo.rigidbody.GetComponentInParent<IMineable>();

            if (Mineable != null)
            {
                return Mineable;
            }
        }

        return null;
    }

    /// <summary>
    /// Applies durability cost to the current runtime item after the target accepts a mining hit.
    /// </summary>
    /// <param name="DurabilityCost">Durability amount to consume.</param>
    private void ApplyAcceptedHitDurabilityCost(float DurabilityCost)
    {
        if (ItemInstance == null || !GetResolvedUsesDurability() || DurabilityCost <= 0f)
        {
            return;
        }

        float NewDurability = Mathf.Max(0f, ItemInstance.GetDurability() - DurabilityCost);
        ItemInstance.SetDurability(NewDurability);

        if (OwnerHotbar != null)
        {
            OwnerHotbar.NotifySelectedItemRuntimeChanged();
        }

        if (NewDurability > 0f || !GetResolvedBreaksAtZeroDurability())
        {
            return;
        }

        Log("Pickaxe broke after accepted mining hit.");

        if (OwnerHotbar != null)
        {
            OwnerHotbar.TryRemoveSelectedItemInstance(ItemInstance);
        }
    }

    /// <summary>
    /// Gets whether the current pickaxe has no remaining durability.
    /// </summary>
    /// <returns>True when the tool is configured to break and has no durability left.</returns>
    private bool IsBroken()
    {
        if (ItemInstance == null || !GetResolvedUsesDurability() || !GetResolvedBreaksAtZeroDurability())
        {
            return false;
        }

        return ItemInstance.GetDurability() <= 0f;
    }

    /// <summary>
    /// Gets the specialized pickaxe definition if the current item uses one.
    /// </summary>
    /// <returns>Pickaxe item definition or null.</returns>
    private PickaxeItemDefinition GetPickaxeDefinition()
    {
        return ItemInstance != null ? ItemInstance.GetDefinition() as PickaxeItemDefinition : null;
    }

    /// <summary>
    /// Gets resolved mining damage from the item definition or fallback values.
    /// </summary>
    private float GetResolvedMiningDamage()
    {
        PickaxeItemDefinition PickaxeDefinition = GetPickaxeDefinition();
        return PickaxeDefinition != null ? PickaxeDefinition.GetMiningDamage() : Mathf.Max(0f, FallbackMiningDamage);
    }

    /// <summary>
    /// Gets resolved mining tier from the item definition or fallback values.
    /// </summary>
    private MiningTier GetResolvedMiningTier()
    {
        PickaxeItemDefinition PickaxeDefinition = GetPickaxeDefinition();

        if (PickaxeDefinition != null)
        {
            return PickaxeDefinition.GetMiningTier();
        }

        return FallbackMiningTier == MiningTier.None ? MiningTier.TierI : FallbackMiningTier;
    }

    /// <summary>
    /// Gets resolved extraction quality from the item definition or fallback values.
    /// </summary>
    private float GetResolvedExtractionQualityMultiplier()
    {
        PickaxeItemDefinition PickaxeDefinition = GetPickaxeDefinition();
        return PickaxeDefinition != null
            ? PickaxeDefinition.GetExtractionQualityMultiplier()
            : Mathf.Max(0.01f, FallbackExtractionQualityMultiplier);
    }

    /// <summary>
    /// Gets whether durability should be consumed from the item definition or fallback values.
    /// </summary>
    private bool GetResolvedUsesDurability()
    {
        PickaxeItemDefinition PickaxeDefinition = GetPickaxeDefinition();
        return PickaxeDefinition != null ? PickaxeDefinition.GetUsesDurability() : FallbackUsesDurability;
    }

    /// <summary>
    /// Gets durability cost from the item definition or fallback values.
    /// </summary>
    private float GetResolvedDurabilityCostPerAcceptedHit()
    {
        PickaxeItemDefinition PickaxeDefinition = GetPickaxeDefinition();
        return PickaxeDefinition != null
            ? PickaxeDefinition.GetDurabilityCostPerAcceptedHit()
            : Mathf.Max(0f, FallbackDurabilityCostPerAcceptedHit);
    }

    /// <summary>
    /// Gets whether the tool should be removed at zero durability.
    /// </summary>
    private bool GetResolvedBreaksAtZeroDurability()
    {
        PickaxeItemDefinition PickaxeDefinition = GetPickaxeDefinition();
        return PickaxeDefinition != null ? PickaxeDefinition.GetBreaksAtZeroDurability() : FallbackBreaksAtZeroDurability;
    }

    /// <summary>
    /// Gets minimum use interval from the item definition or fallback values.
    /// </summary>
    private float GetResolvedMinimumUseInterval()
    {
        PickaxeItemDefinition PickaxeDefinition = GetPickaxeDefinition();
        return PickaxeDefinition != null
            ? PickaxeDefinition.GetMinimumUseInterval()
            : Mathf.Max(0f, FallbackMinimumUseInterval);
    }

    /// <summary>
    /// Plays the feedback associated with an accepted mining result.
    /// </summary>
    /// <param name="MiningResult">Accepted mining result.</param>
    /// <param name="FeedbackContext">Runtime feedback context.</param>
    private void PlayAcceptedMiningFeedback(MiningHitResult MiningResult, GameFeedbackContext FeedbackContext)
    {
        if (MiningResult.DidBreak)
        {
            if (PlayAcceptedFeedbackOnBreakingHit)
            {
                PlayFeedback(GameFeedbackEventIds.MiningAcceptedHit, FeedbackContext);
            }

            PlayFeedback(GameFeedbackEventIds.MiningBreak, FeedbackContext);
            return;
        }

        PlayFeedback(GameFeedbackEventIds.MiningAcceptedHit, FeedbackContext);
    }

    /// <summary>
    /// Plays the feedback associated with a rejected mining result.
    /// </summary>
    /// <param name="MiningResult">Rejected mining result.</param>
    /// <param name="FeedbackContext">Runtime feedback context.</param>
    private void PlayRejectedMiningFeedback(MiningHitResult MiningResult, GameFeedbackContext FeedbackContext)
    {
        switch (MiningResult.ResultType)
        {
            case MiningHitResultType.InsufficientTier:
                PlayFeedback(GameFeedbackEventIds.MiningInsufficientTier, FeedbackContext);
                break;

            case MiningHitResultType.TargetUnavailable:
                PlayFeedback(GameFeedbackEventIds.MiningTargetUnavailable, FeedbackContext);
                break;

            case MiningHitResultType.NoDamage:
                PlayFeedback(GameFeedbackEventIds.MiningNoDamage, FeedbackContext);
                break;

            default:
                PlayFeedback(GameFeedbackEventIds.MiningRejectedHit, FeedbackContext);
                break;
        }
    }

    /// <summary>
    /// Routes one gameplay feedback event to the configured generic feedback emitter.
    /// </summary>
    /// <param name="EventId">Stable feedback event id.</param>
    /// <param name="FeedbackContext">Runtime feedback context.</param>
    private void PlayFeedback(string EventId, GameFeedbackContext FeedbackContext)
    {
        if (FeedbackEmitter == null)
        {
            ResolveFeedbackEmitter();
        }

        if (FeedbackEmitter == null)
        {
            return;
        }

        FeedbackEmitter.Play(EventId, FeedbackContext);
    }

    /// <summary>
    /// Writes a debug log for a rejected mining request.
    /// </summary>
    /// <param name="MiningResult">Rejected mining result.</param>
    private void LogRejectedMiningResult(MiningHitResult MiningResult)
    {
        switch (MiningResult.ResultType)
        {
            case MiningHitResultType.InsufficientTier:
                Log("Mining rejected because tool tier is too low. Required: " + MiningResult.RequiredTier + " | Source: " + MiningResult.SourceTier);
                break;

            case MiningHitResultType.TargetUnavailable:
                Log("Mining rejected because the target is unavailable.");
                break;

            case MiningHitResultType.NoDamage:
                Log("Mining rejected because the request has no damage.");
                break;

            default:
                Log("Mining rejected. Result: " + MiningResult.ResultType);
                break;
        }
    }
}
