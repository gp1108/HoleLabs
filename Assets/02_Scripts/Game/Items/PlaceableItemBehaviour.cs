using UnityEngine;

/// <summary>
/// Equipped behaviour for placeable items.
/// It raycasts placement spots, displays a ghost preview and installs the selected hotbar item on primary use.
/// </summary>
public sealed class PlaceableItemBehaviour : EquippedItemBehaviour
{
    [Header("References")]
    [Tooltip("Camera used to raycast placement spots. If empty, one is resolved from the owner hotbar.")]
    [SerializeField] private Camera PlayerCamera;

    [Header("Placement")]
    [Tooltip("Maximum distance used to detect installation spots.")]
    [SerializeField] private float PlacementDistance = 4f;

    [Tooltip("Layers considered valid for installation spot raycasts.")]
    [SerializeField] private LayerMask PlacementLayers = ~0;

    [Tooltip("If true, the ghost preview is shown only while a compatible spot is targeted.")]
    [SerializeField] private bool HideGhostWhenInvalid = true;

    [Header("Debug")]
    [Tooltip("Draws the placement ray in the Scene view.")]
    [SerializeField] private bool DrawDebugRay = false;

    [Tooltip("Logs placeable item operations.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Ghost instance currently used by this equipped item.
    /// </summary>
    private GameObject CurrentGhostInstance;

    /// <summary>
    /// Current compatible placement spot under the crosshair.
    /// </summary>
    private PlaceableInstallationSpot CurrentTargetSpot;

    /// <summary>
    /// Cached placeable definition for this equipped item.
    /// </summary>
    private PlaceableItemDefinition PlaceableDefinition;

    /// <summary>
    /// Initializes references and creates the ghost preview instance when configured.
    /// </summary>
    /// <param name="OwnerHotbar">Hotbar that owns this equipped item.</param>
    /// <param name="ItemInstance">Runtime item instance attached to this behaviour.</param>
    public override void Initialize(HotbarController OwnerHotbar, ItemInstance ItemInstance)
    {
        base.Initialize(OwnerHotbar, ItemInstance);

        PlaceableDefinition = ItemInstance != null ? ItemInstance.GetDefinition() as PlaceableItemDefinition : null;

        if (PlayerCamera == null && this.OwnerHotbar != null)
        {
            PlayerCamera = this.OwnerHotbar.GetComponentInChildren<Camera>();
        }

        CreateGhostInstance();
    }

    /// <summary>
    /// Ensures the ghost preview is visible when equipped.
    /// </summary>
    public override void OnEquipped()
    {
        base.OnEquipped();
        UpdateGhostVisibility(false);
    }

    /// <summary>
    /// Destroys transient ghost preview when unequipped.
    /// </summary>
    public override void OnUnequipped()
    {
        base.OnUnequipped();
        DestroyGhostInstance();
    }

    /// <summary>
    /// Destroys transient ghost preview when usage is interrupted.
    /// </summary>
    public override void ForceStopItemUsage()
    {
        base.ForceStopItemUsage();
        CurrentTargetSpot = null;
        UpdateGhostVisibility(false);
    }

    /// <summary>
    /// Updates the current placement target and ghost pose.
    /// </summary>
    private void Update()
    {
        CurrentTargetSpot = ResolveCurrentTargetSpot();
        UpdateGhostPose();
    }

    /// <summary>
    /// Attempts to install the selected placeable item on the current target spot.
    /// </summary>
    public override void OnPrimaryUseStarted()
    {
        base.OnPrimaryUseStarted();

        if (CurrentTargetSpot == null || ItemInstance == null || OwnerHotbar == null)
        {
            Log("No valid installation spot targeted.");
            return;
        }

        if (CurrentTargetSpot.TryInstall(ItemInstance, OwnerHotbar))
        {
            Log("Placeable item installed successfully.");
            DestroyGhostInstance();
        }
    }

    /// <summary>
    /// Creates the ghost preview instance from the placeable definition.
    /// </summary>
    private void CreateGhostInstance()
    {
        DestroyGhostInstance();

        if (PlaceableDefinition == null || PlaceableDefinition.GetGhostPrefab() == null)
        {
            return;
        }

        CurrentGhostInstance = Instantiate(PlaceableDefinition.GetGhostPrefab());
        UpdateGhostVisibility(false);
    }

    /// <summary>
    /// Destroys the current ghost preview instance.
    /// </summary>
    private void DestroyGhostInstance()
    {
        if (CurrentGhostInstance == null)
        {
            return;
        }

        Destroy(CurrentGhostInstance);
        CurrentGhostInstance = null;
    }

    /// <summary>
    /// Resolves the currently targeted compatible installation spot.
    /// </summary>
    /// <returns>Compatible installation spot or null.</returns>
    private PlaceableInstallationSpot ResolveCurrentTargetSpot()
    {
        if (PlayerCamera == null || ItemInstance == null)
        {
            return null;
        }

        Ray PlacementRay = PlayerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (DrawDebugRay)
        {
            Debug.DrawRay(PlacementRay.origin, PlacementRay.direction * PlacementDistance, Color.cyan);
        }

        if (!Physics.Raycast(PlacementRay, out RaycastHit HitInfo, PlacementDistance, PlacementLayers, QueryTriggerInteraction.Collide))
        {
            return null;
        }

        PlaceableInstallationSpot Spot = HitInfo.collider.GetComponent<PlaceableInstallationSpot>() ??
                                         HitInfo.collider.GetComponentInParent<PlaceableInstallationSpot>();

        if (Spot == null || !Spot.CanInstall(ItemInstance))
        {
            return null;
        }

        return Spot;
    }

    /// <summary>
    /// Moves the ghost preview to the current target spot and updates visibility.
    /// </summary>
    private void UpdateGhostPose()
    {
        if (CurrentGhostInstance == null)
        {
            return;
        }

        bool HasValidTarget = CurrentTargetSpot != null;

        if (HasValidTarget)
        {
            CurrentGhostInstance.transform.SetPositionAndRotation(
                CurrentTargetSpot.GetPreviewPosition(),
                CurrentTargetSpot.GetPreviewRotation());
        }

        UpdateGhostVisibility(HasValidTarget || !HideGhostWhenInvalid);
    }

    /// <summary>
    /// Shows or hides the ghost preview instance.
    /// </summary>
    /// <param name="IsVisible">True to show the ghost.</param>
    private void UpdateGhostVisibility(bool IsVisible)
    {
        if (CurrentGhostInstance != null)
        {
            CurrentGhostInstance.SetActive(IsVisible);
        }
    }

    /// <summary>
    /// Logs placeable item messages when debug logging is enabled.
    /// </summary>
    /// <param name="Message">Message to write.</param>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[PlaceableItemBehaviour] " + Message, this);
    }
}
