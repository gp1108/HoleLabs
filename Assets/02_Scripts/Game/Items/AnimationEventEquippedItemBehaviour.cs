using UnityEngine;

/// <summary>
/// Base class for equipped items driven by animation events.
/// The gameplay effect and the action end are both triggered by the Animator clip itself,
/// which keeps gameplay timing aligned with the actual visible animation.
/// 
/// Important:
/// - Primary and secondary clicks are hard-locked while their action is running.
/// - Hold repeat no longer tries to start directly from OnPrimaryUseHeld.
/// - Hold only queues a pending repeat, and the base class starts it only when the Animator
///   is truly ready to accept a new trigger.
/// 
/// This avoids the common issue where the action visually finishes but the Animator is still
/// inside the action state or in transition, causing the next trigger to be lost.
/// </summary>
public abstract class AnimationEventEquippedItemBehaviour : EquippedItemBehaviour
{
    [Header("References")]
    [Tooltip("Optional animator used by this item. If empty, one will be searched in children.")]
    [SerializeField] protected Animator ItemAnimator;

    [Header("Animation Parameters")]
    [Tooltip("Animator trigger used when the primary action starts.")]
    [SerializeField] protected string PrimaryUseTriggerName = "PrimaryUse";

    [Tooltip("Animator trigger used when the secondary action starts.")]
    [SerializeField] protected string SecondaryUseTriggerName = "SecondaryUse";

    [Tooltip("Animator bool enabled while any action is running.")]
    [SerializeField] protected string IsUsingBoolName = "IsUsing";

    [Header("Animator Readiness")]
    [Tooltip("Animator layer index checked before starting a new primary action.")]
    [SerializeField] protected int ActionAnimatorLayer = 0;

    [Tooltip("Tag used by action animation states such as mining, scan or pull.")]
    [SerializeField] protected string ActionStateTag = "Action";

    [Tooltip("If true, the item waits until the Animator is fully out of an action state before retriggering hold repeat.")]
    [SerializeField] protected bool WaitUntilAnimatorLeavesActionState = true;

    [Tooltip("If true, an event relay is automatically installed on child Animator objects so animation events can reach this behaviour even when the Animator is not on the same GameObject.")]
    [SerializeField] protected bool AutoInstallAnimatorEventRelay = true;

    [Header("Animation Variants")]
    [Tooltip("Animator int parameter set before the primary action trigger is fired. Leave empty to disable primary animation variants.")]
    [SerializeField] protected string PrimaryAnimationVariantParameterName = "PrimaryVariant";

    [Tooltip("Amount of authored primary action variants available in the Animator. Variant indexes are zero-based.")]
    [SerializeField] protected int PrimaryAnimationVariantCount = 1;

    [Tooltip("If true, the primary animation variant is selected randomly before each primary action starts.")]
    [SerializeField] protected bool RandomizePrimaryAnimationVariant = false;

    [Tooltip("If true and more than one variant exists, the same primary variant is not selected twice in a row.")]
    [SerializeField] protected bool PreventImmediatePrimaryVariantRepeat = true;

    [Tooltip("Animator int parameter set before the secondary action trigger is fired. Leave empty to disable secondary animation variants.")]
    [SerializeField] protected string SecondaryAnimationVariantParameterName = "SecondaryVariant";

    [Tooltip("Amount of authored secondary action variants available in the Animator. Variant indexes are zero-based.")]
    [SerializeField] protected int SecondaryAnimationVariantCount = 1;

    [Tooltip("If true, the secondary animation variant is selected randomly before each secondary action starts.")]
    [SerializeField] protected bool RandomizeSecondaryAnimationVariant = false;

    [Tooltip("If true and more than one variant exists, the same secondary variant is not selected twice in a row.")]
    [SerializeField] protected bool PreventImmediateSecondaryVariantRepeat = true;

    [Header("Behaviour")]
    [Tooltip("If true, holding the primary input starts a new action when the current one finishes.")]
    [SerializeField] protected bool AllowPrimaryHoldRepeat = true;

    [Tooltip("If true, holding the secondary input starts a new action when the current one finishes.")]
    [SerializeField] protected bool AllowSecondaryHoldRepeat = false;

