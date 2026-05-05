using UnityEngine;

/// <summary>
/// First person CharacterController motor with stable crouch obstruction checks, save/load crouch restore,
/// ceiling hit cancellation, frame-rate independent mouse look and explicit moving platform carry support.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerInputReader))]
public sealed class PlayerController : MonoBehaviour
{
    /// <summary>
    /// Defines how the cached look input must be converted into rotation degrees.
    /// </summary>
    private enum LookInputTimingMode
    {
        /// <summary>
        /// Use this for mouse delta input. Mouse deltas are already frame-relative and must not be multiplied by Time.deltaTime.
        /// </summary>
        FrameDelta,

        /// <summary>
        /// Use this for gamepad stick input. Stick values are continuous input and must be multiplied by Time.deltaTime.
        /// </summary>
        TimeScaled
    }

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
    [Tooltip("Horizontal look sensitivity. In FrameDelta mode, the value is normalized against the reference frame rate so existing 60 FPS sensitivity is preserved.")]
    [SerializeField] private float LookSensitivityX = 1.5f;

    [Tooltip("Vertical look sensitivity. In FrameDelta mode, the value is normalized against the reference frame rate so existing 60 FPS sensitivity is preserved.")]
    [SerializeField] private float LookSensitivityY = 1.5f;

    [Tooltip("Minimum vertical camera angle.")]
    [SerializeField] private float MinPitch = -85f;

    [Tooltip("Maximum vertical camera angle.")]
    [SerializeField] private float MaxPitch = 85f;

    [Header("Look Timing")]
    [Tooltip("How look input is converted into rotation. Use FrameDelta for mouse delta and TimeScaled for gamepad stick input.")]
    [SerializeField] private LookInputTimingMode LookTiming = LookInputTimingMode.FrameDelta;

    [Tooltip("Reference frame rate used by FrameDelta look mode to preserve the previous 60 FPS sensitivity while removing frame-rate dependency.")]
    [SerializeField] private float FrameDeltaReferenceFrameRate = 60f;

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

    [Tooltip("Vertical velocity forced after hitting a ceiling. Use 0 or a small negative value.")]
    [SerializeField] private float CeilingHitVerticalVelocity = -0.25f;

    [Tooltip("Buffered time window that still accepts a recent jump press.")]
    [SerializeField] private float JumpBufferTime = 0.12f;

    [Tooltip("Grace time after losing grounded where jump is still allowed.")]
    [SerializeField] private float GroundGraceTime = 0.12f;

    [Tooltip("Grace time after losing platform support where jump is still allowed.")]
    [SerializeField] private float PlatformGraceTime = 0.12f;

    [Header("Crouch")]
    [Tooltip("If true, crouch toggles on press. If false, crouch is hold-based but restored save crouch persists until the next crouch press.")]
    [SerializeField] private bool ToggleCrouch = true;

    [Tooltip("CharacterController height while standing.")]
    [SerializeField] private float StandingHeight = 2f;

    [Tooltip("CharacterController height while crouching.")]
    [SerializeField] private float CrouchingHeight = 1.2f;

    [Tooltip("Interpolation speed used when changing capsule and camera pivot height.")]
    [SerializeField] private float CrouchTransitionSpeed = 12f;

    [Tooltip("Local Y offset applied to the camera pivot while crouching.")]
    [SerializeField] private float CrouchCameraOffset = -0.45f;

    [Tooltip("Standing local Y used by the camera pivot. Use a negative value to keep the current scene value.")]
    [SerializeField] private float StandingCameraPivotLocalY = 1.55f;

    [Header("Crouch Obstruction")]
    [Tooltip("Layers that can block the player from standing up. Exclude player-only and interaction-only layers.")]
    [SerializeField] private LayerMask StandUpBlockerLayers = ~0;

    [Tooltip("Trigger interaction mode used by the stand-up obstruction probe.")]
    [SerializeField] private QueryTriggerInteraction StandUpTriggerInteraction = QueryTriggerInteraction.Ignore;

    [Tooltip("Small reduction applied to the stand-up probe radius to avoid false positives from skin contact.")]
    [SerializeField] private float StandUpProbeRadiusShrink = 0.02f;

    [Tooltip("Small vertical clearance removed from the stand-up probe to avoid touching floor and ceiling by numerical noise.")]
    [SerializeField] private float StandUpProbeVerticalShrink = 0.03f;

    [Tooltip("Maximum number of colliders that can be inspected by the stand-up obstruction probe.")]
    [SerializeField] private int StandUpProbeBufferSize = 16;

