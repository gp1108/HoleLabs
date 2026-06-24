using System;
using UnityEngine;

/// <summary>
/// Dedicated world slot where a placeable item can be installed.
/// Installation consumes the equipped hotbar item, spawns the installed visual and applies the configured upgrade effect.
/// The spot also owns final placement validation so visual previews and real installation use the same rules.
/// </summary>
public sealed class PlaceableInstallationSpot : MonoBehaviour
{
    /// <summary>
    /// Describes the evaluated installation state for a candidate placeable item.
    /// </summary>
    public enum PlacementEvaluationStatus
    {
        InvalidItem = 0,
        IncompatibleSpot = 1,
        Occupied = 2,
        Obstructed = 3,
        Ready = 4
    }

    [Header("Placement")]
    [Tooltip("Placement id accepted by this spot. It must match the placeable item definition.")]
    [SerializeField] private string AcceptedPlacementId;

    [Tooltip("Transform used as the final installed visual pose. If empty, this transform is used.")]
    [SerializeField] private Transform InstallRoot;

    [Tooltip("Transform used as the ghost preview pose. If empty, Install Root is used.")]
    [SerializeField] private Transform PreviewRoot;

    [Tooltip("If true, an installed item can be replaced by another compatible item.")]
    [SerializeField] private bool AllowReplacement = true;

    [Tooltip("If true, a replaced item is returned as a physical world item.")]
    [SerializeField] private bool ReturnReplacedItem = true;

    [Tooltip("Point where replaced items are dropped. If empty, this transform is used.")]
    [SerializeField] private Transform ReplacementDropPoint;

    [Header("Placement Occupancy")]
    [Tooltip("If true, installation is blocked when the configured final placement volume overlaps obstruction colliders.")]
    [SerializeField] private bool UseOccupancyCheck = true;

    [Tooltip("Layers considered blockers for final placement. Use object, ore, carryable and player layers. Do not include the placement spot layer or static floor layer unless intended.")]
    [SerializeField] private LayerMask ObstructionLayers = 0;

    [Tooltip("Transform that defines the local orientation of the placement obstruction box. If empty, Install Root is used.")]
    [SerializeField] private Transform OccupancyCheckRoot;

    [Tooltip("Local center offset of the placement obstruction box, relative to Occupancy Check Root.")]
    [SerializeField] private Vector3 OccupancyCheckLocalCenter = Vector3.zero;

    [Tooltip("World-space size of the placement obstruction box before rotation is applied.")]
    [SerializeField] private Vector3 OccupancyCheckSize = new Vector3(1f, 1f, 1f);

    [Tooltip("If true, trigger colliders are ignored by the placement obstruction test.")]
    [SerializeField] private bool IgnoreTriggerObstructions = true;

    [Tooltip("If true, colliders belonging to this spot hierarchy and the currently installed visual are ignored by the obstruction test.")]
    [SerializeField] private bool IgnoreOwnHierarchyObstructions = true;

    [Tooltip("Maximum number of obstruction colliders that can be checked per placement evaluation.")]
    [SerializeField] private int MaxObstructionResults = 64;

    [Header("References")]
    [Tooltip("Upgrade manager used to apply installed upgrade effects.")]
    [SerializeField] private UpgradeManager UpgradeManager;

    [Header("Debug")]
    [Tooltip("Logs placement operations.")]
    [SerializeField] private bool DebugLogs = false;

    [Tooltip("Draws the configured placement occupancy volume when this spot is selected.")]
    [SerializeField] private bool DrawOccupancyGizmo = true;

    /// <summary>
    /// Current installed item instance stored by this spot.
    /// </summary>
    private ItemInstance CurrentInstalledItem;

    /// <summary>
    /// Current installed visual object spawned by this spot.
    /// </summary>
    private GameObject CurrentInstalledVisual;

    /// <summary>
    /// Reusable overlap buffer used by the placement obstruction check.
    /// </summary>
    private Collider[] ObstructionResults;

    /// <summary>
    /// Raised after this spot installs or restores an item and its installed visual has been spawned.
    /// </summary>
    public event Action<PlaceableInstallationSpot, ItemInstance, GameObject> InstallationChanged;

    /// <summary>
    /// Raised after this spot clears its installed runtime state.
    /// </summary>
    public event Action<PlaceableInstallationSpot> InstallationCleared;

