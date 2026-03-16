using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Handles player interaction with carryable physical objects using a single forward raycast.
/// Designed to be scalable by centralizing detection in the player instead of in each object.
/// </summary>
[RequireComponent(typeof(PlayerInput))]
public class PlayerInteractionController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Camera used to perform the interaction raycast.")]
    [SerializeField] private Camera PlayerCamera;

    [Tooltip("Transform used as the target position for held objects.")]
    [SerializeField] private Transform HoldPoint;

    [Header("Interaction Settings")]
    [Tooltip("Maximum distance used to detect carryable objects.")]
    [SerializeField] private float InteractionDistance = 4f;

    [Tooltip("Layers considered valid for interaction.")]
    [SerializeField] private LayerMask InteractionLayers;

    [Header("Input")]
    [Tooltip("Name of the interact action inside the assigned Input Actions asset.")]
    [SerializeField] private string InteractActionName = "Interact";

    private PlayerInput PlayerInput;
    private InputAction InteractAction;

    private PhysicsCarryable CurrentHeldObject;
    private PhysicsCarryable CurrentLookedObject;

    /// <summary>
    /// Caches input references and required scene references.
    /// </summary>
    private void Awake()
    {
        PlayerInput = GetComponent<PlayerInput>();

        if (PlayerInput == null)
        {
            Debug.LogError("PlayerInput component is missing.");
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
            PlayerCamera = Camera.main;
        }

        if (PlayerCamera == null)
        {
            Debug.LogError("Player camera reference is missing.");
            enabled = false;
            return;
        }

        if (HoldPoint == null)
        {
            Debug.LogError("HoldPoint reference is missing.");
            enabled = false;
            return;
        }
    }

    /// <summary>
    /// Subscribes to the interact input callback.
    /// </summary>
    private void OnEnable()
    {
        InteractAction.performed += OnInteractPerformed;
    }

    /// <summary>
    /// Unsubscribes from the interact input callback.
    /// </summary>
    private void OnDisable()
    {
        InteractAction.performed -= OnInteractPerformed;
    }

    /// <summary>
    /// Updates the currently looked carryable object using a single forward raycast.
    /// </summary>
    private void Update()
    {
        UpdateLookedObject();
    }

    /// <summary>
    /// Detects the carryable object currently being looked at.
    /// </summary>
    private void UpdateLookedObject()
    {
        CurrentLookedObject = null;

        Ray CameraRay = new Ray(PlayerCamera.transform.position, PlayerCamera.transform.forward);

        if (Physics.Raycast(CameraRay, out RaycastHit HitInfo, InteractionDistance, InteractionLayers, QueryTriggerInteraction.Ignore))
        {
            CurrentLookedObject = HitInfo.collider.GetComponentInParent<PhysicsCarryable>();
        }
    }

    /// <summary>
    /// Handles picking up or dropping a carryable object when the interact key is pressed.
    /// </summary>
    /// <param name="Context">Input callback context.</param>
    private void OnInteractPerformed(InputAction.CallbackContext Context)
    {
        if (CurrentHeldObject != null)
        {
            DropCurrentObject();
            return;
        }

        if (CurrentLookedObject != null)
        {
            PickUpObject(CurrentLookedObject);
        }
    }

    /// <summary>
    /// Starts carrying the provided object.
    /// </summary>
    /// <param name="TargetObject">Object to be picked up.</param>
    private void PickUpObject(PhysicsCarryable TargetObject)
    {
        CurrentHeldObject = TargetObject;
        CurrentHeldObject.PickUp(HoldPoint);
    }

    /// <summary>
    /// Drops the currently held object if any.
    /// </summary>
    private void DropCurrentObject()
    {
        if (CurrentHeldObject == null)
        {
            return;
        }

        CurrentHeldObject.Drop();
        CurrentHeldObject = null;
    }
}