    [Header("Debug")]
    [Tooltip("Logs animation-event item flow.")]
    [SerializeField] protected bool DebugLogs = false;

    /// <summary>
    /// Whether a primary action is currently in progress and waiting for animation events.
    /// </summary>
    protected bool IsPrimaryActionRunning;

    /// <summary>
    /// Whether a secondary action is currently in progress and waiting for animation events.
    /// </summary>
    protected bool IsSecondaryActionRunning;

    /// <summary>
    /// Whether a new primary action should be started as soon as the Animator is ready.
    /// </summary>
    protected bool PendingPrimaryRepeat;

    /// <summary>
    /// Whether a new secondary action should be started as soon as the Animator is ready.
    /// </summary>
    protected bool PendingSecondaryRepeat;

    /// <summary>
    /// Last primary animation variant index selected by this item.
    /// </summary>
    private int LastPrimaryAnimationVariantIndex = -1;

    /// <summary>
    /// Last secondary animation variant index selected by this item.
    /// </summary>
    private int LastSecondaryAnimationVariantIndex = -1;

    /// <summary>
    /// Initializes runtime references and resolves missing animator references.
    /// </summary>
    public override void Initialize(HotbarController ownerHotbar, ItemInstance itemInstance)
    {
        base.Initialize(ownerHotbar, itemInstance);
        ResolveAnimatorReference(ownerHotbar);
        EnsureAnimatorEventRelay();
    }

    /// <summary>
    /// Resolves the Animator used by this equipped item.
    /// The search supports root behaviours, nested behaviours and a separate non-animated motion pivot hierarchy.
    /// </summary>
    /// <param name="OwnerHotbarValue">Hotbar that spawned this equipped item.</param>
    protected virtual void ResolveAnimatorReference(HotbarController OwnerHotbarValue)
    {
        if (ItemAnimator != null)
        {
            return;
        }

        ItemAnimator = GetComponentInChildren<Animator>(true);

        if (ItemAnimator != null)
        {
            return;
        }

        ItemAnimator = GetComponentInParent<Animator>();

        if (ItemAnimator != null)
        {
            return;
        }

        if (OwnerHotbarValue != null && OwnerHotbarValue.GetCurrentEquippedObject() != null)
        {
            ItemAnimator = OwnerHotbarValue.GetCurrentEquippedObject().GetComponentInChildren<Animator>(true);
        }

        if (ItemAnimator == null)
        {
            Log("No Animator could be resolved for this equipped item. Primary and secondary actions will run logic state but cannot play authored animations.");
        }
    }

    /// <summary>
    /// Installs an animation event relay on the Animator GameObject when the gameplay behaviour lives on a different object.
    /// Unity animation events are sent to components on the same GameObject as the Animator, not automatically to parent behaviours.
    /// </summary>
    protected virtual void EnsureAnimatorEventRelay()
    {
        if (!AutoInstallAnimatorEventRelay || ItemAnimator == null || ItemAnimator.gameObject == gameObject)
        {
            return;
        }

        AnimationEventEquippedItemRelay Relay = ItemAnimator.GetComponent<AnimationEventEquippedItemRelay>();

        if (Relay == null)
        {
            Relay = ItemAnimator.gameObject.AddComponent<AnimationEventEquippedItemRelay>();
        }

        Relay.Initialize(this);
        Log("Installed animation event relay on Animator object: " + ItemAnimator.name);
    }

    /// <summary>
    /// Processes queued hold repeats after the Animator finished updating for the frame.
    /// </summary>
    protected virtual void LateUpdate()
    {
        ProcessPendingPrimaryRepeat();
        ProcessPendingSecondaryRepeat();
    }

    /// <summary>
    /// Starts the primary action if no other primary action is currently running.
    /// Repeated clicks during the same action are ignored.
    /// </summary>
    public override void OnPrimaryUseStarted()
    {
        base.OnPrimaryUseStarted();

        if (IsPrimaryActionRunning)
        {
            Log("Primary input ignored because the primary action is already running.");
            return;
        }

        TryStartPrimaryAction();
    }

