using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Rigidbody based FPS controller designed for precise movement,
/// stable support velocity inheritance, crouch handling and smooth interaction
/// with kinematic elevators and rotating platforms.
/// 
/// Horizontal look is applied to a visual yaw root in render space instead of rotating
/// the rigidbody in FixedUpdate. This removes visible mouse-look jitter while preserving
/// stable physics movement.
/// </summary>
[DefaultExecutionOrder(-50)]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
[RequireComponent(typeof(PlayerInput))]
public sealed class PlayerController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Transform used for visual yaw rotation. Usually a child object that contains the camera pivot and visuals.")]
    [SerializeField] private Transform ViewRoot;

    [Tooltip("Camera pivot used for vertical look rotation.")]
    [SerializeField] private Transform CameraPivot;

    [Header("Movement")]
    [Tooltip("Maximum horizontal speed while standing.")]
    [SerializeField] private float MoveSpeed = 7.5f;

    [Tooltip("Speed multiplier applied while crouching.")]
    [SerializeField] private float CrouchSpeedMultiplier = 0.5f;

    [Tooltip("Acceleration used while grounded.")]
    [SerializeField] private float GroundAcceleration = 90f;

    [Tooltip("Deceleration used while grounded and no input is provided.")]
    [SerializeField] private float GroundDeceleration = 100f;

    [Tooltip("Acceleration used while airborne.")]
    [SerializeField] private float AirAcceleration = 35f;

    [Tooltip("Maximum horizontal speed while airborne.")]
    [SerializeField] private float MaxAirSpeed = 7.5f;

    [Header("Jump And Gravity")]
    [Tooltip("Jump height expressed in meters.")]
    [SerializeField] private float JumpHeight = 1.35f;

    [Tooltip("Custom gravity applied while airborne.")]
    [SerializeField] private float Gravity = 25f;

    [Tooltip("Small downward velocity applied while grounded to keep stable ground contact.")]
    [SerializeField] private float GroundStickyVelocity = 8f;

    [Tooltip("Grace time after leaving ground during which jump is still allowed.")]
    [SerializeField] private float CoyoteTime = 0.12f;

    [Tooltip("Grace time after pressing jump during which landing can still consume the jump.")]
    [SerializeField] private float JumpBufferTime = 0.12f;

    [Header("Look")]
    [Tooltip("Mouse delta sensitivity multiplier.")]
    [SerializeField] private float MouseSensitivity = 0.12f;

    [Tooltip("Gamepad look sensitivity in degrees per second.")]
    [SerializeField] private float GamepadSensitivity = 120f;

    [Tooltip("Minimum vertical camera angle.")]
    [SerializeField] private float MinPitch = -85f;

    [Tooltip("Maximum vertical camera angle.")]
    [SerializeField] private float MaxPitch = 85f;

    [Header("Ground Check")]
    [Tooltip("Layers considered valid ground and solid blockers.")]
    [SerializeField] private LayerMask GroundLayers = ~0;

    [Tooltip("Extra distance used to detect ground below the capsule.")]
    [SerializeField] private float GroundCheckDistance = 0.2f;

    [Tooltip("Small cast shell used to reduce clipping and unstable probes.")]
    [SerializeField] private float GroundProbeShell = 0.02f;

    [Tooltip("Maximum walkable slope angle in degrees.")]
    [Range(0f, 89f)]
    [SerializeField] private float MaxGroundAngle = 55f;

    [Header("Support")]
    [Tooltip("If enabled, the player inherits yaw rotation from the current support rigidbody.")]
    [SerializeField] private bool InheritSupportYaw = true;

    [Tooltip("Multiplier applied to inherited support linear velocity.")]
    [Range(0f, 1f)]
    [SerializeField] private float SupportVelocityInfluence = 1f;

    [Header("Crouch")]
    [Tooltip("Capsule height while standing.")]
    [SerializeField] private float StandingHeight = 2f;

    [Tooltip("Capsule height while crouching.")]
    [SerializeField] private float CrouchingHeight = 1.2f;

    [Tooltip("Interpolation speed for crouch collider height changes.")]
    [SerializeField] private float CrouchTransitionSpeed = 12f;

    [Tooltip("Local camera pivot height while standing.")]
    [SerializeField] private float StandingCameraLocalY = 0.8f;

    [Tooltip("Local camera pivot height while crouching.")]
    [SerializeField] private float CrouchingCameraLocalY = 0.45f;

    [Tooltip("Interpolation speed for camera height changes while crouching.")]
    [SerializeField] private float CameraCrouchTransitionSpeed = 12f;

    private Rigidbody Rigidbody;
    private CapsuleCollider CapsuleCollider;
    private BoxCollider PlayerCollider;
    private BoxCollider MagnetCollider;
    private PlayerInput PlayerInput;
    private float PlayerColliderBaseHeight;
    private float MagnetColliderBaseHeight;
    private Vector3 MagnetColliderOriginalCenter;

    private InputAction MoveAction;
    private InputAction LookAction;
    private InputAction JumpAction;
    private InputAction CrouchAction;

    private Vector2 MoveInput;
    private Vector2 LookInput;

    private bool IsGrounded;
    private bool IsCrouching;
    private bool JumpQueued;

    private float Pitch;
    private float Yaw;
    private float LastGroundedTime = -999f;
    private float LastJumpPressedTime = -999f;
    private float TargetCapsuleHeight;
    private float TargetCameraLocalY;

    private Vector3 GroundNormal = Vector3.up;
    private Rigidbody SupportRigidbody;
    private Vector3 SupportPointVelocity;

    private Rigidbody LastSupportRigidbody;
    private Quaternion LastSupportRotation;
    private bool HadSupportLastFrame;

    private readonly Collider[] StandUpHits = new Collider[8];

    /// <summary>
    /// Initializes component references, input actions and collider defaults.
    /// </summary>
    private void Awake()
    {
        Rigidbody = GetComponent<Rigidbody>();
        CapsuleCollider = GetComponent<CapsuleCollider>();
        BoxCollider[] boxcolliders = GetComponentsInChildren<BoxCollider>();
        for (int i = 0; i < boxcolliders.Length; i++)
        {
            if(boxcolliders[i].gameObject.layer == LayerMask.NameToLayer("Default"))
            {
                MagnetCollider = boxcolliders[i];
            }

            if (boxcolliders[i].gameObject.layer == LayerMask.NameToLayer("Player"))
            {
                PlayerCollider = boxcolliders[i];
            }


        }

        // Guardamos alturas base
        MagnetColliderOriginalCenter = MagnetCollider.center;
        PlayerColliderBaseHeight = PlayerCollider.size.y;
        MagnetColliderBaseHeight = MagnetCollider.size.y;

        PlayerInput = GetComponent<PlayerInput>();

        MoveAction = PlayerInput.actions["Move"];
        LookAction = PlayerInput.actions["Look"];
        JumpAction = PlayerInput.actions["Jump"];
        CrouchAction = PlayerInput.actions["Crouch"];

        Rigidbody.useGravity = false;
        Rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        Rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;
        Rigidbody.constraints = RigidbodyConstraints.FreezeRotation;

        TargetCapsuleHeight = StandingHeight;
        TargetCameraLocalY = StandingCameraLocalY;

        CapsuleCollider.height = StandingHeight;
        PlayerCollider.size = new Vector3(PlayerCollider.size.x,StandingHeight, PlayerCollider.size.z);
        MagnetCollider.size = new Vector3(MagnetCollider.size.x, StandingHeight/2, MagnetCollider.size.z);
        CapsuleCollider.center = new Vector3(0f, StandingHeight * 0.5f, 0f);

        Yaw = transform.eulerAngles.y;

        if (ViewRoot != null)
        {
            ViewRoot.rotation = Quaternion.Euler(0f, Yaw, 0f);
        }

        if (CameraPivot != null)
        {
            Vector3 LocalPosition = CameraPivot.localPosition;
            LocalPosition.y = StandingCameraLocalY;
            CameraPivot.localPosition = LocalPosition;
            CameraPivot.localRotation = Quaternion.Euler(Pitch, 0f, 0f);
        }
    }

    /// <summary>
    /// Subscribes to input callbacks and locks the cursor.
    /// </summary>
    private void OnEnable()
    {
        JumpAction.performed += OnJumpPerformed;
        CrouchAction.performed += OnCrouchPerformed;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    /// <summary>
    /// Unsubscribes from input callbacks.
    /// </summary>
    private void OnDisable()
    {
        JumpAction.performed -= OnJumpPerformed;
        CrouchAction.performed -= OnCrouchPerformed;
    }

    /// <summary>
    /// Reads frame input, updates look input accumulation and smooth crouch visuals.
    /// </summary>
    private void Update()
    {
        ReadInput();
        HandleLookInput();
        UpdateCrouch();
    }

    /// <summary>
    /// Applies visual yaw and camera pitch after the rest of the frame to reduce visible jitter.
    /// </summary>
    private void LateUpdate()
    {
        UpdateViewRotation();
    }

    /// <summary>
    /// Resolves support state, support rotation, movement and jump inside the physics step.
    /// </summary>
    private void FixedUpdate()
    {
        UpdateGroundState();
        ApplySupportYawRotation();
        HandleMovement();
        HandleJump();
    }

    /// <summary>
    /// Reads current move and look input values.
    /// </summary>
    private void ReadInput()
    {
        MoveInput = MoveAction.ReadValue<Vector2>();
        LookInput = LookAction.ReadValue<Vector2>();
    }

    /// <summary>
    /// Accumulates look input into yaw and pitch values.
    /// Mouse input is applied raw per frame, while gamepad input is time-scaled.
    /// </summary>
    private void HandleLookInput()
    {
        bool IsMouseInput = PlayerInput.currentControlScheme != null &&
                            PlayerInput.currentControlScheme.ToLower().Contains("keyboard");

        float Sensitivity = IsMouseInput
            ? MouseSensitivity
            : GamepadSensitivity * Time.deltaTime;

        Yaw += LookInput.x * Sensitivity;
        Pitch -= LookInput.y * Sensitivity;
        Pitch = Mathf.Clamp(Pitch, MinPitch, MaxPitch);
    }

    /// <summary>
    /// Updates visual yaw and camera pitch in render space.
    /// </summary>
    private void UpdateViewRotation()
    {
        if (ViewRoot != null)
        {
            ViewRoot.rotation = Quaternion.Euler(0f, Yaw, 0f);
        }

        if (CameraPivot != null)
        {
            CameraPivot.localRotation = Quaternion.Euler(Pitch, 0f, 0f);
        }
    }

    /// <summary>
    /// Smoothly updates capsule height, collider center and camera height while crouching.
    /// </summary>
    /// <summary>
    /// Smoothly updates capsule height, collider center and camera height while crouching.
    /// This version does not force automatic crouch when headroom is blocked.
    /// The player can crouch manually, but can only stand up if there is enough space.
    /// </summary>
    private void UpdateCrouch()
    {
        float NewHeight = Mathf.Lerp(
            CapsuleCollider.height,
            TargetCapsuleHeight,
            CrouchTransitionSpeed * Time.deltaTime
        );

        CapsuleCollider.height = NewHeight;
        CapsuleCollider.center = new Vector3(0f, NewHeight * 0.5f, 0f);

        // 🔹 FACTOR DE ESCALA respecto a la altura original
        float HeightRatio = NewHeight / StandingHeight;

        // -------------------------
        // PLAYER COLLIDER (principal)
        // -------------------------
        float PlayerHeight = PlayerColliderBaseHeight * HeightRatio;

        Vector3 PlayerSize = PlayerCollider.size;
        PlayerSize.y = PlayerHeight;
        PlayerCollider.size = PlayerSize;

        Vector3 PlayerCenter = PlayerCollider.center;
        PlayerCenter.y = PlayerHeight * 0.5f;
        PlayerCollider.center = PlayerCenter;

        // -------------------------
        // MAGNET COLLIDER (especial)
        // -------------------------
        float MagnetHeight = MagnetColliderBaseHeight * HeightRatio;

        Vector3 MagnetSize = MagnetCollider.size;
        MagnetSize.y = MagnetHeight;
        MagnetCollider.size = MagnetSize;

        // 🔹 Si está completamente de pie → restaurar center original
        if (!IsCrouching && Mathf.Abs(NewHeight - StandingHeight) < 0.01f)
        {
            MagnetCollider.center = MagnetColliderOriginalCenter;
        }
        else
        {
            // 🔹 Durante crouch → comportamiento adaptativo
            Vector3 MagnetCenter = MagnetCollider.center;
            MagnetCenter.y = MagnetHeight * 0.5f;
            MagnetCollider.center = MagnetCenter;
        }

        // -------------------------
        // CAMERA
        // -------------------------
        if (CameraPivot != null)
        {
            Vector3 LocalPosition = CameraPivot.localPosition;
            LocalPosition.y = Mathf.Lerp(
                LocalPosition.y,
                TargetCameraLocalY,
                CameraCrouchTransitionSpeed * Time.deltaTime
            );
            CameraPivot.localPosition = LocalPosition;
        }
    }

    /// <summary>
    /// Updates grounded state, support rigidbody and point velocity from the current support.
    /// </summary>
    private void UpdateGroundState()
    {
        Vector3 BottomHemisphere = GetCapsuleBottomHemisphere();
        float ProbeRadius = Mathf.Max(0.01f, CapsuleCollider.radius - GroundProbeShell);
        Vector3 CastOrigin = BottomHemisphere + Vector3.up * 0.05f;
        float CastDistance = GroundCheckDistance + 0.05f;

        IsGrounded = false;
        GroundNormal = Vector3.up;
        SupportRigidbody = null;
        SupportPointVelocity = Vector3.zero;

        if (!Physics.SphereCast(
                CastOrigin,
                ProbeRadius,
                Vector3.down,
                out RaycastHit HitInfo,
                CastDistance,
                GroundLayers,
                QueryTriggerInteraction.Ignore))
        {
            ClearSupportWhenUngrounded();
            return;
        }

        float GroundAngle = Vector3.Angle(HitInfo.normal, Vector3.up);

        if (GroundAngle > MaxGroundAngle)
        {
            ClearSupportWhenUngrounded();
            return;
        }

        IsGrounded = true;
        LastGroundedTime = Time.time;
        GroundNormal = HitInfo.normal;
        SupportRigidbody = HitInfo.rigidbody != null ? HitInfo.rigidbody : HitInfo.collider.attachedRigidbody;

        if (SupportRigidbody == null)
        {
            SupportPointVelocity = Vector3.zero;
            return;
        }

        if (SupportRigidbody.TryGetComponent(out ElevatorController ElevatorController))
        {
            SupportPointVelocity = ElevatorController.GetVelocityAtPoint(HitInfo.point);
        }
        else
        {
            SupportPointVelocity = SupportRigidbody.GetPointVelocity(HitInfo.point);
        }
    }

    /// <summary>
    /// Applies inherited support yaw when standing on the same support across frames.
    /// </summary>
    private void ApplySupportYawRotation()
    {
        if (!InheritSupportYaw || !IsGrounded || SupportRigidbody == null)
        {
            HadSupportLastFrame = false;
            LastSupportRigidbody = null;
            return;
        }

        Quaternion CurrentSupportRotation = SupportRigidbody.rotation;

        if (!HadSupportLastFrame || SupportRigidbody != LastSupportRigidbody)
        {
            LastSupportRotation = CurrentSupportRotation;
            LastSupportRigidbody = SupportRigidbody;
            HadSupportLastFrame = true;
            return;
        }

        Vector3 PreviousForward = Vector3.ProjectOnPlane(LastSupportRotation * Vector3.forward, Vector3.up);
        Vector3 CurrentForward = Vector3.ProjectOnPlane(CurrentSupportRotation * Vector3.forward, Vector3.up);

        if (PreviousForward.sqrMagnitude > 0.0001f && CurrentForward.sqrMagnitude > 0.0001f)
        {
            float DeltaYaw = Vector3.SignedAngle(PreviousForward, CurrentForward, Vector3.up);
            Yaw += DeltaYaw;
        }

        LastSupportRotation = CurrentSupportRotation;
        LastSupportRigidbody = SupportRigidbody;
        HadSupportLastFrame = true;
    }

    /// <summary>
    /// Applies planar locomotion relative to the current support velocity and custom gravity.
    /// </summary>
    private void HandleMovement()
    {
        Vector3 EffectiveSupportVelocity = IsGrounded ? SupportPointVelocity * SupportVelocityInfluence : Vector3.zero;
        Vector3 CurrentVelocity = Rigidbody.linearVelocity;
        Vector3 RelativeVelocity = CurrentVelocity - EffectiveSupportVelocity;

        Quaternion YawRotation = Quaternion.Euler(0f, Yaw, 0f);

        Vector3 MoveForward = YawRotation * Vector3.forward;
        Vector3 MoveRight = YawRotation * Vector3.right;

        if (IsGrounded)
        {
            MoveForward = Vector3.ProjectOnPlane(MoveForward, GroundNormal).normalized;
            MoveRight = Vector3.ProjectOnPlane(MoveRight, GroundNormal).normalized;
        }
        else
        {
            MoveForward.y = 0f;
            MoveRight.y = 0f;
            MoveForward.Normalize();
            MoveRight.Normalize();
        }

        Vector3 WishDirection = (MoveForward * MoveInput.y) + (MoveRight * MoveInput.x);

        if (WishDirection.sqrMagnitude > 1f)
        {
            WishDirection.Normalize();
        }

        float CurrentMoveSpeed = IsCrouching ? MoveSpeed * CrouchSpeedMultiplier : MoveSpeed;
        Vector3 RelativePlanarVelocity = Vector3.ProjectOnPlane(RelativeVelocity, Vector3.up);
        Vector3 TargetPlanarVelocity = WishDirection * (IsGrounded ? CurrentMoveSpeed : Mathf.Min(CurrentMoveSpeed, MaxAirSpeed));

        Vector3 NewRelativePlanarVelocity = IsGrounded
            ? AccelerateGroundVelocity(RelativePlanarVelocity, TargetPlanarVelocity)
            : AccelerateAirVelocity(RelativePlanarVelocity, TargetPlanarVelocity);

        float VerticalVelocity = RelativeVelocity.y;

        if (IsGrounded)
        {
            if (VerticalVelocity <= 0f)
            {
                VerticalVelocity = -GroundStickyVelocity;
            }
        }
        else
        {
            VerticalVelocity -= Gravity * Time.fixedDeltaTime;
        }

        Vector3 NewWorldVelocity = EffectiveSupportVelocity + NewRelativePlanarVelocity + (Vector3.up * VerticalVelocity);
        Rigidbody.linearVelocity = NewWorldVelocity;
    }

    /// <summary>
    /// Executes buffered jump input during the physics step if coyote time still allows it.
    /// </summary>
    private void HandleJump()
    {
        if (!JumpQueued)
        {
            return;
        }

        bool CanUseBufferedJump = Time.time - LastJumpPressedTime <= JumpBufferTime;

        if (!CanUseBufferedJump)
        {
            JumpQueued = false;
            return;
        }

        bool CanJump = IsGrounded || (Time.time - LastGroundedTime <= CoyoteTime);

        if (!CanJump)
        {
            return;
        }

        JumpQueued = false;
        LastJumpPressedTime = -999f;
        IsGrounded = false;
        SupportRigidbody = null;
        SupportPointVelocity = Vector3.zero;

        Vector3 Velocity = Rigidbody.linearVelocity;
        float JumpVelocity = Mathf.Sqrt(2f * Gravity * JumpHeight);

        Velocity.y = JumpVelocity;
        Rigidbody.linearVelocity = Velocity;
    }

    /// <summary>
    /// Queues jump input so it is resolved inside FixedUpdate.
    /// </summary>
    private void OnJumpPerformed(InputAction.CallbackContext Context)
    {
        JumpQueued = true;
        LastJumpPressedTime = Time.time;
    }

    /// <summary>
    /// Toggles crouch state when enough headroom is available.
    /// </summary>
    private void OnCrouchPerformed(InputAction.CallbackContext Context)
    {
        if (IsCrouching)
        {
            if (!CanStandUp())
            {
                return;
            }

            IsCrouching = false;
            TargetCapsuleHeight = StandingHeight;
            TargetCameraLocalY = StandingCameraLocalY;
        }
        else
        {
            IsCrouching = true;
            TargetCapsuleHeight = CrouchingHeight;
            TargetCameraLocalY = CrouchingCameraLocalY;
        }
    }

    /// <summary>
    /// Accelerates grounded planar velocity using separate acceleration and deceleration behavior.
    /// </summary>
    /// <param name="CurrentVelocity">Current planar velocity relative to support.</param>
    /// <param name="TargetVelocity">Desired planar velocity relative to support.</param>
    /// <returns>Adjusted planar velocity.</returns>
    private Vector3 AccelerateGroundVelocity(Vector3 CurrentVelocity, Vector3 TargetVelocity)
    {
        bool IsStopping = TargetVelocity.sqrMagnitude <= 0.0001f;
        float Rate = IsStopping ? GroundDeceleration : GroundAcceleration;
        return Vector3.MoveTowards(CurrentVelocity, TargetVelocity, Rate * Time.fixedDeltaTime);
    }

    /// <summary>
    /// Accelerates airborne planar velocity toward the air target velocity.
    /// </summary>
    /// <param name="CurrentVelocity">Current planar velocity relative to support.</param>
    /// <param name="TargetVelocity">Desired airborne planar velocity.</param>
    /// <returns>Adjusted planar velocity.</returns>
    private Vector3 AccelerateAirVelocity(Vector3 CurrentVelocity, Vector3 TargetVelocity)
    {
        return Vector3.MoveTowards(CurrentVelocity, TargetVelocity, AirAcceleration * Time.fixedDeltaTime);
    }


    /// Checks whether there is enough headroom to return to standing height.
    /// This version ignores the player own collider explicitly.
    /// </summary>
    /// <returns>True if the player can stand up safely.</returns>
    /// <summary>
    /// Checks whether there is enough headroom to return to standing height.
    /// This version ignores the player own collider explicitly.
    /// </summary>
    /// <returns>True if the player can stand up safely.</returns>
    private bool CanStandUp()
    {
        float Radius = Mathf.Max(0.01f, CapsuleCollider.radius * 0.95f);

        Vector3 CurrentCenterWorld = transform.TransformPoint(CapsuleCollider.center);

        float CurrentHalfHeight = Mathf.Max(CapsuleCollider.height * 0.5f, Radius);
        float CurrentOffset = CurrentHalfHeight - CapsuleCollider.radius;
        Vector3 CurrentBottom = CurrentCenterWorld - transform.up * CurrentOffset;

        float StandingHalfHeight = Mathf.Max(StandingHeight * 0.5f, Radius);
        float StandingOffset = StandingHalfHeight - Radius;

        Vector3 StandingCenterWorld = CurrentBottom + transform.up * StandingHalfHeight;
        Vector3 Bottom = StandingCenterWorld - transform.up * StandingOffset;
        Vector3 Top = StandingCenterWorld + transform.up * StandingOffset;

        int HitCount = Physics.OverlapCapsuleNonAlloc(
            Bottom,
            Top,
            Radius,
            StandUpHits,
            GroundLayers,
            QueryTriggerInteraction.Ignore
        );

        for (int Index = 0; Index < HitCount; Index++)
        {
            Collider HitCollider = StandUpHits[Index];

            if (HitCollider == null)
            {
                continue;
            }

            if (HitCollider == CapsuleCollider)
            {
                continue;
            }

            if (HitCollider.transform.IsChildOf(transform))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns the world position of the capsule bottom hemisphere center.
    /// </summary>
    /// <returns>Bottom hemisphere center in world space.</returns>
    private Vector3 GetCapsuleBottomHemisphere()
    {
        Vector3 Center = transform.TransformPoint(CapsuleCollider.center);
        float HemisphereOffset = (CapsuleCollider.height * 0.5f) - CapsuleCollider.radius;
        return Center - transform.up * HemisphereOffset;
    }

    /// <summary>
    /// Clears support tracking when the player is not grounded.
    /// </summary>
    private void ClearSupportWhenUngrounded()
    {
        IsGrounded = false;
        GroundNormal = Vector3.up;
        SupportRigidbody = null;
        SupportPointVelocity = Vector3.zero;
        HadSupportLastFrame = false;
        LastSupportRigidbody = null;
    }
}