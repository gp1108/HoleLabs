using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Emits footstep audio and optional gameplay feedback from real player locomotion instead of raw input.
/// Steps do not play while standing on a moving elevator, pushing into a wall without advancing, or being airborne.
/// Surface detection can resolve different audio events from FootstepSurface markers, physics materials or layers.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(250)]
public sealed class PlayerFootstepAudioEmitter : MonoBehaviour
{
    [Serializable]
    private sealed class FootstepSurfaceAudioRule
    {
        [Tooltip("Optional label used only for inspector readability and debug logs.")]
        [SerializeField] private string RuleId = "Surface";

        [Tooltip("Optional physics material matched from the ground collider. Leave empty to ignore physics material matching for this rule.")]
        [SerializeField] private PhysicsMaterial PhysicsMaterial;

        [Tooltip("Optional layer mask matched from the ground collider. Leave empty to ignore layer matching for this rule.")]
        [SerializeField] private LayerMask SurfaceLayers = 0;

        [Tooltip("Audio event played when this rule matches the current ground surface.")]
        [SerializeField] private GameAudioEvent FootstepAudioEvent;

        /// <summary>
        /// Gets the optional debug label used by this rule.
        /// </summary>
        public string GetRuleId()
        {
            return RuleId;
        }

        /// <summary>
        /// Gets the configured audio event for this rule.
        /// </summary>
        public GameAudioEvent GetFootstepAudioEvent()
        {
            return FootstepAudioEvent;
        }

        /// <summary>
        /// Returns whether this rule matches the provided ground collider.
        /// Matching is intentionally permissive: physics material or layer can match independently.
        /// </summary>
        /// <param name="SurfaceCollider">Ground collider detected below the player.</param>
        /// <returns>True when the rule matches and has a valid audio event.</returns>
        public bool Matches(Collider SurfaceCollider)
        {
            if (SurfaceCollider == null || FootstepAudioEvent == null)
            {
                return false;
            }

            if (PhysicsMaterial != null && SurfaceCollider.sharedMaterial == PhysicsMaterial)
            {
                return true;
            }

            int SurfaceLayerBit = 1 << SurfaceCollider.gameObject.layer;
            return SurfaceLayers.value != 0 && (SurfaceLayers.value & SurfaceLayerBit) != 0;
        }
    }

    [Header("References")]
    [Tooltip("Locomotion source used to decide when the player is truly walking.")]
    [SerializeField] private PlayerLocomotionFeedbackSource LocomotionSource;

    [Tooltip("Optional transform used as the world position for footstep sounds and surface probing. If empty, this transform is used.")]
    [SerializeField] private Transform FootstepOrigin;

    [Tooltip("Optional feedback emitter used to play particles, decals, VFX or Feel events on each footstep.")]
    [SerializeField] private GameFeedbackEmitter FeedbackEmitter;

    [Header("Audio")]
    [Tooltip("Fallback audio event played when no surface-specific audio event is resolved.")]
    [SerializeField] private GameAudioEvent DefaultFootstepAudioEvent;

    [Tooltip("If true, surface probing resolves a specific audio event before falling back to the default event.")]
    [SerializeField] private bool UseSurfaceAudio = true;

    [Tooltip("Surface audio rules evaluated after FootstepSurface markers and before the default event.")]
    [SerializeField] private List<FootstepSurfaceAudioRule> SurfaceAudioRules = new();

    [Header("Surface Detection")]
    [Tooltip("Layers considered valid ground surfaces for footstep surface probing.")]
    [SerializeField] private LayerMask SurfaceProbeLayers = ~0;

    [Tooltip("Vertical offset added above the footstep origin before casting down. This prevents starting the ray inside thin floor colliders.")]
    [SerializeField] private float SurfaceProbeStartHeight = 0.25f;

    [Tooltip("Additional distance cast below the footstep origin to find the current walking surface.")]
    [SerializeField] private float SurfaceProbeDownDistance = 0.75f;

    [Tooltip("Trigger handling used by the surface probe. Ignore is recommended for physical floors.")]
    [SerializeField] private QueryTriggerInteraction SurfaceProbeTriggerInteraction = QueryTriggerInteraction.Ignore;

    [Tooltip("If true, a FootstepSurface component on the hit collider or its parents overrides material and layer rules.")]
    [SerializeField] private bool PreferFootstepSurfaceComponent = true;

    [Header("Feedback")]
    [Tooltip("If true, the optional feedback emitter plays a feedback event on every footstep.")]
    [SerializeField] private bool UseGameFeedback = false;

    [Tooltip("Feedback event id played on each footstep when Use Game Feedback is enabled.")]
    [SerializeField] private string FootstepFeedbackEventId = GameFeedbackEventIds.PlayerFootstep;

    [Tooltip("Intensity multiplier passed to footstep feedback events. The value is multiplied by locomotion intensity.")]
    [SerializeField] private float FootstepFeedbackIntensity = 1f;