    /// <summary>
    /// While holding, the base class only queues a repeat request.
    /// The actual restart is deferred until the Animator is ready.
    /// </summary>
    public override void OnPrimaryUseHeld()
    {
        if (!AllowPrimaryHoldRepeat || !IsPrimaryUseActive)
        {
            return;
        }

        if (IsPrimaryActionRunning)
        {
            return;
        }

        PendingPrimaryRepeat = true;
    }

    /// <summary>
    /// Ends the primary hold state.
    /// </summary>
    public override void OnPrimaryUseEnded()
    {
        base.OnPrimaryUseEnded();
        PendingPrimaryRepeat = false;
    }

    /// <summary>
    /// Starts the secondary action if no other secondary action is currently running.
    /// Repeated clicks during the same action are ignored.
    /// </summary>
    public override void OnSecondaryUseStarted()
    {
        base.OnSecondaryUseStarted();

        if (IsSecondaryActionRunning)
        {
            Log("Secondary input ignored because the secondary action is already running.");
            return;
        }

        TryStartSecondaryAction();
    }

    /// <summary>
    /// While holding, the base class only queues a secondary repeat request.
    /// The actual restart is deferred until the Animator is ready.
    /// </summary>
    public override void OnSecondaryUseHeld()
    {
        if (!AllowSecondaryHoldRepeat || !IsSecondaryUseActive)
        {
            return;
        }

        if (IsSecondaryActionRunning)
        {
            return;
        }

        PendingSecondaryRepeat = true;
    }

    /// <summary>
    /// Ends the secondary hold state.
    /// </summary>
    public override void OnSecondaryUseEnded()
    {
        base.OnSecondaryUseEnded();
        PendingSecondaryRepeat = false;
    }

    /// <summary>
    /// Safely interrupts any active action before the item is unequipped.
    /// This prevents stuck animations, delayed impacts or blocked tool states.
    /// </summary>
    public override void ForceStopItemUsage()
    {
        base.ForceStopItemUsage();

        IsPrimaryActionRunning = false;
        IsSecondaryActionRunning = false;
        PendingPrimaryRepeat = false;
        PendingSecondaryRepeat = false;

        ResetAnimatorTrigger(PrimaryUseTriggerName);
        ResetAnimatorTrigger(SecondaryUseTriggerName);
        SetAnimatorUsingState(false);

        OnForcedUsageStopped();
    }

    /// <summary>
    /// Tries to process a queued primary repeat when the action and the Animator are ready.
    /// </summary>
    protected virtual void ProcessPendingPrimaryRepeat()
    {
        if (!PendingPrimaryRepeat)
        {
            return;
        }

        if (!AllowPrimaryHoldRepeat || !IsPrimaryUseActive)
        {
            PendingPrimaryRepeat = false;
            return;
        }

        if (IsPrimaryActionRunning || !IsAnimatorReadyForPrimaryAction())
        {
            return;
        }

        PendingPrimaryRepeat = false;
        TryStartPrimaryAction();
    }

    /// <summary>
    /// Tries to process a queued secondary repeat when the action and the Animator are ready.
    /// </summary>
    protected virtual void ProcessPendingSecondaryRepeat()
    {
        if (!PendingSecondaryRepeat)
        {
            return;
        }

        if (!AllowSecondaryHoldRepeat || !IsSecondaryUseActive)
        {
            PendingSecondaryRepeat = false;
            return;
        }

        if (IsSecondaryActionRunning || !IsAnimatorReadyForSecondaryAction())
        {
            return;
        }

        PendingSecondaryRepeat = false;
        TryStartSecondaryAction();
    }

    /// <summary>
    /// Attempts to start the primary action and trigger the corresponding animation.
    /// </summary>
    protected virtual void TryStartPrimaryAction()
    {
        if (IsPrimaryActionRunning || !CanStartPrimaryAction() || !IsAnimatorReadyForPrimaryAction())
        {
            return;
        }

        IsPrimaryActionRunning = true;
        SetAnimatorUsingState(true);
        ApplyPrimaryAnimationVariant();
        TryPlayAnimatorTrigger(PrimaryUseTriggerName);
        OnPrimaryActionStarted();
    }

