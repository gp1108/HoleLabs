using UnityEngine;

/// <summary>
/// Adds procedural walking motion to an equipped item without touching the gameplay animation state.
/// Place this component on the equipped prefab and assign a non-animated motion root whenever possible.
/// The motion automatically returns to neutral while the item is using primary or secondary actions.
/// </summary>
[DisallowMultipleComponent]
public sealed class EquippedItemViewMotion : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Motion source on the player. If empty, the component searches in parents and then in the scene.")]
    [SerializeField] private PlayerLocomotionFeedbackSource LocomotionSource;

    [Tooltip("Equipped item behaviour used to suppress bob while the item is playing use animations.")]
    [SerializeField] private EquippedItemBehaviour EquippedItemBehaviour;

    [Tooltip("Transform moved by procedural bob. Use a stable non-animated pivot above the visual Animator root.")]
    [SerializeField] private Transform MotionRoot;

    [Header("Position Bob")]
    [Tooltip("Maximum local position offset applied at full locomotion intensity.")]
    [SerializeField] private Vector3 PositionAmplitude = new Vector3(0.025f, 0.035f, 0.012f);

    [Tooltip("Additional multiplier applied to all position offsets.")]
    [SerializeField] private float PositionMultiplier = 1f;

    [Header("Rotation Bob")]
    [Tooltip("Maximum local rotation offset in degrees applied at full locomotion intensity.")]
    [SerializeField] private Vector3 RotationAmplitude = new Vector3(1.25f, 0.65f, 1.65f);

    [Tooltip("Additional multiplier applied to all rotation offsets.")]
    [SerializeField] private float RotationMultiplier = 1f;

    [Header("Response")]
    [Tooltip("Speed used to follow procedural target offsets while locomotion is active.")]
    [SerializeField] private float FollowSpeed = 14f;

    [Tooltip("Speed used to return the item to its neutral pose when locomotion or item usage blocks bob.")]
    [SerializeField] private float ReturnSpeed = 18f;

    [Tooltip("If true, procedural motion is suppressed whenever the equipped item reports active usage.")]
    [SerializeField] private bool SuppressWhileItemIsUsing = true;

    [Header("Debug")]
    [Tooltip("Logs missing references and suppression state changes.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Neutral local position captured from the configured motion root.
    /// </summary>
    private Vector3 NeutralLocalPosition;

    /// <summary>
    /// Neutral local rotation captured from the configured motion root.
    /// </summary>
    private Quaternion NeutralLocalRotation;

    /// <summary>
    /// Whether the neutral pose has been captured.
    /// </summary>
    private bool HasCapturedNeutralPose;

    /// <summary>
    /// Last known suppression state used for debug logs.
    /// </summary>
    private bool WasSuppressedLastFrame;

    /// <summary>
    /// Resolves references and captures the neutral pose.
    /// </summary>
    private void Awake()
    {
        ResolveReferences();
        CaptureNeutralPose();
    }

    /// <summary>
    /// Recaptures neutral pose when the equipped prefab becomes active.
    /// </summary>
    private void OnEnable()
    {
        ResolveReferences();
        CaptureNeutralPose();
        WasSuppressedLastFrame = GetIsSuppressedByItemUsage();
    }

    /// <summary>
    /// Applies procedural view motion after regular Update-driven systems.
    /// </summary>
    private void LateUpdate()
    {
        if (MotionRoot == null || !HasCapturedNeutralPose)
        {
            return;
        }

        bool IsSuppressed = GetIsSuppressedByItemUsage();
        float TargetIntensity = ResolveTargetIntensity(IsSuppressed);
        Vector3 TargetLocalPosition = NeutralLocalPosition + CalculatePositionOffset(TargetIntensity);
        Quaternion TargetLocalRotation = NeutralLocalRotation * Quaternion.Euler(CalculateRotationOffset(TargetIntensity));
        float SelectedSpeed = IsSuppressed || TargetIntensity <= 0.001f ? ReturnSpeed : FollowSpeed;
        float LerpFactor = 1f - Mathf.Exp(-Mathf.Max(0.01f, SelectedSpeed) * Time.deltaTime);

        MotionRoot.localPosition = Vector3.Lerp(MotionRoot.localPosition, TargetLocalPosition, LerpFactor);
        MotionRoot.localRotation = Quaternion.Slerp(MotionRoot.localRotation, TargetLocalRotation, LerpFactor);

        if (WasSuppressedLastFrame != IsSuppressed)
        {
            WasSuppressedLastFrame = IsSuppressed;
            Log("View motion suppression changed: " + IsSuppressed);
        }
    }

    /// <summary>
    /// Resolves missing references from the equipped item hierarchy and scene.
    /// </summary>
    private void ResolveReferences()
    {
        if (MotionRoot == null)
        {
            MotionRoot = transform;
        }

        if (EquippedItemBehaviour == null)
        {
            EquippedItemBehaviour = GetComponentInParent<EquippedItemBehaviour>();
        }

        if (EquippedItemBehaviour == null)
        {
            EquippedItemBehaviour = GetComponentInChildren<EquippedItemBehaviour>(true);
        }

        if (LocomotionSource == null)
        {
            LocomotionSource = GetComponentInParent<PlayerLocomotionFeedbackSource>();
        }

        if (LocomotionSource == null)
        {
            LocomotionSource = FindFirstObjectByType<PlayerLocomotionFeedbackSource>();
        }
    }

    /// <summary>
    /// Captures the neutral local pose of the motion root.
    /// </summary>
    public void CaptureNeutralPose()
    {
        if (MotionRoot == null)
        {
            return;
        }

        NeutralLocalPosition = MotionRoot.localPosition;
        NeutralLocalRotation = MotionRoot.localRotation;
        HasCapturedNeutralPose = true;
    }

    /// <summary>
    /// Gets whether item usage currently suppresses procedural motion.
    /// </summary>
    /// <returns>True when the equipped item is using an action and suppression is enabled.</returns>
    private bool GetIsSuppressedByItemUsage()
    {
        return SuppressWhileItemIsUsing && EquippedItemBehaviour != null && EquippedItemBehaviour.ShouldBlockProceduralViewMotion();
    }

    /// <summary>
    /// Resolves the intensity used by this frame's procedural motion.
    /// </summary>
    /// <param name="IsSuppressed">Whether item usage is suppressing motion.</param>
    /// <returns>Normalized intensity in the [0, 1] range.</returns>
    private float ResolveTargetIntensity(bool IsSuppressed)
    {
        if (IsSuppressed || LocomotionSource == null)
        {
            return 0f;
        }

        return Mathf.Clamp01(LocomotionSource.LocomotionIntensity);
    }

    /// <summary>
    /// Calculates the local position offset for the current locomotion phase.
    /// </summary>
    /// <param name="Intensity">Normalized locomotion intensity.</param>
    /// <returns>Local position offset.</returns>
    private Vector3 CalculatePositionOffset(float Intensity)
    {
        if (LocomotionSource == null || Intensity <= 0.001f)
        {
            return Vector3.zero;
        }

        float Phase = LocomotionSource.StepCycle01 * Mathf.PI * 2f;
        float Horizontal = Mathf.Sin(Phase);
        float Vertical = 1f - Mathf.Cos(Phase * 2f);
        float Forward = Mathf.Cos(Phase);

        return new Vector3(
            Horizontal * PositionAmplitude.x,
            Vertical * 0.5f * PositionAmplitude.y,
            Forward * PositionAmplitude.z) * Mathf.Max(0f, PositionMultiplier) * Intensity;
    }

    /// <summary>
    /// Calculates the local rotation offset for the current locomotion phase.
    /// </summary>
    /// <param name="Intensity">Normalized locomotion intensity.</param>
    /// <returns>Euler rotation offset in degrees.</returns>
    private Vector3 CalculateRotationOffset(float Intensity)
    {
        if (LocomotionSource == null || Intensity <= 0.001f)
        {
            return Vector3.zero;
        }

        float Phase = LocomotionSource.StepCycle01 * Mathf.PI * 2f;
        float Pitch = Mathf.Cos(Phase * 2f) * RotationAmplitude.x;
        float Yaw = Mathf.Sin(Phase) * RotationAmplitude.y;
        float Roll = -Mathf.Sin(Phase) * RotationAmplitude.z;

        return new Vector3(Pitch, Yaw, Roll) * Mathf.Max(0f, RotationMultiplier) * Intensity;
    }

    /// <summary>
    /// Logs view motion messages when debug logging is enabled.
    /// </summary>
    /// <param name="Message">Message to log.</param>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[EquippedItemViewMotion] " + Message, this);
    }
}
