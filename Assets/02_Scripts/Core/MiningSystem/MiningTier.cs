/// <summary>
/// Identifies the progression tier used by mining tools and ore veins.
/// Higher tier tools can mine lower tier ores, but lower tier tools cannot mine higher tier ores.
/// </summary>
public enum MiningTier
{
    None = 0,
    TierI = 1,
    TierII = 2,
    TierIII = 3,
    TierIV = 4
}
