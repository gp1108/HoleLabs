/// <summary>
/// Generic interface for objects that can be mined by a mining tool, machine or explosion.
/// </summary>
public interface IMineable
{
    /// <summary>
    /// Attempts to apply one complete mining request to this target.
    /// </summary>
    /// <param name="MiningRequest">Complete mining request containing damage, tier, purity bonus and world context.</param>
    /// <returns>Detailed mining result used by tools to consume durability and play feedback.</returns>
    MiningHitResult TryMine(MiningHitRequest MiningRequest);
}
