using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Handles player interaction with both physical carryable objects and world inventory items.
/// This version gathers all player colliders so held objects can ignore the full player body,
/// which is important when the player uses multiple colliders such as box and capsule combinations.
/// </summary>
[RequireComponent(typeof(PlayerInput))]
[RequireComponent(typeof(HotbarController))]
public sealed class PlayerInteractionController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Camera used to cast the interaction ray.")]
    [SerializeField] private Camera PlayerCamera;

    [Tooltip("Optional explicit colliders used by the player body. If empty, colliders will be gathered automatically.")]
    [SerializeField] private Collider[] PlayerColliders;

    [Tooltip("Transform used as the hold anchor target for carried physical objects.")]
    [SerializeField] private Transform HoldAnchor;

    [Tooltip("Hotbar controller used to store and swap inventory items.")]
    [SerializeField] private HotbarController HotbarController;

    [Header("Interaction")]
    [Tooltip("Maximum distance used to detect interactable objects.")]
    [SerializeField] private float InteractionDistance = 4f;

    [Tooltip("Layers considered valid for interaction raycasts.")]
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

    private PhysicsCarryable CurrentHeldCarryable;
    private PhysicsCarryable CurrentLookedCarryable;
    private WorldItem CurrentLookedWorldItem;

    private void Awake()
    {
        PlayerInput = GetComponent<PlayerInput>();

        if (PlayerCamera == null)
        {
            PlayerCamera = Camera.main;
        }

        if (HotbarController == null)
        {
            HotbarController = GetComponent<HotbarController>();
        }

        if (PlayerColliders == null || PlayerColliders.Length == 0)
        {
            PlayerColliders = GetComponents<Collider>();
        }

        EnsureHoldAnchor();

        if (PlayerInput == null || PlayerInput.actions == null)
        {
            Debug.LogError("PlayerInput or Input Actions asset is missing.");
            enabled = false;
            return;
        }

        InteractAction = PlayerInput.actions[InteractActionName];

        if (InteractAction == null)
        {
            Debug.LogError("The interact action named '" + InteractActionName + "' was not found.");
            enabled = false;
            return;
        }

        if (PlayerCamera == null || HoldAnchor == null || HotbarController == null)
        {
            Debug.LogError("One or more required references are missing on PlayerInteractionController.");
            enabled = false;
            return;
        }

        if (PlayerColliders == null || PlayerColliders.Length == 0)
        {
            Debug.LogError("No player colliders were found for PlayerInteractionController.");
            enabled = false;
        }
    }

    private void OnEnable()
    {
        if (InteractAction != null)
        {
            InteractAction.Enable();
        }
    }

    private void OnDisable()
    {
        if (InteractAction != null)
        {
            InteractAction.Disable();
        }
    }

    private void Update()
    {
        UpdateHoldAnchor();
        UpdateLookTargets();
        HandleInteractInput();
    }

    private void UpdateHoldAnchor()
    {
        if (HoldAnchor == null || PlayerCamera == null)
        {
            return;
        }

        float clampedHoldDistance = Mathf.Max(HoldDistance, 1.2f);
        HoldAnchor.SetLocalPositionAndRotation(new Vector3(0f, HoldHeightOffset, clampedHoldDistance), Quaternion.identity);
    }

    private void UpdateLookTargets()
    {
        CurrentLookedWorldItem = null;
        CurrentLookedCarryable = null;

        if (PlayerCamera == null)
        {
            return;
        }

        Ray viewRay = PlayerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (DrawDebugRay)
        {
            Debug.DrawRay(viewRay.origin, viewRay.direction * InteractionDistance, Color.cyan);
        }

        if (!Physics.Raycast(viewRay, out RaycastHit hitInfo, InteractionDistance, InteractionLayers, QueryTriggerInteraction.Ignore))
        {
            return;
        }

        CurrentLookedWorldItem = ResolveWorldItem(hitInfo);
        CurrentLookedCarryable = ResolveCarryable(hitInfo);
    }

    private void HandleInteractInput()
    {
        if (InteractAction == null || !InteractAction.WasPressedThisFrame())
        {
            return;
        }

        if (CurrentLookedWorldItem != null)
        {
            TryInteractWithWorldItem(CurrentLookedWorldItem);
            return;
        }

        if (CurrentHeldCarryable != null)
        {
            DropCurrentCarryable();
            return;
        }

        if (CurrentLookedCarryable != null)
        {
            PickUpCarryable(CurrentLookedCarryable);
            return;
        }

        Log("Interact pressed but no valid target was found.");
    }

    private void TryInteractWithWorldItem(WorldItem worldItem)
    {
        if (worldItem == null)
        {
            return;
        }

        ItemInstance worldItemInstance = worldItem.CreateItemInstance();
        if (worldItemInstance == null)
        {
            Log("The looked world item has no valid item definition.");
            return;
        }

        int insertedSlotIndex;
        bool wasAdded = HotbarController.TryAddItem(worldItemInstance.Clone(), HotbarController.GetSelectedIndex(), out insertedSlotIndex);

        if (wasAdded)
        {
            Log("Picked world item into hotbar slot: " + insertedSlotIndex);
            Destroy(worldItem.gameObject);
            CurrentLookedWorldItem = null;
            return;
        }

        if (!HotbarController.HasSelectedItem())
        {
            Log("Hotbar is full and there is no selected item to swap.");
            return;
        }

        Vector3 spawnPosition = worldItem.GetWorldPosition();
        Quaternion spawnRotation = worldItem.GetWorldRotation();
        Vector3 spawnLinearVelocity = worldItem.GetLinearVelocity();
        Vector3 spawnAngularVelocity = worldItem.GetAngularVelocity();

        Destroy(worldItem.gameObject);
        CurrentLookedWorldItem = null;

        ItemInstance previousSelectedItem = HotbarController.ReplaceSelectedItem(worldItemInstance.Clone());
        if (previousSelectedItem == null)
        {
            Log("Swap failed because the selected hotbar slot was unexpectedly empty.");
            return;
        }

        HotbarController.SpawnWorldItem(previousSelectedItem, spawnPosition, spawnRotation, spawnLinearVelocity, spawnAngularVelocity, false);
        Log("Swapped looked world item with currently selected hotbar item.");
    }

    private WorldItem ResolveWorldItem(RaycastHit hitInfo)
    {
        if (hitInfo.collider == null)
        {
            return null;
        }

        WorldItem worldItem = hitInfo.collider.GetComponent<WorldItem>() ?? hitInfo.collider.GetComponentInParent<WorldItem>();

        if (worldItem != null)
        {
            return worldItem;
        }

        if (hitInfo.rigidbody != null)
        {
            worldItem = hitInfo.rigidbody.GetComponent<WorldItem>() ?? hitInfo.rigidbody.GetComponentInParent<WorldItem>();
        }

        return worldItem;
    }

    private PhysicsCarryable ResolveCarryable(RaycastHit hitInfo)
    {
        if (hitInfo.collider == null)
        {
            return null;
        }

        PhysicsCarryable carryable = hitInfo.collider.GetComponent<PhysicsCarryable>() ?? hitInfo.collider.GetComponentInParent<PhysicsCarryable>();

        if (carryable != null)
        {
            return carryable;
        }

        if (hitInfo.rigidbody != null)
        {
            carryable = hitInfo.rigidbody.GetComponent<PhysicsCarryable>() ?? hitInfo.rigidbody.GetComponentInParent<PhysicsCarryable>();
        }

        return carryable;
    }

    private void PickUpCarryable(PhysicsCarryable targetObject)
    {
        if (targetObject == null)
        {
            return;
        }

        CurrentHeldCarryable = targetObject;
        CurrentHeldCarryable.BeginHold(HoldAnchor, PlayerColliders);
        Log("Picked up carryable object: " + CurrentHeldCarryable.name);
    }

    private void DropCurrentCarryable()
    {
        if (CurrentHeldCarryable == null)
        {
            return;
        }

        Log("Dropped carryable object: " + CurrentHeldCarryable.name);
        CurrentHeldCarryable.EndHold();
        CurrentHeldCarryable = null;
    }

    private void EnsureHoldAnchor()
    {
        if (HoldAnchor != null || !AutoCreateHoldAnchor || PlayerCamera == null)
        {
            return;
        }

        GameObject holdAnchorObject = new GameObject("HoldAnchor");
        HoldAnchor = holdAnchorObject.transform;
        HoldAnchor.SetParent(PlayerCamera.transform, false);
        HoldAnchor.localPosition = new Vector3(0f, HoldHeightOffset, HoldDistance);
        HoldAnchor.localRotation = Quaternion.identity;
    }

    private void Log(string message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[PlayerInteractionController] " + message);
    }
}
