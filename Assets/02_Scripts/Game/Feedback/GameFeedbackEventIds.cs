/// <summary>
/// Central string identifiers for reusable gameplay feedback events.
/// Keep event ids stable because they are used by feedback profiles configured in the Unity inspector.
/// </summary>
public static class GameFeedbackEventIds
{
    public const string MiningAcceptedHit = "Mining.AcceptedHit";
    public const string MiningBreak = "Mining.Break";
    public const string MiningInsufficientTier = "Mining.InsufficientTier";
    public const string MiningTargetUnavailable = "Mining.TargetUnavailable";
    public const string MiningNoDamage = "Mining.NoDamage";
    public const string MiningRejectedHit = "Mining.RejectedHit";
    public const string MiningNonMineableHit = "Mining.NonMineableHit";
    public const string MiningMiss = "Mining.Miss";

    public const string MachineProcessStarted = "Machine.ProcessStarted";
    public const string MachineProcessTick = "Machine.ProcessTick";
    public const string MachineProcessCompleted = "Machine.ProcessCompleted";
    public const string MachineOutputSpawned = "Machine.OutputSpawned";
    public const string MachineBlocked = "Machine.Blocked";

    public const string ShopPurchaseSucceeded = "Shop.PurchaseSucceeded";
    public const string ShopPurchaseDenied = "Shop.PurchaseDenied";
    public const string ShopProductReissued = "Shop.ProductReissued";

    public const string ResearchActivated = "Research.Activated";
    public const string ResearchProgressAdded = "Research.ProgressAdded";
    public const string ResearchCompleted = "Research.Completed";
    public const string ResearchDenied = "Research.Denied";

    public const string ElevatorOverweight = "Elevator.Overweight";
    public const string ElevatorUnlocked = "Elevator.Unlocked";
    public const string ElevatorMovementStarted = "Elevator.MovementStarted";
    public const string ElevatorMovementStopped = "Elevator.MovementStopped";
}