    /// <summary>
    /// Resolves missing references.
    /// </summary>
    private void Awake()
    {
        EnsureReferences();
        EnsureObstructionBuffer();
    }

    /// <summary>
    /// Resolves runtime references needed by placement, save restoration and upgrade application.
    /// </summary>
    private void EnsureReferences()
    {
        if (InstallRoot == null)
        {
            InstallRoot = transform;
        }

        if (PreviewRoot == null)
        {
            PreviewRoot = InstallRoot;
        }

        if (OccupancyCheckRoot == null)
        {
            OccupancyCheckRoot = InstallRoot;
        }

        if (ReplacementDropPoint == null)
        {
            ReplacementDropPoint = transform;
        }

        if (UpgradeManager == null)
        {
            UpgradeManager = FindFirstObjectByType<UpgradeManager>();
        }
    }

    /// <summary>
    /// Ensures the non-alloc obstruction buffer matches the configured capacity.
    /// </summary>
    private void EnsureObstructionBuffer()
    {
        int TargetSize = Mathf.Max(1, MaxObstructionResults);

        if (ObstructionResults == null || ObstructionResults.Length != TargetSize)
        {
            ObstructionResults = new Collider[TargetSize];
        }
    }

    /// <summary>
    /// Gets whether this spot currently has an installed item.
    /// </summary>
    public bool GetIsOccupied()
    {
        return CurrentInstalledItem != null;
    }

    /// <summary>
    /// Gets the currently spawned installed visual object owned by this spot.
    /// This should be used by companion systems that need to initialize components on the installed prefab without owning placement.
    /// </summary>
    /// <returns>Installed visual instance, or null when the spot is empty or the installed definition has no visual prefab.</returns>
    public GameObject GetCurrentInstalledVisual()
    {
        return CurrentInstalledVisual;
    }

    /// <summary>
    /// Gets a cloned runtime item instance representing the currently installed item.
    /// </summary>
    /// <returns>Installed item clone, or null when this spot is empty.</returns>
    public ItemInstance CreateInstalledItemSnapshot()
    {
        return CurrentInstalledItem != null ? CurrentInstalledItem.Clone() : null;
    }

    /// <summary>
    /// Gets the current installed placeable definition.
    /// </summary>
    /// <returns>Installed placeable item definition, or null when this spot is empty or invalid.</returns>
    public PlaceableItemDefinition GetCurrentInstalledPlaceableDefinition()
    {
        return GetPlaceableDefinition(CurrentInstalledItem);
    }

    /// <summary>
    /// Clears the installed state without returning a physical item.
    /// This is used by save/load restoration before applying the saved state.
    /// </summary>
    public void ClearInstalledState()
    {
        ClearInstalledVisual();
        CurrentInstalledItem = null;
        NotifyInstallationCleared();
    }

    /// <summary>
    /// Restores this spot from saved placement state.
    /// The saved item is installed without consuming hotbar content.
    /// </summary>
    /// <param name="IsOccupied">Whether the spot should contain an installed item.</param>
    /// <param name="SavedInstalledItem">Saved installed item payload.</param>
    /// <param name="ReapplyUpgrade">If true, reapplies the configured upgrade effect after restoring the visual.</param>
    public void ApplySavedState(bool IsOccupied, ItemInstance SavedInstalledItem, bool ReapplyUpgrade)
    {
        EnsureReferences();
        ClearInstalledState();

        if (!IsOccupied || SavedInstalledItem == null)
        {
            Log("Restored empty placement spot state.");
            return;
        }

        PlaceableItemDefinition PlaceableDefinition = GetPlaceableDefinition(SavedInstalledItem);

        if (PlaceableDefinition == null || !IsCompatiblePlacementDefinition(PlaceableDefinition))
        {
            Log("Saved installed item was not compatible with this spot and was ignored.");
            return;
        }

        CurrentInstalledItem = SavedInstalledItem.Clone();
        SpawnInstalledVisual(PlaceableDefinition);

        if (ReapplyUpgrade)
        {
            ApplyInstalledUpgrade(PlaceableDefinition);
        }

        NotifyInstallationChanged();
        Log("Restored installed item: " + PlaceableDefinition.GetDisplayName());
    }

    /// <summary>
    /// Gets the preview position used by equipped placeable ghosts.
    /// </summary>
    public Vector3 GetPreviewPosition()
    {
        return PreviewRoot != null ? PreviewRoot.position : transform.position;
    }

