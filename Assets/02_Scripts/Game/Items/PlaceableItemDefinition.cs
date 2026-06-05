using UnityEngine;

/// <summary>
/// Item definition for objects that must be equipped and installed into a dedicated placement spot.
/// This supports shop-delivered physical upgrades such as elevator levers, beams and future machines.
/// </summary>
[CreateAssetMenu(fileName = "PlaceableItem_", menuName = "Game/Items/Placeable Item Definition")]
public sealed class PlaceableItemDefinition : ItemDefinition
{
    /// <summary>
    /// Defines how this installed item applies its upgrade effect.
    /// </summary>
    public enum InstalledUpgradeApplyMode
    {
        None = 0,
        SetToLevel = 1,
        AddLevels = 2
    }

    [Header("Placement")]
    [Tooltip("Placement id required by compatible installation spots. Examples: Elevator.SpeedLever, Elevator.WeightBeams, Lab.PurityMachine.")]
    [SerializeField] private string PlacementId;

    [Tooltip("Optional ghost prefab displayed while aiming at a compatible placement spot.")]
    [SerializeField] private GameObject GhostPrefab;

    [Tooltip("Prefab instantiated permanently on the placement spot after installation. If empty, the world prefab is used as visual fallback.")]
    [SerializeField] private GameObject InstalledPrefab;

    [Header("Installed Upgrade")]
    [Tooltip("Upgrade modified after this item is successfully installed.")]
    [SerializeField] private UpgradeDefinition AppliedUpgradeDefinition;

    [Tooltip("How the installed item modifies the referenced upgrade.")]
    [SerializeField] private InstalledUpgradeApplyMode ApplyMode = InstalledUpgradeApplyMode.SetToLevel;

    [Tooltip("Target level used when Apply Mode is Set To Level.")]
    [SerializeField] private int TargetUpgradeLevel = 1;

    [Tooltip("Level increment used when Apply Mode is Add Levels.")]
    [SerializeField] private int UpgradeLevelIncrement = 1;

    /// <summary>
    /// Gets the placement id required by compatible spots.
    /// </summary>
    public string GetPlacementId()
    {
        return PlacementId;
    }

    /// <summary>
    /// Gets the ghost prefab displayed while aiming.
    /// </summary>
    public GameObject GetGhostPrefab()
    {
        return GhostPrefab;
    }

    /// <summary>
    /// Gets the visual prefab instantiated after installation.
    /// </summary>
    public GameObject GetInstalledPrefab()
    {
        return InstalledPrefab != null ? InstalledPrefab : GetWorldPrefab();
    }

    /// <summary>
    /// Gets the upgrade modified after installation.
    /// </summary>
    public UpgradeDefinition GetAppliedUpgradeDefinition()
    {
        return AppliedUpgradeDefinition;
    }

    /// <summary>
    /// Gets the configured upgrade apply mode.
    /// </summary>
    public InstalledUpgradeApplyMode GetApplyMode()
    {
        return ApplyMode;
    }

    /// <summary>
    /// Gets the target upgrade level used by Set To Level mode.
    /// </summary>
    public int GetTargetUpgradeLevel()
    {
        return Mathf.Max(0, TargetUpgradeLevel);
    }

    /// <summary>
    /// Gets the level increment used by Add Levels mode.
    /// </summary>
    public int GetUpgradeLevelIncrement()
    {
        return Mathf.Max(1, UpgradeLevelIncrement);
    }
}
