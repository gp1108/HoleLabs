using Unity.VisualScripting;


/// <summary>
/// Identifies all gameplay stats that can be modified by upgrades.
/// Keep this enum focused on numeric values that gameplay systems can query directly.
/// </summary>
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
    
    ScannerRange = 7,
    ScannerDuration = 8,
    CarryCapacity = 9,
    OreRespawnTimeMultiplier = 10,
    OrePurityMultiplier = 11,
    OreSizeMultiplier = 12,
    ResearchSellValueMultiplier = 13,
    
    
    //Elevator
    ElevatorMoveSpeed = 14,
    ElevatorMaxTravelDistance = 15,
    ElevatorMaxAllowedWeight = 16,

    //Ores
    OreYieldAmount = 5,
    OreSellValueMultiplier = 6,
    OreYieldAmountMin = 17,
    OreYieldAmountMax = 18,
    OreSellValueMultiplierPerOre = 19,
    OreSellValueFlatBonusPerOre = 20
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