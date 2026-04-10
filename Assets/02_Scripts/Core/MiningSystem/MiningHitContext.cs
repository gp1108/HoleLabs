using UnityEngine;

/// <summary>
/// Describes who or what caused a mining hit.
/// This keeps ore-break reactions explicit and future-proof.
/// </summary>
public struct MiningHitContext
{
    /// <summary>
    /// Identifies the source category of the mining hit.
    /// </summary>
    public enum HitSourceType
    {
        Unknown = 0,
        Player = 1,
        Machine = 2,
        Explosion = 3,
        Environment = 4
    }

    /// <summary>
    /// Source category that caused this mining hit.
    /// </summary>
    public HitSourceType SourceType;

    /// <summary>
    /// Optional world object that caused the hit.
    /// This can be the player, a machine or any future system.
    /// </summary>
    public GameObject SourceObject;

    /// <summary>
    /// Creates a new mining hit context.
    /// </summary>
    /// <param name="SourceTypeValue">Category of the source that caused the hit.</param>
    /// <param name="SourceObjectValue">Optional world object that caused the hit.</param>
    public MiningHitContext(HitSourceType SourceTypeValue, GameObject SourceObjectValue)
    {
        SourceType = SourceTypeValue;
        SourceObject = SourceObjectValue;
    }

    /// <summary>
    /// Returns true when the hit was explicitly caused by the player.
    /// </summary>
    public bool IsPlayerSource()
    {
        return SourceType == HitSourceType.Player;
    }

    /// <summary>
    /// Creates an unknown hit context.
    /// </summary>
    public static MiningHitContext CreateUnknown()
    {
        return new MiningHitContext(HitSourceType.Unknown, null);
    }
}