    /// <summary>
    /// Gets the preview rotation used by equipped placeable ghosts.
    /// </summary>
    public Quaternion GetPreviewRotation()
    {
        return PreviewRoot != null ? PreviewRoot.rotation : transform.rotation;
    }

    /// <summary>
    /// Gets whether the provided placeable definition belongs to this spot id and is worth showing as a nearby preview.
    /// Occupancy obstruction is not treated as incompatibility because blocked spots must still be able to show a red ghost.
    /// </summary>
    /// <param name="PlaceableDefinition">Candidate placeable definition.</param>
    /// <returns>True when this spot is compatible enough to display a ghost preview.</returns>
    public bool CanPreview(PlaceableItemDefinition PlaceableDefinition)
    {
        if (PlaceableDefinition == null)
        {
            return false;
        }

        if (!IsCompatiblePlacementDefinition(PlaceableDefinition))
        {
            return false;
        }

        if (CurrentInstalledItem != null && !AllowReplacement)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Evaluates whether this spot can currently install the provided item instance.
    /// </summary>
    /// <param name="ItemInstance">Candidate item instance.</param>
    /// <returns>Detailed placement evaluation status.</returns>
    public PlacementEvaluationStatus EvaluatePlacement(ItemInstance ItemInstance)
    {
        return EvaluatePlacementDefinition(GetPlaceableDefinition(ItemInstance));
    }

    /// <summary>
    /// Evaluates whether this spot can currently install the provided placeable definition.
    /// </summary>
    /// <param name="PlaceableDefinition">Candidate placeable definition.</param>
    /// <returns>Detailed placement evaluation status.</returns>
    public PlacementEvaluationStatus EvaluatePlacementDefinition(PlaceableItemDefinition PlaceableDefinition)
    {
        if (PlaceableDefinition == null)
        {
            return PlacementEvaluationStatus.InvalidItem;
        }

        if (!IsCompatiblePlacementDefinition(PlaceableDefinition))
        {
            return PlacementEvaluationStatus.IncompatibleSpot;
        }

        if (CurrentInstalledItem != null && !AllowReplacement)
        {
            return PlacementEvaluationStatus.Occupied;
        }

        if (HasBlockingObstruction())
        {
            return PlacementEvaluationStatus.Obstructed;
        }

        return PlacementEvaluationStatus.Ready;
    }

    /// <summary>
    /// Returns whether this spot can currently accept the provided item instance.
    /// </summary>
    /// <param name="ItemInstance">Candidate item instance.</param>
    /// <returns>True when the item can be installed here.</returns>
    public bool CanInstall(ItemInstance ItemInstance)
    {
        return EvaluatePlacement(ItemInstance) == PlacementEvaluationStatus.Ready;
    }

    /// <summary>
    /// Attempts to install the provided hotbar item into this spot.
    /// The selected hotbar item is consumed only after this spot accepts the installation.
    /// </summary>
    /// <param name="ItemInstance">Candidate item instance.</param>
    /// <param name="OwnerHotbar">Hotbar that owns and will consume the selected item.</param>
    /// <returns>True when installation succeeded.</returns>
    public bool TryInstall(ItemInstance ItemInstance, HotbarController OwnerHotbar)
    {
        if (ItemInstance == null || OwnerHotbar == null)
        {
            return false;
        }

        PlaceableItemDefinition PlaceableDefinition = GetPlaceableDefinition(ItemInstance);

        if (PlaceableDefinition == null || !CanInstall(ItemInstance))
        {
            Log("Installation rejected. Status=" + EvaluatePlacement(ItemInstance));
            return false;
        }

        if (CurrentInstalledItem != null)
        {
            ReturnOrClearCurrentInstalledItem(OwnerHotbar);
        }

        ItemInstance ConsumedItem = OwnerHotbar.RemoveSelectedItem();

        if (ConsumedItem == null)
        {
            return false;
        }

        CurrentInstalledItem = ConsumedItem.Clone();
        SpawnInstalledVisual(PlaceableDefinition);
        ApplyInstalledUpgrade(PlaceableDefinition);
        NotifyInstallationChanged();

        Log("Installed item: " + PlaceableDefinition.GetDisplayName());
        return true;
    }

    /// <summary>
    /// Extracts a placeable item definition from a runtime item instance.
    /// </summary>
    /// <param name="ItemInstance">Runtime item instance.</param>
    /// <returns>Placeable item definition or null.</returns>
    private PlaceableItemDefinition GetPlaceableDefinition(ItemInstance ItemInstance)
    {
        if (ItemInstance == null)
        {
            return null;
        }

        return ItemInstance.GetDefinition() as PlaceableItemDefinition;
    }

    /// <summary>
    /// Returns the current installed item as a world item when configured, otherwise clears it.
    /// </summary>
    /// <param name="OwnerHotbar">Hotbar used to spawn the replaced world item.</param>
    private void ReturnOrClearCurrentInstalledItem(HotbarController OwnerHotbar)
    {
        ClearInstalledVisual();

        if (ReturnReplacedItem && CurrentInstalledItem != null)
        {
            Vector3 DropPosition = ReplacementDropPoint != null ? ReplacementDropPoint.position : transform.position;
            Vector3 DropDirection = ReplacementDropPoint != null ? ReplacementDropPoint.forward : transform.forward;
            OwnerHotbar.SpawnWorldItem(CurrentInstalledItem.Clone(), DropPosition, DropDirection);
        }

        CurrentInstalledItem = null;
        NotifyInstallationCleared();
    }

    /// <summary>
    /// Spawns the installed visual prefab for the provided placeable definition.
    /// </summary>
    /// <param name="PlaceableDefinition">Definition being installed.</param>
    private void SpawnInstalledVisual(PlaceableItemDefinition PlaceableDefinition)
    {
        ClearInstalledVisual();

        GameObject InstalledPrefab = PlaceableDefinition.GetInstalledPrefab();

        if (InstalledPrefab == null)
        {
            return;
        }

        CurrentInstalledVisual = Instantiate(
            InstalledPrefab,
            InstallRoot.position,
            InstallRoot.rotation,
            InstallRoot);
    }

    /// <summary>
    /// Returns whether the configured obstruction volume currently overlaps any blocking collider.
    /// </summary>
    /// <returns>True when installation should be blocked.</returns>
    private bool HasBlockingObstruction()
    {
        if (!UseOccupancyCheck || ObstructionLayers.value == 0)
        {
            return false;
        }

        EnsureReferences();
        EnsureObstructionBuffer();

        Vector3 HalfExtents = GetOccupancyHalfExtents();

        if (HalfExtents.x <= 0f || HalfExtents.y <= 0f || HalfExtents.z <= 0f)
        {
            return false;
        }

        QueryTriggerInteraction TriggerInteraction = IgnoreTriggerObstructions
            ? QueryTriggerInteraction.Ignore
            : QueryTriggerInteraction.Collide;

        int HitCount = Physics.OverlapBoxNonAlloc(
            GetOccupancyWorldCenter(),
            HalfExtents,
            ObstructionResults,
            GetOccupancyWorldRotation(),
            ObstructionLayers,
            TriggerInteraction);

        for (int Index = 0; Index < HitCount; Index++)
        {
            Collider Candidate = ObstructionResults[Index];
            ObstructionResults[Index] = null;

            if (ShouldIgnoreObstruction(Candidate))
            {
                continue;
            }

            Log("Placement blocked by obstruction: " + Candidate.name);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the world center used by the placement obstruction volume.
    /// </summary>
    private Vector3 GetOccupancyWorldCenter()
    {
        Transform Root = OccupancyCheckRoot != null ? OccupancyCheckRoot : transform;
        return Root.TransformPoint(OccupancyCheckLocalCenter);
    }

    /// <summary>
    /// Gets the world rotation used by the placement obstruction volume.
    /// </summary>
    private Quaternion GetOccupancyWorldRotation()
    {
        Transform Root = OccupancyCheckRoot != null ? OccupancyCheckRoot : transform;
        return Root.rotation;
    }

    /// <summary>
    /// Gets the half extents used by the placement obstruction volume.
    /// </summary>
    private Vector3 GetOccupancyHalfExtents()
    {
        return new Vector3(
            Mathf.Max(0f, OccupancyCheckSize.x * 0.5f),
            Mathf.Max(0f, OccupancyCheckSize.y * 0.5f),
            Mathf.Max(0f, OccupancyCheckSize.z * 0.5f));
    }

    /// <summary>
    /// Returns whether the obstruction collider should be ignored by this spot.
    /// </summary>
    /// <param name="Candidate">Collider found by the overlap test.</param>
    /// <returns>True when the collider should not block placement.</returns>
    private bool ShouldIgnoreObstruction(Collider Candidate)
    {
        if (Candidate == null || !Candidate.enabled)
        {
            return true;
        }

        if (IgnoreTriggerObstructions && Candidate.isTrigger)
        {
            return true;
        }

        if (!IgnoreOwnHierarchyObstructions)
        {
            return false;
        }

        if (Candidate.transform == transform || Candidate.transform.IsChildOf(transform))
        {
            return true;
        }

        if (CurrentInstalledVisual != null && Candidate.transform.IsChildOf(CurrentInstalledVisual.transform))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns whether the provided placeable definition matches this spot placement id.
    /// </summary>
    /// <param name="PlaceableDefinition">Placeable definition to validate.</param>
    /// <returns>True when the definition can be installed into this spot id.</returns>
    private bool IsCompatiblePlacementDefinition(PlaceableItemDefinition PlaceableDefinition)
    {
        if (PlaceableDefinition == null)
        {
            return false;
        }

        return string.Equals(
            AcceptedPlacementId,
            PlaceableDefinition.GetPlacementId(),
            System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Destroys the current installed visual object without changing the stored item payload.
    /// </summary>
    private void ClearInstalledVisual()
    {
        if (CurrentInstalledVisual == null)
        {
            return;
        }

        Destroy(CurrentInstalledVisual);
        CurrentInstalledVisual = null;
    }

    /// <summary>
    /// Notifies companion systems that the installed visual has changed.
    /// </summary>
    private void NotifyInstallationChanged()
    {
        InstallationChanged?.Invoke(this, CurrentInstalledItem != null ? CurrentInstalledItem.Clone() : null, CurrentInstalledVisual);
    }

    /// <summary>
    /// Notifies companion systems that the installed state has been cleared.
    /// </summary>
    private void NotifyInstallationCleared()
    {
        InstallationCleared?.Invoke(this);
    }

    /// <summary>
    /// Applies the upgrade effect configured by the installed item definition.
    /// </summary>
    /// <param name="PlaceableDefinition">Installed item definition.</param>
    private void ApplyInstalledUpgrade(PlaceableItemDefinition PlaceableDefinition)
    {
        if (UpgradeManager == null || PlaceableDefinition.GetAppliedUpgradeDefinition() == null)
        {
            return;
        }

        UpgradeDefinition UpgradeDefinition = PlaceableDefinition.GetAppliedUpgradeDefinition();
        int CurrentLevel = UpgradeManager.GetUpgradeLevel(UpgradeDefinition);
        int TargetLevel = CurrentLevel;

        switch (PlaceableDefinition.GetApplyMode())
        {
            case PlaceableItemDefinition.InstalledUpgradeApplyMode.SetToLevel:
                TargetLevel = PlaceableDefinition.GetTargetUpgradeLevel();
                break;

            case PlaceableItemDefinition.InstalledUpgradeApplyMode.AddLevels:
                TargetLevel = CurrentLevel + PlaceableDefinition.GetUpgradeLevelIncrement();
                break;

            case PlaceableItemDefinition.InstalledUpgradeApplyMode.None:
                return;
        }

        UpgradeManager.SetUpgradeLevel(UpgradeDefinition, TargetLevel);
    }

    /// <summary>
    /// Draws the final placement obstruction volume for scene-authoring validation.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!DrawOccupancyGizmo || !UseOccupancyCheck)
        {
            return;
        }

        EnsureReferences();

        Matrix4x4 PreviousMatrix = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(GetOccupancyWorldCenter(), GetOccupancyWorldRotation(), Vector3.one);
        Gizmos.color = new Color(1f, 0.2f, 0.1f, 0.25f);
        Gizmos.DrawCube(Vector3.zero, OccupancyCheckSize);
        Gizmos.color = new Color(1f, 0.2f, 0.1f, 0.9f);
        Gizmos.DrawWireCube(Vector3.zero, OccupancyCheckSize);
        Gizmos.matrix = PreviousMatrix;
    }

    /// <summary>
    /// Logs placement messages when debug logging is enabled.
    /// </summary>
    /// <param name="Message">Message to write.</param>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[PlaceableInstallationSpot] " + Message, this);
    }
}
