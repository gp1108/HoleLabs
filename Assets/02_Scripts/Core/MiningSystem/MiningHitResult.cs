/// <summary>
/// Describes why a mining request was accepted or rejected.
/// </summary>
public enum MiningHitResultType
{
    None = 0,
    Accepted = 1,
    Broken = 2,
    TargetUnavailable = 3,
    InsufficientTier = 4,
    NoDamage = 5
}

/// <summary>
/// Result returned by a mineable target after processing a mining request.
/// Tools use this to decide whether durability should be consumed and which feedback should play.
/// </summary>
public readonly struct MiningHitResult
{
    /// <summary>
    /// Type of result produced by the mining request.
    /// </summary>
    public readonly MiningHitResultType ResultType;

    /// <summary>
    /// Whether the target accepted the hit and actually received mining damage.
    /// </summary>
    public readonly bool WasAccepted;

    /// <summary>
    /// Whether this accepted hit depleted and broke the target.
    /// </summary>
    public readonly bool DidBreak;

    /// <summary>
    /// Integer mining damage applied to the target.
    /// </summary>
    public readonly int DamageApplied;

    /// <summary>
    /// Remaining target durability after the hit.
    /// </summary>
    public readonly int RemainingDurability;

    /// <summary>
    /// Required mining tier of the target when a tier check was involved.
    /// </summary>
    public readonly MiningTier RequiredTier;

    /// <summary>
    /// Mining tier supplied by the source.
    /// </summary>
    public readonly MiningTier SourceTier;

    /// <summary>
    /// Creates a mining hit result.
    /// </summary>
    public MiningHitResult(
        MiningHitResultType ResultTypeValue,
        bool WasAcceptedValue,
        bool DidBreakValue,
        int DamageAppliedValue,
        int RemainingDurabilityValue,
        MiningTier RequiredTierValue,
        MiningTier SourceTierValue)
    {
        ResultType = ResultTypeValue;
        WasAccepted = WasAcceptedValue;
        DidBreak = DidBreakValue;
        DamageApplied = DamageAppliedValue;
        RemainingDurability = RemainingDurabilityValue;
        RequiredTier = RequiredTierValue;
        SourceTier = SourceTierValue;
    }

    /// <summary>
    /// Creates an accepted hit result.
    /// </summary>
    public static MiningHitResult Accepted(int DamageApplied, int RemainingDurability)
    {
        bool DidBreak = RemainingDurability <= 0;
        return new MiningHitResult(
            DidBreak ? MiningHitResultType.Broken : MiningHitResultType.Accepted,
            true,
            DidBreak,
            DamageApplied,
            RemainingDurability,
            MiningTier.None,
            MiningTier.None);
    }

    /// <summary>
    /// Creates a rejected hit result for an unavailable target.
    /// </summary>
    public static MiningHitResult TargetUnavailable()
    {
        return new MiningHitResult(
            MiningHitResultType.TargetUnavailable,
            false,
            false,
            0,
            0,
            MiningTier.None,
            MiningTier.None);
    }

    /// <summary>
    /// Creates a rejected hit result for a source tier that is too low.
    /// </summary>
    public static MiningHitResult InsufficientTier(MiningTier RequiredTier, MiningTier SourceTier, int RemainingDurability)
    {
        return new MiningHitResult(
            MiningHitResultType.InsufficientTier,
            false,
            false,
            0,
            RemainingDurability,
            RequiredTier,
            SourceTier);
    }

    /// <summary>
    /// Creates a rejected hit result for a request with no usable damage.
    /// </summary>
    public static MiningHitResult NoDamage(int RemainingDurability)
    {
        return new MiningHitResult(
            MiningHitResultType.NoDamage,
            false,
            false,
            0,
            RemainingDurability,
            MiningTier.None,
            MiningTier.None);
    }
}
