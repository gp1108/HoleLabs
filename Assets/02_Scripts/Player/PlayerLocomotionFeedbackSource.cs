using System;
using UnityEngine;

/// <summary>
/// Converts player motor state into stable locomotion feedback data for footsteps, viewmodel bob and similar systems.
/// This component uses real horizontal displacement and movement input together, so moving platforms, vertical elevators
/// and pushing against walls do not accidentally trigger walking feedback.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(200)]
public sealed class PlayerLocomotionFeedbackSource : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Player controller used to read grounded state, movement input and motor velocity.")]
    [SerializeField] private PlayerController PlayerController;

    [Header("Activation")]
    [Tooltip("Minimum movement input magnitude required before locomotion feedback can become active.")]
    [SerializeField] private float InputActivationThreshold = 0.1f;

    [Tooltip("Minimum real horizontal speed required before locomotion feedback can become active.")]
    [SerializeField] private float HorizontalSpeedActivationThreshold = 0.12f;

    [Tooltip("Horizontal speed treated as full locomotion intensity for bob and footsteps.")]
    [SerializeField] private float FullIntensityHorizontalSpeed = 5f;

    [Tooltip("If true, locomotion feedback is disabled while the player is not grounded.")]
    [SerializeField] private bool RequireGrounded = true;

    [Header("Smoothing")]
    [Tooltip("Speed used to smooth locomotion intensity in and out.")]
    [SerializeField] private float IntensitySmoothingSpeed = 12f;

    [Tooltip("Smallest delta time used when calculating real velocity from frame displacement.")]
    [SerializeField] private float MinimumDeltaTime = 0.0001f;

    [Header("Step Phase")]
    [Tooltip("Base phase frequency used by procedural bob. This is not the same as footstep interval.")]
    [SerializeField] private float BasePhaseFrequency = 1.8f;

    [Tooltip("Additional phase frequency multiplier applied at full locomotion intensity.")]
    [SerializeField] private float FullIntensityPhaseMultiplier = 1.25f;

    [Header("Debug")]
    [Tooltip("Logs locomotion state transitions for debugging.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Fired when the locomotion active state changes.
    /// </summary>
    public event Action<bool> OnLocomotionActiveChanged;

    /// <summary>
    /// Last sampled player world position.
    /// </summary>
    private Vector3 LastWorldPosition;

    /// <summary>
    /// Whether the last world position has been initialized.
    /// </summary>
    private bool HasLastWorldPosition;

    /// <summary>
    /// Current real horizontal velocity measured from transform displacement.
    /// </summary>
    public Vector3 RealHorizontalVelocity { get; private set; }

    /// <summary>
    /// Current real horizontal speed measured from transform displacement.
    /// </summary>
    public float RealHorizontalSpeed { get; private set; }

    /// <summary>
    /// Smoothed normalized locomotion intensity in the [0, 1] range.
    /// </summary>
    public float LocomotionIntensity { get; private set; }

    /// <summary>
    /// Raw target locomotion intensity before smoothing.
    /// </summary>
    public float TargetLocomotionIntensity { get; private set; }

    /// <summary>
    /// Continuous procedural step phase in cycles.
    /// </summary>
    public float StepPhase { get; private set; }

    /// <summary>
    /// Step phase wrapped into the [0, 1] range.
    /// </summary>
    public float StepCycle01 => Mathf.Repeat(StepPhase, 1f);

    /// <summary>
    /// Whether feedback should currently be considered active.
    /// </summary>
    public bool IsLocomotionActive { get; private set; }

    /// <summary>
    /// Resolves references and initializes the position cache.
    /// </summary>
    private void Awake()
    {
        if (PlayerController == null)
        {
            PlayerController = GetComponent<PlayerController>();
        }

        if (PlayerController == null)
        {
            PlayerController = GetComponentInParent<PlayerController>();
        }

        ResetPositionCache();
    }

    /// <summary>
    /// Resets transient motion state when the component becomes active.
    /// </summary>
    private void OnEnable()
    {
        ResetPositionCache();
        RealHorizontalVelocity = Vector3.zero;
        RealHorizontalSpeed = 0f;
        LocomotionIntensity = 0f;
        TargetLocomotionIntensity = 0f;
        IsLocomotionActive = false;
    }

    /// <summary>
    /// Samples player movement after the motor has updated.
    /// </summary>
    private void Update()
    {
        float DeltaTime = Mathf.Max(Time.deltaTime, MinimumDeltaTime);
        UpdateRealHorizontalVelocity(DeltaTime);
        UpdateIntensity(DeltaTime);
        UpdateStepPhase(DeltaTime);
    }

    /// <summary>
    /// Clears the cached position used to calculate frame velocity.
    /// </summary>
    public void ResetPositionCache()
    {
        LastWorldPosition = transform.position;
        HasLastWorldPosition = true;
    }

    /// <summary>
    /// Calculates horizontal velocity from actual transform displacement.
    /// </summary>
    /// <param name="DeltaTime">Frame delta time used for velocity calculation.</param>
    private void UpdateRealHorizontalVelocity(float DeltaTime)
    {
        Vector3 CurrentWorldPosition = transform.position;

        if (!HasLastWorldPosition)
        {
            LastWorldPosition = CurrentWorldPosition;
            HasLastWorldPosition = true;
            RealHorizontalVelocity = Vector3.zero;
            RealHorizontalSpeed = 0f;
            return;
        }

        Vector3 Delta = CurrentWorldPosition - LastWorldPosition;
        Delta.y = 0f;

        RealHorizontalVelocity = Delta / DeltaTime;
        RealHorizontalSpeed = RealHorizontalVelocity.magnitude;
        LastWorldPosition = CurrentWorldPosition;
    }

    /// <summary>
    /// Updates the smoothed locomotion intensity from input, grounded state and real movement.
    /// </summary>
    /// <param name="DeltaTime">Current frame delta time.</param>
    private void UpdateIntensity(float DeltaTime)
    {
        bool WasLocomotionActive = IsLocomotionActive;
        Vector2 MoveInput = PlayerController != null ? PlayerController.MoveInput : Vector2.zero;
        bool HasMoveInput = MoveInput.magnitude >= InputActivationThreshold;
        bool HasRealHorizontalMotion = RealHorizontalSpeed >= HorizontalSpeedActivationThreshold;
        bool HasRequiredGrounding = !RequireGrounded || PlayerController == null || PlayerController.IsGrounded;

        bool ShouldBeActive = HasRequiredGrounding && HasMoveInput && HasRealHorizontalMotion;
        TargetLocomotionIntensity = ShouldBeActive
            ? Mathf.InverseLerp(HorizontalSpeedActivationThreshold, Mathf.Max(HorizontalSpeedActivationThreshold + 0.01f, FullIntensityHorizontalSpeed), RealHorizontalSpeed)
            : 0f;

        LocomotionIntensity = Mathf.MoveTowards(
            LocomotionIntensity,
            TargetLocomotionIntensity,
            Mathf.Max(0.01f, IntensitySmoothingSpeed) * DeltaTime);

        IsLocomotionActive = LocomotionIntensity > 0.01f && ShouldBeActive;

        if (WasLocomotionActive != IsLocomotionActive)
        {
            OnLocomotionActiveChanged?.Invoke(IsLocomotionActive);
            Log("Locomotion active changed: " + IsLocomotionActive);
        }
    }

    /// <summary>
    /// Advances the shared procedural locomotion phase while the player is actually moving.
    /// </summary>
    /// <param name="DeltaTime">Current frame delta time.</param>
    private void UpdateStepPhase(float DeltaTime)
    {
        if (LocomotionIntensity <= 0.001f)
        {
            return;
        }

        float PhaseFrequency = Mathf.Max(0f, BasePhaseFrequency) * Mathf.Lerp(1f, Mathf.Max(0.01f, FullIntensityPhaseMultiplier), LocomotionIntensity);
        StepPhase += PhaseFrequency * LocomotionIntensity * DeltaTime;
    }

    /// <summary>
    /// Logs locomotion source messages when debug logging is enabled.
    /// </summary>
    /// <param name="Message">Message to log.</param>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[PlayerLocomotionFeedbackSource] " + Message, this);
    }
}
