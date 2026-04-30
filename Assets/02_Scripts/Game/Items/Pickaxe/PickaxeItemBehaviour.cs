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
    /// <param name="OwnerHotbar">Hotbar that owns this equipped item.</param>
    /// <param name="ItemInstance">Runtime item instance attached to this behaviour.</param>
    public override void Initialize(HotbarController OwnerHotbar, ItemInstance ItemInstance)
    {
        base.Initialize(OwnerHotbar, ItemInstance);

        if (PlayerCamera == null && this.OwnerHotbar != null)
        {
            PlayerCamera = this.OwnerHotbar.GetComponentInChildren<Camera>();
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

        Ray MiningRay = PlayerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (DrawDebugRay)
        {
            Debug.DrawRay(MiningRay.origin, MiningRay.direction * MiningDistance, Color.yellow, 0.5f);
        }

        if (!Physics.Raycast(MiningRay, out RaycastHit HitInfo, MiningDistance, MiningLayers, QueryTriggerInteraction.Ignore))
        {
            Log("Mining ray hit nothing.");
            return;
        }

        IMineable Mineable = ResolveMineable(HitInfo);

        if (Mineable == null)
        {
            Log("Mining ray hit a non-mineable target.");
            return;
        }

        MiningHitContext HitContext = new MiningHitContext(
            MiningHitContext.HitSourceType.Player,
            this.OwnerHotbar != null ? this.OwnerHotbar.gameObject : gameObject,
            HitInfo.point,
            HitInfo.normal);

        bool WasMined = Mineable.TryMine(MiningPower, HitContext);

        if (WasMined)
        {
            Log("Mineable target was successfully hit at animation impact time.");
        }
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
}