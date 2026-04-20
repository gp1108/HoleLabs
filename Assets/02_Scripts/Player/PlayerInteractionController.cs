using UnityEngine;

/// <summary>
/// Central contextual interaction controller for the player.
/// This component owns the interact input consumption and resolves world interaction priority:
/// shop station, world item, held carryable release, carryable pickup and money collection.
/// </summary>
[RequireComponent(typeof(HotbarController))]
[RequireComponent(typeof(PlayerInputReader))]
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

    [Tooltip("Centralized player input reader used as the only source of interaction input.")]
    [SerializeField] private PlayerInputReader PlayerInputReader;

    [Tooltip("Optional upgrade shop interactor used as the highest-priority contextual interaction.")]
    [SerializeField] private UpgradeShopInteractor UpgradeShopInteractor;

    [Tooltip("Optional money collector used as the final contextual interaction fallback.")]
    [SerializeField] private MoneyCollector MoneyCollector;

    [Header("Interaction")]
    [Tooltip("Maximum distance used to detect interactable objects.")]
    [SerializeField] private float InteractionDistance = 4f;

    [Tooltip("Layers considered valid for interaction raycasts.")]
    [SerializeField] private LayerMask InteractionLayers = ~0;

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
    /// Whether interaction input is currently blocked by an external modal state.
    /// </summary>
    private bool IsExternalInteractionBlocked;

    /// <summary>
    /// Allows external systems to block or restore interaction processing.
    /// </summary>
    /// <param name="IsBlocked">True to block interactions, false to restore them.</param>
    public void SetExternalInteractionBlocked(bool IsBlocked)
    {
        IsExternalInteractionBlocked = IsBlocked;
    }

    /// <summary>
    /// Initializes references and gathers player colliders from the full hierarchy when not assigned explicitly.
    /// </summary>
    private void Awake()
    {
        if (PlayerCamera == null)
        {
            PlayerCamera = Camera.main;
        }

        if (HotbarController == null)
        {
            HotbarController = GetComponent<HotbarController>();
        }

        if (PlayerInputReader == null)
        {
            PlayerInputReader = GetComponent<PlayerInputReader>();
        }

        if (UpgradeShopInteractor == null)
        {
            UpgradeShopInteractor = GetComponent<UpgradeShopInteractor>();
        }

        if (MoneyCollector == null)
        {
            MoneyCollector = GetComponent<MoneyCollector>();
        }

        RefreshPlayerColliderCache();
        EnsureHoldAnchor();

        if (PlayerCamera == null || HoldAnchor == null || HotbarController == null || PlayerInputReader == null)
        {
            Debug.LogError("One or more required references are missing on PlayerInteractionController.", this);
            enabled = false;
            return;
        }

        if (PlayerColliders == null || PlayerColliders.Length == 0)
        {
            Debug.LogError("No player colliders were found for PlayerInteractionController.", this);
            enabled = false;
        }
    }

    /// <summary>
    /// Subscribes interact callbacks from the centralized input reader.
    /// </summary>
    private void OnEnable()
    {
        if (PlayerInputReader != null)
        {
            PlayerInputReader.InteractPerformed += HandleInteractPerformed;
        }
    }

    /// <summary>
    /// Unsubscribes interact callbacks and safely drops the held carryable when the component turns off.
    /// </summary>
    private void OnDisable()
    {
        if (PlayerInputReader != null)
        {
            PlayerInputReader.InteractPerformed -= HandleInteractPerformed;
        }

        if (CurrentHeldCarryable != null)
        {
            CurrentHeldCarryable.EndHold();
            CurrentHeldCarryable = null;
        }
    }

    /// <summary>
    /// Updates the hold anchor position and the current look targets.
    /// </summary>
    private void Update()
    {
        if (IsExternalInteractionBlocked)
        {
            return;
        }

        UpdateHoldAnchor();
        UpdateLookTargets();
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
    /// Resolves the contextual interaction priority when the interact input is pressed.
    /// </summary>
    private void HandleInteractPerformed()
    {
        if (IsExternalInteractionBlocked)
        {
            return;
        }

        if (TryOpenNearbyShop())
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

        if (TryCollectMoney())
        {
            return;
        }

        Log("Interact pressed but no valid contextual target was found.");
    }

    /// <summary>
    /// Tries to open the nearby shop station if one is currently available.
    /// </summary>
    private bool TryOpenNearbyShop()
    {
        if (UpgradeShopInteractor == null)
        {
            return false;
        }

        if (!UpgradeShopInteractor.HasNearbyStation())
        {
            return false;
        }

        return UpgradeShopInteractor.TryOpenNearbyStation();
    }

    /// <summary>
    /// Tries to collect a looked money pickup through the optional money collector helper.
    /// </summary>
    private bool TryCollectMoney()
    {
        if (MoneyCollector == null)
        {
            return false;
        }

        return MoneyCollector.TryCollectCurrentLookedMoney();
    }

    /// <summary>
    /// Attempts to insert the looked world item into the hotbar or swap it with the selected slot.
    /// Scene-placed persistent world items are hidden instead of destroyed so they can be restored by save/load.
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

        ScenePlacedWorldItemPersistence ScenePersistence = WorldItem.GetComponentInParent<ScenePlacedWorldItemPersistence>();

        int InsertedSlotIndex;
        bool WasAdded = HotbarController.TryAddItem(WorldItemInstance.Clone(), HotbarController.GetSelectedIndex(), out InsertedSlotIndex);

        if (WasAdded)
        {
            Log("Picked world item into hotbar slot: " + InsertedSlotIndex);

            if (ScenePersistence != null)
            {
                ScenePersistence.SetPresent(false);
            }
            else
            {
                Destroy(WorldItem.gameObject);
            }

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

        if (ScenePersistence != null)
        {
            ScenePersistence.SetPresent(false);
        }
        else
        {
            Destroy(WorldItem.gameObject);
        }

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
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[PlayerInteractionController] " + Message, this);
    }
}