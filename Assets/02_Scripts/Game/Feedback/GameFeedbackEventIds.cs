/// <summary>
/// Central string identifiers for reusable gameplay feedback events.
/// Keep event ids stable because they are used by feedback profiles configured in the Unity inspector.
/// Treat this file as append-only: do not remove or rename validated ids without a migration pass.
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
    /// Event fired when the scanner starts reading a target.
    /// </summary>
    public const string ScannerScanStarted = "Scanner.ScanStarted";

    /// <summary>
    /// Event fired when the scanner successfully scans a target.
    /// </summary>
    public const string ScannerScanCompleted = "Scanner.ScanCompleted";

    /// <summary>
    /// Event fired when the scanner cannot scan the current target.
    /// </summary>
    public const string ScannerScanDenied = "Scanner.ScanDenied";

    /// <summary>
    /// Event fired when a new ore definition is discovered by scanning.
    /// </summary>
    public const string ScannerOreDiscovered = "Scanner.OreDiscovered";

    /// <summary>
    /// Event fired when the scanner inspects a target that was already known.
    /// </summary>
    public const string ScannerAlreadyKnown = "Scanner.AlreadyKnown";

    /// <summary>
    /// Event fired when the magnet starts pulling valid ore targets.
    /// </summary>
    public const string MagnetPullStarted = "Magnet.PullStarted";

    /// <summary>
    /// Event fired when the magnet stops pulling targets.
    /// </summary>
    public const string MagnetPullStopped = "Magnet.PullStopped";

    /// <summary>
    /// Event fired when a single magnet filter is set or replaced.
    /// </summary>
    public const string MagnetFilterSet = "Magnet.FilterSet";

    /// <summary>
    /// Event fired when a magnet filter is added to a multi-filter list.
    /// </summary>
    public const string MagnetFilterAdded = "Magnet.FilterAdded";

    /// <summary>
    /// Event fired when an existing magnet filter is removed.
    /// </summary>
    public const string MagnetFilterRemoved = "Magnet.FilterRemoved";

    /// <summary>
    /// Event fired when all magnet filters are cleared.
    /// </summary>
    public const string MagnetFilterCleared = "Magnet.FilterCleared";

    /// <summary>
    /// Event fired when a magnet filter request is rejected, for example because the filter list is full.
    /// </summary>
    public const string MagnetFilterRejected = "Magnet.FilterRejected";

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
    /// Event fired when a money pickup is collected.
    /// </summary>
    public const string MoneyCollected = "Money.Collected";

    /// <summary>
    /// Event fired when an area collection action collects one or more money pickups.
    /// </summary>
    public const string MoneyAreaCollected = "Money.AreaCollected";

    /// <summary>
    /// Event fired when a machine transfers credits directly without spawning physical money.
    /// </summary>
    public const string MoneyAutoTransferred = "Money.AutoTransferred";

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
    /// Event fired when a research entry contains undiscovered requirements.
    /// </summary>
    public const string ResearchUnknownRequirement = "Research.UnknownRequirement";

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
    /// Event fired when a wall drill starts producing ore.
    /// </summary>
    public const string WallDrillStarted = "WallDrill.Started";

    /// <summary>
    /// Event fired when a wall drill production tick completes.
    /// </summary>
    public const string WallDrillTick = "WallDrill.Tick";

    /// <summary>
    /// Event fired when a wall drill production is paused because output is being ejected or the drill cannot produce.
    /// </summary>
    public const string WallDrillPaused = "WallDrill.Paused";

    /// <summary>
    /// Event fired when a wall drill production resumes after an output ejection sequence finishes or is stopped.
    /// </summary>
    public const string WallDrillResumed = "WallDrill.Resumed";

    /// <summary>
    /// Event fired when a wall drill reaches output capacity.
    /// </summary>
    public const string WallDrillFull = "WallDrill.Full";

    /// <summary>
    /// Event fired when a wall drill output claim starts.
    /// </summary>
    public const string WallDrillClaimed = "WallDrill.Claimed";

    /// <summary>
    /// Event fired when a wall drill output ejection sequence is manually stopped before all stored output is spawned.
    /// </summary>
    public const string WallDrillEjectionStopped = "WallDrill.EjectionStopped";

    /// <summary>
    /// Event fired when a wall drill ejects a physical ore output.
    /// </summary>
    public const string WallDrillOutputSpawned = "WallDrill.OutputSpawned";

    /// <summary>
    /// Event fired when a wall drill cannot operate.
    /// </summary>
    public const string WallDrillBlocked = "WallDrill.Blocked";


    /// <summary>
    /// Event fired when the shared laboratory mineral elevator hub becomes active.
    /// </summary>
    public const string MineralElevatorHubActivated = "MineralElevator.HubActivated";

    /// <summary>
    /// Event fired when a mineral elevator access point starts visually transferring pending ore to the hub.
    /// </summary>
    public const string MineralElevatorTransferStarted = "MineralElevator.TransferStarted";

    /// <summary>
    /// Event fired when a mineral elevator interaction is blocked for any non-capacity reason.
    /// </summary>
    public const string MineralElevatorBlocked = "MineralElevator.Blocked";

    /// <summary>
    /// Event fired when the player starts claiming stored output from the shared mineral elevator hub.
    /// </summary>
    public const string MineralElevatorClaimStarted = "MineralElevator.ClaimStarted";

    /// <summary>
    /// Event fired when the player manually stops mineral elevator hub output ejection.
    /// </summary>
    public const string MineralElevatorClaimStopped = "MineralElevator.ClaimStopped";

    /// <summary>
    /// Event fired when a mineral elevator accepts a physical ore item.
    /// </summary>
    public const string MineralElevatorItemAccepted = "MineralElevator.ItemAccepted";

    /// <summary>
    /// Event fired when a mineral elevator rejects input because it is full.
    /// </summary>
    public const string MineralElevatorFull = "MineralElevator.Full";

    /// <summary>
    /// Event fired when a mineral elevator completes a transfer to its output point.
    /// </summary>
    public const string MineralElevatorTransferCompleted = "MineralElevator.TransferCompleted";

    /// <summary>
    /// Event fired when a mineral elevator spawns or releases transported ore at its output point.
    /// </summary>
    public const string MineralElevatorOutputSpawned = "MineralElevator.OutputSpawned";

    /// <summary>
    /// Event fired when the elevator drill module is enabled.
    /// </summary>
    public const string ElevatorDrillEnabled = "ElevatorDrill.Enabled";

    /// <summary>
    /// Event fired when the elevator drill module is disabled.
    /// </summary>
    public const string ElevatorDrillDisabled = "ElevatorDrill.Disabled";

    /// <summary>
    /// Event fired when the elevator drill acquires a mineable target.
    /// </summary>
    public const string ElevatorDrillTargetAcquired = "ElevatorDrill.TargetAcquired";

    /// <summary>
    /// Event fired when the elevator drill breaks a mineable target.
    /// </summary>
    public const string ElevatorDrillTargetBroken = "ElevatorDrill.TargetBroken";

    /// <summary>
    /// Event fired when the player produces a valid footstep.
    /// </summary>
    public const string PlayerFootstep = "Player.Footstep";
}
