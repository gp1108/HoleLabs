/// <summary>
/// Generic interface for objects that can be mined by a mining tool, machine or explosion.
/// The request/result path is the authoritative API. The legacy float overload remains only for compatibility.
/// </summary>
public interface IMineable
{
    /// <summary>
    /// Attempts to apply one complete mining request to this target.
    /// </summary>
    /// <param name="MiningRequest">Complete mining request containing damage, tier, extraction quality and world context.</param>
    /// <returns>Detailed mining result used by tools to consume durability and play feedback.</returns>
    MiningHitResult TryMine(MiningHitRequest MiningRequest);

    /// <summary>
    /// Attempts to apply one legacy mining hit to this target.
    /// New code should use the MiningHitRequest overload instead.
    /// </summary>
    /// <param name="MiningPower">Legacy power value of the mining hit.</param>
    /// <param name="HitContext">Explicit source context that caused the hit.</param>
    /// <returns>True when the target accepted the mining hit.</returns>
    bool TryMine(float MiningPower, MiningHitContext HitContext);
}