    /// <summary>
    /// Whether camera look is temporarily blocked by an external interaction such as lever dragging.
    /// </summary>
    private bool IsExternalLookBlocked;

    /// <summary>
    /// Whether movement input is temporarily blocked by an external modal state.
    /// </summary>
    private bool IsExternalMovementBlocked;

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
    /// Whether the player is currently crouching, either by input, save restore or forced head clearance.
    /// </summary>
    public bool IsCrouching { get; private set; }

    /// <summary>
    /// Whether the current crouch is forced because the stand-up probe is blocked.
    /// </summary>
    public bool IsCrouchForced { get; private set; }

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
    /// Whether the player currently wants to crouch through toggle mode or save restore.
    /// </summary>
    private bool WantsToCrouch;

    /// <summary>
    /// Whether crouch was restored from save while using hold crouch mode.
    /// This keeps the player crouched after load until the next crouch press releases the restore lock.
    /// </summary>
    private bool SavedHoldCrouchLock;

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
    /// Runtime allocation-free collider buffer used by the stand-up obstruction probe.
    /// </summary>
    private Collider[] StandUpProbeHits;

    /// <summary>
    /// Gets the current player world position.
    /// </summary>
    public Vector3 GetWorldPosition()
    {
        return transform.position;
    }

    /// <summary>
    /// Allows external systems to temporarily block or restore camera look processing.
    /// </summary>
    /// <param name="IsBlocked">True to block look input, false to restore it.</param>
    public void SetExternalLookBlocked(bool IsBlocked)
    {
        IsExternalLookBlocked = IsBlocked;
    }

    /// <summary>
    /// Allows external systems to temporarily block or restore movement processing.
    /// </summary>
    /// <param name="IsBlocked">True to block movement, false to restore it.</param>
    public void SetExternalMovementBlocked(bool IsBlocked)
    {
        IsExternalMovementBlocked = IsBlocked;
    }

    /// <summary>
    /// Restores the saved player pose and crouch state in a stable way.
    /// The controller is temporarily disabled so repositioning does not fight CharacterController collision resolution.
    /// </summary>
    /// <param name="Position">Saved world position.</param>
    /// <param name="SavedIsCrouching">Saved crouch state.</param>
    public void ApplySavedState(Vector3 Position, bool SavedIsCrouching)
    {
        if (CharacterController == null)
        {
            return;
        }

        bool WasEnabled = CharacterController.enabled;
        CharacterController.enabled = false;
        transform.position = Position;
        CharacterController.enabled = WasEnabled;

        WantsToCrouch = SavedIsCrouching;
        SavedHoldCrouchLock = SavedIsCrouching;
        IsCrouching = SavedIsCrouching;
        IsCrouchForced = false;

        ApplyControllerHeight(SavedIsCrouching ? CrouchingHeight : StandingHeight);
        ApplyCameraPivotHeight(SavedIsCrouching, true);

        VerticalVelocity = 0f;
        Velocity = Vector3.zero;
        JumpRequested = false;
        JumpBufferTimer = 0f;
        GroundGraceTimer = 0f;
        PlatformGraceTimer = 0f;
        LastPlatformUpwardSpeed = 0f;
        CurrentPlatform = null;
    }

