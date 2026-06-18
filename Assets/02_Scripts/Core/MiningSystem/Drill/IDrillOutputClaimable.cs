/// <summary>
/// Allows the player interaction controller to claim buffered physical output from a placed drill machine.
/// </summary>
public interface IDrillOutputClaimable
{
    /// <summary>
    /// Returns whether this object currently has claimable output.
    /// </summary>
    bool CanClaimOutput();

    /// <summary>
    /// Tries to claim the currently buffered output.
    /// </summary>
    bool TryClaimOutput();
}
