using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Basic FPS character controller using Unity's New Input System.
/// Supports movement, mouse/gamepad look, jumping and crouching.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerInput))]
public class PlayerController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Reference to the CharacterController used for movement and collision handling.")]
    [SerializeField] private CharacterController CharacterController;

    [Tooltip("Reference to the camera pivot used for vertical look rotation.")]
    [SerializeField] private Transform CameraTransform;

    [Header("Movement")]
    [Tooltip("Horizontal movement speed while standing.")]
    [SerializeField] private float MoveSpeed = 5f;

    [Tooltip("Horizontal movement speed multiplier while crouching.")]
    [SerializeField] private float CrouchSpeedMultiplier = 0.5f;

    [Header("Look")]
    [Tooltip("Horizontal mouse sensitivity.")]
    [SerializeField] private float MouseSensitivityX = 0.1f;

    [Tooltip("Vertical mouse sensitivity.")]
    [SerializeField] private float MouseSensitivityY = 0.1f;

    [Tooltip("Horizontal gamepad look sensitivity.")]
    [SerializeField] private float GamepadSensitivityX = 120f;

    [Tooltip("Vertical gamepad look sensitivity.")]
    [SerializeField] private float GamepadSensitivityY = 120f;

    [Tooltip("Minimum vertical camera angle.")]
    [SerializeField] private float MinPitch = -85f;

    [Tooltip("Maximum vertical camera angle.")]
    [SerializeField] private float MaxPitch = 85f;

    [Header("Jump")]
    [Tooltip("Desired jump height in world units.")]
    [SerializeField] private float JumpHeight = 1.5f;

    [Tooltip("Gravity acceleration applied to the player.")]
    [SerializeField] private float Gravity = -20f;

    [Tooltip("Small downward force applied while grounded to keep the character attached to the floor.")]
    [SerializeField] private float GroundedGravity = -2f;

    [Header("Crouch")]
    [Tooltip("CharacterController height while standing.")]
    [SerializeField] private float StandingHeight = 2f;

    [Tooltip("CharacterController height while crouching.")]
    [SerializeField] private float CrouchingHeight = 1f;

    [Tooltip("Speed used to interpolate the CharacterController height during crouch transitions.")]
    [SerializeField] private float CrouchTransitionSpeed = 10f;

    [Tooltip("Local Y position of the camera while standing.")]
    [SerializeField] private float StandingCameraLocalY = 0.8f;

    [Tooltip("Local Y position of the camera while crouching.")]
    [SerializeField] private float CrouchingCameraLocalY = 0.4f;

    [Tooltip("Speed used to interpolate camera height during crouch transitions.")]
    [SerializeField] private float CameraCrouchTransitionSpeed = 10f;

    [Header("Ground Check")]
    [Tooltip("Layers considered valid ground.")]
    [SerializeField] private LayerMask GroundLayers = ~0;

    [Tooltip("Extra distance used when checking whether the player is grounded.")]
    [SerializeField] private float GroundCheckDistance = 0.2f;

    private PlayerInput PlayerInput;
    private InputAction MoveAction;
    private InputAction LookAction;
    private InputAction JumpAction;
    private InputAction CrouchAction;

    private Vector2 MoveInput;
    private Vector2 LookInput;
    private Vector3 Velocity;

    private bool IsGrounded;
    private bool IsCrouching;

    private float TargetHeight;
    private float TargetCameraLocalY;
    private float Pitch;

    /// <summary>
    /// Initializes references, input actions and default crouch values.
    /// </summary>
    private void Awake()
    {
        if (CharacterController == null)
        {
            CharacterController = GetComponent<CharacterController>();
        }

        PlayerInput = GetComponent<PlayerInput>();

        if (PlayerInput == null)
        {
            Debug.LogError("PlayerInput component is missing.");
            enabled = false;
            return;
        }

        MoveAction = PlayerInput.actions["Move"];
        LookAction = PlayerInput.actions["Look"];
        JumpAction = PlayerInput.actions["Jump"];
        CrouchAction = PlayerInput.actions["Crouch"];

        if (MoveAction == null || LookAction == null || JumpAction == null || CrouchAction == null)
        {
            Debug.LogError("One or more required input actions are missing. Required actions: Move, Look, Jump, Crouch.");
            enabled = false;
            return;
        }

        TargetHeight = StandingHeight;
        TargetCameraLocalY = StandingCameraLocalY;

        if (CameraTransform != null)
        {
            Vector3 LocalPosition = CameraTransform.localPosition;
            LocalPosition.y = StandingCameraLocalY;
            CameraTransform.localPosition = LocalPosition;
        }
    }

    /// <summary>
    /// Subscribes to input callbacks and locks the cursor for FPS control.
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
    /// Updates input, movement, crouch, look and gravity each frame.
    /// </summary>
    private void Update()
    {
        ReadInput();
        CheckGrounded();
        HandleLook();
        HandleCrouchHeight();
        HandleMovement();
        HandleGravity();
    }

    /// <summary>
    /// Reads current movement and look input values.
    /// </summary>
    private void ReadInput()
    {
        MoveInput = MoveAction.ReadValue<Vector2>();
        LookInput = LookAction.ReadValue<Vector2>();
    }

    /// <summary>
    /// Rotates the player horizontally and the camera vertically for FPS view control.
    /// </summary>
    private void HandleLook()
    {
        if (CameraTransform == null)
        {
            return;
        }

        bool IsMouseScheme = PlayerInput.currentControlScheme != null &&
                             PlayerInput.currentControlScheme.ToLower().Contains("keyboard");

        float DeltaTimeMultiplier = IsMouseScheme ? 1f : Time.deltaTime;

        float LookX = IsMouseScheme
            ? LookInput.x * MouseSensitivityX
            : LookInput.x * GamepadSensitivityX * DeltaTimeMultiplier;

        float LookY = IsMouseScheme
            ? LookInput.y * MouseSensitivityY
            : LookInput.y * GamepadSensitivityY * DeltaTimeMultiplier;

        transform.Rotate(Vector3.up * LookX);

        Pitch -= LookY;
        Pitch = Mathf.Clamp(Pitch, MinPitch, MaxPitch);

        Vector3 CameraEulerAngles = CameraTransform.localEulerAngles;
        CameraEulerAngles.x = Pitch;
        CameraEulerAngles.y = 0f;
        CameraEulerAngles.z = 0f;
        CameraTransform.localEulerAngles = CameraEulerAngles;
    }

    /// <summary>
    /// Moves the player relative to the current horizontal facing direction.
    /// </summary>
    private void HandleMovement()
    {
        Vector3 Forward = transform.forward;
        Vector3 Right = transform.right;

        Forward.y = 0f;
        Right.y = 0f;

        Forward.Normalize();
        Right.Normalize();

        Vector3 MoveDirection = (Forward * MoveInput.y) + (Right * MoveInput.x);

        if (MoveDirection.sqrMagnitude > 1f)
        {
            MoveDirection.Normalize();
        }

        float CurrentSpeed = IsCrouching ? MoveSpeed * CrouchSpeedMultiplier : MoveSpeed;
        Vector3 HorizontalMovement = MoveDirection * CurrentSpeed;

        CharacterController.Move(HorizontalMovement * Time.deltaTime);
    }

    /// <summary>
    /// Applies gravity and vertical movement to the CharacterController.
    /// </summary>
    private void HandleGravity()
    {
        if (IsGrounded && Velocity.y < 0f)
        {
            Velocity.y = GroundedGravity;
        }
        else
        {
            Velocity.y += Gravity * Time.deltaTime;
        }

        CharacterController.Move(Velocity * Time.deltaTime);
    }

    /// <summary>
    /// Smoothly updates the CharacterController height and camera height when crouching or standing.
    /// </summary>
    private void HandleCrouchHeight()
    {
        float NewHeight = Mathf.Lerp(CharacterController.height, TargetHeight, CrouchTransitionSpeed * Time.deltaTime);
        CharacterController.height = NewHeight;

        Vector3 ControllerCenter = CharacterController.center;
        ControllerCenter.y = NewHeight * 0.5f;
        CharacterController.center = ControllerCenter;

        if (CameraTransform != null)
        {
            Vector3 CameraLocalPosition = CameraTransform.localPosition;
            CameraLocalPosition.y = Mathf.Lerp(CameraLocalPosition.y, TargetCameraLocalY, CameraCrouchTransitionSpeed * Time.deltaTime);
            CameraTransform.localPosition = CameraLocalPosition;
        }
    }

    /// <summary>
    /// Checks whether the player is currently grounded using a sphere cast.
    /// </summary>
    private void CheckGrounded()
    {
        Vector3 SphereOrigin = transform.position + CharacterController.center;
        float SphereRadius = CharacterController.radius * 0.9f;
        float CastDistance = (CharacterController.height * 0.5f) + GroundCheckDistance;

        IsGrounded = Physics.SphereCast(
            SphereOrigin,
            SphereRadius,
            Vector3.down,
            out _,
            CastDistance,
            GroundLayers,
            QueryTriggerInteraction.Ignore
        );
    }

    /// <summary>
    /// Executes jump logic when the jump input is performed.
    /// </summary>
    /// <param name="Context">Input callback context.</param>
    private void OnJumpPerformed(InputAction.CallbackContext Context)
    {
        if (!IsGrounded)
        {
            return;
        }

        if (IsCrouching)
        {
            return;
        }

        Velocity.y = Mathf.Sqrt(JumpHeight * -2f * Gravity);
    }

    /// <summary>
    /// Toggles crouch state when the crouch input is performed.
    /// </summary>
    /// <param name="Context">Input callback context.</param>
    private void OnCrouchPerformed(InputAction.CallbackContext Context)
    {
        if (IsCrouching)
        {
            if (CanStandUp())
            {
                SetCrouchState(false);
            }
        }
        else
        {
            SetCrouchState(true);
        }
    }

    /// <summary>
    /// Sets the crouch state and updates target controller and camera values.
    /// </summary>
    /// <param name="NewIsCrouching">Desired crouch state.</param>
    private void SetCrouchState(bool NewIsCrouching)
    {
        IsCrouching = NewIsCrouching;
        TargetHeight = IsCrouching ? CrouchingHeight : StandingHeight;
        TargetCameraLocalY = IsCrouching ? CrouchingCameraLocalY : StandingCameraLocalY;
    }

    /// <summary>
    /// Checks if there is enough free space above the player to stand up.
    /// </summary>
    /// <returns>True if the player can stand up safely.</returns>
    private bool CanStandUp()
    {
        float HeightDifference = StandingHeight - CharacterController.height;

        if (HeightDifference <= 0f)
        {
            return true;
        }

        Vector3 SphereOrigin = transform.position + CharacterController.center;
        float SphereRadius = CharacterController.radius * 0.95f;

        return !Physics.SphereCast(
            SphereOrigin,
            SphereRadius,
            Vector3.up,
            out _,
            HeightDifference,
            GroundLayers,
            QueryTriggerInteraction.Ignore
        );
    }

    /// <summary>
    /// Draws debug gizmos for the ground check and stand up check areas.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (CharacterController == null)
        {
            CharacterController = GetComponent<CharacterController>();
        }

        if (CharacterController == null)
        {
            return;
        }

        Gizmos.color = Color.yellow;

        Vector3 SphereOrigin = transform.position + CharacterController.center;
        float SphereRadius = CharacterController.radius * 0.9f;
        float CastDistance = (CharacterController.height * 0.5f) + GroundCheckDistance;
        Vector3 EndPoint = SphereOrigin + (Vector3.down * CastDistance);

        Gizmos.DrawWireSphere(SphereOrigin, SphereRadius);
        Gizmos.DrawWireSphere(EndPoint, SphereRadius);
        Gizmos.DrawLine(SphereOrigin, EndPoint);
    }
}