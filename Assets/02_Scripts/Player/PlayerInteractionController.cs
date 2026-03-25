using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Handles player interaction with physical carryable objects and world inventory items.
/// The controller owns the interaction ray, the player collider cache used for collision ignores,
/// and the hold anchor used by spring-joint driven carryable objects.
/// </summary>
[RequireComponent(typeof(PlayerInput))]
[RequireComponent(typeof(HotbarController))]
public sealed class PlayerInteractionController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Camera used to cast the interaction ray.")]
    [SerializeField] private Camera PlayerCamera;

    [Tooltip("Optional explicit colliders used by the player body. If empty, colliders will be gathered from the full hierarchy.")]
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
    [SerializeField] private float HoldDistance = 1.35f;

    [Tooltip("Vertical local offset applied to the hold anchor.")]
    [SerializeField] private float HoldHeightOffset = -0.15f;

    [Header("Debug")]
    [Tooltip("Draws the interaction ray in the Scene view.")]
    [SerializeField] private bool DrawDebugRay = false;

    [Tooltip("Logs interaction events to the console.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Cached PlayerInput used to resolve the interact action.
    /// </summary>
    private PlayerInput PlayerInput;

    /// <summary>
    /// Action used to pick up, drop or interact with looked-at targets.
    /// </summary>
    private InputAction InteractAction;

    /// <summary>
    /// Carryable object currently held by the player.
    /// </summary>
    private PhysicsCarryable CurrentHeldCarryable;

    /// <summary>
    /// Carryable object currently under the center-screen interaction ray.
    /// </summary>
    private PhysicsCarryable CurrentLookedCarryable;

    /// <summary>
    /// World item currently under the center-screen interaction ray.
    /// </summary>
    private WorldItem CurrentLookedWorldItem;

    /// <summary>
    /// Initializes references and gathers player colliders from the full hierarchy when not assigned explicitly.
    /// </summary>
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

        RefreshPlayerColliderCache();
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
    /// Disables the interact action and safely drops the held carryable when the component turns off.
    /// </summary>
    private void OnDisable()
    {
        if (InteractAction != null)
        {
            InteractAction.Disable();
        }

        if (CurrentHeldCarryable != null)
        {
            CurrentHeldCarryable.EndHold();
            CurrentHeldCarryable = null;
        }
    }

    /// <summary>
    /// Updates the hold anchor position, the current look target and the interact input.
    /// </summary>
    private void Update()
    {
        UpdateHoldAnchor();
        UpdateLookTargets();
        HandleInteractInput();
    }

    /// <summary>
    /// Returns the colliders that represent the player body for collision-ignore operations.
    /// </summary>
    public Collider[] GetPlayerColliders()
    {
        return PlayerColliders;
    }

    /// <summary>
    /// Keeps the hold anchor in front of the player camera.
    /// </summary>
    private void UpdateHoldAnchor()
    {
        if (HoldAnchor == null || PlayerCamera == null)
        {
            return;
        }

        HoldAnchor.SetLocalPositionAndRotation(new Vector3(0f, HoldHeightOffset, Mathf.Max(1f, HoldDistance)), Quaternion.identity);
    }

    /// <summary>
    /// Updates the world item and carryable currently looked at by the interaction ray.
    /// </summary>
    private void UpdateLookTargets()
    {
        CurrentLookedWorldItem = null;
        CurrentLookedCarryable = null;

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

        CurrentLookedWorldItem = ResolveWorldItem(HitInfo);
        CurrentLookedCarryable = ResolveCarryable(HitInfo);
    }

    /// <summary>
    /// Processes the interact input to pick up, drop or exchange the current target.
    /// </summary>
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

    /// <summary>
    /// Attempts to insert the looked world item into the hotbar or swap it with the selected slot.
    /// </summary>
    private void TryInteractWithWorldItem(WorldItem WorldItem)
    {
        if (WorldItem == null)
        {
            return;
        }

        ItemInstance WorldItemInstance = WorldItem.CreateItemInstance();
        if (WorldItemInstance == null)
        {
            Log("The looked world item has no valid item definition.");
            return;
        }

        int InsertedSlotIndex;
        bool WasAdded = HotbarController.TryAddItem(WorldItemInstance.Clone(), HotbarController.GetSelectedIndex(), out InsertedSlotIndex);

        if (WasAdded)
        {
            Log("Picked world item into hotbar slot: " + InsertedSlotIndex);
            Destroy(WorldItem.gameObject);
            CurrentLookedWorldItem = null;
            return;
        }

        if (!HotbarController.HasSelectedItem())
        {
            Log("Hotbar is full and there is no selected item to swap.");
            return;
        }

        Vector3 SpawnPosition = WorldItem.GetWorldPosition();
        Quaternion SpawnRotation = WorldItem.GetWorldRotation();
        Vector3 SpawnLinearVelocity = WorldItem.GetLinearVelocity();
        Vector3 SpawnAngularVelocity = WorldItem.GetAngularVelocity();

        Destroy(WorldItem.gameObject);
        CurrentLookedWorldItem = null;

        ItemInstance PreviousSelectedItem = HotbarController.ReplaceSelectedItem(WorldItemInstance.Clone());
        if (PreviousSelectedItem == null)
        {
            Log("Swap failed because the selected hotbar slot was unexpectedly empty.");
            return;
        }

        HotbarController.SpawnWorldItem(PreviousSelectedItem, SpawnPosition, SpawnRotation, SpawnLinearVelocity, SpawnAngularVelocity, false);
        Log("Swapped looked world item with currently selected hotbar item.");
    }

    /// <summary>
    /// Resolves a world item from the current raycast hit.
    /// </summary>
    private WorldItem ResolveWorldItem(RaycastHit HitInfo)
    {
        if (HitInfo.collider == null)
        {
            return null;
        }

        WorldItem WorldItem = HitInfo.collider.GetComponent<WorldItem>() ?? HitInfo.collider.GetComponentInParent<WorldItem>();

        if (WorldItem != null)
        {
            return WorldItem;
        }

        if (HitInfo.rigidbody != null)
        {
            WorldItem = HitInfo.rigidbody.GetComponent<WorldItem>() ?? HitInfo.rigidbody.GetComponentInParent<WorldItem>();
        }

        return WorldItem;
    }

    /// <summary>
    /// Resolves a physical carryable from the current raycast hit.
    /// </summary>
    private PhysicsCarryable ResolveCarryable(RaycastHit HitInfo)
    {
        if (HitInfo.collider == null)
        {
            return null;
        }

        PhysicsCarryable Carryable = HitInfo.collider.GetComponent<PhysicsCarryable>() ?? HitInfo.collider.GetComponentInParent<PhysicsCarryable>();

        if (Carryable != null)
        {
            return Carryable;
        }

        if (HitInfo.rigidbody != null)
        {
            Carryable = HitInfo.rigidbody.GetComponent<PhysicsCarryable>() ?? HitInfo.rigidbody.GetComponentInParent<PhysicsCarryable>();
        }

        return Carryable;
    }

    /// <summary>
    /// Starts carrying the target object through the carryable API.
    /// Stored carryables can be picked up because PhysicsCarryable releases external carry internally.
    /// </summary>
    private void PickUpCarryable(PhysicsCarryable TargetObject)
    {
        if (TargetObject == null)
        {
            return;
        }

        if (!TargetObject.CanBeginHold())
        {
            Log("Carryable cannot begin hold right now: " + TargetObject.name);
            return;
        }

        TargetObject.BeginHold(HoldAnchor, PlayerColliders);

        if (!TargetObject.GetIsHeld())
        {
            Log("Hold request did not succeed for carryable: " + TargetObject.name);
            return;
        }

        CurrentHeldCarryable = TargetObject;
        Log("Picked up carryable object: " + CurrentHeldCarryable.name);
    }

    /// <summary>
    /// Releases the currently held carryable object.
    /// </summary>
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

    /// <summary>
    /// Creates a hold anchor under the player camera when one was not assigned manually.
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

    /// <summary>
    /// Refreshes the player collider cache from the full hierarchy when no manual list is provided.
    /// </summary>
    private void RefreshPlayerColliderCache()
    {
        if (PlayerColliders != null && PlayerColliders.Length > 0)
        {
            return;
        }

        PlayerColliders = PhysicsUtils.GetCachedHierarchyColliders(gameObject, true);
    }

    /// <summary>
    /// Writes an interaction-specific debug message when logging is enabled.
    /// </summary>
    /// <param name="Message">Message to log.</param>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[PlayerInteractionController] " + Message, this);
    }
}