    /// <summary>
    /// Attempts to start the secondary action and trigger the corresponding animation.
    /// </summary>
    protected virtual void TryStartSecondaryAction()
    {
        if (IsSecondaryActionRunning || !CanStartSecondaryAction() || !IsAnimatorReadyForSecondaryAction())
        {
            return;
        }

        IsSecondaryActionRunning = true;
        SetAnimatorUsingState(true);
        ApplySecondaryAnimationVariant();
        TryPlayAnimatorTrigger(SecondaryUseTriggerName);
        OnSecondaryActionStarted();
    }

    /// <summary>
    /// Checks whether the Animator is ready to accept a new primary trigger.
    /// </summary>
    protected virtual bool IsAnimatorReadyForPrimaryAction()
    {
        return IsAnimatorReadyForNewAction();
    }

    /// <summary>
    /// Checks whether the Animator is ready to accept a new secondary trigger.
    /// </summary>
    protected virtual bool IsAnimatorReadyForSecondaryAction()
    {
        return IsAnimatorReadyForNewAction();
    }

    /// <summary>
    /// Returns whether the Animator is outside transitions and no longer inside an action-tagged state.
    /// </summary>
    protected virtual bool IsAnimatorReadyForNewAction()
    {
        if (ItemAnimator == null)
        {
            return true;
        }

        if (ItemAnimator.IsInTransition(ActionAnimatorLayer))
        {
            return false;
        }

        if (!WaitUntilAnimatorLeavesActionState)
        {
            return true;
        }

        AnimatorStateInfo currentStateInfo = ItemAnimator.GetCurrentAnimatorStateInfo(ActionAnimatorLayer);

        if (!string.IsNullOrWhiteSpace(ActionStateTag) && currentStateInfo.IsTag(ActionStateTag))
        {
            return false;
        }

        return true;
    }


    /// <summary>
    /// Gets whether this animation-event item is currently using input or waiting for animation events.
    /// </summary>
    /// <returns>True when input usage or an animation-event action is active.</returns>
    public override bool GetIsUsageActive()
    {
        return base.GetIsUsageActive() || IsPrimaryActionRunning || IsSecondaryActionRunning;
    }

    /// <summary>
    /// Gets whether procedural view motion should be suppressed while this animated item action is active.
    /// </summary>
    /// <returns>True while the item is using input or waiting for action animation events.</returns>
    public override bool ShouldBlockProceduralViewMotion()
    {
        return GetIsUsageActive();
    }

    /// <summary>
    /// Checks whether the primary action is allowed to start.
    /// Subclasses can override this for ammo, cooldowns or validation.
    /// </summary>
    protected virtual bool CanStartPrimaryAction()
    {
        return true;
    }

    /// <summary>
    /// Checks whether the secondary action is allowed to start.
    /// Subclasses can override this for custom gating logic.
    /// </summary>
    protected virtual bool CanStartSecondaryAction()
    {
        return true;
    }

    /// <summary>
    /// Called immediately when the primary action starts.
    /// Use this for start sounds, charge-up VFX or temporary states.
    /// </summary>
    protected virtual void OnPrimaryActionStarted()
    {
        Log("Primary action started.");
    }

    /// <summary>
    /// Called exactly when the animation event signals the primary gameplay impact frame.
    /// </summary>
    protected abstract void OnPrimaryActionImpact();

    /// <summary>
    /// Called when the animation event signals that the primary action has fully finished.
    /// </summary>
    protected virtual void OnPrimaryActionFinished()
    {
        Log("Primary action finished.");
    }

    /// <summary>
    /// Called immediately when the secondary action starts.
    /// </summary>
    protected virtual void OnSecondaryActionStarted()
    {
        Log("Secondary action started.");
    }

    /// <summary>
    /// Called exactly when the animation event signals the secondary gameplay impact frame.
    /// </summary>
    protected virtual void OnSecondaryActionImpact()
    {
    }

    /// <summary>
    /// Called when the animation event signals that the secondary action has fully finished.
    /// </summary>
    protected virtual void OnSecondaryActionFinished()
    {
        Log("Secondary action finished.");
    }

    /// <summary>
    /// Called after all active usage has been forcefully interrupted.
    /// </summary>
    protected virtual void OnForcedUsageStopped()
    {
        Log("Item usage was forcefully stopped.");
    }

