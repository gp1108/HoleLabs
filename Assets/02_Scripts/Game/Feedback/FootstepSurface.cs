using UnityEngine;

/// <summary>
/// Optional surface marker used by PlayerFootstepAudioEmitter to resolve a specific footstep sound.
/// Place it on floor colliders or on a parent of those colliders when layer or physics material matching is not precise enough.
/// </summary>
[DisallowMultipleComponent]
public sealed class FootstepSurface : MonoBehaviour
{
    [Header("Identity")]
    [Tooltip("Optional stable surface identifier used only for debugging and inspector readability.")]
    [SerializeField] private string SurfaceId = "Default";

    [Header("Audio")]
    [Tooltip("Audio event played when the player steps on this surface. If empty, the emitter falls back to material, layer or default rules.")]
    [SerializeField] private GameAudioEvent FootstepAudioEvent;

    /// <summary>
    /// Gets the optional stable surface identifier.
    /// </summary>
    /// <returns>Surface identifier used for debug logs.</returns>
    public string GetSurfaceId()
    {
        return SurfaceId;
    }

    /// <summary>
    /// Gets the audio event assigned to this surface.
    /// </summary>
    /// <returns>Footstep audio event, or null when this surface should not override the emitter fallback.</returns>
    public GameAudioEvent GetFootstepAudioEvent()
    {
        return FootstepAudioEvent;
    }
}
