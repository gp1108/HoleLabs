using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Reusable data asset that maps gameplay event ids to particles, Visual Effect Graph prefabs, decals and audio events.
/// This profile is intentionally generic so tools, ores, machines, shop interactions and UI can share the same feedback infrastructure.
/// </summary>
[CreateAssetMenu(fileName = "FeedbackProfile_", menuName = "Game/Feedback/Game Feedback Profile")]
public sealed class GameFeedbackProfile : ScriptableObject
{
    /// <summary>
    /// Defines how multiple feedback entries of the same kind are selected.
    /// </summary>
    public enum FeedbackSelectionMode
    {
        PlayAll = 0,
        RandomOne = 1
    }

    /// <summary>
    /// Defines how spawned surface-oriented prefabs should align to the feedback normal.
    /// </summary>
    public enum SurfaceAlignmentMode
    {
        UpToSurfaceNormal = 0,
        ForwardToSurfaceNormal = 1,
        ForwardIntoSurface = 2
    }

    /// <summary>
    /// Describes how a requested event id resolves inside this profile.
    /// </summary>
    public enum EventResolutionStatus
    {
        InvalidEventId = 0,
        MissingProfileEvent = 1,
        DisabledProfileEvent = 2,
        EnabledProfileEvent = 3
    }

    [Serializable]
    public sealed class GameFeedbackEvent
    {
        [Tooltip("Stable event id matched by gameplay code. Use GameFeedbackEventIds constants to avoid typos.")]
        [SerializeField] private string EventId;

        [Tooltip("If false, this feedback event is ignored even if gameplay requests it.")]
        [SerializeField] private bool Enabled = true;

        [Header("Particles")]
        [Tooltip("Particle prefabs spawned when this event plays.")]
        [SerializeField] private List<GameObject> ParticlePrefabs = new();

        [Tooltip("How particle prefabs are selected when multiple are configured.")]
        [SerializeField] private FeedbackSelectionMode ParticleSelectionMode = FeedbackSelectionMode.PlayAll;

        [Tooltip("Local offset applied after the final particle rotation is resolved.")]
        [SerializeField] private Vector3 ParticlePositionOffset = Vector3.zero;

        [Tooltip("If true, particle up direction is aligned to the feedback surface normal when one is available.")]
        [SerializeField] private bool AlignParticlesToSurfaceNormal = true;

        [Tooltip("If true, spawned particles are parented to the context parent transform when one is provided.")]
        [SerializeField] private bool ParentParticlesToContextParent = false;

        [Tooltip("Seconds before spawned particle prefabs are destroyed. Use zero or less to leave lifecycle to the prefab.")]
        [SerializeField] private float ParticleDestroyDelay = 5f;

        [Header("Visual Effects")]
        [Tooltip("Visual Effect Graph prefabs spawned when this event plays. Assign a prefab containing a VisualEffect component, not the hit target object.")]
        [SerializeField] private List<GameObject> VisualEffectPrefabs = new();

        [Tooltip("How visual effect prefabs are selected when multiple are configured.")]
        [SerializeField] private FeedbackSelectionMode VisualEffectSelectionMode = FeedbackSelectionMode.PlayAll;

        [Tooltip("Local offset applied after the final visual effect rotation is resolved.")]
        [SerializeField] private Vector3 VisualEffectPositionOffset = Vector3.zero;

        [Tooltip("If true, visual effect up direction is aligned to the feedback surface normal when one is available.")]
        [SerializeField] private bool AlignVisualEffectsToSurfaceNormal = true;

        [Tooltip("If true, spawned visual effects are parented to the context parent transform when one is provided. Keep this disabled for most impact effects.")]
        [SerializeField] private bool ParentVisualEffectsToContextParent = false;

        [Tooltip("If true, spawned and local VisualEffect components are reinitialized before Play is called.")]
        [SerializeField] private bool ReinitializeVisualEffectsBeforePlay = true;

        [Tooltip("Seconds after Play before Stop is called on spawned VisualEffect components. Use zero or less if the graph is a clean one-shot that does not loop.")]
        [SerializeField] private float VisualEffectStopDelay = 0.25f;

        [Tooltip("Seconds before spawned visual effect prefabs are destroyed. Use zero or less to leave lifecycle to the prefab. For looping VFX, keep this above zero.")]
        [SerializeField] private float VisualEffectDestroyDelay = 2f;

        [Header("Decals")]
        [Tooltip("Decal prefabs spawned at the feedback surface. These can be URP/HDRP DecalProjector prefabs or mesh decal prefabs.")]
        [SerializeField] private List<GameObject> DecalPrefabs = new();