    /// <summary>
    /// Animation Event hook for the primary impact frame.
    /// Call this from the animation clip at the exact frame where the effect should happen.
    /// </summary>
    public void AnimationEvent_PrimaryImpact()
    {
        if (!IsPrimaryActionRunning)
        {
            Log("Primary impact animation event ignored because no primary action is running.");
            return;
        }

        OnPrimaryActionImpact();
    }

    /// <summary>
    /// Animation Event hook for the end of the primary action.
    /// Call this near the end of the primary animation clip.
    /// </summary>
    public void AnimationEvent_PrimaryFinished()
    {
        if (!IsPrimaryActionRunning)
        {
            Log("Primary finished animation event ignored because no primary action is running.");
            return;
        }

        OnPrimaryActionFinished();
        IsPrimaryActionRunning = false;

        if (!IsSecondaryActionRunning)
        {
            SetAnimatorUsingState(false);
        }

        if (AllowPrimaryHoldRepeat && IsPrimaryUseActive)
        {
            PendingPrimaryRepeat = true;
        }
    }

    /// <summary>
    /// Animation Event hook for the secondary impact frame.
    /// Call this from the animation clip at the exact frame where the effect should happen.
    /// </summary>
    public void AnimationEvent_SecondaryImpact()
    {
        if (!IsSecondaryActionRunning)
        {
            Log("Secondary impact animation event ignored because no secondary action is running.");
            return;
        }

        OnSecondaryActionImpact();
    }

    /// <summary>
    /// Animation Event hook for the end of the secondary action.
    /// Call this near the end of the secondary animation clip.
    /// </summary>
    public void AnimationEvent_SecondaryFinished()
    {
        if (!IsSecondaryActionRunning)
        {
            Log("Secondary finished animation event ignored because no secondary action is running.");
            return;
        }

        OnSecondaryActionFinished();
        IsSecondaryActionRunning = false;

        if (!IsPrimaryActionRunning)
        {
            SetAnimatorUsingState(false);
        }

        if (AllowSecondaryHoldRepeat && IsSecondaryUseActive)
        {
            PendingSecondaryRepeat = true;
        }
    }

    /// <summary>
    /// Selects and applies the primary animation variant parameter before the primary trigger is fired.
    /// The Animator can then route the same primary trigger to different authored states using this integer.
    /// </summary>
    protected virtual void ApplyPrimaryAnimationVariant()
    {
        int VariantIndex = ResolveAnimationVariantIndex(
            PrimaryAnimationVariantCount,
            RandomizePrimaryAnimationVariant,
            PreventImmediatePrimaryVariantRepeat,
            LastPrimaryAnimationVariantIndex);

        LastPrimaryAnimationVariantIndex = VariantIndex;
        TrySetAnimatorInteger(PrimaryAnimationVariantParameterName, VariantIndex);
    }

    /// <summary>
    /// Selects and applies the secondary animation variant parameter before the secondary trigger is fired.
    /// </summary>
    protected virtual void ApplySecondaryAnimationVariant()
    {
        int VariantIndex = ResolveAnimationVariantIndex(
            SecondaryAnimationVariantCount,
            RandomizeSecondaryAnimationVariant,
            PreventImmediateSecondaryVariantRepeat,
            LastSecondaryAnimationVariantIndex);

        LastSecondaryAnimationVariantIndex = VariantIndex;
        TrySetAnimatorInteger(SecondaryAnimationVariantParameterName, VariantIndex);
    }

    /// <summary>
    /// Resolves one animation variant index using either deterministic zero or random selection.
    /// </summary>
    /// <param name="VariantCount">Amount of authored variants available.</param>
    /// <param name="RandomizeVariant">Whether a random variant should be selected.</param>
    /// <param name="PreventImmediateRepeat">Whether the previous variant should be avoided when possible.</param>
    /// <param name="LastVariantIndex">Previously selected variant index.</param>
    /// <returns>Resolved zero-based variant index.</returns>
    private int ResolveAnimationVariantIndex(int VariantCount, bool RandomizeVariant, bool PreventImmediateRepeat, int LastVariantIndex)
    {
        int SafeVariantCount = Mathf.Max(1, VariantCount);

        if (!RandomizeVariant || SafeVariantCount <= 1)
        {
            return 0;
        }

        int VariantIndex = Random.Range(0, SafeVariantCount);

        if (PreventImmediateRepeat && SafeVariantCount > 1 && VariantIndex == LastVariantIndex)
        {
            VariantIndex = (VariantIndex + Random.Range(1, SafeVariantCount)) % SafeVariantCount;
        }

        return VariantIndex;
    }

