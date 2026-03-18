using UnityEngine;

/// <summary>
/// Base class for equipped items driven by animation events.
/// The gameplay effect and the action end are both triggered by the Animator clip itself,
/// which keeps gameplay timing aligned with the actual visible animation.
/// This is the correct approach for tools such as pickaxes, scanners and similar items
/// where the effect must happen at an exact frame.
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

    [Header("Behaviour")]
    [Tooltip("If true, holding the primary input starts a new action when the current one finishes.")]
    [SerializeField] protected bool AllowPrimaryHoldRepeat = true;

    [Tooltip("If true, holding the secondary input starts a new action when the current one finishes.")]
    [SerializeField] protected bool AllowSecondaryHoldRepeat = false;

    [Header("Repeat")]
    [Tooltip("If true, hold-repeat waits until the next frame after the current action finishes before starting a new one.")]
    [SerializeField] protected bool DeferHoldRepeatToNextFrame = true;

    /// <summary>
    /// Whether a new primary action should be started as soon as the animator is ready.
    /// </summary>
    protected bool PendingPrimaryRepeat;

    /// <summary>
    /// Whether a new secondary action should be started as soon as the animator is ready.
    /// </summary>
    protected bool PendingSecondaryRepeat;

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
    /// Initializes runtime references and resolves missing animator references.
    /// </summary>
    public override void Initialize(HotbarController ownerHotbar, ItemInstance itemInstance)
    {
        base.Initialize(ownerHotbar, itemInstance);

        if (ItemAnimator == null)
        {
            ItemAnimator = GetComponentInChildren<Animator>();
        }
    }

    /// <summary>
    /// Processes queued hold repeats after the Animator had time to update its state.
    /// This avoids losing triggers when the previous action has just finished.
    /// </summary>
    protected virtual void LateUpdate()
    {
        ProcessPendingPrimaryRepeat();
        ProcessPendingSecondaryRepeat();
    }

    /// <summary>
    /// Tries to process a queued primary repeat when the action is no longer running.
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

        if (IsPrimaryActionRunning)
        {
            return;
        }

        if (!CanStartPrimaryAction())
        {
            PendingPrimaryRepeat = false;
            return;
        }

        PendingPrimaryRepeat = false;
        TryStartPrimaryAction();
    }

    /// <summary>
    /// Tries to process a queued secondary repeat when the action is no longer running.
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

        if (IsSecondaryActionRunning)
        {
            return;
        }

        if (!CanStartSecondaryAction())
        {
            PendingSecondaryRepeat = false;
            return;
        }

        PendingSecondaryRepeat = false;
        TryStartSecondaryAction();
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
    /// Repeats the primary action only after the current one is fully finished.
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

        TryStartPrimaryAction();
    }

    /// <summary>
    /// Ends the primary hold state.
    /// </summary>
    public override void OnPrimaryUseEnded()
    {
        base.OnPrimaryUseEnded();
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
    /// Repeats the secondary action only after the current one is fully finished.
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

        TryStartSecondaryAction();
    }

    /// <summary>
    /// Ends the secondary hold state.
    /// </summary>
    public override void OnSecondaryUseEnded()
    {
        base.OnSecondaryUseEnded();
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
    /// Attempts to start the primary action and trigger the corresponding animation.
    /// </summary>
    protected virtual void TryStartPrimaryAction()
    {
        if (IsPrimaryActionRunning || !CanStartPrimaryAction())
        {
            return;
        }

        IsPrimaryActionRunning = true;
        SetAnimatorUsingState(true);
        TryPlayAnimatorTrigger(PrimaryUseTriggerName);
        OnPrimaryActionStarted();
    }

    /// <summary>
    /// Attempts to start the secondary action and trigger the corresponding animation.
    /// </summary>
    protected virtual void TryStartSecondaryAction()
    {
        if (IsSecondaryActionRunning || !CanStartSecondaryAction())
        {
            return;
        }

        IsSecondaryActionRunning = true;
        SetAnimatorUsingState(true);
        TryPlayAnimatorTrigger(SecondaryUseTriggerName);
        OnSecondaryActionStarted();
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
            if (DeferHoldRepeatToNextFrame)
            {
                PendingPrimaryRepeat = true;
            }
            else
            {
                TryStartPrimaryAction();
            }
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
            if (DeferHoldRepeatToNextFrame)
            {
                PendingSecondaryRepeat = true;
            }
            else
            {
                TryStartSecondaryAction();
            }
        }
    }

    /// <summary>
    /// Triggers an animator parameter if the name is valid.
    /// </summary>
    protected void TryPlayAnimatorTrigger(string triggerName)
    {
        if (ItemAnimator == null || string.IsNullOrWhiteSpace(triggerName))
        {
            return;
        }

        ItemAnimator.SetTrigger(triggerName);
    }

    /// <summary>
    /// Resets an animator trigger if the name is valid.
    /// </summary>
    protected void ResetAnimatorTrigger(string triggerName)
    {
        if (ItemAnimator == null || string.IsNullOrWhiteSpace(triggerName))
        {
            return;
        }

        ItemAnimator.ResetTrigger(triggerName);
    }

    /// <summary>
    /// Sets the animator using bool if configured.
    /// </summary>
    protected void SetAnimatorUsingState(bool isUsing)
    {
        if (ItemAnimator == null || string.IsNullOrWhiteSpace(IsUsingBoolName))
        {
            return;
        }

        ItemAnimator.SetBool(IsUsingBoolName, isUsing);
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