        [Tooltip("How decal prefabs are selected when multiple are configured.")]
        [SerializeField] private FeedbackSelectionMode DecalSelectionMode = FeedbackSelectionMode.RandomOne;

        [Tooltip("How decal prefabs should align to the feedback surface normal.")]
        [SerializeField] private SurfaceAlignmentMode DecalAlignmentMode = SurfaceAlignmentMode.ForwardIntoSurface;

        [Tooltip("Local offset applied after the final decal rotation is resolved.")]
        [SerializeField] private Vector3 DecalPositionOffset = Vector3.zero;

        [Tooltip("Small outward offset along the hit normal to prevent z-fighting with the impacted surface.")]
        [SerializeField] private float DecalSurfaceOffset = 0.01f;

        [Tooltip("If true, a random roll is applied around the hit normal so repeated decals do not look identical.")]
        [SerializeField] private bool RandomizeDecalRoll = true;

        [Tooltip("Minimum random decal roll in degrees.")]
        [SerializeField] private float DecalRollMin = 0f;

        [Tooltip("Maximum random decal roll in degrees.")]
        [SerializeField] private float DecalRollMax = 360f;

        [Tooltip("If true, spawned decals are parented to the context parent transform when one is provided. Disable this for most world impact decals.")]
        [SerializeField] private bool ParentDecalsToContextParent = false;

        [Tooltip("Seconds before spawned decal prefabs are destroyed. Use zero or less for persistent decals.")]
        [SerializeField] private float DecalDestroyDelay = 15f;

        [Header("Audio")]
        [Tooltip("Audio events played when this feedback event fires.")]
        [SerializeField] private List<GameAudioEvent> AudioEvents = new();

        [Tooltip("How audio events are selected when multiple are configured.")]
        [SerializeField] private FeedbackSelectionMode AudioSelectionMode = FeedbackSelectionMode.RandomOne;

        [Tooltip("If true, audio is played as a 3D event at the feedback position. If false, it is played as a regular 2D SFX.")]
        [SerializeField] private bool PlayAudioAtWorldPosition = true;

        [Header("Feel")]
        [Tooltip("Multiplier applied to the context intensity when local Feel feedback players are triggered.")]
        [SerializeField] private float FeelIntensityMultiplier = 1f;

        /// <summary>
        /// Gets the event id used by this entry.
        /// </summary>
        public string GetEventId() => EventId;

        /// <summary>
        /// Gets whether this feedback entry can currently play.
        /// </summary>
        public bool GetEnabled() => Enabled;

        /// <summary>
        /// Normalizes inspector-authored values that can safely be clamped or trimmed without changing gameplay intent.
        /// </summary>
        public void NormalizeForInspector()
        {
            EventId = string.IsNullOrWhiteSpace(EventId) ? string.Empty : EventId.Trim();
            ParticleDestroyDelay = Mathf.Max(0f, ParticleDestroyDelay);
            VisualEffectStopDelay = Mathf.Max(0f, VisualEffectStopDelay);
            VisualEffectDestroyDelay = Mathf.Max(0f, VisualEffectDestroyDelay);
            DecalSurfaceOffset = Mathf.Max(0f, DecalSurfaceOffset);
            DecalDestroyDelay = Mathf.Max(0f, DecalDestroyDelay);
            FeelIntensityMultiplier = Mathf.Max(0f, FeelIntensityMultiplier);

            if (DecalRollMax < DecalRollMin)
            {
                float CachedMin = DecalRollMin;
                DecalRollMin = DecalRollMax;
                DecalRollMax = CachedMin;
            }
        }

        /// <summary>
        /// Gets the configured particle prefabs.
        /// </summary>
        public IReadOnlyList<GameObject> GetParticlePrefabs() => ParticlePrefabs;

        /// <summary>
        /// Gets how particle prefabs should be selected.
        /// </summary>
        public FeedbackSelectionMode GetParticleSelectionMode() => ParticleSelectionMode;

        /// <summary>
        /// Gets the local offset applied to spawned particles.
        /// </summary>
        public Vector3 GetParticlePositionOffset() => ParticlePositionOffset;

        /// <summary>
        /// Gets whether particles should align to the context surface normal.
        /// </summary>
        public bool GetAlignParticlesToSurfaceNormal() => AlignParticlesToSurfaceNormal;

        /// <summary>
        /// Gets whether spawned particles should be parented to the context parent transform.
        /// </summary>
        public bool GetParentParticlesToContextParent() => ParentParticlesToContextParent;

        /// <summary>
        /// Gets how long spawned particle prefabs should live before automatic destruction.
        /// </summary>
        public float GetParticleDestroyDelay() => ParticleDestroyDelay;