    [Header("Timing")]
    [Tooltip("Distance in meters between footsteps at low movement speed.")]
    [SerializeField] private float SlowStepDistance = 1.15f;

    [Tooltip("Distance in meters between footsteps at full movement speed.")]
    [SerializeField] private float FastStepDistance = 0.72f;

    [Tooltip("Minimum seconds between footstep sounds, used as a safety gate for sudden velocity spikes.")]
    [SerializeField] private float MinimumStepInterval = 0.16f;

    [Tooltip("If true, the first step is delayed until enough distance has been accumulated after movement starts.")]
    [SerializeField] private bool DelayFirstStepUntilDistanceReached = false;

    [Header("Debug")]
    [Tooltip("Logs footstep playback and surface resolution for validation.")]
    [SerializeField] private bool DebugLogs = false;

    [Tooltip("Draws the surface probe ray in the Scene view.")]
    [SerializeField] private bool DrawDebugSurfaceProbe = false;

    /// <summary>
    /// Distance accumulated since the last footstep.
    /// </summary>
    private float StepDistanceAccumulator;

    /// <summary>
    /// Last unscaled time at which a footstep was played.
    /// </summary>
    private float LastStepTime = -999f;

    /// <summary>
    /// Resolves required references.
    /// </summary>
    private void Awake()
    {
        if (LocomotionSource == null)
        {
            LocomotionSource = GetComponent<PlayerLocomotionFeedbackSource>();
        }

        if (LocomotionSource == null)
        {
            LocomotionSource = GetComponentInParent<PlayerLocomotionFeedbackSource>();
        }

        if (FeedbackEmitter == null)
        {
            FeedbackEmitter = GetComponent<GameFeedbackEmitter>();
        }

        if (FeedbackEmitter == null)
        {
            FeedbackEmitter = GetComponentInParent<GameFeedbackEmitter>();
        }

        if (FootstepOrigin == null)
        {
            FootstepOrigin = transform;
        }
    }

    /// <summary>
    /// Subscribes to locomotion state changes.
    /// </summary>
    private void OnEnable()
    {
        if (LocomotionSource != null)
        {
            LocomotionSource.OnLocomotionActiveChanged += HandleLocomotionActiveChanged;
        }
    }

    /// <summary>
    /// Unsubscribes from locomotion state changes.
    /// </summary>
    private void OnDisable()
    {
        if (LocomotionSource != null)
        {
            LocomotionSource.OnLocomotionActiveChanged -= HandleLocomotionActiveChanged;
        }
    }

    /// <summary>
    /// Accumulates travelled distance and emits footsteps when enough real motion has occurred.
    /// </summary>
    private void Update()
    {
        if (LocomotionSource == null || !LocomotionSource.IsLocomotionActive)
        {
            return;
        }

        StepDistanceAccumulator += LocomotionSource.RealHorizontalSpeed * Time.deltaTime;

        float RequiredDistance = Mathf.Lerp(
            Mathf.Max(0.01f, SlowStepDistance),
            Mathf.Max(0.01f, FastStepDistance),
            Mathf.Clamp01(LocomotionSource.LocomotionIntensity));

        if (StepDistanceAccumulator < RequiredDistance)
        {
            return;
        }

        if (Time.unscaledTime < LastStepTime + Mathf.Max(0f, MinimumStepInterval))
        {
            return;
        }

        StepDistanceAccumulator -= RequiredDistance;
        PlayFootstep();
    }

    /// <summary>
    /// Resets step accumulation when locomotion starts or stops.
    /// </summary>
    /// <param name="IsActive">Whether locomotion just became active.</param>
    private void HandleLocomotionActiveChanged(bool IsActive)
    {
        StepDistanceAccumulator = DelayFirstStepUntilDistanceReached || !IsActive
            ? 0f
            : Mathf.Max(0.01f, SlowStepDistance) * 0.65f;
    }

    /// <summary>
    /// Plays one footstep audio event and optional gameplay feedback at the resolved surface point.
    /// </summary>
    private void PlayFootstep()
    {
        Vector3 FallbackWorldPosition = FootstepOrigin != null ? FootstepOrigin.position : transform.position;
        bool HasSurfaceHit = TryProbeSurface(out RaycastHit SurfaceHit);
        Vector3 PlaybackPosition = HasSurfaceHit ? SurfaceHit.point : FallbackWorldPosition;
        GameAudioEvent ResolvedAudioEvent = ResolveFootstepAudioEvent(HasSurfaceHit, SurfaceHit);

        if (ResolvedAudioEvent != null)
        {
            GameAudio.PlayAt(ResolvedAudioEvent, PlaybackPosition);
        }

        PlayFootstepFeedback(HasSurfaceHit, SurfaceHit, PlaybackPosition);

        LastStepTime = Time.unscaledTime;
        Log("Footstep played at " + PlaybackPosition + " | Audio: " + (ResolvedAudioEvent != null ? ResolvedAudioEvent.name : "None"));
    }

