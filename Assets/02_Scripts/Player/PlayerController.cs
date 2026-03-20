using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// CharacterController based first person motor tailored for projects that mix player locomotion
/// with physically simulated world objects.
/// 
/// Design goals:
/// - Keep the player decoupled from Rigidbody impulse resolution.
/// - Preserve a stable hierarchy based on ViewRoot for yaw and CameraPivot for pitch/crouch.
/// - Support jumping, sprinting, crouching and slope handling.
/// - Follow moving elevators and rotating platforms without visible jitter by inheriting the
///   support transform delta instead of sampling Rigidbody velocity in Update.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-50)]
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerInput))]
public sealed class PlayerController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Transform that rotates around Yaw. Usually the visual root that contains the camera pivot.")]
    [SerializeField] private Transform ViewRoot;

    [Tooltip("Transform used for camera pitch and crouch height offsets.")]
    [SerializeField] private Transform CameraPivot;

    [Tooltip("Main gameplay camera used by interaction and equipped items.")]
    [SerializeField] private Camera PlayerCameraComponent;

    [Tooltip("Optional transform used by held/view model tools. Kept for compatibility with tool systems.")]
    [SerializeField] private Transform ViewModelContainerTransform;

    [Header("Input Actions")]
    [Tooltip("Name of the move action in the PlayerInput asset.")]
    [SerializeField] private string MoveActionName = "Move";

    [Tooltip("Name of the look action in the PlayerInput asset.")]
    [SerializeField] private string LookActionName = "Look";

    [Tooltip("Name of the jump action in the PlayerInput asset.")]
    [SerializeField] private string JumpActionName = "Jump";

    [Tooltip("Name of the sprint action in the PlayerInput asset.")]
    [SerializeField] private string SprintActionName = "Sprint";

    [Tooltip("Name of the crouch action in the PlayerInput asset.")]
    [SerializeField] private string CrouchActionName = "Crouch";

    [Header("Look")]
    [Tooltip("Sensitivity multiplier applied to mouse look input.")]
    [SerializeField] private float LookSensitivity = 0.12f;

    [Tooltip("Degrees per second applied to gamepad look input.")]
    [SerializeField] private float GamepadLookSpeed = 140f;

    [Tooltip("Minimum allowed pitch angle.")]
    [SerializeField] private float MinPitch = -85f;

    [Tooltip("Maximum allowed pitch angle.")]
    [SerializeField] private float MaxPitch = 85f;

    [Tooltip("If true, vertical look input is inverted.")]
    [SerializeField] private bool InvertY = false;

    [Header("Movement")]
    [Tooltip("Maximum planar speed while walking.")]
    [SerializeField] private float WalkSpeed = 5f;

    [Tooltip("Maximum planar speed while sprinting.")]
    [SerializeField] private float SprintSpeed = 7.5f;

    [Tooltip("Maximum planar speed while crouching.")]
    [SerializeField] private float CrouchSpeed = 2.75f;

    [Tooltip("Acceleration used while grounded.")]
    [SerializeField] private float GroundAcceleration = 40f;

    [Tooltip("Acceleration used while airborne.")]
    [SerializeField] private float AirAcceleration = 10f;

    [Tooltip("Planar braking used while grounded and there is no move input.")]
    [SerializeField] private float GroundFriction = 18f;

    [Tooltip("Additional downhill acceleration applied on non-walkable slopes.")]
    [SerializeField] private float SteepSlideGravity = 18f;

    [Header("Jump")]
    [Tooltip("Desired jump height in meters.")]
    [SerializeField] private float JumpHeight = 1.35f;

    [Tooltip("Gravity acceleration magnitude applied every frame.")]
    [SerializeField] private float Gravity = 25f;

    [Tooltip("Small downward speed used to keep ground contact stable.")]
    [SerializeField] private float GroundSnapSpeed = 4f;

    [Tooltip("Grace time after leaving the ground during which jump is still allowed.")]
    [SerializeField] private float CoyoteTime = 0.1f;

    [Tooltip("Grace time that stores a jump input shortly before landing.")]
    [SerializeField] private float JumpBufferTime = 0.12f;

    [Tooltip("Time during which ground snapping is ignored immediately after starting a jump.")]
    [SerializeField] private float JumpGroundIgnoreTime = 0.12f;

    [Header("Crouch")]
    [Tooltip("If true, crouch toggles on press. Otherwise it behaves as hold-to-crouch.")]
    [SerializeField] private bool ToggleCrouch = false;

    [Tooltip("CharacterController height while standing.")]
    [SerializeField] private float StandingHeight = 2f;

    [Tooltip("CharacterController height while crouching.")]
    [SerializeField] private float CrouchingHeight = 1.2f;

    [Tooltip("Interpolation speed used when changing capsule height and camera pivot height.")]
    [SerializeField] private float CrouchTransitionSpeed = 12f;

    [Tooltip("Local Y offset applied to the camera pivot while crouching.")]
    [SerializeField] private float CrouchCameraOffset = -0.35f;

    [Header("Grounding")]
    [Tooltip("Layers considered valid ground for probing and standing checks.")]
    [SerializeField] private LayerMask GroundLayers = ~0;

    [Tooltip("Extra distance checked below the controller for stable grounding.")]
    [SerializeField] private float GroundProbeDistance = 0.2f;

    [Tooltip("Padding removed from the probe radius to reduce wall interference.")]
    [SerializeField] private float GroundProbeRadiusPadding = 0.03f;

    [Tooltip("Maximum walkable ground angle.")]
    [Range(0f, 89f)]
    [SerializeField] private float MaxStableGroundAngle = 55f;

    [Header("Moving Platforms")]
    [Tooltip("If true, translation from the current support transform is inherited while grounded.")]
    [SerializeField] private bool InheritSupportTranslation = true;

    [Tooltip("If true, yaw rotation from the current support transform is inherited while grounded.")]
    [SerializeField] private bool InheritSupportYaw = true;

    [Tooltip("Minimum downward dot product required to keep a support while grounded.")]
    [SerializeField] private float MinimumSupportUpDot = 0.2f;

    [Header("Debug")]
    [Tooltip("Draws the ground probe and support contact information in the Scene view.")]
    [SerializeField] private bool DrawDebug = false;

    [Tooltip("Fixed local Y used by the camera pivot while standing. Set a negative value to keep the scene value.")]
    [SerializeField] private float StandingCameraPivotLocalY = 1.8f;

    [Tooltip("Extra safety margin used when checking if the player can stand up.")]
    [SerializeField] private float StandUpCheckPadding = 0.02f;

    /// <summary>
    /// Current raw movement input.
    /// </summary>
    public Vector2 MoveInput { get; private set; }

    /// <summary>
    /// Current estimated world velocity applied by the motor.
    /// </summary>
    public Vector3 Velocity { get; private set; }

    /// <summary>
    /// Whether the player is currently stably grounded.
    /// </summary>
    public bool IsGrounded { get; private set; }

    /// <summary>
    /// Whether the player is currently crouching.
    /// </summary>
    public bool IsCrouching { get; private set; }

    /// <summary>
    /// Public camera accessor kept for tool compatibility.
    /// </summary>
    public Camera PlayerCamera => PlayerCameraComponent;

    /// <summary>
    /// Public container accessor kept for tool compatibility.
    /// </summary>
    public Transform ViewModelContainer => ViewModelContainerTransform;

    private CharacterController CharacterController;
    private PlayerInput PlayerInput;

    private InputAction MoveAction;
    private InputAction LookAction;
    private InputAction JumpAction;
    private InputAction SprintAction;
    private InputAction CrouchAction;

    /// <summary>
    /// Current planar velocity in world space, excluding support delta.
    /// </summary>
    private Vector3 PlanarVelocity;

    /// <summary>
    /// Current vertical velocity in meters per second.
    /// </summary>
    private float VerticalVelocity;

    /// <summary>
    /// Current pitch angle in degrees.
    /// </summary>
    private float PitchDegrees;

    /// <summary>
    /// Remaining buffered jump time.
    /// </summary>
    private float JumpBufferCounter;

    /// <summary>
    /// Remaining coyote time.
    /// </summary>
    private float CoyoteCounter;

    /// <summary>
    /// Remaining time during which stable grounding is ignored after jumping.
    /// </summary>
    private float JumpGroundIgnoreCounter;

    /// <summary>
    /// Whether crouch is being requested by input.
    /// </summary>
    private bool WantsToCrouch;

    /// <summary>
    /// Cached local camera pivot position when standing.
    /// </summary>
    private Vector3 CameraPivotStandingLocalPosition;

    /// <summary>
    /// Default step offset used when grounded.
    /// </summary>
    private float DefaultStepOffset;

    /// <summary>
    /// Cached colliders from the full player hierarchy used to ignore self hits.
    /// </summary>
    private Collider[] OwnColliders;

    /// <summary>
    /// Latest valid ground hit.
    /// </summary>
    private RaycastHit GroundHit;

    /// <summary>
    /// Whether the current frame found any ground hit.
    /// </summary>
    private bool HasGroundHit;

    /// <summary>
    /// Current support transform used for moving platform inheritance.
    /// </summary>
    private Transform CurrentSupportTransform;

    /// <summary>
    /// Current support rigidbody used for optional diagnostics and future extensions.
    /// </summary>
    private Rigidbody CurrentSupportRigidbody;

    /// <summary>
    /// Cached support local point corresponding to the player's world position.
    /// </summary>
    private Vector3 SupportLocalPoint;

    /// <summary>
    /// Cached support world point from the previous frame.
    /// </summary>
    private Vector3 LastSupportWorldPoint;

    /// <summary>
    /// Cached support rotation from the previous frame.
    /// </summary>
    private Quaternion LastSupportRotation;

    /// <summary>
    /// Whether support tracking data is currently valid.
    /// </summary>
    private bool HasTrackedSupport;

    /// <summary>
    /// Initializes references, input actions and controller defaults.
    /// </summary>
    private void Awake()
    {
        CharacterController = GetComponent<CharacterController>();
        PlayerInput = GetComponent<PlayerInput>();
        OwnColliders = GetComponentsInChildren<Collider>(true);

        if (ViewRoot == null)
        {
            ViewRoot = transform;
        }

        if (PlayerCameraComponent == null)
        {
            PlayerCameraComponent = GetComponentInChildren<Camera>(true);
        }

        if (CameraPivot == null && PlayerCameraComponent != null)
        {
            CameraPivot = PlayerCameraComponent.transform.parent;
        }

        if (CameraPivot == null)
        {
            CameraPivot = ViewRoot;
        }

        if (PlayerInput == null || PlayerInput.actions == null)
        {
            Debug.LogError("PlayerController requires a PlayerInput with a valid Input Actions asset.");
            enabled = false;
            return;
        }

        MoveAction = PlayerInput.actions[MoveActionName];
        LookAction = PlayerInput.actions[LookActionName];
        JumpAction = PlayerInput.actions[JumpActionName];
        SprintAction = PlayerInput.actions[SprintActionName];
        CrouchAction = PlayerInput.actions[CrouchActionName];

        if (MoveAction == null || LookAction == null || JumpAction == null || SprintAction == null || CrouchAction == null)
        {
            Debug.LogError("PlayerController could not resolve one or more configured input actions.");
            enabled = false;
            return;
        }

        DefaultStepOffset = CharacterController.stepOffset;
        CameraPivotStandingLocalPosition = CameraPivot.localPosition;

        if (StandingCameraPivotLocalY >= 0f)
        {
            CameraPivotStandingLocalPosition = new Vector3(
                CameraPivotStandingLocalPosition.x,
                StandingCameraPivotLocalY,
                CameraPivotStandingLocalPosition.z);

            CameraPivot.localPosition = CameraPivotStandingLocalPosition;
        }

        float MinimumHeight = CharacterController.radius * 2f + CharacterController.skinWidth + 0.01f;
        StandingHeight = Mathf.Max(StandingHeight, MinimumHeight);
        CrouchingHeight = Mathf.Clamp(CrouchingHeight, MinimumHeight, StandingHeight);

        ApplyControllerHeightImmediate(StandingHeight);
    }

    /// <summary>
    /// Initializes pitch from the current pivot transform.
    /// </summary>
    private void Start()
    {
        PitchDegrees = NormalizeAngle(CameraPivot.localEulerAngles.x);
        PitchDegrees = Mathf.Clamp(PitchDegrees, MinPitch, MaxPitch);
        ApplyPitchRotation();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    /// <summary>
    /// Updates look, crouch, locomotion and support following.
    /// </summary>
    private void Update()
    {
        float DeltaTime = Time.deltaTime;

        ReadInput();
        HandleLook(DeltaTime);

        JumpBufferCounter -= DeltaTime;
        CoyoteCounter -= DeltaTime;
        JumpGroundIgnoreCounter -= DeltaTime;

        ApplySupportMotionBeforeLocomotion();
        UpdateGroundState();
        HandleCrouch(DeltaTime);
        HandleMovement(DeltaTime);
        UpdateGroundState();
        CacheSupportTracking();
    }

    /// <summary>
    /// Reads the latest input values from the input action asset.
    /// </summary>
    private void ReadInput()
    {
        MoveInput = MoveAction.ReadValue<Vector2>();

        if (JumpAction.WasPressedThisFrame() || JumpAction.triggered)
        {
            JumpBufferCounter = JumpBufferTime;
        }

        if (ToggleCrouch)
        {
            if (CrouchAction.WasPressedThisFrame() || CrouchAction.triggered)
            {
                WantsToCrouch = !WantsToCrouch;
            }
        }
        else
        {
            WantsToCrouch = CrouchAction.IsPressed() || CrouchAction.ReadValue<float>() > 0.5f;
        }
    }

    /// <summary>
    /// Applies yaw and pitch using the configured hierarchy.
    /// </summary>
    private void HandleLook(float DeltaTime)
    {
        Vector2 CurrentLookInput = LookAction.ReadValue<Vector2>();

        bool IsGamepadScheme = PlayerInput.currentControlScheme != null && PlayerInput.currentControlScheme.Contains("Gamepad");
        float DeltaYaw = IsGamepadScheme ? CurrentLookInput.x * GamepadLookSpeed * DeltaTime : CurrentLookInput.x * LookSensitivity;
        float DeltaPitch = IsGamepadScheme ? CurrentLookInput.y * GamepadLookSpeed * DeltaTime : CurrentLookInput.y * LookSensitivity;

        if (InvertY)
        {
            DeltaPitch = -DeltaPitch;
        }

        ViewRoot.Rotate(Vector3.up, DeltaYaw, Space.World);

        PitchDegrees = Mathf.Clamp(PitchDegrees - DeltaPitch, MinPitch, MaxPitch);
        ApplyPitchRotation();
    }

    /// <summary>
    /// Applies the cached pitch angle to the camera pivot.
    /// </summary>
    private void ApplyPitchRotation()
    {
        Vector3 LocalEulerAngles = CameraPivot.localEulerAngles;
        LocalEulerAngles.x = PitchDegrees;
        LocalEulerAngles.y = 0f;
        LocalEulerAngles.z = 0f;
        CameraPivot.localEulerAngles = LocalEulerAngles;
    }

    /// <summary>
    /// Follows the tracked support transform using real transform delta instead of Rigidbody velocity.
    /// This removes the visible jitter that appears when a CharacterController samples a kinematic
    /// elevator moved in FixedUpdate.
    /// </summary>
    private void ApplySupportMotionBeforeLocomotion()
    {
        if (!InheritSupportTranslation && !InheritSupportYaw)
        {
            return;
        }

        if (!HasTrackedSupport || CurrentSupportTransform == null)
        {
            return;
        }

        Vector3 NewSupportWorldPoint = CurrentSupportTransform.TransformPoint(SupportLocalPoint);
        Vector3 SupportDeltaPosition = NewSupportWorldPoint - LastSupportWorldPoint;
        Quaternion CurrentSupportRotation = CurrentSupportTransform.rotation;
        Quaternion SupportDeltaRotation = CurrentSupportRotation * Quaternion.Inverse(LastSupportRotation);

        if (InheritSupportTranslation && SupportDeltaPosition.sqrMagnitude > 0f)
        {
            CharacterController.Move(SupportDeltaPosition);
        }

        if (InheritSupportYaw)
        {
            Vector3 PreviousForward = Vector3.ProjectOnPlane(LastSupportRotation * Vector3.forward, Vector3.up);
            Vector3 CurrentForward = Vector3.ProjectOnPlane(CurrentSupportRotation * Vector3.forward, Vector3.up);

            if (PreviousForward.sqrMagnitude > 0.0001f && CurrentForward.sqrMagnitude > 0.0001f)
            {
                float DeltaYaw = Vector3.SignedAngle(PreviousForward, CurrentForward, Vector3.up);
                ViewRoot.Rotate(Vector3.up, DeltaYaw, Space.World);
            }
        }

        LastSupportWorldPoint = NewSupportWorldPoint;
        LastSupportRotation = CurrentSupportRotation;
    }

    /// <summary>
    /// Updates stable grounding information and resolves the current support object.
    /// </summary>
    private void UpdateGroundState()
    {
        HasGroundHit = ProbeGround(out GroundHit);
        IsGrounded = false;
        CurrentSupportTransform = null;
        CurrentSupportRigidbody = null;

        if (JumpGroundIgnoreCounter > 0f && VerticalVelocity > 0f)
        {
            CharacterController.stepOffset = 0f;
            return;
        }

        if (!HasGroundHit)
        {
            CharacterController.stepOffset = 0f;
            return;
        }

        float GroundAngle = Vector3.Angle(GroundHit.normal, Vector3.up);
        bool IsStableGround = GroundAngle <= MaxStableGroundAngle;

        if (!IsStableGround)
        {
            CharacterController.stepOffset = 0f;
            return;
        }

        IsGrounded = true;
        CoyoteCounter = CoyoteTime;
        CharacterController.stepOffset = DefaultStepOffset;

        if (GroundHit.collider != null)
        {
            CurrentSupportTransform = GroundHit.collider.transform;
            CurrentSupportRigidbody = GroundHit.rigidbody != null ? GroundHit.rigidbody : GroundHit.collider.attachedRigidbody;
        }

        if (DrawDebug)
        {
            Debug.DrawRay(GroundHit.point, GroundHit.normal * 0.5f, Color.green);
        }
    }

    /// <summary>
    /// Updates crouch state, controller height and camera pivot height.
    /// The player only crouches from input, or remains crouched if there is not enough headroom to stand.
    /// </summary>
    private void HandleCrouch(float DeltaTime)
    {
        bool MustStayCrouchedBecauseOfCeiling = IsCrouching && !CanStandUp();
        bool ShouldCrouch = WantsToCrouch || MustStayCrouchedBecauseOfCeiling;

        float TargetHeight = ShouldCrouch ? CrouchingHeight : StandingHeight;
        float NewHeight = Mathf.MoveTowards(CharacterController.height, TargetHeight, CrouchTransitionSpeed * DeltaTime);
        ApplyControllerHeightImmediate(NewHeight);

        Vector3 TargetPivotLocalPosition = CameraPivotStandingLocalPosition;
        if (ShouldCrouch)
        {
            TargetPivotLocalPosition += new Vector3(0f, CrouchCameraOffset, 0f);
        }

        CameraPivot.localPosition = Vector3.MoveTowards(
            CameraPivot.localPosition,
            TargetPivotLocalPosition,
            CrouchTransitionSpeed * DeltaTime);

        bool IsNearCrouchHeight = Mathf.Abs(CharacterController.height - CrouchingHeight) < 0.02f;
        IsCrouching = ShouldCrouch || IsNearCrouchHeight;
    }

    /// <summary>
    /// Applies locomotion, jump logic, gravity and slope sliding.
    /// </summary>
    private void HandleMovement(float DeltaTime)
    {
        Quaternion YawRotation = Quaternion.Euler(0f, ViewRoot.eulerAngles.y, 0f);
        Vector3 Forward = YawRotation * Vector3.forward;
        Vector3 Right = YawRotation * Vector3.right;
        Forward.y = 0f;
        Right.y = 0f;
        Forward.Normalize();
        Right.Normalize();

        Vector3 WishDirection = Forward * MoveInput.y + Right * MoveInput.x;
        if (WishDirection.sqrMagnitude > 1f)
        {
            WishDirection.Normalize();
        }

        float SelectedSpeed = IsCrouching ? CrouchSpeed : (SprintAction.IsPressed() ? SprintSpeed : WalkSpeed);
        Vector3 TargetPlanarVelocity = WishDirection * SelectedSpeed;
        float SelectedAcceleration = IsGrounded ? GroundAcceleration : AirAcceleration;

        PlanarVelocity = Vector3.MoveTowards(PlanarVelocity, TargetPlanarVelocity, SelectedAcceleration * DeltaTime);

        if (IsGrounded && WishDirection.sqrMagnitude <= 0.0001f)
        {
            PlanarVelocity = Vector3.MoveTowards(PlanarVelocity, Vector3.zero, GroundFriction * DeltaTime);
        }

        bool StartedJumpThisFrame = false;

        if (IsGrounded)
        {
            if (JumpBufferCounter > 0f && CoyoteCounter > 0f)
            {
                VerticalVelocity = Mathf.Sqrt(2f * Gravity * JumpHeight);
                JumpBufferCounter = 0f;
                CoyoteCounter = 0f;
                JumpGroundIgnoreCounter = JumpGroundIgnoreTime;
                IsGrounded = false;
                StartedJumpThisFrame = true;
                ClearSupportTracking();
            }
            else
            {
                VerticalVelocity = -GroundSnapSpeed;
            }
        }
        else
        {
            VerticalVelocity -= Gravity * DeltaTime;
        }

        if (HasGroundHit && !IsGrounded)
        {
            Vector3 DownSlope = Vector3.ProjectOnPlane(Vector3.down, GroundHit.normal).normalized;
            if (DownSlope.sqrMagnitude > 0.0001f)
            {
                PlanarVelocity += DownSlope * (SteepSlideGravity * DeltaTime);
            }
        }

        Vector3 Motion = (PlanarVelocity * DeltaTime) + (Vector3.up * (VerticalVelocity * DeltaTime));
        CollisionFlags MoveFlags = CharacterController.Move(Motion);

        if ((MoveFlags & CollisionFlags.Above) != 0 && VerticalVelocity > 0f)
        {
            VerticalVelocity = 0f;
        }

        if (!StartedJumpThisFrame && (MoveFlags & CollisionFlags.Below) != 0 && VerticalVelocity < 0f)
        {
            VerticalVelocity = -GroundSnapSpeed;
        }

        Velocity = DeltaTime > 0f ? Motion / DeltaTime : Vector3.zero;
    }

    /// <summary>
    /// Stores support tracking data after the final movement of the frame.
    /// </summary>
    private void CacheSupportTracking()
    {
        if (!IsGrounded || CurrentSupportTransform == null)
        {
            ClearSupportTracking();
            return;
        }

        Vector3 SupportUp = CurrentSupportTransform.up;
        if (Vector3.Dot(SupportUp, Vector3.up) < MinimumSupportUpDot)
        {
            ClearSupportTracking();
            return;
        }

        HasTrackedSupport = true;
        SupportLocalPoint = CurrentSupportTransform.InverseTransformPoint(transform.position);
        LastSupportWorldPoint = CurrentSupportTransform.TransformPoint(SupportLocalPoint);
        LastSupportRotation = CurrentSupportTransform.rotation;
    }

    /// <summary>
    /// Clears cached support tracking data.
    /// </summary>
    private void ClearSupportTracking()
    {
        HasTrackedSupport = false;
        CurrentSupportTransform = null;
        CurrentSupportRigidbody = null;
    }

    /// <summary>
    /// Applies a controller height while keeping the capsule feet anchored at the transform origin.
    /// </summary>
    private void ApplyControllerHeightImmediate(float NewHeight)
    {
        float ClampedHeight = Mathf.Clamp(NewHeight, CrouchingHeight, StandingHeight);
        CharacterController.height = ClampedHeight;
        CharacterController.center = new Vector3(0f, ClampedHeight * 0.5f, 0f);
    }

    /// <summary>
    /// Checks whether there is enough headroom to return to standing height.
    /// </summary>
    /// <summary>
    /// Checks whether there is enough extra headroom to expand from the current controller height to standing height.
    /// Only the additional upper volume is tested so nearby objects at feet level cannot force crouch.
    /// </summary>
    private bool CanStandUp()
    {
        float CurrentHeight = CharacterController.height;
        float HeightDifference = StandingHeight - CurrentHeight;

        if (HeightDifference <= 0.001f)
        {
            return true;
        }

        float ProbeRadius = Mathf.Max(0.01f, CharacterController.radius - CharacterController.skinWidth - GroundProbeRadiusPadding);
        float BottomSphereY = CurrentHeight - ProbeRadius;
        float TopSphereY = StandingHeight - ProbeRadius;

        BottomSphereY += StandUpCheckPadding;
        TopSphereY -= StandUpCheckPadding;

        if (TopSphereY <= BottomSphereY)
        {
            return true;
        }

        Vector3 Bottom = transform.position + Vector3.up * BottomSphereY;
        Vector3 Top = transform.position + Vector3.up * TopSphereY;

        Collider[] Hits = Physics.OverlapCapsule(Bottom, Top, ProbeRadius, GroundLayers, QueryTriggerInteraction.Ignore);
        for (int Index = 0; Index < Hits.Length; Index++)
        {
            Collider Candidate = Hits[Index];
            if (Candidate == null || IsOwnCollider(Candidate))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// Performs a downward sphere cast from the lower part of the capsule to detect nearby ground.
    /// </summary>
    private bool ProbeGround(out RaycastHit HitInfo)
    {
        Vector3 ProbeOrigin = GetGroundProbeOrigin();
        float ProbeRadius = Mathf.Max(0.01f, CharacterController.radius - GroundProbeRadiusPadding);
        float ProbeLength = Mathf.Max(0.02f, GroundProbeDistance + CharacterController.skinWidth);

        RaycastHit[] Hits = Physics.SphereCastAll(ProbeOrigin, ProbeRadius, Vector3.down, ProbeLength, GroundLayers, QueryTriggerInteraction.Ignore);

        float BestDistance = float.MaxValue;
        HitInfo = default;
        bool FoundHit = false;

        for (int Index = 0; Index < Hits.Length; Index++)
        {
            RaycastHit CandidateHit = Hits[Index];
            if (CandidateHit.collider == null || IsOwnCollider(CandidateHit.collider))
            {
                continue;
            }

            if (CandidateHit.distance < BestDistance)
            {
                BestDistance = CandidateHit.distance;
                HitInfo = CandidateHit;
                FoundHit = true;
            }
        }

        if (DrawDebug)
        {
            Color ProbeColor = FoundHit ? Color.green : Color.red;
            Debug.DrawRay(ProbeOrigin, Vector3.down * ProbeLength, ProbeColor);
        }

        return FoundHit;
    }

    /// <summary>
    /// Returns the world origin used by the ground probe.
    /// </summary>
    private Vector3 GetGroundProbeOrigin()
    {
        float ProbeStartHeight = Mathf.Max(CharacterController.radius + 0.02f, 0.08f);
        return transform.position + Vector3.up * ProbeStartHeight;
    }

    /// <summary>
    /// Returns true when the provided collider belongs to the player hierarchy.
    /// </summary>
    private bool IsOwnCollider(Collider CandidateCollider)
    {
        if (CandidateCollider == null)
        {
            return false;
        }

        for (int Index = 0; Index < OwnColliders.Length; Index++)
        {
            if (OwnColliders[Index] == CandidateCollider)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Normalizes an angle to the [-180, 180] range.
    /// </summary>
    private static float NormalizeAngle(float Angle)
    {
        while (Angle > 180f)
        {
            Angle -= 360f;
        }

        while (Angle < -180f)
        {
            Angle += 360f;
        }

        return Angle;
    }
}
