/// <summary>
/// Provides an authoritative gameplay weight for any physical object that can contribute to elevator load.
/// Implement this on minerals, world items or simple props that should be counted by weight systems.
/// </summary>
public interface IWeightProvider
{
    /// <summary>
    /// Gets the current gameplay weight contributed by this object.
    /// </summary>
    /// <returns>Non-negative weight value.</returns>
    float GetWeight();
}
