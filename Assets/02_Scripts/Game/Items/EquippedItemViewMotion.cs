using UnityEngine;

/// <summary>
/// Adds procedural motion to an equipped item without touching the gameplay animation state.
/// This component can apply walking bob and horizontal look sway to a stable non-animated motion root.
/// Procedural motion automatically returns to neutral while the item is using primary or secondary actions.
/// </summary>
[DisallowMultipleComponent]
public sealed class EquippedItemViewMotion : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Motion source on the player. If empty, the component searches in parents and then in the scene.")]
    [SerializeField] private PlayerLocomotionFeedbackSource LocomotionSource;

    [Tooltip("Equipped item behaviour used to suppress procedural motion while the item is playing use animations.")]
    [SerializeField] private EquippedItemBehaviour EquippedItemBehaviour;

    [Tooltip("Transform moved by procedural motion. Use a stable non-animated pivot above the visual Animator root.")]
    [SerializeField] private Transform MotionRoot;

    [Tooltip("Transform used to measure horizontal camera yaw for look sway. If empty, the component tries to resolve the gameplay camera.")]
    [SerializeField] private Transform LookSourceTransform;

    [Header("Bob Activation")]
    [Tooltip("If true, walking bob is applied while the player has real grounded horizontal movement.")]
    [SerializeField] private bool EnableBob = true;

    [Tooltip("If true, bob amplitude is multiplied by the locomotion source stance multiplier, making sprint stronger and crouch softer.")]
    [SerializeField] private bool UseStanceMultiplierForBob = true;

    [Tooltip("Additional global multiplier applied to walking bob intensity after locomotion and stance have been evaluated.")]
    [SerializeField] private float BobIntensityMultiplier = 1f;

    [Header("Position Bob")]
    [Tooltip("Maximum local position offset applied at full locomotion intensity.")]
    [SerializeField] private Vector3 PositionAmplitude = new Vector3(0.045f, 0.055f, 0.02f);

    [Tooltip("Additional multiplier applied to all position bob offsets.")]
    [SerializeField] private float PositionMultiplier = 1f;

    [Header("Rotation Bob")]
    [Tooltip("Maximum local rotation offset in degrees applied at full locomotion intensity.")]
    [SerializeField] private Vector3 RotationAmplitude = new Vector3(1.75f, 0.85f, 2.25f);

    [Tooltip("Additional multiplier applied to all rotation bob offsets.")]
    [SerializeField] private float RotationMultiplier = 1f;

    [Header("Look Sway Activation")]
    [Tooltip("If true, the equipped item subtly leans when the player rotates the camera horizontally.")]
    [SerializeField] private bool EnableLookSway = true;

    [Tooltip("If true, look sway is suppressed whenever the equipped item reports active usage.")]
    [SerializeField] private bool SuppressLookSwayWhileItemIsUsing = true;

    [Tooltip("If true, the horizontal look sway input is inverted before being applied to position and rotation offsets.")]
    [SerializeField] private bool InvertHorizontalLookSway = false;

    [Header("Look Sway")]
    [Tooltip("Local position offset applied when horizontal look sway reaches full strength. Negative X usually makes the item lag behind camera turns.")]
    [SerializeField] private Vector3 LookSwayPositionAmplitude = new Vector3(-0.025f, 0f, 0f);

    [Tooltip("Local rotation offset in degrees applied when horizontal look sway reaches full strength. Z controls the visible hand/tool lean.")]
    [SerializeField] private Vector3 LookSwayRotationAmplitude = new Vector3(0f, -0.75f, 3f);

    [Tooltip("Sensitivity used to convert horizontal camera yaw speed into normalized sway. Lower values make sway subtler.")]
    [SerializeField] private float LookSwaySensitivity = 0.0075f;

    [Tooltip("Maximum absolute normalized sway value after sensitivity is applied.")]
    [SerializeField] private float MaxLookSway = 1f;

    [Tooltip("Speed used to follow camera yaw changes while look sway is active.")]
    [SerializeField] private float LookSwayFollowSpeed = 12f;

    [Tooltip("Speed used to return look sway to neutral when the camera stops moving or item usage suppresses sway.")]
    [SerializeField] private float LookSwayReturnSpeed = 18f;

    [Header("Response")]
    [Tooltip("Speed used to follow procedural target offsets while locomotion is active.")]
    [SerializeField] private float FollowSpeed = 12f;

    [Tooltip("Speed used to return the item to its neutral pose when locomotion or item usage blocks bob.")]
    [SerializeField] private float ReturnSpeed = 18f;

    [Tooltip("If true, bob is suppressed whenever the equipped item reports active usage.")]
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
    /// Last known horizontal yaw value used to calculate look sway velocity.
    /// </summary>
    private float LastLookYaw;

    /// <summary>
    /// Whether the look yaw cache has been initialized.
    /// </summary>
    private bool HasLastLookYaw;

    /// <summary>
    /// Current smoothed horizontal look sway value in the [-1, 1] range.
    /// </summary>
    private float CurrentHorizontalLookSway;

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
        ResetLookSwayCache();
    }

    /// <summary>
    /// Recaptures neutral pose when the equipped prefab becomes active.
    /// </summary>
    private void OnEnable()
    {
        ResolveReferences();
        CaptureNeutralPose();
        ResetLookSwayCache();
        CurrentHorizontalLookSway = 0f;
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

        float DeltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
        bool IsSuppressed = GetIsSuppressedByItemUsage();
        float TargetBobIntensity = ResolveTargetBobIntensity(IsSuppressed);
        UpdateLookSway(DeltaTime, IsSuppressed);

        Vector3 TargetLocalPosition = NeutralLocalPosition + CalculatePositionBobOffset(TargetBobIntensity) + CalculateLookSwayPositionOffset();
        Quaternion TargetLocalRotation = NeutralLocalRotation * Quaternion.Euler(CalculateRotationBobOffset(TargetBobIntensity) + CalculateLookSwayRotationOffset());
        float SelectedSpeed = IsSuppressed || TargetBobIntensity <= 0.001f ? ReturnSpeed : FollowSpeed;
        float LerpFactor = 1f - Mathf.Exp(-Mathf.Max(0.01f, SelectedSpeed) * DeltaTime);

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

        if (LookSourceTransform == null)
        {
            PlayerController PlayerController = EquippedItemBehaviour != null
                ? EquippedItemBehaviour.GetComponentInParent<PlayerController>()
                : null;

            if (PlayerController == null && LocomotionSource != null)
            {
                PlayerController = LocomotionSource.GetComponent<PlayerController>();
            }

            if (PlayerController != null && PlayerController.PlayerCamera != null)
            {
                LookSourceTransform = PlayerController.PlayerCamera.transform;
            }
        }

        if (LookSourceTransform == null && Camera.main != null)
        {
            LookSourceTransform = Camera.main.transform;
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
    /// Resets the horizontal camera yaw cache used by look sway.
    /// </summary>
    public void ResetLookSwayCache()
    {
        if (LookSourceTransform == null)
        {
            HasLastLookYaw = false;
            return;
        }

        LastLookYaw = LookSourceTransform.eulerAngles.y;
        HasLastLookYaw = true;
    }

    /// <summary>
    /// Gets whether item usage currently suppresses procedural motion.
    /// </summary>
    /// <returns>True when the equipped item is using an action and suppression is enabled.</returns>
    private bool GetIsSuppressedByItemUsage()
    {
        return EquippedItemBehaviour != null && EquippedItemBehaviour.ShouldBlockProceduralViewMotion();
    }

    /// <summary>
    /// Resolves the bob intensity used by this frame's procedural motion.
    /// </summary>
    /// <param name="IsSuppressed">Whether item usage is suppressing item motion.</param>
    /// <returns>Normalized intensity in the [0, +inf] range.</returns>
    private float ResolveTargetBobIntensity(bool IsSuppressed)
    {
        if (!EnableBob || IsSuppressed && SuppressWhileItemIsUsing || LocomotionSource == null)
        {
            return 0f;
        }

        float Intensity = Mathf.Clamp01(LocomotionSource.LocomotionIntensity) * Mathf.Max(0f, BobIntensityMultiplier);

        if (UseStanceMultiplierForBob)
        {
            Intensity *= Mathf.Max(0f, LocomotionSource.StanceFeedbackMultiplier);
        }

        return Intensity;
    }

    /// <summary>
    /// Updates the current smoothed look sway from horizontal camera yaw velocity.
    /// </summary>
    /// <param name="DeltaTime">Frame delta time.</param>
    /// <param name="IsSuppressed">Whether item usage is suppressing item motion.</param>
    private void UpdateLookSway(float DeltaTime, bool IsSuppressed)
    {
        if (!EnableLookSway || LookSourceTransform == null || IsSuppressed && SuppressLookSwayWhileItemIsUsing)
        {
            CurrentHorizontalLookSway = MoveSwayTowards(CurrentHorizontalLookSway, 0f, LookSwayReturnSpeed, DeltaTime);
            ResetLookSwayCache();
            return;
        }

        if (!HasLastLookYaw)
        {
            ResetLookSwayCache();
            CurrentHorizontalLookSway = MoveSwayTowards(CurrentHorizontalLookSway, 0f, LookSwayReturnSpeed, DeltaTime);
            return;
        }

        float CurrentYaw = LookSourceTransform.eulerAngles.y;
        float YawDelta = Mathf.DeltaAngle(LastLookYaw, CurrentYaw);
        float YawVelocity = YawDelta / DeltaTime;
        LastLookYaw = CurrentYaw;

        float DirectionMultiplier = InvertHorizontalLookSway ? -1f : 1f;
        float TargetSway = Mathf.Clamp(YawVelocity * Mathf.Max(0f, LookSwaySensitivity) * DirectionMultiplier, -Mathf.Max(0f, MaxLookSway), Mathf.Max(0f, MaxLookSway));
        float SelectedSpeed = Mathf.Abs(TargetSway) > 0.001f ? LookSwayFollowSpeed : LookSwayReturnSpeed;
        CurrentHorizontalLookSway = MoveSwayTowards(CurrentHorizontalLookSway, TargetSway, SelectedSpeed, DeltaTime);
    }

    /// <summary>
    /// Moves a sway value towards its target using exponential smoothing.
    /// </summary>
    /// <param name="Current">Current sway value.</param>
    /// <param name="Target">Target sway value.</param>
    /// <param name="Speed">Smoothing speed.</param>
    /// <param name="DeltaTime">Frame delta time.</param>
    /// <returns>Smoothed sway value.</returns>
    private static float MoveSwayTowards(float Current, float Target, float Speed, float DeltaTime)
    {
        float LerpFactor = 1f - Mathf.Exp(-Mathf.Max(0.01f, Speed) * DeltaTime);
        return Mathf.Lerp(Current, Target, LerpFactor);
    }

    /// <summary>
    /// Calculates the local position offset for the current locomotion phase.
    /// </summary>
    /// <param name="Intensity">Normalized locomotion intensity.</param>
    /// <returns>Local position offset.</returns>
    private Vector3 CalculatePositionBobOffset(float Intensity)
    {
        if (!EnableBob || LocomotionSource == null || Intensity <= 0.001f)
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
    private Vector3 CalculateRotationBobOffset(float Intensity)
    {
        if (!EnableBob || LocomotionSource == null || Intensity <= 0.001f)
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
    /// Calculates the local position offset caused by horizontal camera look sway.
    /// </summary>
    /// <returns>Local position offset.</returns>
    private Vector3 CalculateLookSwayPositionOffset()
    {
        if (!EnableLookSway || Mathf.Abs(CurrentHorizontalLookSway) <= 0.001f)
        {
            return Vector3.zero;
        }

        return LookSwayPositionAmplitude * CurrentHorizontalLookSway;
    }

    /// <summary>
    /// Calculates the local rotation offset caused by horizontal camera look sway.
    /// </summary>
    /// <returns>Euler rotation offset in degrees.</returns>
    private Vector3 CalculateLookSwayRotationOffset()
    {
        if (!EnableLookSway || Mathf.Abs(CurrentHorizontalLookSway) <= 0.001f)
        {
            return Vector3.zero;
        }

        return LookSwayRotationAmplitude * CurrentHorizontalLookSway;
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
