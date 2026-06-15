using UnityEngine;

/// <summary>
/// Emits footstep audio from real player locomotion feedback instead of raw input.
/// Steps do not play while standing on a moving elevator, pushing into a wall without advancing, or being airborne.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(250)]
public sealed class PlayerFootstepAudioEmitter : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Locomotion source used to decide when the player is truly walking.")]
    [SerializeField] private PlayerLocomotionFeedbackSource LocomotionSource;

    [Tooltip("Optional transform used as the world position for footstep sounds. If empty, this transform is used.")]
    [SerializeField] private Transform FootstepOrigin;

    [Header("Audio")]
    [Tooltip("Audio event played for each player footstep.")]
    [SerializeField] private GameAudioEvent FootstepAudioEvent;

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
    [Tooltip("Logs footstep playback for validation.")]
    [SerializeField] private bool DebugLogs = false;

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
        if (LocomotionSource == null || FootstepAudioEvent == null || !LocomotionSource.IsLocomotionActive)
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
    /// Plays one footstep audio event at the configured origin.
    /// </summary>
    private void PlayFootstep()
    {
        Vector3 WorldPosition = FootstepOrigin != null ? FootstepOrigin.position : transform.position;
        GameAudio.PlayAt(FootstepAudioEvent, WorldPosition);
        LastStepTime = Time.unscaledTime;
        Log("Footstep played at " + WorldPosition);
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