    /// <summary>
    /// Attempts to detect the current ground surface below the footstep origin.
    /// </summary>
    /// <param name="SurfaceHit">Detected surface hit data.</param>
    /// <returns>True when a valid surface was detected.</returns>
    private bool TryProbeSurface(out RaycastHit SurfaceHit)
    {
        Vector3 Origin = FootstepOrigin != null ? FootstepOrigin.position : transform.position;
        Origin += Vector3.up * Mathf.Max(0f, SurfaceProbeStartHeight);

        float ProbeDistance = Mathf.Max(0.01f, SurfaceProbeStartHeight + SurfaceProbeDownDistance);

        if (DrawDebugSurfaceProbe)
        {
            Debug.DrawRay(Origin, Vector3.down * ProbeDistance, Color.cyan, 0.05f);
        }

        return Physics.Raycast(
            Origin,
            Vector3.down,
            out SurfaceHit,
            ProbeDistance,
            SurfaceProbeLayers,
            SurfaceProbeTriggerInteraction);
    }

    /// <summary>
    /// Resolves the best footstep audio event for the current surface.
    /// </summary>
    /// <param name="HasSurfaceHit">Whether a surface hit was detected.</param>
    /// <param name="SurfaceHit">Current surface hit data.</param>
    /// <returns>Resolved audio event, or null when no event is configured.</returns>
    private GameAudioEvent ResolveFootstepAudioEvent(bool HasSurfaceHit, RaycastHit SurfaceHit)
    {
        if (!UseSurfaceAudio || !HasSurfaceHit || SurfaceHit.collider == null)
        {
            return DefaultFootstepAudioEvent;
        }

        if (PreferFootstepSurfaceComponent && TryResolveSurfaceComponentAudio(SurfaceHit.collider, out GameAudioEvent SurfaceComponentAudioEvent))
        {
            return SurfaceComponentAudioEvent;
        }

        for (int Index = 0; Index < SurfaceAudioRules.Count; Index++)
        {
            FootstepSurfaceAudioRule Rule = SurfaceAudioRules[Index];

            if (Rule == null || !Rule.Matches(SurfaceHit.collider))
            {
                continue;
            }

            Log("Footstep surface rule matched: " + Rule.GetRuleId());
            return Rule.GetFootstepAudioEvent();
        }

        return DefaultFootstepAudioEvent;
    }

    /// <summary>
    /// Attempts to resolve footstep audio from a FootstepSurface component on the hit collider hierarchy.
    /// </summary>
    /// <param name="SurfaceCollider">Surface collider detected below the player.</param>
    /// <param name="AudioEvent">Resolved footstep audio event.</param>
    /// <returns>True when a surface component with audio was found.</returns>
    private bool TryResolveSurfaceComponentAudio(Collider SurfaceCollider, out GameAudioEvent AudioEvent)
    {
        AudioEvent = null;

        if (SurfaceCollider == null)
        {
            return false;
        }

        FootstepSurface Surface = SurfaceCollider.GetComponent<FootstepSurface>();

        if (Surface == null)
        {
            Surface = SurfaceCollider.GetComponentInParent<FootstepSurface>();
        }

        if (Surface == null || Surface.GetFootstepAudioEvent() == null)
        {
            return false;
        }

        AudioEvent = Surface.GetFootstepAudioEvent();
        Log("Footstep surface component matched: " + Surface.GetSurfaceId());
        return true;
    }

    /// <summary>
    /// Plays optional non-audio feedback at the current footstep surface.
    /// </summary>
    /// <param name="HasSurfaceHit">Whether a surface hit was detected.</param>
    /// <param name="SurfaceHit">Current surface hit data.</param>
    /// <param name="FallbackPosition">Fallback position used when there is no hit.</param>
    private void PlayFootstepFeedback(bool HasSurfaceHit, RaycastHit SurfaceHit, Vector3 FallbackPosition)
    {
        if (!UseGameFeedback || FeedbackEmitter == null || string.IsNullOrWhiteSpace(FootstepFeedbackEventId))
        {
            return;
        }

        float Intensity = Mathf.Max(0f, FootstepFeedbackIntensity) * Mathf.Clamp01(LocomotionSource != null ? LocomotionSource.LocomotionIntensity : 1f);
        GameFeedbackContext Context = HasSurfaceHit
            ? GameFeedbackContext.FromRaycastHit(SurfaceHit, transform, Intensity)
            : GameFeedbackContext.FromPosition(FallbackPosition, transform, Intensity);

        FeedbackEmitter.Play(FootstepFeedbackEventId, Context);
    }

    /// <summary>
    /// Logs footstep messages when debug logging is enabled.
    /// </summary>
    /// <param name="Message">Message to log.</param>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[PlayerFootstepAudioEmitter] " + Message, this);
    }
}
