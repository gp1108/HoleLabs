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
    /// True when this context contains an explicit world hit point.
    /// </summary>
    public bool HasWorldPoint;

    /// <summary>
    /// Exact world-space point where the mining hit landed.
    /// </summary>
    public Vector3 WorldPoint;

    /// <summary>
    /// Surface normal at the mining impact point.
    /// </summary>
    public Vector3 WorldNormal;

    /// <summary>
    /// Creates a new mining hit context without a precise impact point.
    /// </summary>
    /// <param name="SourceTypeValue">Category of the source that caused the hit.</param>
    /// <param name="SourceObjectValue">Optional world object that caused the hit.</param>
    public MiningHitContext(HitSourceType SourceTypeValue, GameObject SourceObjectValue)
    {
        SourceType = SourceTypeValue;
        SourceObject = SourceObjectValue;
        HasWorldPoint = false;
        WorldPoint = Vector3.zero;
        WorldNormal = Vector3.up;
    }

    /// <summary>
    /// Creates a new mining hit context with a precise impact point and surface normal.
    /// </summary>
    /// <param name="SourceTypeValue">Category of the source that caused the hit.</param>
    /// <param name="SourceObjectValue">Optional world object that caused the hit.</param>
    /// <param name="WorldPointValue">Exact world-space point where the hit landed.</param>
    /// <param name="WorldNormalValue">Surface normal at the impact point.</param>
    public MiningHitContext(
        HitSourceType SourceTypeValue,
        GameObject SourceObjectValue,
        Vector3 WorldPointValue,
        Vector3 WorldNormalValue)
    {
        SourceType = SourceTypeValue;
        SourceObject = SourceObjectValue;
        HasWorldPoint = true;
        WorldPoint = WorldPointValue;
        WorldNormal = WorldNormalValue.sqrMagnitude > 0.0001f ? WorldNormalValue.normalized : Vector3.up;
    }

    /// <summary>
    /// Returns true when the hit was explicitly caused by the player.
    /// </summary>
    public bool IsPlayerSource()
    {
        return SourceType == HitSourceType.Player;
    }

    /// <summary>
    /// Gets the best world position available for feedback playback.
    /// </summary>
    /// <param name="FallbackPosition">Position used when the context has no explicit hit point.</param>
    /// <returns>Impact position if available, otherwise the fallback position.</returns>
    public Vector3 GetFeedbackPosition(Vector3 FallbackPosition)
    {
        return HasWorldPoint ? WorldPoint : FallbackPosition;
    }

    /// <summary>
    /// Creates an unknown hit context.
    /// </summary>
    public static MiningHitContext CreateUnknown()
    {
        return new MiningHitContext(HitSourceType.Unknown, null);
    }
}