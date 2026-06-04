using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Defines one reusable audio event for the game audio system.
/// This asset stores clip selection, mixer routing, volume, pitch, cooldown and spatial settings.
/// </summary>
[CreateAssetMenu(fileName = "AudioEvent_", menuName = "Game/Audio/Audio Event")]
public sealed class GameAudioEvent : ScriptableObject
{
    [Header("Clips")]
    [Tooltip("Audio clips available for this event. One clip is selected randomly every time the event is played.")]
    [SerializeField] private AudioClip[] Clips;

    [Header("Mixer")]
    [Tooltip("Audio Mixer Group used to route this event into the correct volume/effects channel.")]
    [SerializeField] private AudioMixerGroup MixerGroup;

    [Header("Volume")]
    [Tooltip("Base volume used by this event before random variation is applied.")]
    [Range(0f, 1f)]
    [SerializeField] private float Volume = 1f;

    [Tooltip("Random volume multiplier range applied on each playback. Use 1,1 for no random volume variation.")]
    [SerializeField] private Vector2 VolumeRandomMultiplierRange = new Vector2(1f, 1f);

    [Header("Pitch")]
    [Tooltip("Base pitch used by this event before random variation is applied.")]
    [SerializeField] private float Pitch = 1f;

    [Tooltip("Random pitch multiplier range applied on each playback. Use 1,1 for no random pitch variation.")]
    [SerializeField] private Vector2 PitchRandomMultiplierRange = new Vector2(1f, 1f);

    [Header("Playback Control")]
    [Tooltip("Delay in seconds applied before this event starts playing. Useful when layering two sounds with a small offset.")]
    [SerializeField] private float PlaybackDelay = 0f;

    [Tooltip("Minimum time in seconds before this same event can be played again. Useful for impact spam and UI hover sounds.")]
    [SerializeField] private float Cooldown = 0f;

    [Tooltip("AudioSource priority. Lower values have higher priority. Unity uses a range from 0 to 256.")]
    [Range(0, 256)]
    [SerializeField] private int Priority = 128;

    [Tooltip("If true, this event ignores AudioListener pause. Useful for pause menu UI sounds.")]
    [SerializeField] private bool IgnoreListenerPause = false;

    [Header("2D Settings")]
    [Tooltip("Stereo pan used mostly for 2D and UI sounds. -1 is left, 0 is center, 1 is right.")]
    [Range(-1f, 1f)]
    [SerializeField] private float StereoPan = 0f;

    [Header("3D Settings")]
    [Tooltip("Spatial blend used by this event. 0 is fully 2D, 1 is fully 3D.")]
    [Range(0f, 1f)]
    [SerializeField] private float SpatialBlend = 1f;

    [Tooltip("Minimum distance used by 3D attenuation.")]
    [SerializeField] private float MinDistance = 1f;

    [Tooltip("Maximum distance used by 3D attenuation.")]
    [SerializeField] private float MaxDistance = 25f;

    [Tooltip("Doppler level used by this event. Keep this at 0 for most gameplay sounds unless velocity-based pitch is desired.")]
    [SerializeField] private float DopplerLevel = 0f;

    [Tooltip("Rolloff mode used by this event for 3D attenuation.")]
    [SerializeField] private AudioRolloffMode RolloffMode = AudioRolloffMode.Logarithmic;

    /// <summary>
    /// Gets a random valid AudioClip from this event.
    /// </summary>
    /// <returns>A random AudioClip, or null if no clips are configured.</returns>
    public AudioClip GetRandomClip()
    {
        if (Clips == null || Clips.Length == 0)
        {
            return null;
        }

        return Clips[Random.Range(0, Clips.Length)];
    }

    /// <summary>
    /// Gets the configured Audio Mixer Group.
    /// </summary>
    /// <returns>The mixer group assigned to this event.</returns>
    public AudioMixerGroup GetMixerGroup()
    {
        return MixerGroup;
    }

    /// <summary>
    /// Gets the final playback volume after random variation.
    /// </summary>
    /// <returns>Resolved volume in the [0, 1] range.</returns>
    public float GetResolvedVolume()
    {
        float RandomMultiplier = Random.Range(
            Mathf.Min(VolumeRandomMultiplierRange.x, VolumeRandomMultiplierRange.y),
            Mathf.Max(VolumeRandomMultiplierRange.x, VolumeRandomMultiplierRange.y));

        return Mathf.Clamp01(Volume * Mathf.Max(0f, RandomMultiplier));
    }

