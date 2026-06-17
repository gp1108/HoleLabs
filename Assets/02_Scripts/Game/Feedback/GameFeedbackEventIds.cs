/// <summary>
/// Central string identifiers for reusable gameplay feedback events.
/// Keep event ids stable because they are used by feedback profiles configured in the Unity inspector.
/// </summary>
public static class GameFeedbackEventIds
{
    /// <summary>
    /// Event fired when a tool primary action starts.
    /// </summary>
    public const string ToolPrimaryStarted = "Tool.PrimaryStarted";

    /// <summary>
    /// Event fired when a tool secondary action starts.
    /// </summary>
    public const string ToolSecondaryStarted = "Tool.SecondaryStarted";

    /// <summary>
    /// Event fired when a tool primary action finishes.
    /// </summary>
    public const string ToolPrimaryFinished = "Tool.PrimaryFinished";

    /// <summary>
    /// Event fired when a tool secondary action finishes.
    /// </summary>
    public const string ToolSecondaryFinished = "Tool.SecondaryFinished";

    /// <summary>
    /// Event fired when a mining tool successfully applies damage to a mineable target.
    /// </summary>
    public const string MiningAcceptedHit = "Mining.AcceptedHit";

    /// <summary>
    /// Event fired from the tool side when a mining hit breaks a mineable target.
    /// </summary>
    public const string MiningBreak = "Mining.Break";

    /// <summary>
    /// Event fired when a mining tool tier is lower than the required target tier.
    /// </summary>
    public const string MiningInsufficientTier = "Mining.InsufficientTier";

    /// <summary>
    /// Event fired when a mineable target cannot currently receive mining damage.
    /// </summary>
    public const string MiningTargetUnavailable = "Mining.TargetUnavailable";

    /// <summary>
    /// Event fired when a mining request has no effective damage.
    /// </summary>
    public const string MiningNoDamage = "Mining.NoDamage";

    /// <summary>
    /// Generic event fired when a mining request is rejected.
    /// </summary>
    public const string MiningRejectedHit = "Mining.RejectedHit";

    /// <summary>
    /// Event fired when a mining tool hits a collider that is not mineable.
    /// </summary>
    public const string MiningNonMineableHit = "Mining.NonMineableHit";

    /// <summary>
    /// Event fired when a mining tool impact ray hits nothing.
    /// </summary>
    public const string MiningMiss = "Mining.Miss";

    /// <summary>
    /// Event fired from the ore side when an ore vein receives a valid hit.
    /// </summary>
    public const string OreHit = "Ore.Hit";

    /// <summary>
    /// Event fired from the ore side when an ore vein breaks.
    /// </summary>
    public const string OreBreak = "Ore.Break";

    /// <summary>
    /// Event fired from the ore side when the incoming mining tier is too low.
    /// </summary>
    public const string OreInsufficientTier = "Ore.InsufficientTier";

    /// <summary>
    /// Event fired from the ore side when the ore vein is unavailable.
    /// </summary>
    public const string OreTargetUnavailable = "Ore.TargetUnavailable";

    /// <summary>
    /// Event fired from the ore side when the incoming mining damage is zero.
    /// </summary>
    public const string OreNoDamage = "Ore.NoDamage";

    /// <summary>
    /// Event fired when an ore vein finishes regrowing.
    /// </summary>
    public const string OreRegrown = "Ore.Regrown";

    /// <summary>
    /// Event fired when a machine starts processing.
    /// </summary>
    public const string MachineProcessStarted = "Machine.ProcessStarted";

    /// <summary>
    /// Event fired during a machine processing tick.
    /// </summary>
    public const string MachineProcessTick = "Machine.ProcessTick";

    /// <summary>
    /// Event fired when a machine completes processing.
    /// </summary>
    public const string MachineProcessCompleted = "Machine.ProcessCompleted";

    /// <summary>
    /// Event fired when a machine spawns or ejects an output.
    /// </summary>
    public const string MachineOutputSpawned = "Machine.OutputSpawned";

    /// <summary>
    /// Event fired when a machine is blocked and cannot operate.
    /// </summary>
    public const string MachineBlocked = "Machine.Blocked";

    /// <summary>
    /// Event fired when a shop purchase succeeds.
    /// </summary>
    public const string ShopPurchaseSucceeded = "Shop.PurchaseSucceeded";

    /// <summary>
    /// Event fired when a shop purchase is denied.
    /// </summary>
    public const string ShopPurchaseDenied = "Shop.PurchaseDenied";

    /// <summary>
    /// Event fired when a unique shop product is reissued.
    /// </summary>
    public const string ShopProductReissued = "Shop.ProductReissued";

    /// <summary>
    /// Event fired when a research entry is activated.
    /// </summary>
    public const string ResearchActivated = "Research.Activated";

    /// <summary>
    /// Event fired when research receives valid progress.
    /// </summary>
    public const string ResearchProgressAdded = "Research.ProgressAdded";

    /// <summary>
    /// Event fired when a research entry completes.
    /// </summary>
    public const string ResearchCompleted = "Research.Completed";

    /// <summary>
    /// Event fired when a research action is denied.
    /// </summary>
    public const string ResearchDenied = "Research.Denied";

    /// <summary>
    /// Event fired when the elevator is overweight.
    /// </summary>
    public const string ElevatorOverweight = "Elevator.Overweight";

    /// <summary>
    /// Event fired when an elevator feature or upgrade is unlocked.
    /// </summary>
    public const string ElevatorUnlocked = "Elevator.Unlocked";

    /// <summary>
    /// Event fired when elevator movement starts.
    /// </summary>
    public const string ElevatorMovementStarted = "Elevator.MovementStarted";

    /// <summary>
    /// Event fired when elevator movement stops.
    /// </summary>
    public const string ElevatorMovementStopped = "Elevator.MovementStopped";

    /// <summary>
    /// Event fired when the player produces a valid footstep.
    /// </summary>
    public const string PlayerFootstep = "Player.Footstep";
}
