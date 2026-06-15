using UnityEngine;

/// <summary>
/// Relays Unity animation events from an Animator GameObject to the equipped item behaviour that owns the gameplay logic.
/// Unity sends animation events only to components on the same GameObject as the Animator, so this relay keeps nested
/// view-model hierarchies safe when the Animator is placed below a procedural motion pivot.
/// </summary>
[DisallowMultipleComponent]
public sealed class AnimationEventEquippedItemRelay : MonoBehaviour
{
    /// <summary>
    /// Equipped item behaviour that receives relayed animation events.
    /// </summary>
    [Tooltip("Equipped item behaviour that receives relayed animation events.")]
    [SerializeField] private AnimationEventEquippedItemBehaviour TargetBehaviour;

    /// <summary>
    /// Assigns the target behaviour used by this relay.
    /// </summary>
    /// <param name="TargetBehaviourValue">Animation-event equipped item behaviour that owns the action state.</param>
    public void Initialize(AnimationEventEquippedItemBehaviour TargetBehaviourValue)
    {
        TargetBehaviour = TargetBehaviourValue;
    }

    /// <summary>
    /// Relays the primary impact animation event to the owning equipped item behaviour.
    /// </summary>
    public void AnimationEvent_PrimaryImpact()
    {
        if (TargetBehaviour != null)
        {
            TargetBehaviour.AnimationEvent_PrimaryImpact();
        }
    }

    /// <summary>
    /// Relays the primary finished animation event to the owning equipped item behaviour.
    /// </summary>
    public void AnimationEvent_PrimaryFinished()
    {
        if (TargetBehaviour != null)
        {
            TargetBehaviour.AnimationEvent_PrimaryFinished();
        }
    }

    /// <summary>
    /// Relays the secondary impact animation event to the owning equipped item behaviour.
    /// </summary>
    public void AnimationEvent_SecondaryImpact()
    {
        if (TargetBehaviour != null)
        {
            TargetBehaviour.AnimationEvent_SecondaryImpact();
        }
    }

    /// <summary>
    /// Relays the secondary finished animation event to the owning equipped item behaviour.
    /// </summary>
    public void AnimationEvent_SecondaryFinished()
    {
        if (TargetBehaviour != null)
        {
            TargetBehaviour.AnimationEvent_SecondaryFinished();
        }
    }
}
