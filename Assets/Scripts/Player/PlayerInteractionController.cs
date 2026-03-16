using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Handles interaction with carryable physical objects using a single forward raycast.
/// Pressing the interact key toggles pickup and drop for the currently looked object.
/// This version is adapted for a Rigidbody based player controller.
/// </summary>
[RequireComponent(typeof(PlayerInput))]
public sealed class PlayerInteractionController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Camera used to cast the interaction ray.")]
    [SerializeField] private Camera PlayerCamera;

    [Tooltip("Player collider used to ignore collisions while holding an object.")]
    [SerializeField] private Collider PlayerCollider;

    [Tooltip("Transform used as the hold anchor target for carried objects.")]
    [SerializeField] private Transform HoldAnchor;

    [Header("Interaction")]
    [Tooltip("Maximum distance used to detect carryable objects.")]
    [SerializeField] private float InteractionDistance = 4f;

    [Tooltip("Layers considered valid for interaction.")]
    [SerializeField] private LayerMask InteractionLayers = ~0;

    [Tooltip("Name of the interact action in the Input Actions asset.")]
    [SerializeField] private string InteractActionName = "Interact";

    [Header("Hold Anchor")]
    [Tooltip("If true, a hold anchor will be created automatically as a child of the camera when none is assigned.")]
    [SerializeField] private bool AutoCreateHoldAnchor = true;

    [Tooltip("Forward distance from the camera to the hold anchor.")]
    [SerializeField] private float HoldDistance = 2.5f;

    [Tooltip("Vertical local offset applied to the hold anchor.")]
    [SerializeField] private float HoldHeightOffset = -0.15f;

    [Header("Debug")]
    [Tooltip("Draws the interaction ray in the Scene view.")]
    [SerializeField] private bool DrawDebugRay = false;

    [Tooltip("Logs interaction events to the console.")]
    [SerializeField] private bool DebugLogs = false;

    private PlayerInput PlayerInput;
    private InputAction InteractAction;

    private PhysicsCarryable CurrentHeldObject;
    private PhysicsCarryable CurrentLookedObject;

    /// <summary>
    /// Initializes references and validates required dependencies.
    /// </summary>
    private void Awake()
    {
        PlayerInput = GetComponent<PlayerInput>();

        if (PlayerCamera == null)
        {
            PlayerCamera = Camera.main;
        }

        if (PlayerCollider == null)
        {
            PlayerCollider = GetComponent<Collider>();
        }

        EnsureHoldAnchor();

        if (PlayerInput == null)
        {
            Debug.LogError("PlayerInput component is missing.");
            enabled = false;
            return;
        }

        if (PlayerInput.actions == null)
        {
            Debug.LogError("PlayerInput has no Input Actions asset assigned.");
            enabled = false;
            return;
        }

        InteractAction = PlayerInput.actions[InteractActionName];

        if (InteractAction == null)
        {
            Debug.LogError($"Input action '{InteractActionName}' was not found.");
            enabled = false;
            return;
        }

        if (PlayerCamera == null)
        {
            Debug.LogError("PlayerCamera reference is missing.");
            enabled = false;
            return;
        }

        if (PlayerCollider == null)
        {
            Debug.LogError("PlayerCollider reference is missing.");
            enabled = false;
            return;
        }

        if (HoldAnchor == null)
        {
            Debug.LogError("HoldAnchor reference is missing.");
            enabled = false;
            return;
        }
    }

    /// <summary>
    /// Enables the interact action when the component becomes active.
    /// </summary>
    private void OnEnable()
    {
        if (InteractAction != null)
        {
            InteractAction.Enable();
        }
    }

    /// <summary>
    /// Disables the interact action when the component becomes inactive.
    /// </summary>
    private void OnDisable()
    {
        if (InteractAction != null)
        {
            InteractAction.Disable();
        }
    }

    /// <summary>
    /// Updates the hold anchor position, target detection and interaction input.
    /// </summary>
    private void Update()
    {
        UpdateHoldAnchor();
        UpdateLookedObject();
        HandleInteractInput();
    }

    /// <summary>
    /// Keeps the hold anchor aligned in front of the camera.
    /// </summary>
    private void UpdateHoldAnchor()
    {
        if (HoldAnchor == null || PlayerCamera == null)
        {
            return;
        }

        HoldAnchor.SetLocalPositionAndRotation(
            new Vector3(0f, HoldHeightOffset, HoldDistance),
            Quaternion.identity
        );
    }

    /// <summary>
    /// Detects the carryable object currently being looked at.
    /// </summary>
    private void UpdateLookedObject()
    {
        CurrentLookedObject = null;

        if (PlayerCamera == null)
        {
            return;
        }

        Ray ViewRay = PlayerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (DrawDebugRay)
        {
            Debug.DrawRay(ViewRay.origin, ViewRay.direction * InteractionDistance, Color.cyan);
        }

        if (!Physics.Raycast(ViewRay, out RaycastHit HitInfo, InteractionDistance, InteractionLayers, QueryTriggerInteraction.Ignore))
        {
            return;
        }

        CurrentLookedObject = ResolveCarryable(HitInfo);
    }

    /// <summary>
    /// Handles pickup and drop input using a toggle interaction.
    /// </summary>
    private void HandleInteractInput()
    {
        if (InteractAction == null || !InteractAction.WasPressedThisFrame())
        {
            return;
        }

        if (CurrentHeldObject != null)
        {
            DropCurrentObject();
            return;
        }

        if (CurrentLookedObject != null)
        {
            PickUpObject(CurrentLookedObject);
        }
        else if (DebugLogs)
        {
            Debug.Log("Interact pressed but no carryable object was detected.");
        }
    }

    /// <summary>
    /// Resolves a PhysicsCarryable from the current raycast hit.
    /// </summary>
    /// <param name="HitInfo">Raycast hit data.</param>
    /// <returns>Detected carryable object or null.</returns>
    private PhysicsCarryable ResolveCarryable(RaycastHit HitInfo)
    {
        if (HitInfo.collider == null)
        {
            return null;
        }

        PhysicsCarryable Carryable = HitInfo.collider.GetComponent<PhysicsCarryable>();

        if (Carryable != null)
        {
            return Carryable;
        }

        Carryable = HitInfo.collider.GetComponentInParent<PhysicsCarryable>();

        if (Carryable != null)
        {
            return Carryable;
        }

        if (HitInfo.rigidbody != null)
        {
            Carryable = HitInfo.rigidbody.GetComponent<PhysicsCarryable>();

            if (Carryable != null)
            {
                return Carryable;
            }

            Carryable = HitInfo.rigidbody.GetComponentInParent<PhysicsCarryable>();

            if (Carryable != null)
            {
                return Carryable;
            }
        }

        return null;
    }

    /// <summary>
    /// Picks up the provided carryable object.
    /// </summary>
    /// <param name="TargetObject">Object to pick up.</param>
    private void PickUpObject(PhysicsCarryable TargetObject)
    {
        if (TargetObject == null)
        {
            return;
        }

        CurrentHeldObject = TargetObject;
        CurrentHeldObject.BeginHold(HoldAnchor, PlayerCollider);

        if (DebugLogs)
        {
            Debug.Log($"Picked up: {CurrentHeldObject.name}");
        }
    }

    /// <summary>
    /// Drops the currently held object.
    /// </summary>
    private void DropCurrentObject()
    {
        if (CurrentHeldObject == null)
        {
            return;
        }

        if (DebugLogs)
        {
            Debug.Log($"Dropped: {CurrentHeldObject.name}");
        }

        CurrentHeldObject.EndHold();
        CurrentHeldObject = null;
    }

    /// <summary>
    /// Ensures a valid hold anchor exists.
    /// </summary>
    private void EnsureHoldAnchor()
    {
        if (HoldAnchor != null || !AutoCreateHoldAnchor || PlayerCamera == null)
        {
            return;
        }

        GameObject HoldAnchorObject = new GameObject("HoldAnchor");
        HoldAnchor = HoldAnchorObject.transform;
        HoldAnchor.SetParent(PlayerCamera.transform, false);
        HoldAnchor.localPosition = new Vector3(0f, HoldHeightOffset, HoldDistance);
        HoldAnchor.localRotation = Quaternion.identity;
    }
}