    /// <summary>
    /// Gets the final playback pitch after random variation.
    /// </summary>
    /// <returns>Resolved pitch clamped above zero.</returns>
    public float GetResolvedPitch()
    {
        float RandomMultiplier = Random.Range(
            Mathf.Min(PitchRandomMultiplierRange.x, PitchRandomMultiplierRange.y),
            Mathf.Max(PitchRandomMultiplierRange.x, PitchRandomMultiplierRange.y));

        return Mathf.Max(0.01f, Pitch * Mathf.Max(0.01f, RandomMultiplier));
    }

    /// <summary>
    /// Gets the delay applied before this event starts playing.
    /// </summary>
    /// <returns>Playback delay in seconds.</returns>
    public float GetPlaybackDelay()
    {
        return Mathf.Max(0f, PlaybackDelay);
    }

    /// <summary>
    /// Gets the cooldown used to rate-limit this event.
    /// </summary>
    /// <returns>Cooldown in seconds.</returns>
    public float GetCooldown()
    {
        return Mathf.Max(0f, Cooldown);
    }

    /// <summary>
    /// Gets the AudioSource priority used by this event.
    /// </summary>
    /// <returns>Priority value in Unity's 0 to 256 range.</returns>
    public int GetPriority()
    {
        return Mathf.Clamp(Priority, 0, 256);
    }

    /// <summary>
    /// Gets whether this event should ignore AudioListener pause.
    /// </summary>
    /// <returns>True if the event ignores listener pause.</returns>
    public bool GetIgnoreListenerPause()
    {
        return IgnoreListenerPause;
    }

    /// <summary>
    /// Gets the stereo pan used by this event.
    /// </summary>
    /// <returns>Stereo pan in the [-1, 1] range.</returns>
    public float GetStereoPan()
    {
        return Mathf.Clamp(StereoPan, -1f, 1f);
    }

    /// <summary>
    /// Gets the spatial blend used by this event.
    /// </summary>
    /// <returns>Spatial blend in the [0, 1] range.</returns>
    public float GetSpatialBlend()
    {
        return Mathf.Clamp01(SpatialBlend);
    }

    /// <summary>
    /// Gets the minimum distance used by 3D attenuation.
    /// </summary>
    /// <returns>Minimum distance greater than zero.</returns>
    public float GetMinDistance()
    {
        return Mathf.Max(0.01f, MinDistance);
    }

    /// <summary>
    /// Gets the maximum distance used by 3D attenuation.
    /// </summary>
    /// <returns>Maximum distance greater than or equal to the minimum distance.</returns>
    public float GetMaxDistance()
    {
        return Mathf.Max(GetMinDistance(), MaxDistance);
    }

    /// <summary>
    /// Gets the Doppler level used by this event.
    /// </summary>
    /// <returns>Doppler level greater than or equal to zero.</returns>
    public float GetDopplerLevel()
    {
        return Mathf.Max(0f, DopplerLevel);
    }

    /// <summary>
    /// Gets the rolloff mode used by this event.
    /// </summary>
    /// <returns>Configured rolloff mode.</returns>
    public AudioRolloffMode GetRolloffMode()
    {
        return RolloffMode;
    }

    /// <summary>
    /// Clamps editor values to safe ranges when edited in the Inspector.
    /// </summary>
    private void OnValidate()
    {
        Volume = Mathf.Clamp01(Volume);
        Pitch = Mathf.Max(0.01f, Pitch);
        PlaybackDelay = Mathf.Max(0f, PlaybackDelay);
        Cooldown = Mathf.Max(0f, Cooldown);
        MinDistance = Mathf.Max(0.01f, MinDistance);
        MaxDistance = Mathf.Max(MinDistance, MaxDistance);
        DopplerLevel = Mathf.Max(0f, DopplerLevel);
        Priority = Mathf.Clamp(Priority, 0, 256);
        SpatialBlend = Mathf.Clamp01(SpatialBlend);
        StereoPan = Mathf.Clamp(StereoPan, -1f, 1f);
    }
}