        /// <summary>
        /// Gets the configured visual effect prefabs.
        /// </summary>
        public IReadOnlyList<GameObject> GetVisualEffectPrefabs() => VisualEffectPrefabs;

        /// <summary>
        /// Gets how visual effect prefabs should be selected.
        /// </summary>
        public FeedbackSelectionMode GetVisualEffectSelectionMode() => VisualEffectSelectionMode;

        /// <summary>
        /// Gets the local offset applied to spawned visual effects.
        /// </summary>
        public Vector3 GetVisualEffectPositionOffset() => VisualEffectPositionOffset;

        /// <summary>
        /// Gets whether visual effects should align to the context surface normal.
        /// </summary>
        public bool GetAlignVisualEffectsToSurfaceNormal() => AlignVisualEffectsToSurfaceNormal;

        /// <summary>
        /// Gets whether spawned visual effects should be parented to the context parent transform.
        /// </summary>
        public bool GetParentVisualEffectsToContextParent() => ParentVisualEffectsToContextParent;

        /// <summary>
        /// Gets whether VisualEffect components should be reinitialized before playback.
        /// </summary>
        public bool GetReinitializeVisualEffectsBeforePlay() => ReinitializeVisualEffectsBeforePlay;

        /// <summary>
        /// Gets how long a VisualEffect should emit before Stop is called.
        /// </summary>
        public float GetVisualEffectStopDelay() => VisualEffectStopDelay;

        /// <summary>
        /// Gets how long spawned visual effect prefabs should live before automatic destruction.
        /// </summary>
        public float GetVisualEffectDestroyDelay() => VisualEffectDestroyDelay;

        /// <summary>
        /// Gets the configured decal prefabs.
        /// </summary>
        public IReadOnlyList<GameObject> GetDecalPrefabs() => DecalPrefabs;

        /// <summary>
        /// Gets how decal prefabs should be selected.
        /// </summary>
        public FeedbackSelectionMode GetDecalSelectionMode() => DecalSelectionMode;

        /// <summary>
        /// Gets how decals should align to the feedback surface normal.
        /// </summary>
        public SurfaceAlignmentMode GetDecalAlignmentMode() => DecalAlignmentMode;

        /// <summary>
        /// Gets the local offset applied to spawned decals.
        /// </summary>
        public Vector3 GetDecalPositionOffset() => DecalPositionOffset;

        /// <summary>
        /// Gets the outward offset applied along the hit normal.
        /// </summary>
        public float GetDecalSurfaceOffset() => Mathf.Max(0f, DecalSurfaceOffset);

        /// <summary>
        /// Gets whether decal roll should be randomized.
        /// </summary>
        public bool GetRandomizeDecalRoll() => RandomizeDecalRoll;

        /// <summary>
        /// Gets the minimum random decal roll.
        /// </summary>
        public float GetDecalRollMin() => DecalRollMin;

        /// <summary>
        /// Gets the maximum random decal roll.
        /// </summary>
        public float GetDecalRollMax() => DecalRollMax;

        /// <summary>
        /// Gets whether spawned decals should be parented to the context parent transform.
        /// </summary>
        public bool GetParentDecalsToContextParent() => ParentDecalsToContextParent;

        /// <summary>
        /// Gets how long spawned decal prefabs should live before automatic destruction.
        /// </summary>
        public float GetDecalDestroyDelay() => DecalDestroyDelay;

        /// <summary>
        /// Gets the configured audio events.
        /// </summary>
        public IReadOnlyList<GameAudioEvent> GetAudioEvents() => AudioEvents;

        /// <summary>
        /// Gets how audio events should be selected.
        /// </summary>
        public FeedbackSelectionMode GetAudioSelectionMode() => AudioSelectionMode;

        /// <summary>
        /// Gets whether audio should play at the feedback world position.
        /// </summary>
        public bool GetPlayAudioAtWorldPosition() => PlayAudioAtWorldPosition;

        /// <summary>
        /// Gets the intensity multiplier used by local Feel feedback bindings.
        /// </summary>
        public float GetFeelIntensityMultiplier() => Mathf.Max(0f, FeelIntensityMultiplier);
    }

    [Header("Events")]
    [Tooltip("Feedback events available in this profile. Event ids must match GameFeedbackEventIds constants exactly.")]
    [SerializeField] private List<GameFeedbackEvent> Events = new();

    /// <summary>
    /// Gets every event entry configured in this profile, including disabled entries.
    /// </summary>
    public IReadOnlyList<GameFeedbackEvent> GetEvents()
    {
        return Events;
    }