    /// <summary>
    /// Sets an Animator integer parameter if it exists on the current Animator Controller.
    /// Missing parameters are ignored so items that do not use variants keep working unchanged.
    /// </summary>
    /// <param name="ParameterName">Animator integer parameter name.</param>
    /// <param name="Value">Integer value assigned to the Animator.</param>
    protected void TrySetAnimatorInteger(string ParameterName, int Value)
    {
        if (ItemAnimator == null || string.IsNullOrWhiteSpace(ParameterName))
        {
            return;
        }

        if (!HasAnimatorParameter(ParameterName, AnimatorControllerParameterType.Int))
        {
            Log("Animator int parameter not found or has the wrong type: " + ParameterName + " on " + ItemAnimator.name);
            return;
        }

        ItemAnimator.SetInteger(ParameterName, Value);
        Log("Animator int parameter set: " + ParameterName + " = " + Value + " on " + ItemAnimator.name);
    }

    /// <summary>
    /// Triggers an animator parameter if the name is valid and exists on the current Animator Controller.
    /// </summary>
    protected void TryPlayAnimatorTrigger(string triggerName)
    {
        if (ItemAnimator == null || string.IsNullOrWhiteSpace(triggerName))
        {
            return;
        }

        if (!HasAnimatorParameter(triggerName, AnimatorControllerParameterType.Trigger))
        {
            Log("Animator trigger not found or has the wrong type: " + triggerName + " on " + ItemAnimator.name);
            return;
        }

        ItemAnimator.ResetTrigger(triggerName);
        ItemAnimator.SetTrigger(triggerName);
        Log("Animator trigger fired: " + triggerName + " on " + ItemAnimator.name);
    }

    /// <summary>
    /// Resets an animator trigger if the name is valid and exists on the current Animator Controller.
    /// </summary>
    protected void ResetAnimatorTrigger(string triggerName)
    {
        if (ItemAnimator == null || string.IsNullOrWhiteSpace(triggerName))
        {
            return;
        }

        if (!HasAnimatorParameter(triggerName, AnimatorControllerParameterType.Trigger))
        {
            return;
        }

        ItemAnimator.ResetTrigger(triggerName);
    }

    /// <summary>
    /// Sets the animator using bool if configured and present on the current Animator Controller.
    /// </summary>
    protected void SetAnimatorUsingState(bool isUsing)
    {
        if (ItemAnimator == null || string.IsNullOrWhiteSpace(IsUsingBoolName))
        {
            return;
        }

        if (!HasAnimatorParameter(IsUsingBoolName, AnimatorControllerParameterType.Bool))
        {
            Log("Animator bool not found or has the wrong type: " + IsUsingBoolName + " on " + ItemAnimator.name);
            return;
        }

        ItemAnimator.SetBool(IsUsingBoolName, isUsing);
    }

    /// <summary>
    /// Returns whether the Animator has a parameter with the requested name and type.
    /// </summary>
    /// <param name="ParameterName">Animator parameter name.</param>
    /// <param name="ParameterType">Expected animator parameter type.</param>
    /// <returns>True when the parameter exists and matches the expected type.</returns>
    private bool HasAnimatorParameter(string ParameterName, AnimatorControllerParameterType ParameterType)
    {
        if (ItemAnimator == null || string.IsNullOrWhiteSpace(ParameterName))
        {
            return false;
        }

        AnimatorControllerParameter[] Parameters = ItemAnimator.parameters;

        for (int Index = 0; Index < Parameters.Length; Index++)
        {
            AnimatorControllerParameter Parameter = Parameters[Index];

            if (Parameter == null)
            {
                continue;
            }

            if (Parameter.type == ParameterType && string.Equals(Parameter.name, ParameterName, System.StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Logs animation-event item messages if debug logging is enabled.
    /// </summary>
    protected void Log(string message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[" + GetType().Name + "] " + message);
    }
}
