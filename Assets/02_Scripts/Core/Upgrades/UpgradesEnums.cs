using Unity.VisualScripting;


/// <summary>
/// Identifies all gameplay stats that can be modified by upgrades.
/// Keep this enum focused on numeric values that gameplay systems can query directly.
/// </summary>
public enum UpgradeStatType
{
    None = 0,
    MiningHitsRequired = 1,
    MiningSwingSpeed = 2,
    ElevatorDownSpeed = 3,
    ElevatorUpSpeed = 4,
    OreYieldAmount = 5,
    OreSellValueMultiplier = 6,
    ScannerRange = 7,
    ScannerDuration = 8,
    CarryCapacity = 9,
    OreRespawnTimeMultiplier = 10,
    OrePurityMultiplier = 11,
    OreSizeMultiplier = 12,
    ResearchSellValueMultiplier = 13
}

/// <summary>
/// Defines how an upgrade modifies a stat value.
/// Add = 0,
/// Subtract = 1,
/// Multiply = 2,
/// Divide = 3,
/// Override = 4
/// </summary>
public enum UpgradeModifierType
{
    Add = 0,
    Subtract = 1,
    Multiply = 2,
    Divide = 3,
    Override = 4
}