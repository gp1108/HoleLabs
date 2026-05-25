using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Central game audio service for SFX, UI sounds, 3D sounds, attached loops and music crossfades.
/// This component owns all pooled AudioSources so gameplay objects do not need manual AudioSource components.
/// </summary>
[DisallowMultipleComponent]
public sealed class GameAudio : MonoBehaviour
{
    /// <summary>
    /// Runtime handle returned by loop playback.
    /// Store this handle if the loop must be stopped later.
    /// </summary>
    public sealed class AudioHandle
    {
        /// <summary>
        /// Internal loop identifier assigned by the audio service.
        /// </summary>
        public int Id;

        /// <summary>
        /// Gets whether this handle currently points to a valid loop.
        /// </summary>
        public bool IsValid => Id > 0;
    }

    /// <summary>
    /// Internal runtime state for one active pooled AudioSource.
    /// </summary>
    private sealed class ActiveAudioSource
    {
        /// <summary>
        /// Handle identifier used only by looped sounds.
        /// </summary>
        public int HandleId;

        /// <summary>
        /// AudioSource currently playing this entry.
        /// </summary>
        public AudioSource Source;

        /// <summary>
        /// Optional target followed every frame by attached sounds.
        /// </summary>
        public Transform FollowTarget;

        /// <summary>
        /// True when this entry is a loop and must not be auto-returned when still playing.
        /// </summary>
        public bool IsLoop;

        /// <summary>
        /// DSP time when this source is scheduled to start playback.
        /// Delayed sources are not returned to the pool before this time.
        /// </summary>
        public double ScheduledStartDspTime;

        /// <summary>
        /// True after the source has reported that playback started at least once.
        /// This prevents delayed one-shots from being returned before Unity starts them.
        /// </summary>
        public bool HasStartedPlaying;
    }

    [Header("Singleton")]
    [Tooltip("If true, this audio service persists between scene loads.")]
    [SerializeField] private bool PersistAcrossScenes = true;

    [Header("Mixer")]
    [Tooltip("Main Audio Mixer used by this audio system. This is only required if you want volume sliders through exposed parameters.")]
    [SerializeField] private AudioMixer MainMixer;

    [Tooltip("Exposed Audio Mixer parameter used for global volume. Leave empty if unused.")]
    [SerializeField] private string MasterVolumeParameter = "MasterVolume";

    [Tooltip("Exposed Audio Mixer parameter used for music volume. Leave empty if unused.")]
    [SerializeField] private string MusicVolumeParameter = "MusicVolume";

    [Tooltip("Exposed Audio Mixer parameter used for ambience volume. Leave empty if unused.")]
    [SerializeField] private string AmbienceVolumeParameter = "AmbienceVolume";

    [Tooltip("Exposed Audio Mixer parameter used for SFX volume. Leave empty if unused.")]
    [SerializeField] private string SfxVolumeParameter = "SfxVolume";

    [Tooltip("Exposed Audio Mixer parameter used for UI volume. Leave empty if unused.")]
    [SerializeField] private string UiVolumeParameter = "UiVolume";

    [Header("Pooling")]
    [Tooltip("Number of AudioSources created during Awake.")]
    [SerializeField] private int InitialPoolSize = 24;

    [Tooltip("Maximum number of pooled AudioSources allowed at runtime.")]
    [SerializeField] private int MaxPoolSize = 64;

    [Tooltip("Optional parent used to store pooled AudioSources. If empty, one is created automatically.")]
    [SerializeField] private Transform PoolRoot;

    [Header("Music")]
    [Tooltip("Dedicated first music source used for crossfading.")]
    [SerializeField] private AudioSource MusicSourceA;

    [Tooltip("Dedicated second music source used for crossfading.")]
    [SerializeField] private AudioSource MusicSourceB;

