using UnityEngine;

/// <summary>
/// Equipped pickaxe behaviour built on top of the animation-event item action system.
/// The mining hit now happens only when the animation clip explicitly sends the impact event,
/// which keeps the visible swing and the gameplay hit perfectly synchronized.
/// </summary>
public sealed class PickaxeItemBehaviour : AnimationEventEquippedItemBehaviour
{
    [Header("References")]
    [Tooltip("Camera used to cast mining rays. If empty, the item looks for one on the owner.")]
    [SerializeField] private Camera PlayerCamera;

    [Header("Mining")]
    [Tooltip("Mining strength applied by this pickaxe.")]
    [SerializeField] private float MiningPower = 1f;

    [Tooltip("Maximum distance used to detect mineable targets.")]
    [SerializeField] private float MiningDistance = 4f;

    [Tooltip("Layers considered valid mining targets.")]
    [SerializeField] private LayerMask MiningLayers = ~0;

    [Header("Debug")]
    [Tooltip("Draws the mining ray in the Scene view when attempting a hit.")]
    [SerializeField] private bool DrawDebugRay = false;

    /// <summary>
    /// Initializes the pickaxe and resolves missing owner references.
    /// </summary>
    public override void Initialize(HotbarController ownerHotbar, ItemInstance itemInstance)
    {
        base.Initialize(ownerHotbar, itemInstance);

        if (PlayerCamera == null && OwnerHotbar != null)
        {
            PlayerCamera = OwnerHotbar.GetComponentInChildren<Camera>();
        }
    }

    /// <summary>
    /// Applies the mining effect exactly when the animation impact event is fired.
    /// </summary>
    protected override void OnPrimaryActionImpact()
    {
        if (PlayerCamera == null)
        {
            Log("No camera was found for the pickaxe mining ray.");
            return;
        }

        Ray miningRay = PlayerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (DrawDebugRay)
        {
            Debug.DrawRay(miningRay.origin, miningRay.direction * MiningDistance, Color.yellow, 0.5f);
        }

        if (!Physics.Raycast(miningRay, out RaycastHit hitInfo, MiningDistance, MiningLayers, QueryTriggerInteraction.Ignore))
        {
            Log("Mining ray hit nothing.");
            return;
        }

        IMineable mineable = ResolveMineable(hitInfo);

        if (mineable == null)
        {
            Log("Mining ray hit a non-mineable target.");
            return;
        }

        bool wasMined = mineable.TryMine(MiningPower);

        if (wasMined)
        {
            Log("Mineable target was successfully hit at animation impact time.");
        }
    }

    /// <summary>
    /// Resolves a mineable target from the current raycast hit.
    /// </summary>
    private IMineable ResolveMineable(RaycastHit hitInfo)
    {
        if (hitInfo.collider == null)
        {
            return null;
        }

        IMineable mineable = hitInfo.collider.GetComponent<IMineable>();

        if (mineable != null)
        {
            return mineable;
        }

        mineable = hitInfo.collider.GetComponentInParent<IMineable>();

        if (mineable != null)
        {
            return mineable;
        }

        if (hitInfo.rigidbody != null)
        {
            mineable = hitInfo.rigidbody.GetComponent<IMineable>();

            if (mineable != null)
            {
                return mineable;
            }

            mineable = hitInfo.rigidbody.GetComponentInParent<IMineable>();

            if (mineable != null)
            {
                return mineable;
            }
        }

        return null;
    }
}