    /// <summary>
    /// Caches required references and initializes the controller dimensions.
    /// </summary>
    private void Awake()
    {
        if (CharacterController == null) CharacterController = GetComponent<CharacterController>();
        if (PlayerInputReader == null) PlayerInputReader = GetComponent<PlayerInputReader>();
        if (ViewRoot == null) ViewRoot = transform;
        if (PlayerCameraComponent == null) PlayerCameraComponent = GetComponentInChildren<Camera>(true);
        if (CameraPivot == null && PlayerCameraComponent != null) CameraPivot = PlayerCameraComponent.transform.parent;
        if (CameraPivot == null) CameraPivot = ViewRoot;

        StandUpProbeHits = new Collider[Mathf.Max(4, StandUpProbeBufferSize)];
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
    /// Updates look, platform carry, crouch, movement and support release in deterministic order.
    /// </summary>
    private void Update()
    {
        UpdateLook();
        ApplyPlatformCarry();
        UpdateJumpSupport();
        UpdateCrouch();
        UpdateMovement();

        if (!CharacterController.isGrounded && VerticalVelocity <= 0f && PlatformGraceTimer <= 0f)
        {
            CurrentPlatform = null;
        }
    }

    /// <summary>
    /// Rotates yaw and pitch from the cached look input using the configured timing mode.
    /// Mouse delta input must be frame-based, while gamepad stick input must be time-scaled.
    /// </summary>
    private void UpdateLook()
    {
        if (IsExternalLookBlocked)
        {
            return;
        }

        Vector2 LookInput = PlayerInputReader.Look;
        float LookMultiplier = GetLookMultiplier();

        ViewRoot.Rotate(0f, LookInput.x * LookSensitivityX * LookMultiplier, 0f, Space.World);
        PitchDegrees = Mathf.Clamp(PitchDegrees - LookInput.y * LookSensitivityY * LookMultiplier, MinPitch, MaxPitch);
        CameraPivot.localRotation = Quaternion.Euler(PitchDegrees, 0f, 0f);
    }

    /// <summary>
    /// Gets the multiplier used to convert look input into degrees for the current timing mode.
    /// FrameDelta mode preserves the old 60 FPS feel without making sensitivity depend on the current frame rate.
    /// </summary>
    /// <returns>Look input multiplier.</returns>
    private float GetLookMultiplier()
    {
        if (LookTiming == LookInputTimingMode.TimeScaled)
        {
            return Time.deltaTime;
        }

        return 1f / Mathf.Max(1f, FrameDeltaReferenceFrameRate);
    }

    /// <summary>
    /// Updates crouch state, forced head clearance, capsule height and camera pivot height.
    /// </summary>
    private void UpdateCrouch()
    {
        bool InputCrouchWanted = GetInputCrouchWanted();
        bool WantsStandingHeight = !InputCrouchWanted;
        bool CanStand = !WantsStandingHeight || CanUseHeight(StandingHeight);

        IsCrouchForced = WantsStandingHeight && !CanStand;
        IsCrouching = InputCrouchWanted || IsCrouchForced;

        float TargetHeight = IsCrouching ? CrouchingHeight : StandingHeight;
        float NewHeight = Mathf.MoveTowards(CharacterController.height, TargetHeight, CrouchTransitionSpeed * Time.deltaTime);

        if (NewHeight > CharacterController.height && !CanUseHeight(NewHeight))
        {
            NewHeight = CharacterController.height;
            IsCrouchForced = true;
            IsCrouching = true;
        }

        ApplyControllerHeight(NewHeight);
        ApplyCameraPivotHeight(IsCrouching, false);
    }

    /// <summary>
    /// Returns whether the player currently wants crouch from input, toggle state or save restore state.
    /// </summary>
    private bool GetInputCrouchWanted()
    {
        if (ToggleCrouch)
        {
            return WantsToCrouch;
        }

        return PlayerInputReader.IsCrouchHeld || SavedHoldCrouchLock;
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
    /// Moves the controller using cached input plus jump, gravity and ceiling-hit cancellation logic.
    /// </summary>
    private void UpdateMovement()
    {
        bool HasJumpSupport = CharacterController.isGrounded || GroundGraceTimer > 0f || PlatformGraceTimer > 0f;

        if (!IsExternalMovementBlocked && JumpRequested && HasJumpSupport)
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

        MoveInput = IsExternalMovementBlocked ? Vector2.zero : PlayerInputReader.Move;

        Vector3 MoveDirection = ViewRoot.forward * MoveInput.y + ViewRoot.right * MoveInput.x;
        float SelectedMoveSpeed = IsCrouching ? CrouchSpeed : (PlayerInputReader.IsSprintHeld ? SprintSpeed : WalkSpeed);
        Vector3 HorizontalMotion = MoveDirection.normalized * SelectedMoveSpeed;
        Vector3 Motion = HorizontalMotion + Vector3.up * VerticalVelocity;

        CollisionFlags CollisionFlags = CharacterController.Move(Motion * Time.deltaTime);

        if ((CollisionFlags & CollisionFlags.Above) != 0 && VerticalVelocity > CeilingHitVerticalVelocity)
        {
            VerticalVelocity = CeilingHitVerticalVelocity;
            JumpRequested = false;
            JumpBufferTimer = 0f;
        }

        Velocity = HorizontalMotion + Vector3.up * VerticalVelocity;
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
    /// Handles crouch press for toggle mode and clears the save-restored hold crouch lock in hold mode.
    /// </summary>
    private void OnCrouchPerformed()
    {
        if (ToggleCrouch)
        {
            WantsToCrouch = !WantsToCrouch;
            return;
        }

        SavedHoldCrouchLock = false;
    }

    /// <summary>
    /// Applies a controller height while keeping the capsule feet anchored at the transform origin.
    /// </summary>
    /// <param name="NewHeight">Target controller height.</param>
    private void ApplyControllerHeight(float NewHeight)
    {
        NewHeight = Mathf.Max(NewHeight, CharacterController.radius * 2f);
        CharacterController.height = NewHeight;
        CharacterController.center = new Vector3(0f, NewHeight * 0.5f, 0f);
    }

    /// <summary>
    /// Moves the camera pivot toward the current crouch or standing view height.
    /// </summary>
    /// <param name="UseCrouchHeight">True to use crouch camera height, false to use standing camera height.</param>
    /// <param name="ApplyInstantly">True to snap immediately instead of interpolating.</param>
    private void ApplyCameraPivotHeight(bool UseCrouchHeight, bool ApplyInstantly)
    {
        Vector3 TargetPivot = CameraPivotStandingLocalPosition + Vector3.up * (UseCrouchHeight ? CrouchCameraOffset : 0f);

        if (ApplyInstantly)
        {
            CameraPivot.localPosition = TargetPivot;
            return;
        }

        CameraPivot.localPosition = Vector3.MoveTowards(CameraPivot.localPosition, TargetPivot, CrouchTransitionSpeed * Time.deltaTime);
    }

    /// <summary>
    /// Checks whether the character capsule can occupy the requested height without intersecting blocking geometry.
    /// Own player colliders are ignored so the probe cannot self-block.
    /// </summary>
    /// <param name="RequestedHeight">Capsule height to validate.</param>
    /// <returns>True when the requested height is clear.</returns>
    private bool CanUseHeight(float RequestedHeight)
    {
        if (StandUpProbeHits == null || StandUpProbeHits.Length != Mathf.Max(4, StandUpProbeBufferSize))
        {
            StandUpProbeHits = new Collider[Mathf.Max(4, StandUpProbeBufferSize)];
        }

        float SafeRadius = Mathf.Max(0.01f, CharacterController.radius - Mathf.Max(0f, StandUpProbeRadiusShrink));
        float SafeHeight = Mathf.Max(SafeRadius * 2f, RequestedHeight - Mathf.Max(0f, StandUpProbeVerticalShrink));
        Vector3 Bottom = transform.position + Vector3.up * SafeRadius;
        Vector3 Top = transform.position + Vector3.up * (SafeHeight - SafeRadius);

        int HitCount = Physics.OverlapCapsuleNonAlloc(
            Bottom,
            Top,
            SafeRadius,
            StandUpProbeHits,
            StandUpBlockerLayers,
            StandUpTriggerInteraction);

        for (int Index = 0; Index < HitCount; Index++)
        {
            Collider HitCollider = StandUpProbeHits[Index];
            StandUpProbeHits[Index] = null;

            if (HitCollider == null)
            {
                continue;
            }

            if (HitCollider == CharacterController)
            {
                continue;
            }

            if (HitCollider.transform == transform || HitCollider.transform.IsChildOf(transform))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// Applies full platform carry to the character controller, including the displacement of the
    /// player support point caused by platform rotation and the visual yaw rotation of the player.
    /// </summary>
    private void ApplyPlatformCarry()
    {
        if (CurrentPlatform == null)
        {
            LastPlatformUpwardSpeed = 0f;
            return;
        }

        if (CurrentPlatform is not Component PlatformComponent || PlatformComponent == null || !PlatformComponent.gameObject.activeInHierarchy)
        {
            CurrentPlatform = null;
            LastPlatformUpwardSpeed = 0f;
            return;
        }

        Vector3 SupportPoint = transform.position;
        Vector3 PlatformPointDelta = CurrentPlatform.GetWorldPointDelta(SupportPoint);

        if (PlatformPointDelta.sqrMagnitude > 0f)
        {
            CharacterController.Move(PlatformPointDelta);
        }

        ApplyPlatformRotation(CurrentPlatform.DeltaRotation);
        LastPlatformUpwardSpeed = Mathf.Max(0f, PlatformPointDelta.y / Mathf.Max(Time.deltaTime, 0.0001f));
    }

    /// <summary>
    /// Applies the carrier yaw rotation to the player view root so the player orientation rotates
    /// with the platform while preserving the independent camera pitch controlled by the camera pivot.
    /// </summary>
    /// <param name="CarrierRotationDelta">Frame rotation delta received from the current platform.</param>
    private void ApplyPlatformRotation(Quaternion CarrierRotationDelta)
    {
        if (ViewRoot == null)
        {
            return;
        }

        Vector3 DeltaEulerAngles = CarrierRotationDelta.eulerAngles;
        float DeltaYaw = NormalizeAngle(DeltaEulerAngles.y);

        if (Mathf.Abs(DeltaYaw) <= 0.0001f)
        {
            return;
        }

        ViewRoot.Rotate(0f, DeltaYaw, 0f, Space.World);
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
