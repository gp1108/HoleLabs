/// <summary>
/// Minimal interface for world objects that consume the player's generic interaction input.
/// Implementations should return true only when the interaction was actually handled.
/// </summary>
public interface IPlayerInteractable
{
    /// <summary>
    /// Attempts to consume the current player interaction.
    /// </summary>
    /// <returns>True when the interaction was handled and lower-priority interactions should not run.</returns>
    bool TryInteract();
}
