using System;

/// <summary>
/// Deprecated property enum from the old generic ore property model.
/// Ore runtime data now stores purity and size explicitly on OreItemData.
/// </summary>
[Obsolete("OrePropertyType is deprecated. Use OreItemData.GetPurityPercent and OreItemData.GetSizeScale instead.")]
public enum OrePropertyType
{
    None = 0,
    Purity = 1,
    Size = 2
}
