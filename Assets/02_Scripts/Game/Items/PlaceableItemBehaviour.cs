using UnityEngine;

/// <summary>
/// Equipped behaviour for placeable items.
/// It shows a persistent nearby ghost preview, validates obstruction state and installs only when the player targets a ready spot.
/// </summary>
public sealed class PlaceableItemBehaviour : EquippedItemBehaviour
{
    /// <summary>
    /// Visual state applied to the current ghost instance.
    /// </summary>
    private enum GhostVisualState
    {
        Hidden = 0,
        Idle = 1,
        Valid = 2,
        Invalid = 3
    }

    [Header("References")]
    [Tooltip("Camera used to raycast placement spots. If empty, one is resolved from the owner hotbar.")]
    [SerializeField] private Camera PlayerCamera;

    [Header("Placement")]
    [Tooltip("Maximum distance used to directly target installation spots for final placement.")]
    [SerializeField] private float PlacementDistance = 4f;

    [Tooltip("Layers considered valid for installation spot raycasts and nearby spot scans.")]
    [SerializeField] private LayerMask PlacementLayers = ~0;

    [Tooltip("If true, the item can only install when the player is directly aiming at the current ghost spot.")]
    [SerializeField] private bool RequireDirectTargetForInstall = true;

    [Header("Nearby Ghost Preview")]
    [Tooltip("If true, compatible placement spots inside the preview radius keep the ghost visible even when the player is not directly aiming at them.")]
    [SerializeField] private bool ShowNearbySpotGhost = true;

    [Tooltip("Radius around the player camera used to find compatible placement spots for persistent ghost visibility.")]
    [SerializeField] private float SpotPreviewRadius = 50f;

    [Tooltip("Seconds between nearby spot overlap scans. Direct raycast targeting still updates every frame.")]
    [SerializeField] private float SpotPreviewRefreshInterval = 0.1f;

    [Tooltip("Maximum number of colliders scanned when looking for nearby placement spots.")]
    [SerializeField] private int MaxPreviewOverlapResults = 64;

    [Tooltip("If true, the ghost is hidden when no compatible nearby placement spot exists. If false, the ghost can remain on the last valid spot.")]
    [SerializeField] private bool HideGhostWhenInvalid = true;

    [Header("Ghost Materials")]
    [Tooltip("Optional material applied while the ghost is visible but not directly targeted.")]
    [SerializeField] private Material GhostIdleMaterial;

    [Tooltip("Optional material applied while the player targets a valid installable spot.")]
    [SerializeField] private Material GhostValidMaterial;

    [Tooltip("Optional material applied while the previewed spot is blocked by objects, ores, player or other configured obstructions.")]
    [SerializeField] private Material GhostInvalidMaterial;

    [Header("Ghost Tint Fallback")]
    [Tooltip("If true, a MaterialPropertyBlock tint is applied to ghost renderers. This works even when explicit ghost materials are not assigned, provided the shader exposes a color property.")]
    [SerializeField] private bool ApplyGhostTint = true;

    [Tooltip("Tint used while the ghost is visible but not directly targeted.")]
    [SerializeField] private Color GhostIdleTint = new Color(1f, 1f, 1f, 0.45f);

    [Tooltip("Tint used while the player targets a valid installable spot.")]
    [SerializeField] private Color GhostValidTint = new Color(0.15f, 1f, 0.25f, 0.55f);

    [Tooltip("Tint used while the previewed spot is blocked or otherwise not installable.")]
    [SerializeField] private Color GhostInvalidTint = new Color(1f, 0.1f, 0.05f, 0.55f);

    [Tooltip("Primary shader color property used by URP/HDRP shaders.")]
    [SerializeField] private string BaseColorPropertyName = "_BaseColor";

    [Tooltip("Secondary shader color property used by built-in shaders.")]
    [SerializeField] private string ColorPropertyName = "_Color";

    [Header("Debug")]
    [Tooltip("Draws the placement ray and nearby preview radius in the Scene view.")]
    [SerializeField] private bool DrawDebugRay = false;

