
using UnityEngine;

/// <summary>
/// Equipped build behaviour that lets the player place descending wall-following pipes using the current hotbar stack.
/// Point A is stored on the first click. While aiming point B, the player sees an exact ghost preview.
/// The final click commits the already-previewed geometry and consumes the required amount from the selected hotbar slot.
/// </summary>
public sealed class PipeBuilderItemBehaviour : EquippedItemBehaviour
{
    [Header("References")]
    [Tooltip("Camera used to cast wall-selection rays. If empty, one is resolved from the owner hotbar.")]
    [SerializeField] private Camera PlayerCamera;

    [Tooltip("Central pipe build controller that owns graph lookup, validation and final instantiation.")]
    [SerializeField] private PipeBuildController PipeBuildController;

    [Tooltip("Optional ghost visualizer used to display the exact preview path before committing.")]
    [SerializeField] private PipeBuilderGhostVisualizer GhostVisualizer;

    [Header("Placement")]
    [Tooltip("Maximum distance used to search the cave wall for preview purposes.")]
    [SerializeField] private float PreviewRayDistance = 18f;

    [Tooltip("Maximum distance at which the player is allowed to commit the currently previewed point B.")]
    [SerializeField] private float CommitBuildDistance = 12f;

    [Tooltip("Center-screen viewport coordinate used for wall selection.")]
    [SerializeField] private Vector2 ViewportAimPoint = new Vector2(0.5f, 0.5f);

    [Tooltip("World center-line distance represented by one pipe item consumed from the hotbar.")]
    [SerializeField] private float WorldUnitsPerPipeItem = 1.5f;

    [Tooltip("Minimum amount that must remain available in the selected slot for the build flow to stay active.")]
    [SerializeField] private int MinimumRequiredSelectedAmount = 1;

    [Header("Behaviour")]
    [Tooltip("If true, right click cancels the currently stored point A.")]
    [SerializeField] private bool SecondaryClickCancelsBuild = true;

    [Tooltip("If true, the preview ghost is hidden when no valid geometry can be resolved.")]
    [SerializeField] private bool HideGhostWhenNoGeometry = true;

    [Header("Debug")]
    [Tooltip("Draws the current build ray in the Scene view.")]
    [SerializeField] private bool DrawDebugRay = false;