    /// <summary>
    /// Tries to resolve the configured feedback event matching the provided id.
    /// </summary>
    /// <param name="EventId">Event id requested by gameplay code.</param>
    /// <param name="FeedbackEvent">Resolved feedback event entry.</param>
    /// <returns>True when a matching enabled event was found.</returns>
    public bool TryGetEvent(string EventId, out GameFeedbackEvent FeedbackEvent)
    {
        return ResolveEvent(EventId, out FeedbackEvent) == EventResolutionStatus.EnabledProfileEvent;
    }

    /// <summary>
    /// Resolves an event id and reports whether the event is missing, disabled or enabled.
    /// </summary>
    /// <param name="EventId">Event id requested by gameplay code.</param>
    /// <param name="FeedbackEvent">Resolved profile entry when one exists.</param>
    /// <returns>Resolution status for diagnostics and playback.</returns>
    public EventResolutionStatus ResolveEvent(string EventId, out GameFeedbackEvent FeedbackEvent)
    {
        FeedbackEvent = null;

        if (string.IsNullOrWhiteSpace(EventId))
        {
            return EventResolutionStatus.InvalidEventId;
        }

        if (Events == null || Events.Count == 0)
        {
            return EventResolutionStatus.MissingProfileEvent;
        }

        for (int Index = 0; Index < Events.Count; Index++)
        {
            GameFeedbackEvent Candidate = Events[Index];

            if (Candidate == null)
            {
                continue;
            }

            if (!string.Equals(Candidate.GetEventId(), EventId, StringComparison.Ordinal))
            {
                continue;
            }

            FeedbackEvent = Candidate;
            return Candidate.GetEnabled() ? EventResolutionStatus.EnabledProfileEvent : EventResolutionStatus.DisabledProfileEvent;
        }

        return EventResolutionStatus.MissingProfileEvent;
    }

    /// <summary>
    /// Checks if this profile contains a matching event id.
    /// </summary>
    /// <param name="EventId">Event id to check.</param>
    /// <param name="IncludeDisabled">If true, disabled entries are considered valid matches.</param>
    /// <returns>True when this profile contains the requested event id.</returns>
    public bool HasEvent(string EventId, bool IncludeDisabled = true)
    {
        EventResolutionStatus Status = ResolveEvent(EventId, out _);
        return Status == EventResolutionStatus.EnabledProfileEvent || (IncludeDisabled && Status == EventResolutionStatus.DisabledProfileEvent);
    }

    /// <summary>
    /// Builds a readable list of configured event ids for inspector diagnostics.
    /// </summary>
    /// <returns>Debug summary containing enabled and disabled event ids.</returns>
    public string BuildDebugSummary()
    {
        StringBuilder Builder = new StringBuilder();
        Builder.AppendLine("[GameFeedbackProfile] Configured events for " + name + ":");

        if (Events == null || Events.Count == 0)
        {
            Builder.AppendLine("- No events configured.");
            return Builder.ToString();
        }

        for (int Index = 0; Index < Events.Count; Index++)
        {
            GameFeedbackEvent FeedbackEvent = Events[Index];

            if (FeedbackEvent == null)
            {
                Builder.AppendLine("- <null event entry>");
                continue;
            }

            string EventId = string.IsNullOrWhiteSpace(FeedbackEvent.GetEventId()) ? "<empty event id>" : FeedbackEvent.GetEventId();
            string State = FeedbackEvent.GetEnabled() ? "Enabled" : "Disabled";
            Builder.AppendLine("- " + EventId + " (" + State + ")");
        }

        return Builder.ToString();
    }

    /// <summary>
    /// Logs configured event ids from the inspector context menu.
    /// </summary>
    [ContextMenu("Game Feedback/Log Configured Event Ids")]
    private void LogConfiguredEventIds()
    {
        Debug.Log(BuildDebugSummary(), this);
    }

    /// <summary>
    /// Validates authoring data and warns about duplicate event ids.
    /// </summary>
    private void OnValidate()
    {
        if (Events == null)
        {
            Events = new List<GameFeedbackEvent>();
            return;
        }

        HashSet<string> SeenEventIds = new HashSet<string>(StringComparer.Ordinal);

        for (int Index = 0; Index < Events.Count; Index++)
        {
            GameFeedbackEvent FeedbackEvent = Events[Index];

            if (FeedbackEvent == null)
            {
                continue;
            }

            FeedbackEvent.NormalizeForInspector();
            string EventId = FeedbackEvent.GetEventId();

            if (string.IsNullOrWhiteSpace(EventId))
            {
                continue;
            }

            if (!SeenEventIds.Add(EventId))
            {
                Debug.LogWarning("[GameFeedbackProfile] Duplicate event id detected: " + EventId + " | Profile: " + name, this);
            }
        }
    }
}
