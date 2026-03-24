using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Equipped magnet tool driven by animation events.
/// Once the activation impact happens, every valid carryable inside the area receives its own
/// spring-joint runtime anchor and follows the magnet target until the use input ends.
/// </summary>
public sealed class MagnetItemBehaviour : AnimationEventEquippedItemBehaviour
{
    [Header("References")]
    [Tooltip("Camera used to place the attraction area in front of the player.")]
    [SerializeField] private Camera PlayerCamera;

    [Tooltip("Optional explicit target point that attracted objects should move towards.")]
    [SerializeField] private Transform MagnetTargetPoint;

    [Header("Magnet Area")]
    [Tooltip("Forward distance from the camera to the center of the magnet area.")]
    [SerializeField] private float AreaForwardDistance = 3.25f;

    [Tooltip("Radius of the attraction area.")]
    [SerializeField] private float AreaRadius = 2.5f;

    [Tooltip("Layers considered valid for magnetic attraction checks.")]
    [SerializeField] private LayerMask AttractionLayers = ~0;

    [Tooltip("If true, already held carryable objects are ignored.")]
    [SerializeField] private bool IgnoreHeldObjects = true;

    [Header("Behaviour")]
    [Tooltip("If true, the magnet stays active continuously while the primary input remains held after activation.")]
    [SerializeField] private bool ContinuousPullWhileHeld = true;

    [Header("Debug")]
    [Tooltip("Draws the attraction area and target point in the Scene view while the pull is active.")]
    [SerializeField] private bool DrawDebug = false;

    /// <summary>
    /// Whether the magnet is currently in its continuous active state.
    /// </summary>
    private bool IsMagnetActive;

    /// <summary>
    /// Player colliders ignored by every carryable attached to this magnet.
    /// </summary>
    [SerializeField]private Collider[] CachedPlayerColliders;

    /// <summary>
    /// List of carryables currently attached to the magnet target.
    /// </summary>
    private readonly List<PhysicsCarryable> ActiveCarryables = new List<PhysicsCarryable>();

    /// <summary>
    /// Initializes the magnet and resolves missing camera references.
    /// </summary>
    public override void Initialize(HotbarController OwnerHotbar, ItemInstance ItemInstance)
    {
        base.Initialize(OwnerHotbar, ItemInstance);

        if (PlayerCamera == null && this.OwnerHotbar != null)
        {
            PlayerCamera = this.OwnerHotbar.GetComponentInChildren<Camera>();
        }

        if (this.OwnerHotbar != null)
        {
            PlayerInteractionController InteractionController = this.OwnerHotbar.GetComponent<PlayerInteractionController>();
            if (InteractionController == null)
            {
                InteractionController = this.OwnerHotbar.GetComponentInParent<PlayerInteractionController>(true);
            }

            if (InteractionController != null)
            {
                CachedPlayerColliders = InteractionController.GetPlayerColliders();
            }

            if (CachedPlayerColliders == null || CachedPlayerColliders.Length == 0)
            {
                CachedPlayerColliders = PhysicsUtils.GetHierarchyColliders(this.OwnerHotbar.gameObject, true);
            }
        }
    }

    /// <summary>
    /// The magnet should only start one activation action when it is currently inactive.
    /// </summary>
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
        CleanupDetachedCarryables();
    }

    /// <summary>
    /// Activates the continuous magnetic pull exactly at the animation impact frame.
    /// </summary>
    protected override void OnPrimaryActionImpact()
    {
        IsMagnetActive = true;
        ApplyMagnetPull();
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
    /// Attaches all valid carryables inside the attraction area to the magnet target.
    /// </summary>
    private void ApplyMagnetPull()
    {
        if (PlayerCamera == null)
        {
            return;
        }

        Vector3 AreaCenter = GetAreaCenter();
        Transform TargetTransform = ResolveTargetTransform();

        if (DrawDebug)
        {
            //DebugExtension.DrawWireSphere(AreaCenter, AreaRadius, Color.cyan, Time.fixedDeltaTime);
            //DebugExtension.DrawWireSphere(TargetTransform.position, 0.15f, Color.green, Time.fixedDeltaTime);
            Debug.DrawLine(AreaCenter, TargetTransform.position, Color.magenta, Time.fixedDeltaTime);
        }

        Collider[] Hits = Physics.OverlapSphere(AreaCenter, AreaRadius, AttractionLayers, QueryTriggerInteraction.Ignore);

        for (int HitIndex = 0; HitIndex < Hits.Length; HitIndex++)
        {
            Collider CurrentCollider = Hits[HitIndex];
            if (CurrentCollider == null)
            {
                continue;
            }

            PhysicsCarryable Carryable = CurrentCollider.GetComponent<PhysicsCarryable>() ?? CurrentCollider.GetComponentInParent<PhysicsCarryable>();
            if (Carryable == null)
            {
                continue;
            }

            if (IgnoreHeldObjects && Carryable.GetIsHeld())
            {
                continue;
            }

            if (!Carryable.CanAttachToMagnet() && !Carryable.GetIsMagnetized())
            {
                continue;
            }

            if (!ActiveCarryables.Contains(Carryable))
            {
                ActiveCarryables.Add(Carryable);
            }

            Carryable.BeginMagnet(TargetTransform, CachedPlayerColliders);
        }
    }

    /// <summary>
    /// Detaches every carryable currently attached to this magnet.
    /// </summary>
    private void StopMagnetPull()
    {
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
    }

    /// <summary>
    /// Removes null or no-longer-magnetized entries from the runtime carryable list.
    /// </summary>
    private void CleanupDetachedCarryables()
    {
        for (int CarryableIndex = ActiveCarryables.Count - 1; CarryableIndex >= 0; CarryableIndex--)
        {
            PhysicsCarryable Carryable = ActiveCarryables[CarryableIndex];
            if (Carryable == null || !Carryable.GetIsMagnetized())
            {
                ActiveCarryables.RemoveAt(CarryableIndex);
            }
        }
    }

    /// <summary>
    /// Computes the world-space center of the spherical attraction area.
    /// </summary>
    private Vector3 GetAreaCenter()
    {
        return PlayerCamera.transform.position + PlayerCamera.transform.forward * AreaForwardDistance;
    }

    /// <summary>
    /// Resolves the transform used as the runtime spring-anchor target.
    /// </summary>
    private Transform ResolveTargetTransform()
    {
        return MagnetTargetPoint != null ? MagnetTargetPoint : transform;
    }

    /// <summary>
    /// Writes a magnet-specific debug message when logging is enabled.
    /// </summary>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[MagnetItemBehaviour] " + Message, this);
    }
}