    [Tooltip("Logs placeable item operations.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Ghost instance currently used by this equipped item.
    /// </summary>
    private GameObject CurrentGhostInstance;

    /// <summary>
    /// Renderers found in the current ghost instance.
    /// </summary>
    private Renderer[] CurrentGhostRenderers;

    /// <summary>
    /// Current compatible placement spot used by the ghost preview.
    /// </summary>
    private PlaceableInstallationSpot CurrentPreviewSpot;

    /// <summary>
    /// Current compatible placement spot directly targeted by the crosshair.
    /// </summary>
    private PlaceableInstallationSpot CurrentTargetSpot;

    /// <summary>
    /// Last known ghost spot kept while invalid hiding is disabled.
    /// </summary>
    private PlaceableInstallationSpot LastPreviewSpot;

    /// <summary>
    /// Cached placement status for the current preview spot.
    /// </summary>
    private PlaceableInstallationSpot.PlacementEvaluationStatus CurrentPlacementStatus;

    /// <summary>
    /// Cached placeable definition for this equipped item.
    /// </summary>
    private PlaceableItemDefinition PlaceableDefinition;

    /// <summary>
    /// Reusable nearby spot scan buffer.
    /// </summary>
    private Collider[] PreviewOverlapResults;

    /// <summary>
    /// Cached property block used for ghost tinting without material instantiation.
    /// </summary>
    private MaterialPropertyBlock GhostPropertyBlock;

    /// <summary>
    /// Remaining time before the next nearby spot scan.
    /// </summary>
    private float NextPreviewScanTime;

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

        EnsurePreviewOverlapBuffer();
        CreateGhostInstance();
    }

    /// <summary>
    /// Ensures the ghost preview starts hidden until a valid compatible spot is found.
    /// </summary>
    public override void OnEquipped()
    {
        base.OnEquipped();
        ForcePreviewRefresh();
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
        CurrentPreviewSpot = null;
        CurrentTargetSpot = null;
        CurrentPlacementStatus = PlaceableInstallationSpot.PlacementEvaluationStatus.InvalidItem;
        UpdateGhostVisibility(false);
    }

    /// <summary>
    /// Updates direct targeting, nearby preview selection and ghost pose.
    /// </summary>
    private void Update()
    {
        RefreshPlacementState();
        UpdateGhostPoseAndState();
    }

    /// <summary>
    /// Attempts to install the selected placeable item on the current targeted spot.
    /// </summary>
    public override void OnPrimaryUseStarted()
    {
        base.OnPrimaryUseStarted();

        if (ItemInstance == null || OwnerHotbar == null)
        {
            Log("Missing item instance or owner hotbar.");
            return;
        }

        if (CurrentPreviewSpot == null)
        {
            Log("No compatible installation spot is currently previewed.");
            return;
        }

        if (RequireDirectTargetForInstall && CurrentTargetSpot != CurrentPreviewSpot)
        {
            Log("Installation rejected because the preview spot is not directly targeted.");
            return;
        }

        if (CurrentPlacementStatus != PlaceableInstallationSpot.PlacementEvaluationStatus.Ready)
        {
            Log("Installation rejected. Status=" + CurrentPlacementStatus);
            return;
        }

        if (CurrentPreviewSpot.TryInstall(ItemInstance, OwnerHotbar))
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
        CurrentGhostRenderers = CurrentGhostInstance.GetComponentsInChildren<Renderer>(true);
        GhostPropertyBlock = new MaterialPropertyBlock();
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
        CurrentGhostRenderers = null;
        CurrentPreviewSpot = null;
        CurrentTargetSpot = null;
    }

    /// <summary>
    /// Refreshes the current placement preview and direct target spot.
    /// </summary>
    private void RefreshPlacementState()
    {
        CurrentTargetSpot = ResolveDirectTargetSpot();

        PlaceableInstallationSpot NearbySpot = null;

        if (ShowNearbySpotGhost)
        {
            NearbySpot = ResolveNearbyPreviewSpot();
        }

        CurrentPreviewSpot = CurrentTargetSpot != null ? CurrentTargetSpot : NearbySpot;

        if (CurrentPreviewSpot == null && !HideGhostWhenInvalid)
        {
            CurrentPreviewSpot = LastPreviewSpot;
        }

        if (CurrentPreviewSpot != null)
        {
            LastPreviewSpot = CurrentPreviewSpot;
            CurrentPlacementStatus = CurrentPreviewSpot.EvaluatePlacement(ItemInstance);
        }
        else
        {
            CurrentPlacementStatus = PlaceableInstallationSpot.PlacementEvaluationStatus.InvalidItem;
        }
    }

    /// <summary>
    /// Resolves the compatible installation spot currently under the crosshair.
    /// </summary>
    /// <returns>Compatible targeted spot or null.</returns>
    private PlaceableInstallationSpot ResolveDirectTargetSpot()
    {
        if (PlayerCamera == null || PlaceableDefinition == null)
        {
            return null;
        }

        Ray PlacementRay = PlayerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (DrawDebugRay)
        {
            Debug.DrawRay(PlacementRay.origin, PlacementRay.direction * PlacementDistance, Color.cyan);
        }

        if (!Physics.Raycast(PlacementRay, out RaycastHit HitInfo, Mathf.Max(0.01f, PlacementDistance), PlacementLayers, QueryTriggerInteraction.Collide))
        {
            return null;
        }

        PlaceableInstallationSpot Spot = HitInfo.collider.GetComponent<PlaceableInstallationSpot>() ??
                                         HitInfo.collider.GetComponentInParent<PlaceableInstallationSpot>();

        if (Spot == null || !Spot.CanPreview(PlaceableDefinition))
        {
            return null;
        }

        return Spot;
    }

    /// <summary>
    /// Resolves the nearest compatible placement spot inside the configured preview radius.
    /// </summary>
    /// <returns>Nearest compatible spot or null.</returns>
    private PlaceableInstallationSpot ResolveNearbyPreviewSpot()
    {
        if (PlayerCamera == null || PlaceableDefinition == null)
        {
            return null;
        }

        if (Time.time < NextPreviewScanTime && CurrentPreviewSpot != null && CurrentPreviewSpot.CanPreview(PlaceableDefinition))
        {
            return CurrentPreviewSpot;
        }

        NextPreviewScanTime = Time.time + Mathf.Max(0.02f, SpotPreviewRefreshInterval);
        EnsurePreviewOverlapBuffer();

        Vector3 ScanCenter = PlayerCamera.transform.position;
        int HitCount = Physics.OverlapSphereNonAlloc(
            ScanCenter,
            Mathf.Max(0.01f, SpotPreviewRadius),
            PreviewOverlapResults,
            PlacementLayers,
            QueryTriggerInteraction.Collide);

        PlaceableInstallationSpot BestSpot = null;
        float BestDistanceSqr = float.MaxValue;

        for (int Index = 0; Index < HitCount; Index++)
        {
            Collider CandidateCollider = PreviewOverlapResults[Index];
            PreviewOverlapResults[Index] = null;

            if (CandidateCollider == null)
            {
                continue;
            }

            PlaceableInstallationSpot CandidateSpot = CandidateCollider.GetComponent<PlaceableInstallationSpot>() ??
                                                      CandidateCollider.GetComponentInParent<PlaceableInstallationSpot>();

            if (CandidateSpot == null || !CandidateSpot.CanPreview(PlaceableDefinition))
            {
                continue;
            }

            float DistanceSqr = (CandidateSpot.GetPreviewPosition() - ScanCenter).sqrMagnitude;

            if (DistanceSqr >= BestDistanceSqr)
            {
                continue;
            }

            BestSpot = CandidateSpot;
            BestDistanceSqr = DistanceSqr;
        }

        return BestSpot;
    }

    /// <summary>
    /// Moves the ghost preview to the current preview spot and applies the visual state.
    /// </summary>
    private void UpdateGhostPoseAndState()
    {
        if (CurrentGhostInstance == null)
        {
            return;
        }

        if (CurrentPreviewSpot == null)
        {
            UpdateGhostVisibility(false);
            ApplyGhostVisualState(GhostVisualState.Hidden);
            return;
        }

        CurrentGhostInstance.transform.SetPositionAndRotation(
            CurrentPreviewSpot.GetPreviewPosition(),
            CurrentPreviewSpot.GetPreviewRotation());

        UpdateGhostVisibility(true);

        bool IsDirectlyTargeted = CurrentTargetSpot == CurrentPreviewSpot;

        if (CurrentPlacementStatus == PlaceableInstallationSpot.PlacementEvaluationStatus.Ready)
        {
            ApplyGhostVisualState(IsDirectlyTargeted ? GhostVisualState.Valid : GhostVisualState.Idle);
        }
        else
        {
            ApplyGhostVisualState(GhostVisualState.Invalid);
        }
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
    /// Applies the configured material or tint for the requested ghost state.
    /// </summary>
    /// <param name="VisualState">State to apply to the ghost renderers.</param>
    private void ApplyGhostVisualState(GhostVisualState VisualState)
    {
        if (CurrentGhostRenderers == null || CurrentGhostRenderers.Length == 0)
        {
            return;
        }

        Material TargetMaterial = ResolveGhostMaterial(VisualState);
        Color TargetTint = ResolveGhostTint(VisualState);

        for (int Index = 0; Index < CurrentGhostRenderers.Length; Index++)
        {
            Renderer TargetRenderer = CurrentGhostRenderers[Index];

            if (TargetRenderer == null)
            {
                continue;
            }

            if (TargetMaterial != null)
            {
                TargetRenderer.sharedMaterial = TargetMaterial;
            }

            if (!ApplyGhostTint)
            {
                continue;
            }

            if (GhostPropertyBlock == null)
            {
                GhostPropertyBlock = new MaterialPropertyBlock();
            }

            TargetRenderer.GetPropertyBlock(GhostPropertyBlock);

            if (!string.IsNullOrWhiteSpace(BaseColorPropertyName))
            {
                GhostPropertyBlock.SetColor(BaseColorPropertyName, TargetTint);
            }

            if (!string.IsNullOrWhiteSpace(ColorPropertyName))
            {
                GhostPropertyBlock.SetColor(ColorPropertyName, TargetTint);
            }

            TargetRenderer.SetPropertyBlock(GhostPropertyBlock);
        }
    }

    /// <summary>
    /// Resolves the explicit material configured for the requested visual state.
    /// </summary>
    /// <param name="VisualState">Ghost visual state.</param>
    /// <returns>Configured material or null.</returns>
    private Material ResolveGhostMaterial(GhostVisualState VisualState)
    {
        switch (VisualState)
        {
            case GhostVisualState.Valid:
                return GhostValidMaterial != null ? GhostValidMaterial : GhostIdleMaterial;

            case GhostVisualState.Invalid:
                return GhostInvalidMaterial != null ? GhostInvalidMaterial : GhostIdleMaterial;

            case GhostVisualState.Idle:
                return GhostIdleMaterial;

            default:
                return null;
        }
    }

    /// <summary>
    /// Resolves the fallback tint configured for the requested visual state.
    /// </summary>
    /// <param name="VisualState">Ghost visual state.</param>
    /// <returns>Configured tint color.</returns>
    private Color ResolveGhostTint(GhostVisualState VisualState)
    {
        switch (VisualState)
        {
            case GhostVisualState.Valid:
                return GhostValidTint;

            case GhostVisualState.Invalid:
                return GhostInvalidTint;

            case GhostVisualState.Idle:
                return GhostIdleTint;

            default:
                return Color.clear;
        }
    }

    /// <summary>
    /// Ensures the nearby preview overlap buffer matches the configured capacity.
    /// </summary>
    private void EnsurePreviewOverlapBuffer()
    {
        int TargetSize = Mathf.Max(1, MaxPreviewOverlapResults);

        if (PreviewOverlapResults == null || PreviewOverlapResults.Length != TargetSize)
        {
            PreviewOverlapResults = new Collider[TargetSize];
        }
    }

    /// <summary>
    /// Forces the next Update call to rescan nearby compatible placement spots immediately.
    /// </summary>
    private void ForcePreviewRefresh()
    {
        NextPreviewScanTime = -999f;
    }

    /// <summary>
    /// Draws debug placement scan information.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!DrawDebugRay || PlayerCamera == null)
        {
            return;
        }

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(PlayerCamera.transform.position, Mathf.Max(0.01f, SpotPreviewRadius));
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
