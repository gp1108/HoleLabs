using UnityEngine;

/// <summary>
/// Equipped drill item behaviour used to preview valid placement spots and place the drill
/// into the world when the primary use input is pressed.
/// </summary>
public sealed class DrillItemBehaviour : EquippedItemBehaviour
{
    [Header("References")]
    [Tooltip("Camera used to detect the currently targeted drill spot.")]
    [SerializeField] private Camera PlayerCamera;

    [Tooltip("Runtime ore service used to initialize newly placed drill machines.")]
    [SerializeField] private OreRuntimeService OreRuntimeService;

    [Header("Placement")]
    [Tooltip("Maximum raycast distance used to target a drill placement spot.")]
    [SerializeField] private float PlacementDistance = 5f;

    [Tooltip("Maximum radius around the player where ghost previews become visible.")]
    [SerializeField] private float GhostVisibilityRadius = 12f;

    [Tooltip("Layers considered valid for spot targeting raycasts.")]
    [SerializeField] private LayerMask PlacementLayers = ~0;

    [Header("Debug")]
    [Tooltip("Draws the placement ray in the Scene view.")]
    [SerializeField] private bool DrawDebugRay = false;

    [Tooltip("Logs placement operations.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Cached list of all drill spots in the scene.
    /// </summary>
    private DrillPlacementSpot[] DrillPlacementSpots;

    /// <summary>
    /// Spot currently targeted by the player ray.
    /// </summary>
    private DrillPlacementSpot CurrentTargetedSpot;

    /// <summary>
    /// Initializes references and caches placement spots.
    /// </summary>
    public override void Initialize(HotbarController OwnerHotbar, ItemInstance ItemInstance)
    {
        base.Initialize(OwnerHotbar, ItemInstance);

        if (PlayerCamera == null && this.OwnerHotbar != null)
        {
            PlayerCamera = this.OwnerHotbar.GetComponentInChildren<Camera>();
        }

        if (OreRuntimeService == null)
        {
            OreRuntimeService = FindFirstObjectByType<OreRuntimeService>();
        }

        DrillPlacementSpots = FindObjectsByType<DrillPlacementSpot>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
    }

    /// <summary>
    /// Shows nearby ghost previews when the drill becomes equipped.
    /// </summary>
    public override void OnEquipped()
    {
        base.OnEquipped();
        RefreshGhostVisibilityAndTargeting();
    }

    /// <summary>
    /// Hides every ghost preview when the drill stops being equipped.
    /// </summary>
    public override void OnUnequipped()
    {
        base.OnUnequipped();
        HideAllGhosts();
    }

    /// <summary>
    /// Attempts one placement on primary click.
    /// </summary>
    public override void OnPrimaryUseStarted()
    {
        base.OnPrimaryUseStarted();
        RefreshGhostVisibilityAndTargeting();
        TryPlaceCurrentTargetedSpot();
    }

    /// <summary>
    /// Updates ghost visibility and target highlighting every frame while equipped.
    /// </summary>
    private void Update()
    {
        RefreshGhostVisibilityAndTargeting();
    }

    /// <summary>
    /// Refreshes nearby ghost previews and the currently targeted valid spot.
    /// </summary>
    private void RefreshGhostVisibilityAndTargeting()
    {
        CurrentTargetedSpot = ResolveTargetedSpot();

        if (DrillPlacementSpots == null)
        {
            return;
        }

        Vector3 ReferencePosition = PlayerCamera != null ? PlayerCamera.transform.position : transform.position;
        float VisibilityRadiusSqr = GhostVisibilityRadius * GhostVisibilityRadius;

        for (int Index = 0; Index < DrillPlacementSpots.Length; Index++)
        {
            DrillPlacementSpot Spot = DrillPlacementSpots[Index];

            if (Spot == null)
            {
                continue;
            }

            bool IsNearEnough = (Spot.GetPlacementWorldPosition() - ReferencePosition).sqrMagnitude <= VisibilityRadiusSqr;
            bool ShouldShowGhost = IsNearEnough && Spot.CanAcceptPlacement();

            Spot.SetGhostVisible(ShouldShowGhost);
            Spot.SetGhostHighlighted(Spot == CurrentTargetedSpot);
        }
    }

    /// <summary>
    /// Resolves the valid currently targeted placement spot from the camera center ray.
    /// </summary>
    private DrillPlacementSpot ResolveTargetedSpot()
    {
        if (PlayerCamera == null)
        {
            return null;
        }

        Ray PlacementRay = PlayerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (DrawDebugRay)
        {
            Debug.DrawRay(PlacementRay.origin, PlacementRay.direction * PlacementDistance, Color.green);
        }

        if (!Physics.Raycast(PlacementRay, out RaycastHit HitInfo, PlacementDistance, PlacementLayers, QueryTriggerInteraction.Collide))
        {
            return null;
        }

        DrillPlacementSpot Spot = HitInfo.collider.GetComponent<DrillPlacementSpot>() ??
                                  HitInfo.collider.GetComponentInParent<DrillPlacementSpot>();

        if (Spot == null || !Spot.CanAcceptPlacement())
        {
            return null;
        }

        return Spot;
    }

    /// <summary>
    /// Places the drill on the current targeted spot and consumes the selected hotbar item.
    /// </summary>
    private void TryPlaceCurrentTargetedSpot()
    {
        if (CurrentTargetedSpot == null || OwnerHotbar == null || ItemInstance == null)
        {
            return;
        }

        bool WasPlaced = CurrentTargetedSpot.TryPlaceDrill(
            OreRuntimeService,
            ItemInstance.GetDefinition());

        if (!WasPlaced)
        {
            Log("Placement failed on targeted spot.");
            return;
        }

        OwnerHotbar.RemoveSelectedItem();
        HideAllGhosts();
        Log("Drill placed successfully.");
    }

    /// <summary>
    /// Hides every ghost preview currently managed by this equipped drill behaviour.
    /// </summary>
    private void HideAllGhosts()
    {
        if (DrillPlacementSpots == null)
        {
            return;
        }

        for (int Index = 0; Index < DrillPlacementSpots.Length; Index++)
        {
            if (DrillPlacementSpots[Index] == null)
            {
                continue;
            }

            DrillPlacementSpots[Index].HideGhost();
        }
    }

    /// <summary>
    /// Writes a debug log when enabled.
    /// </summary>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[DrillItemBehaviour] " + Message, this);
    }
}