    [Header("Debug")]
    [Tooltip("If true, this system logs warnings and useful audio state messages.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Current singleton instance.
    /// </summary>
    private static GameAudio Instance;

    /// <summary>
    /// Pooled AudioSources currently ready to be reused.
    /// </summary>
    private readonly Queue<AudioSource> AvailableSources = new();

    /// <summary>
    /// Runtime list of AudioSources currently playing.
    /// </summary>
    private readonly List<ActiveAudioSource> ActiveSources = new();

    /// <summary>
    /// Last playback time for each audio event used by event cooldowns.
    /// </summary>
    private readonly Dictionary<GameAudioEvent, float> LastPlaybackTimes = new();

    /// <summary>
    /// Music source that is currently audible.
    /// </summary>
    private AudioSource ActiveMusicSource;

    /// <summary>
    /// Music source prepared for the next crossfade.
    /// </summary>
    private AudioSource InactiveMusicSource;

    /// <summary>
    /// Active music fade coroutine.
    /// </summary>
    private Coroutine MusicFadeRoutine;

    /// <summary>
    /// Last generated audio handle id.
    /// </summary>
    private int LastHandleId;

    /// <summary>
    /// Initializes the singleton, pool and dedicated music sources.
    /// </summary>
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (PersistAcrossScenes)
        {
            DontDestroyOnLoad(gameObject);
        }

        EnsurePoolRoot();
        EnsureMusicSources();
        PrewarmPool();
    }

    /// <summary>
    /// Releases the singleton instance when this service is destroyed.
    /// </summary>
    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// Updates attached sounds and returns completed one-shot sources to the pool.
    /// </summary>
    private void Update()
    {
        for (int Index = ActiveSources.Count - 1; Index >= 0; Index--)
        {
            ActiveAudioSource ActiveSource = ActiveSources[Index];

            if (ActiveSource == null || ActiveSource.Source == null)
            {
                ActiveSources.RemoveAt(Index);
                continue;
            }

            if (ActiveSource.FollowTarget != null)
            {
                ActiveSource.Source.transform.position = ActiveSource.FollowTarget.position;
            }

            if (AudioSettings.dspTime < ActiveSource.ScheduledStartDspTime)
            {
                continue;
            }

            if (ActiveSource.Source.isPlaying)
            {
                ActiveSource.HasStartedPlaying = true;
                continue;
            }

            if (!ActiveSource.HasStartedPlaying && AudioSettings.dspTime < ActiveSource.ScheduledStartDspTime + 0.1d)
            {
                continue;
            }

            if (ActiveSource.IsLoop)
            {
                continue;
            }

            ReturnSourceToPool(ActiveSource.Source);
            ActiveSources.RemoveAt(Index);
        }
    }

    /// <summary>
    /// Gets whether the global audio service exists in the current scene.
    /// </summary>
    /// <returns>True when the service is ready.</returns>
    public static bool GetIsReady()
    {
        return Instance != null;
    }

    /// <summary>
    /// Plays a regular 2D sound effect.
    /// </summary>
    /// <param name="AudioEvent">Audio event to play.</param>
    public static void Play(GameAudioEvent AudioEvent)
    {
        if (Instance == null)
        {
            return;
        }

        Instance.PlayInternal(AudioEvent, Vector3.zero, null, true, false);
    }

    /// <summary>
    /// Plays a UI sound as fully 2D audio.
    /// </summary>
    /// <param name="AudioEvent">Audio event to play.</param>
    public static void PlayUi(GameAudioEvent AudioEvent)
    {
        if (Instance == null)
        {
            return;
        }

        Instance.PlayInternal(AudioEvent, Vector3.zero, null, true, false);
    }

    /// <summary>
    /// Plays a one-shot 3D sound at a world position.
    /// </summary>
    /// <param name="AudioEvent">Audio event to play.</param>
    /// <param name="WorldPosition">World position where the sound should play.</param>
    public static void PlayAt(GameAudioEvent AudioEvent, Vector3 WorldPosition)
    {
        if (Instance == null)
        {
            return;
        }

        Instance.PlayInternal(AudioEvent, WorldPosition, null, false, false);
    }

    /// <summary>
    /// Plays a looped 3D sound at a fixed world position.
    /// </summary>
    /// <param name="AudioEvent">Audio event to play.</param>
    /// <param name="WorldPosition">World position where the loop should play.</param>
    /// <returns>Handle used to stop this loop later.</returns>
    public static AudioHandle PlayLoopAt(GameAudioEvent AudioEvent, Vector3 WorldPosition)
    {
        if (Instance == null)
        {
            return null;
        }

        return Instance.PlayInternal(AudioEvent, WorldPosition, null, false, true);
    }

    /// <summary>
    /// Plays a looped 3D sound that follows a target transform.
    /// </summary>
    /// <param name="AudioEvent">Audio event to play.</param>
    /// <param name="Target">Transform followed by the looped source.</param>
    /// <returns>Handle used to stop this loop later.</returns>
    public static AudioHandle PlayLoopAttached(GameAudioEvent AudioEvent, Transform Target)
    {
        if (Instance == null || Target == null)
        {
            return null;
        }

        return Instance.PlayInternal(AudioEvent, Target.position, Target, false, true);
    }

    /// <summary>
    /// Stops a previously created loop.
    /// </summary>
    /// <param name="Handle">Loop handle returned by PlayLoopAt or PlayLoopAttached.</param>
    public static void Stop(AudioHandle Handle)
    {
        if (Instance == null || Handle == null || !Handle.IsValid)
        {
            return;
        }

        Instance.StopInternal(Handle);
    }

    /// <summary>
    /// Stops every currently active looped sound.
    /// </summary>
    public static void StopAllLoops()
    {
        if (Instance == null)
        {
            return;
        }

        Instance.StopAllLoopsInternal();
    }

    /// <summary>
    /// Plays music through the dedicated music sources with optional crossfade.
    /// </summary>
    /// <param name="MusicEvent">Music event to play.</param>
    /// <param name="FadeDuration">Crossfade duration in seconds.</param>
    public static void PlayMusic(GameAudioEvent MusicEvent, float FadeDuration = 1f)
    {
        if (Instance == null)
        {
            return;
        }

        Instance.PlayMusicInternal(MusicEvent, FadeDuration);
    }

    /// <summary>
    /// Stops the current music with optional fade out.
    /// </summary>
    /// <param name="FadeDuration">Fade out duration in seconds.</param>
    public static void StopMusic(float FadeDuration = 1f)
    {
        if (Instance == null)
        {
            return;
        }

        Instance.StopMusicInternal(FadeDuration);
    }

    /// <summary>
    /// Sets the master mixer volume using a normalized value.
    /// </summary>
    /// <param name="NormalizedVolume">Volume in the [0, 1] range.</param>
    public static void SetMasterVolume(float NormalizedVolume)
    {
        if (Instance == null)
        {
            return;
        }

        Instance.SetMixerVolumeInternal(Instance.MasterVolumeParameter, NormalizedVolume);
    }

    /// <summary>
    /// Sets the music mixer volume using a normalized value.
    /// </summary>
    /// <param name="NormalizedVolume">Volume in the [0, 1] range.</param>
    public static void SetMusicVolume(float NormalizedVolume)
    {
        if (Instance == null)
        {
            return;
        }

        Instance.SetMixerVolumeInternal(Instance.MusicVolumeParameter, NormalizedVolume);
    }

    /// <summary>
    /// Sets the ambience mixer volume using a normalized value.
    /// </summary>
    /// <param name="NormalizedVolume">Volume in the [0, 1] range.</param>
    public static void SetAmbienceVolume(float NormalizedVolume)
    {
        if (Instance == null)
        {
            return;
        }

        Instance.SetMixerVolumeInternal(Instance.AmbienceVolumeParameter, NormalizedVolume);
    }

    /// <summary>
    /// Sets the SFX mixer volume using a normalized value.
    /// </summary>
    /// <param name="NormalizedVolume">Volume in the [0, 1] range.</param>
    public static void SetSfxVolume(float NormalizedVolume)
    {
        if (Instance == null)
        {
            return;
        }

        Instance.SetMixerVolumeInternal(Instance.SfxVolumeParameter, NormalizedVolume);
    }

    /// <summary>
    /// Sets the UI mixer volume using a normalized value.
    /// </summary>
    /// <param name="NormalizedVolume">Volume in the [0, 1] range.</param>
    public static void SetUiVolume(float NormalizedVolume)
    {
        if (Instance == null)
        {
            return;
        }

        Instance.SetMixerVolumeInternal(Instance.UiVolumeParameter, NormalizedVolume);
    }

    /// <summary>
    /// Plays an audio event using a pooled AudioSource.
    /// </summary>
    /// <param name="AudioEvent">Audio event to play.</param>
    /// <param name="WorldPosition">World position used for 3D playback.</param>
    /// <param name="FollowTarget">Optional target followed by attached sounds.</param>
    /// <param name="Force2D">True to force fully 2D playback.</param>
    /// <param name="IsLoop">True to play this source as a loop.</param>
    /// <returns>A loop handle if IsLoop is true, otherwise null.</returns>
    private AudioHandle PlayInternal(
        GameAudioEvent AudioEvent,
        Vector3 WorldPosition,
        Transform FollowTarget,
        bool Force2D,
        bool IsLoop)
    {
        if (!CanPlay(AudioEvent))
        {
            return null;
        }

        AudioClip Clip = AudioEvent.GetRandomClip();

        if (Clip == null)
        {
            LogWarning("Audio event has no valid clips: " + AudioEvent.name);
            return null;
        }

        AudioSource Source = GetAvailableSource();

        if (Source == null)
        {
            LogWarning("No available AudioSource. Increase MaxPoolSize if this happens often.");
            return null;
        }

        ConfigureSource(Source, AudioEvent, Clip, WorldPosition, Force2D, IsLoop);

        float PlaybackDelay = AudioEvent.GetPlaybackDelay();
        double ScheduledStartDspTime = AudioSettings.dspTime + PlaybackDelay;

        if (PlaybackDelay > 0f)
        {
            Source.PlayScheduled(ScheduledStartDspTime);
        }
        else
        {
            Source.Play();
        }

        LastPlaybackTimes[AudioEvent] = Time.unscaledTime;

        ActiveAudioSource ActiveSource = new ActiveAudioSource
        {
            HandleId = IsLoop ? GetNextHandleId() : 0,
            Source = Source,
            FollowTarget = FollowTarget,
            IsLoop = IsLoop,
            ScheduledStartDspTime = ScheduledStartDspTime,
            HasStartedPlaying = PlaybackDelay <= 0f && Source.isPlaying
        };

        ActiveSources.Add(ActiveSource);

        if (!IsLoop)
        {
            return null;
        }

        return new AudioHandle
        {
            Id = ActiveSource.HandleId
        };
    }

    /// <summary>
    /// Stops one active loop by handle.
    /// </summary>
    /// <param name="Handle">Handle to stop.</param>
    private void StopInternal(AudioHandle Handle)
    {
        for (int Index = ActiveSources.Count - 1; Index >= 0; Index--)
        {
            ActiveAudioSource ActiveSource = ActiveSources[Index];

            if (ActiveSource == null || ActiveSource.HandleId != Handle.Id)
            {
                continue;
            }

            if (ActiveSource.Source != null)
            {
                ReturnSourceToPool(ActiveSource.Source);
            }

            ActiveSources.RemoveAt(Index);
            Handle.Id = 0;
            return;
        }

        Handle.Id = 0;
    }

    /// <summary>
    /// Stops every active loop source and returns it to the pool.
    /// </summary>
    private void StopAllLoopsInternal()
    {
        for (int Index = ActiveSources.Count - 1; Index >= 0; Index--)
        {
            ActiveAudioSource ActiveSource = ActiveSources[Index];

            if (ActiveSource == null || !ActiveSource.IsLoop)
            {
                continue;
            }

            if (ActiveSource.Source != null)
            {
                ReturnSourceToPool(ActiveSource.Source);
            }

            ActiveSources.RemoveAt(Index);
        }
    }

    /// <summary>
    /// Plays music through two dedicated AudioSources so the transition can crossfade.
    /// </summary>
    /// <param name="MusicEvent">Music event to play.</param>
    /// <param name="FadeDuration">Fade duration in seconds.</param>
    private void PlayMusicInternal(GameAudioEvent MusicEvent, float FadeDuration)
    {
        if (MusicEvent == null)
        {
            return;
        }

        AudioClip Clip = MusicEvent.GetRandomClip();

        if (Clip == null)
        {
            LogWarning("Music event has no valid clips: " + MusicEvent.name);
            return;
        }

        if (ActiveMusicSource != null && ActiveMusicSource.clip == Clip && ActiveMusicSource.isPlaying)
        {
            return;
        }

        if (MusicFadeRoutine != null)
        {
            StopCoroutine(MusicFadeRoutine);
        }

        MusicFadeRoutine = StartCoroutine(CrossfadeMusicRoutine(MusicEvent, Clip, Mathf.Max(0f, FadeDuration)));
    }

    /// <summary>
    /// Stops current music through a fade out.
    /// </summary>
    /// <param name="FadeDuration">Fade duration in seconds.</param>
    private void StopMusicInternal(float FadeDuration)
    {
        if (MusicFadeRoutine != null)
        {
            StopCoroutine(MusicFadeRoutine);
        }

        MusicFadeRoutine = StartCoroutine(StopMusicRoutine(Mathf.Max(0f, FadeDuration)));
    }

    /// <summary>
    /// Performs a music crossfade from the current source to the inactive source.
    /// </summary>
    /// <param name="MusicEvent">Music event used to configure the new source.</param>
    /// <param name="Clip">Music clip to play.</param>
    /// <param name="FadeDuration">Fade duration in seconds.</param>
    private IEnumerator CrossfadeMusicRoutine(GameAudioEvent MusicEvent, AudioClip Clip, float FadeDuration)
    {
        float PlaybackDelay = MusicEvent.GetPlaybackDelay();

        if (PlaybackDelay > 0f)
        {
            yield return new WaitForSecondsRealtime(PlaybackDelay);
        }

        AudioSource FromSource = ActiveMusicSource;
        AudioSource ToSource = InactiveMusicSource;

        ToSource.clip = Clip;
        ToSource.outputAudioMixerGroup = MusicEvent.GetMixerGroup();
        ToSource.volume = 0f;
        ToSource.pitch = MusicEvent.GetResolvedPitch();
        ToSource.loop = true;
        ToSource.playOnAwake = false;
        ToSource.spatialBlend = 0f;
        ToSource.priority = MusicEvent.GetPriority();
        ToSource.ignoreListenerPause = MusicEvent.GetIgnoreListenerPause();
        ToSource.Play();

        float TargetVolume = MusicEvent.GetResolvedVolume();
        float StartVolume = FromSource != null ? FromSource.volume : 0f;
        float Timer = 0f;

        while (Timer < FadeDuration)
        {
            Timer += Time.unscaledDeltaTime;
            float Progress = FadeDuration <= 0f ? 1f : Mathf.Clamp01(Timer / FadeDuration);

            ToSource.volume = Mathf.Lerp(0f, TargetVolume, Progress);

            if (FromSource != null)
            {
                FromSource.volume = Mathf.Lerp(StartVolume, 0f, Progress);
            }

            yield return null;
        }

        if (FromSource != null)
        {
            FromSource.Stop();
            FromSource.clip = null;
            FromSource.volume = 0f;
        }

        ToSource.volume = TargetVolume;
        ActiveMusicSource = ToSource;
        InactiveMusicSource = FromSource;
        MusicFadeRoutine = null;
    }

    /// <summary>
    /// Fades out and stops the active music source.
    /// </summary>
    /// <param name="FadeDuration">Fade duration in seconds.</param>
    private IEnumerator StopMusicRoutine(float FadeDuration)
    {
        AudioSource Source = ActiveMusicSource;

        if (Source == null)
        {
            MusicFadeRoutine = null;
            yield break;
        }

        float StartVolume = Source.volume;
        float Timer = 0f;

        while (Timer < FadeDuration)
        {
            Timer += Time.unscaledDeltaTime;
            float Progress = FadeDuration <= 0f ? 1f : Mathf.Clamp01(Timer / FadeDuration);
            Source.volume = Mathf.Lerp(StartVolume, 0f, Progress);
            yield return null;
        }

        Source.Stop();
        Source.clip = null;
        Source.volume = 0f;
        MusicFadeRoutine = null;
    }

    /// <summary>
    /// Returns true if an event is valid and not blocked by cooldown.
    /// </summary>
    /// <param name="AudioEvent">Audio event to evaluate.</param>
    /// <returns>True when the event can play now.</returns>
    private bool CanPlay(GameAudioEvent AudioEvent)
    {
        if (AudioEvent == null)
        {
            return false;
        }

        float Cooldown = AudioEvent.GetCooldown();

        if (Cooldown <= 0f)
        {
            return true;
        }

        if (!LastPlaybackTimes.TryGetValue(AudioEvent, out float LastPlaybackTime))
        {
            return true;
        }

        return Time.unscaledTime >= LastPlaybackTime + Cooldown;
    }

    /// <summary>
    /// Configures an AudioSource before playback.
    /// </summary>
    /// <param name="Source">AudioSource to configure.</param>
    /// <param name="AudioEvent">Audio event providing settings.</param>
    /// <param name="Clip">Audio clip to play.</param>
    /// <param name="WorldPosition">World position for 3D audio.</param>
    /// <param name="Force2D">True to force 2D playback.</param>
    /// <param name="IsLoop">True to configure the source as a loop.</param>
    private void ConfigureSource(
        AudioSource Source,
        GameAudioEvent AudioEvent,
        AudioClip Clip,
        Vector3 WorldPosition,
        bool Force2D,
        bool IsLoop)
    {
        Source.transform.position = WorldPosition;
        Source.clip = Clip;
        Source.outputAudioMixerGroup = AudioEvent.GetMixerGroup();
        Source.volume = AudioEvent.GetResolvedVolume();
        Source.pitch = AudioEvent.GetResolvedPitch();
        Source.loop = IsLoop;
        Source.playOnAwake = false;
        Source.priority = AudioEvent.GetPriority();
        Source.ignoreListenerPause = AudioEvent.GetIgnoreListenerPause();
        Source.panStereo = AudioEvent.GetStereoPan();
        Source.spatialBlend = Force2D ? 0f : AudioEvent.GetSpatialBlend();
        Source.minDistance = AudioEvent.GetMinDistance();
        Source.maxDistance = AudioEvent.GetMaxDistance();
        Source.dopplerLevel = AudioEvent.GetDopplerLevel();
        Source.rolloffMode = AudioEvent.GetRolloffMode();
    }

    /// <summary>
    /// Gets a source from the pool or creates a new one if the maximum pool size allows it.
    /// </summary>
    /// <returns>Available AudioSource, or null if the pool limit has been reached.</returns>
    private AudioSource GetAvailableSource()
    {
        while (AvailableSources.Count > 0)
        {
            AudioSource Source = AvailableSources.Dequeue();

            if (Source == null)
            {
                continue;
            }

            Source.gameObject.SetActive(true);
            return Source;
        }

        int TotalSourceCount = AvailableSources.Count + ActiveSources.Count;

        if (TotalSourceCount >= Mathf.Max(1, MaxPoolSize))
        {
            return null;
        }

        return CreatePooledSource();
    }

    /// <summary>
    /// Returns one AudioSource to the pool after clearing runtime state.
    /// </summary>
    /// <param name="Source">AudioSource to return.</param>
    private void ReturnSourceToPool(AudioSource Source)
    {
        if (Source == null)
        {
            return;
        }

        Source.Stop();
        Source.clip = null;
        Source.loop = false;
        Source.outputAudioMixerGroup = null;
        Source.transform.SetParent(PoolRoot, false);
        Source.transform.localPosition = Vector3.zero;
        Source.gameObject.SetActive(false);
        AvailableSources.Enqueue(Source);
    }

    /// <summary>
    /// Creates the initial pool of reusable AudioSources.
    /// </summary>
    private void PrewarmPool()
    {
        int Count = Mathf.Clamp(InitialPoolSize, 0, Mathf.Max(1, MaxPoolSize));

        for (int Index = 0; Index < Count; Index++)
        {
            AudioSource Source = CreatePooledSource();

            if (Source != null)
            {
                ReturnSourceToPool(Source);
            }
        }
    }

    /// <summary>
    /// Creates one pooled AudioSource under the pool root.
    /// </summary>
    /// <returns>Created AudioSource.</returns>
    private AudioSource CreatePooledSource()
    {
        GameObject SourceObject = new GameObject("PooledAudioSource");
        SourceObject.transform.SetParent(PoolRoot, false);

        AudioSource Source = SourceObject.AddComponent<AudioSource>();
        Source.playOnAwake = false;

        return Source;
    }

    /// <summary>
    /// Ensures a pool root exists in the hierarchy.
    /// </summary>
    private void EnsurePoolRoot()
    {
        if (PoolRoot != null)
        {
            return;
        }

        GameObject PoolRootObject = new GameObject("AudioPoolRoot");
        PoolRootObject.transform.SetParent(transform, false);
        PoolRoot = PoolRootObject.transform;
    }

    /// <summary>
    /// Ensures two dedicated music sources exist.
    /// </summary>
    private void EnsureMusicSources()
    {
        if (MusicSourceA == null)
        {
            GameObject SourceObject = new GameObject("MusicSourceA");
            SourceObject.transform.SetParent(transform, false);
            MusicSourceA = SourceObject.AddComponent<AudioSource>();
        }

        if (MusicSourceB == null)
        {
            GameObject SourceObject = new GameObject("MusicSourceB");
            SourceObject.transform.SetParent(transform, false);
            MusicSourceB = SourceObject.AddComponent<AudioSource>();
        }

        MusicSourceA.playOnAwake = false;
        MusicSourceB.playOnAwake = false;
        MusicSourceA.spatialBlend = 0f;
        MusicSourceB.spatialBlend = 0f;

        ActiveMusicSource = MusicSourceA;
        InactiveMusicSource = MusicSourceB;
    }

    /// <summary>
    /// Generates a new unique handle id.
    /// </summary>
    /// <returns>New handle id.</returns>
    private int GetNextHandleId()
    {
        LastHandleId++;

        if (LastHandleId <= 0)
        {
            LastHandleId = 1;
        }

        return LastHandleId;
    }

    /// <summary>
    /// Sets one exposed Audio Mixer volume parameter from a normalized value.
    /// </summary>
    /// <param name="ParameterName">Exposed parameter name.</param>
    /// <param name="NormalizedVolume">Volume in the [0, 1] range.</param>
    private void SetMixerVolumeInternal(string ParameterName, float NormalizedVolume)
    {
        if (MainMixer == null || string.IsNullOrWhiteSpace(ParameterName))
        {
            return;
        }

        float ClampedVolume = Mathf.Clamp01(NormalizedVolume);
        float DecibelValue = ClampedVolume <= 0.0001f ? -80f : Mathf.Log10(ClampedVolume) * 20f;
        MainMixer.SetFloat(ParameterName, DecibelValue);
    }

    /// <summary>
    /// Logs a warning if debug logging is enabled.
    /// </summary>
    /// <param name="Message">Warning message.</param>
    private void LogWarning(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.LogWarning("[GameAudio] " + Message, this);
    }

    /// <summary>
    /// Clamps editor values to safe ranges.
    /// </summary>
    private void OnValidate()
    {
        InitialPoolSize = Mathf.Max(0, InitialPoolSize);
        MaxPoolSize = Mathf.Max(1, MaxPoolSize);

        if (InitialPoolSize > MaxPoolSize)
        {
            InitialPoolSize = MaxPoolSize;
        }
    }
}
