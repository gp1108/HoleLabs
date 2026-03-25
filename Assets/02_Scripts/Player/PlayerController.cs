using UnityEngine;

/// <summary>
/// Simplified first person CharacterController motor that keeps only the useful public API
/// and replaces the old moving platform logic with explicit platform delta carry.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerInputReader))]
public sealed class PlayerController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Transform rotated on yaw. Usually the visual root that contains the camera pivot.")]
    [SerializeField] private Transform ViewRoot;

    [Tooltip("Transform used for camera pitch and crouch height offsets.")]
    [SerializeField] private Transform CameraPivot;

    [Tooltip("Main gameplay camera used by interaction and equipped items.")]
    [SerializeField] private Camera PlayerCameraComponent;

    [Tooltip("Optional transform used by held or view model tools.")]
    [SerializeField] private Transform ViewModelContainerTransform;

    [Tooltip("Character controller used to move the player.")]
    [SerializeField] private CharacterController CharacterController;

    [Tooltip("Input reader that provides cached gameplay input values.")]
    [SerializeField] private PlayerInputReader PlayerInputReader;

    [Header("Look")]
    [Tooltip("Horizontal look sensitivity.")]
    [SerializeField] private float LookSensitivityX = 1.5f;

    [Tooltip("Vertical look sensitivity.")]
    [SerializeField] private float LookSensitivityY = 1.5f;

    [Tooltip("Minimum vertical camera angle.")]
    [SerializeField] private float MinPitch = -85f;

    [Tooltip("Maximum vertical camera angle.")]
    [SerializeField] private float MaxPitch = 85f;

    [Header("Movement")]
    [Tooltip("Walking speed in meters per second.")]
    [SerializeField] private float WalkSpeed = 5f;

    [Tooltip("Sprinting speed in meters per second.")]
    [SerializeField] private float SprintSpeed = 7.5f;

    [Tooltip("Crouching speed in meters per second.")]
    [SerializeField] private float CrouchSpeed = 2.75f;

    [Tooltip("Jump height in meters.")]
    [SerializeField] private float JumpHeight = 1.35f;

    [Tooltip("Gravity acceleration magnitude applied every frame.")]
    [SerializeField] private float Gravity = 25f;

    [Tooltip("Small downward force used while grounded.")]
    [SerializeField] private float GroundedForce = -2f;

    [Tooltip("Buffered time window that still accepts a recent jump press.")]
    [SerializeField] private float JumpBufferTime = 0.12f;

    [Tooltip("Grace time after losing grounded where jump is still allowed.")]
    [SerializeField] private float GroundGraceTime = 0.12f;

    [Tooltip("Grace time after losing platform support where jump is still allowed.")]
    [SerializeField] private float PlatformGraceTime = 0.12f;

    [Header("Crouch")]
    [Tooltip("If true, crouch toggles on press.")]
    [SerializeField] private bool ToggleCrouch = true;

    [Tooltip("CharacterController height while standing.")]
    [SerializeField] private float StandingHeight = 2f;

    [Tooltip("CharacterController height while crouching.")]
    [SerializeField] private float CrouchingHeight = 1.2f;

    [Tooltip("Interpolation speed used when changing capsule and camera pivot height.")]
    [SerializeField] private float CrouchTransitionSpeed = 12f;

    [Tooltip("Local Y offset applied to the camera pivot while crouching.")]
    [SerializeField] private float CrouchCameraOffset = -0.35f;

    [Tooltip("Standing local Y used by the camera pivot. Use a negative value to keep the current scene value.")]
    [SerializeField] private float StandingCameraPivotLocalY = 1.8f;

    /// <summary>
    /// Current raw movement input.
    /// </summary>
    public Vector2 MoveInput { get; private set; }

    /// <summary>
    /// Current estimated world velocity applied by the motor.
    /// </summary>
    public Vector3 Velocity { get; private set; }

    /// <summary>
    /// Whether the player is currently grounded.
    /// </summary>
    public bool IsGrounded => CharacterController != null && CharacterController.isGrounded;

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

    /// <summary>
    /// Current vertical velocity applied by the motor.
    /// </summary>
    private float VerticalVelocity;

    /// <summary>
    /// Current pitch rotation in degrees.
    /// </summary>
    private float PitchDegrees;

    /// <summary>
    /// Whether a jump was requested and is waiting to be consumed.
    /// </summary>
    private bool JumpRequested;

    /// <summary>
    /// Whether the player currently wants to crouch.
    /// </summary>
    private bool WantsToCrouch;

    /// <summary>
    /// Cached standing local position of the camera pivot.
    /// </summary>
    private Vector3 CameraPivotStandingLocalPosition;

    /// <summary>
    /// Current motion carrier used to inherit platform delta while grounded.
    /// </summary>
    private IMotionCarrier CurrentPlatform;

    /// <summary>
    /// Remaining time where a jump input is still valid.
    /// </summary>
    private float JumpBufferTimer;

    /// <summary>
    /// Remaining time where recent grounded support still allows a jump.
    /// </summary>
    private float GroundGraceTimer;

    /// <summary>
    /// Remaining time where recent platform support still allows a jump.
    /// </summary>
    private float PlatformGraceTimer;

    /// <summary>
    /// Cached upward platform speed used to preserve jump feel on ascending elevators.
    /// </summary>
    private float LastPlatformUpwardSpeed;

    /// <summary>
    /// Caches references and initializes the standing controller state.
    /// </summary>
    private void Awake()
    {
        if (CharacterController == null) CharacterController = GetComponent<CharacterController>();
        if (PlayerInputReader == null) PlayerInputReader = GetComponent<PlayerInputReader>();
        if (ViewRoot == null) ViewRoot = transform;
        if (PlayerCameraComponent == null) PlayerCameraComponent = GetComponentInChildren<Camera>(true);
        if (CameraPivot == null && PlayerCameraComponent != null) CameraPivot = PlayerCameraComponent.transform.parent;
        if (CameraPivot == null) CameraPivot = ViewRoot;

        CameraPivotStandingLocalPosition = CameraPivot.localPosition;

        if (StandingCameraPivotLocalY >= 0f)
        {
            CameraPivotStandingLocalPosition = new Vector3(
                CameraPivotStandingLocalPosition.x,
                StandingCameraPivotLocalY,
                CameraPivotStandingLocalPosition.z);

            CameraPivot.localPosition = CameraPivotStandingLocalPosition;
        }

        PitchDegrees = NormalizeAngle(CameraPivot.localEulerAngles.x);
        PitchDegrees = Mathf.Clamp(PitchDegrees, MinPitch, MaxPitch);
        CameraPivot.localRotation = Quaternion.Euler(PitchDegrees, 0f, 0f);

        ApplyControllerHeight(StandingHeight);

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    /// <summary>
    /// Subscribes discrete input events.
    /// </summary>
    private void OnEnable()
    {
        PlayerInputReader.JumpPerformed += OnJumpPerformed;
        PlayerInputReader.CrouchPerformed += OnCrouchPerformed;
    }

    /// <summary>
    /// Unsubscribes discrete input events.
    /// </summary>
    private void OnDisable()
    {
        PlayerInputReader.JumpPerformed -= OnJumpPerformed;
        PlayerInputReader.CrouchPerformed -= OnCrouchPerformed;
    }

    /// <summary>
    /// Updates look, platform carry, crouch and movement.
    /// </summary>
    private void Update()
    {
        UpdateLook();

        if (CurrentPlatform != null)
        {
            CharacterController.Move(CurrentPlatform.DeltaPosition);
            LastPlatformUpwardSpeed = Mathf.Max(0f, CurrentPlatform.DeltaPosition.y / Mathf.Max(Time.deltaTime, 0.0001f));
        }
        else
        {
            LastPlatformUpwardSpeed = 0f;
        }

        UpdateJumpSupport();
        UpdateCrouch();
        UpdateMovement();

        if (!CharacterController.isGrounded && VerticalVelocity <= 0f && PlatformGraceTimer <= 0f)
        {
            CurrentPlatform = null;
        }
    }

    /// <summary>
    /// Rotates yaw and pitch from the cached look input.
    /// </summary>
    private void UpdateLook()
    {
        Vector2 LookInput = PlayerInputReader.Look;
        ViewRoot.Rotate(0f, LookInput.x * LookSensitivityX * Time.deltaTime, 0f, Space.World);
        PitchDegrees = Mathf.Clamp(PitchDegrees - LookInput.y * LookSensitivityY * Time.deltaTime, MinPitch, MaxPitch);
        CameraPivot.localRotation = Quaternion.Euler(PitchDegrees, 0f, 0f);
    }

    /// <summary>
    /// Updates crouch state, capsule height and camera pivot height.
    /// </summary>
    private void UpdateCrouch()
    {
        IsCrouching = ToggleCrouch ? WantsToCrouch : PlayerInputReader.IsCrouchHeld;

        float TargetHeight = IsCrouching ? CrouchingHeight : StandingHeight;
        float NewHeight = Mathf.MoveTowards(CharacterController.height, TargetHeight, CrouchTransitionSpeed * Time.deltaTime);
        ApplyControllerHeight(NewHeight);

        Vector3 TargetPivot = CameraPivotStandingLocalPosition + Vector3.up * (IsCrouching ? CrouchCameraOffset : 0f);
        CameraPivot.localPosition = Vector3.MoveTowards(CameraPivot.localPosition, TargetPivot, CrouchTransitionSpeed * Time.deltaTime);
    }

    /// <summary>
    /// Updates jump buffering and support grace windows for ground and moving platforms.
    /// </summary>
    private void UpdateJumpSupport()
    {
        if (JumpRequested)
        {
            JumpBufferTimer -= Time.deltaTime;

            if (JumpBufferTimer <= 0f)
            {
                JumpRequested = false;
                JumpBufferTimer = 0f;
            }
        }

        if (CharacterController.isGrounded)
        {
            GroundGraceTimer = GroundGraceTime;
        }
        else if (GroundGraceTimer > 0f)
        {
            GroundGraceTimer -= Time.deltaTime;
        }

        if (CurrentPlatform != null)
        {
            PlatformGraceTimer = PlatformGraceTime;
        }
        else if (PlatformGraceTimer > 0f)
        {
            PlatformGraceTimer -= Time.deltaTime;
        }
    }

    /// <summary>
    /// Moves the controller using cached input plus minimal jump and gravity logic.
    /// </summary>
    private void UpdateMovement()
    {
        bool HasJumpSupport = CharacterController.isGrounded || GroundGraceTimer > 0f || PlatformGraceTimer > 0f;

        if (JumpRequested && HasJumpSupport)
        {
            VerticalVelocity = Mathf.Sqrt(JumpHeight * 2f * Gravity) + LastPlatformUpwardSpeed;
            JumpRequested = false;
            JumpBufferTimer = 0f;
            GroundGraceTimer = 0f;
            PlatformGraceTimer = 0f;
            CurrentPlatform = null;
        }
        else if (CharacterController.isGrounded && VerticalVelocity <= 0f)
        {
            VerticalVelocity = GroundedForce;
        }
        else
        {
            VerticalVelocity -= Gravity * Time.deltaTime;
        }

        MoveInput = PlayerInputReader.Move;
        Vector3 MoveDirection = ViewRoot.forward * MoveInput.y + ViewRoot.right * MoveInput.x;
        float SelectedMoveSpeed = IsCrouching ? CrouchSpeed : (PlayerInputReader.IsSprintHeld ? SprintSpeed : WalkSpeed);
        Vector3 Motion = MoveDirection.normalized * SelectedMoveSpeed + Vector3.up * VerticalVelocity;
        CharacterController.Move(Motion * Time.deltaTime);
        Velocity = Motion;
    }

    /// <summary>
    /// Captures the motion carrier under the player when colliding with its top face.
    /// </summary>
    /// <param name="Hit">Collision information returned by CharacterController.</param>
    private void OnControllerColliderHit(ControllerColliderHit Hit)
    {
        if (Hit.moveDirection.y > 0f)
        {
            return;
        }

        if (Hit.normal.y < 0.5f)
        {
            return;
        }

        CurrentPlatform = Hit.collider.GetComponentInParent<IMotionCarrier>();
    }

    /// <summary>
    /// Buffers a jump request for the next movement update.
    /// </summary>
    private void OnJumpPerformed()
    {
        JumpRequested = true;
        JumpBufferTimer = JumpBufferTime;
    }

    /// <summary>
    /// Toggles crouch when toggle mode is enabled.
    /// </summary>
    private void OnCrouchPerformed()
    {
        if (!ToggleCrouch)
        {
            return;
        }

        WantsToCrouch = !WantsToCrouch;
    }

    /// <summary>
    /// Applies a controller height while keeping the capsule feet anchored at the transform origin.
    /// </summary>
    /// <param name="NewHeight">Target controller height.</param>
    private void ApplyControllerHeight(float NewHeight)
    {
        CharacterController.height = NewHeight;
        CharacterController.center = new Vector3(0f, NewHeight * 0.5f, 0f);
    }

    /// <summary>
    /// Normalizes an angle to the [-180, 180] range.
    /// </summary>
    /// <param name="Angle">Input angle in degrees.</param>
    /// <returns>Normalized angle.</returns>
    private static float NormalizeAngle(float Angle)
    {
        while (Angle > 180f) Angle -= 360f;
        while (Angle < -180f) Angle += 360f;
        return Angle;
    }
}