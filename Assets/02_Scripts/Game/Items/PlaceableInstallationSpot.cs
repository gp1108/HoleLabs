using UnityEngine;

/// <summary>
/// Dedicated world slot where a placeable item can be installed.
/// Installation consumes the equipped hotbar item, spawns the installed visual and applies the configured upgrade effect.
/// </summary>
public sealed class PlaceableInstallationSpot : MonoBehaviour
{
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

    [Header("References")]
    [Tooltip("Upgrade manager used to apply installed upgrade effects.")]
    [SerializeField] private UpgradeManager UpgradeManager;

    [Header("Debug")]
    [Tooltip("Logs placement operations.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Current installed item instance stored by this spot.
    /// </summary>
    private ItemInstance CurrentInstalledItem;

    /// <summary>
    /// Current installed visual object spawned by this spot.
    /// </summary>
    private GameObject CurrentInstalledVisual;

    /// <summary>
    /// Resolves missing references.
    /// </summary>
    private void Awake()
    {
        if (InstallRoot == null)
        {
            InstallRoot = transform;
        }

        if (PreviewRoot == null)
        {
            PreviewRoot = InstallRoot;
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
    /// Gets whether this spot currently has an installed item.
    /// </summary>
    public bool GetIsOccupied()
    {
        return CurrentInstalledItem != null;
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
    /// Returns whether this spot can currently accept the provided item instance.
    /// </summary>
    /// <param name="ItemInstance">Candidate item instance.</param>
    /// <returns>True when the item can be installed here.</returns>
    public bool CanInstall(ItemInstance ItemInstance)
    {
        PlaceableItemDefinition PlaceableDefinition = GetPlaceableDefinition(ItemInstance);

        if (PlaceableDefinition == null)
        {
            return false;
        }

        if (!string.Equals(AcceptedPlacementId, PlaceableDefinition.GetPlacementId(), System.StringComparison.Ordinal))
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
        if (CurrentInstalledVisual != null)
        {
            Destroy(CurrentInstalledVisual);
            CurrentInstalledVisual = null;
        }

        if (ReturnReplacedItem && CurrentInstalledItem != null)
        {
            Vector3 DropPosition = ReplacementDropPoint != null ? ReplacementDropPoint.position : transform.position;
            Vector3 DropDirection = ReplacementDropPoint != null ? ReplacementDropPoint.forward : transform.forward;
            OwnerHotbar.SpawnWorldItem(CurrentInstalledItem.Clone(), DropPosition, DropDirection);
        }

        CurrentInstalledItem = null;
    }

    /// <summary>
    /// Spawns the installed visual prefab for the provided placeable definition.
    /// </summary>
    /// <param name="PlaceableDefinition">Definition being installed.</param>
    private void SpawnInstalledVisual(PlaceableItemDefinition PlaceableDefinition)
    {
        if (CurrentInstalledVisual != null)
        {
            Destroy(CurrentInstalledVisual);
            CurrentInstalledVisual = null;
        }

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
