/// <summary>
/// Generic interface for objects that can be mined by a mining tool.
/// This allows the pickaxe to remain decoupled from a specific ore implementation.
/// </summary>
public interface IMineable
{
    /// <summary>
    /// Attempts to apply one mining hit to this target.
    /// </summary>
    bool TryMine(float miningPower);
}