    [Tooltip("Logs build tool decisions and failure reasons.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Whether the current frame successfully hit a cave wall for preview evaluation.
    /// </summary>
    private bool HasCurrentWallHit;

    /// <summary>
    /// Latest wall hit information used by preview and commit.
    /// </summary>
    private RaycastHit CurrentWallHit;

    /// <summary>
    /// Latest preview result evaluated from the current wall hit.
    /// </summary>
    private PipeBuildController.PipePreviewResult CurrentPreviewResult;

    /// <summary>
    /// Whether the latest preview is within commit range.
    /// </summary>
    private bool IsPreviewWithinCommitRange;

    /// <summary>
    /// Whether the selected hotbar slot currently has enough material to pay for the preview.
    /// </summary>
    private bool HasEnoughItemsForPreview;

    /// <summary>
    /// Required item count for the current preview.
    /// </summary>
    private int CurrentRequiredItemCount;

    /// <summary>
    /// Initializes missing references.
    /// </summary>
    public override void Initialize(HotbarController OwnerHotbar, ItemInstance ItemInstance)
    {
        base.Initialize(OwnerHotbar, ItemInstance);

        if (PlayerCamera == null && this.OwnerHotbar != null)
        {
            PlayerCamera = this.OwnerHotbar.GetComponentInChildren<Camera>();
        }

        if (PipeBuildController == null)
        {
            PipeBuildController = FindFirstObjectByType<PipeBuildController>();
        }

        if (GhostVisualizer == null)
        {
            GhostVisualizer = GetComponentInChildren<PipeBuilderGhostVisualizer>(true);
        }
    }

    /// <summary>
    /// Clears transient preview state when the tool becomes the selected item.
    /// </summary>
    public override void OnEquipped()
    {
        base.OnEquipped();
        ResetTransientPreviewState();
    }

    /// <summary>
    /// Cancels any in-progress build when the item stops being the selected hotbar entry.
    /// </summary>
    public override void OnUnequipped()
    {
        CancelCurrentBuildFlow();
        base.OnUnequipped();
    }

    /// <summary>
    /// Cancels any in-progress build when a higher-priority interaction interrupts the equipped item.
    /// </summary>
    public override void ForceStopItemUsage()
    {
        base.ForceStopItemUsage();
        CancelCurrentBuildFlow();
    }

    /// <summary>
    /// Stores point A on the first click or commits the already-previewed point B on the second click.
    /// </summary>
    public override void OnPrimaryUseStarted()
    {
        base.OnPrimaryUseStarted();

        if (!CanUseBuildTool())
        {
            return;
        }

        if (!HasCurrentWallHit)
        {
            Log("Build click ignored because no valid cave wall is currently under the crosshair.");
            return;
        }

        if (!PipeBuildController.GetHasPendingStartPoint())
        {
            if (!HasMinimumSelectedAmount())
            {
                Log("Cannot start pipe construction because the selected slot has no remaining build material.");
                return;
            }

            if (CurrentWallHit.distance > Mathf.Max(0.1f, CommitBuildDistance))
            {
                Log("Cannot start pipe construction because point A is outside the allowed build range.");
                return;
            }

            if (PipeBuildController.TryBeginBuildFromWallPoint(CurrentWallHit.point))
            {
                Log("Stored point A for pipe construction.");
            }

            return;
        }

        if (CurrentPreviewResult == null || !CurrentPreviewResult.IsGeometryValid)
        {
            Log("Cannot commit pipe construction because the current preview geometry is invalid.");
            return;
        }

        if (!IsPreviewWithinCommitRange)
        {
            Log("Cannot commit pipe construction because point B is outside the allowed build range.");
            return;
        }

        if (!HasEnoughItemsForPreview)
        {
            Log("Cannot commit pipe construction because the selected hotbar stack does not contain enough pipe items.");
            return;
        }

        if (!PipeBuildController.TryBuildFromPreview(CurrentPreviewResult, out PipePathInstance BuiltPipe) || BuiltPipe == null)
        {
            Log("Pipe preview commit failed: " + PipeBuildController.GetLastFailureReason());
            return;
        }

        if (OwnerHotbar == null || !OwnerHotbar.TryConsumeSelectedItemAmount(CurrentRequiredItemCount, ItemInstance != null ? ItemInstance.GetDefinition() : null))
        {
            Log("Pipe was built but selected hotbar consumption failed unexpectedly. The built instance will be removed to keep the state coherent.");
            Destroy(BuiltPipe.gameObject);
            return;
        }

        ResetTransientPreviewState();
        Log("Built pipe successfully and consumed " + CurrentRequiredItemCount + " pipe item(s).");
    }

    /// <summary>
    /// Optional right-click cancel flow.
    /// </summary>
    public override void OnSecondaryUseStarted()
    {
        base.OnSecondaryUseStarted();

        if (!SecondaryClickCancelsBuild)
        {
            return;
        }

        CancelCurrentBuildFlow();
    }

    /// <summary>
    /// Updates the runtime ghost preview and validates material availability while the item is equipped.
    /// </summary>
    private void Update()
    {
        if (!CanUseBuildTool())
        {
            HideGhost();
            return;
        }

        if (!HasMinimumSelectedAmount() && PipeBuildController.GetHasPendingStartPoint())
        {
            CancelCurrentBuildFlow();
            return;
        }

        UpdateCurrentWallHit();
        UpdateGhostPreview();
    }

    /// <summary>
    /// Updates the currently looked cave wall point.
    /// </summary>
    private void UpdateCurrentWallHit()
    {
        HasCurrentWallHit = false;
        CurrentWallHit = default;

        if (PlayerCamera == null || PipeBuildController == null)
        {
            return;
        }

        Ray BuildRay = PlayerCamera.ViewportPointToRay(new Vector3(ViewportAimPoint.x, ViewportAimPoint.y, 0f));

        if (DrawDebugRay)
        {
            Debug.DrawRay(BuildRay.origin, BuildRay.direction * PreviewRayDistance, Color.cyan);
        }

        HasCurrentWallHit = PipeBuildController.TryRaycastCaveWall(BuildRay, PreviewRayDistance, out CurrentWallHit);
    }

    /// <summary>
    /// Evaluates and renders the current ghost preview from the stored point A to the current looked wall point.
    /// </summary>
    private void UpdateGhostPreview()
    {
        CurrentPreviewResult = null;
        CurrentRequiredItemCount = 0;
        IsPreviewWithinCommitRange = false;
        HasEnoughItemsForPreview = false;

        if (PipeBuildController == null || !PipeBuildController.GetHasPendingStartPoint())
        {
            HideGhost();
            return;
        }

        if (!HasCurrentWallHit)
        {
            if (HideGhostWhenNoGeometry)
            {
                HideGhost();
            }

            return;
        }

        CurrentPreviewResult = PipeBuildController.EvaluatePreviewFromWallPoint(CurrentWallHit.point);
        if (CurrentPreviewResult == null || !CurrentPreviewResult.IsGeometryValid)
        {
            if (HideGhostWhenNoGeometry)
            {
                HideGhost();
            }

            return;
        }

        CurrentRequiredItemCount = CalculateRequiredPipeItemCount(CurrentPreviewResult.GetTotalLength());
        IsPreviewWithinCommitRange = CurrentWallHit.distance <= Mathf.Max(0.1f, CommitBuildDistance);
        HasEnoughItemsForPreview = GetAvailableSelectedAmount() >= CurrentRequiredItemCount;

        bool IsCommitValid = IsPreviewWithinCommitRange && HasEnoughItemsForPreview;
        if (GhostVisualizer != null)
        {
            GhostVisualizer.ShowPreview(
                CurrentPreviewResult.CenterPoints,
                CurrentPreviewResult.SupportDirections,
                PipeBuildController.GetBuildSettings(),
                IsCommitValid);
        }
    }

    /// <summary>
    /// Returns the required amount of pipe items for the provided path length.
    /// </summary>
    private int CalculateRequiredPipeItemCount(float PathLength)
    {
        if (PathLength <= 0.0001f)
        {
            return 0;
        }

        return Mathf.Max(1, Mathf.CeilToInt(PathLength / Mathf.Max(0.01f, WorldUnitsPerPipeItem)));
    }

    /// <summary>
    /// Returns the currently available amount in the selected slot when it still matches this equipped pipe item.
    /// </summary>
    private int GetAvailableSelectedAmount()
    {
        if (OwnerHotbar == null || ItemInstance == null || ItemInstance.GetDefinition() == null)
        {
            return 0;
        }

        ItemInstance SelectedItem = OwnerHotbar.GetSelectedItem();
        if (SelectedItem == null || SelectedItem.GetDefinition() != ItemInstance.GetDefinition())
        {
            return 0;
        }

        return Mathf.Max(0, SelectedItem.GetAmount());
    }

    /// <summary>
    /// Returns whether the selected slot still contains enough material to keep this build flow alive.
    /// </summary>
    private bool HasMinimumSelectedAmount()
    {
        return GetAvailableSelectedAmount() >= Mathf.Max(1, MinimumRequiredSelectedAmount);
    }

    /// <summary>
    /// Cancels the currently stored point A and hides the preview.
    /// </summary>
    private void CancelCurrentBuildFlow()
    {
        if (PipeBuildController != null)
        {
            PipeBuildController.ClearPendingStartPoint();
        }

        ResetTransientPreviewState();
        HideGhost();
    }

    /// <summary>
    /// Clears transient non-persistent preview state.
    /// </summary>
    private void ResetTransientPreviewState()
    {
        CurrentPreviewResult = null;
        CurrentRequiredItemCount = 0;
        IsPreviewWithinCommitRange = false;
        HasEnoughItemsForPreview = false;
        HasCurrentWallHit = false;
        CurrentWallHit = default;
    }

    /// <summary>
    /// Hides the ghost preview safely.
    /// </summary>
    private void HideGhost()
    {
        if (GhostVisualizer != null)
        {
            GhostVisualizer.HidePreview();
        }
    }

    /// <summary>
    /// Returns whether the build tool still has the minimum references needed to operate.
    /// </summary>
    private bool CanUseBuildTool()
    {
        return PlayerCamera != null && PipeBuildController != null && OwnerHotbar != null;
    }

    /// <summary>
    /// Writes builder-specific debug messages when enabled.
    /// </summary>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[PipeBuilderItemBehaviour] " + Message, this);
    }
}
