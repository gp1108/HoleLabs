using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Centralized runtime input reader for the player.
/// This component only reads input from the active PlayerInput and exposes
/// cached values plus high-level events for gameplay systems.
/// 
/// This implementation intentionally uses frame polling for both continuous
/// and discrete inputs so it stays fully aligned with the rest of the project,
/// which already relies on WasPressedThisFrame, WasReleasedThisFrame and IsPressed.
/// </summary>
[RequireComponent(typeof(PlayerInput))]
public sealed class PlayerInputReader : MonoBehaviour
{
    [Header("Action Names")]
    [Tooltip("Exact action name used for movement input.")]
    [SerializeField] private string MoveActionName = "Move";

    [Tooltip("Exact action name used for camera look input.")]
    [SerializeField] private string LookActionName = "Look";

    [Tooltip("Exact action name used for the primary use input.")]
    [SerializeField] private string UsePrimaryActionName = "UsePrimary";

    [Tooltip("Exact action name used for the secondary use input.")]
    [SerializeField] private string UseSecondaryActionName = "UseSecondary";

    [Tooltip("Exact action name used for the interact input.")]
    [SerializeField] private string InteractActionName = "Interact";

    [Tooltip("Exact action name used for crouch input.")]
    [SerializeField] private string CrouchActionName = "Crouch";

    [Tooltip("Exact action name used for jump input.")]
    [SerializeField] private string JumpActionName = "Jump";

    [Tooltip("Exact action name used for previous selection input.")]
    [SerializeField] private string PreviousActionName = "Previous";

    [Tooltip("Exact action name used for next selection input.")]
    [SerializeField] private string NextActionName = "Next";

    [Tooltip("Exact action name used for sprint input.")]
    [SerializeField] private string SprintActionName = "Sprint";

    [Tooltip("Exact action name used for dropping the selected item.")]
    [SerializeField] private string DropItemActionName = "DropItem";

    [Tooltip("Exact action name used for hotbar scroll input.")]
    [SerializeField] private string HotbarScrollActionName = "HotbarScroll";

    [Tooltip("Exact action name used for direct hotbar slot 1 input.")]
    [SerializeField] private string Slot1ActionName = "Slot1";

    [Tooltip("Exact action name used for direct hotbar slot 2 input.")]
    [SerializeField] private string Slot2ActionName = "Slot2";

    [Tooltip("Exact action name used for direct hotbar slot 3 input.")]
    [SerializeField] private string Slot3ActionName = "Slot3";

    [Tooltip("Exact action name used for direct hotbar slot 4 input.")]
    [SerializeField] private string Slot4ActionName = "Slot4";

    [Tooltip("Exact action name used for direct hotbar slot 5 input.")]
    [SerializeField] private string Slot5ActionName = "Slot5";

    [Tooltip("Exact action name used for direct hotbar slot 6 input.")]
    [SerializeField] private string Slot6ActionName = "Slot6";

    [Tooltip("Exact action name used for direct hotbar slot 7 input.")]
    [SerializeField] private string Slot7ActionName = "Slot7";

    [Tooltip("Exact action name used for direct hotbar slot 8 input.")]
    [SerializeField] private string Slot8ActionName = "Slot8";

    [Tooltip("Exact action name used for direct hotbar slot 9 input.")]
    [SerializeField] private string Slot9ActionName = "Slot9";

    [Header("Debug")]
    [Tooltip("Logs action binding and discrete input events.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Cached movement input.
    /// </summary>
    public Vector2 Move { get; private set; }

    /// <summary>
    /// Cached look input.
    /// </summary>
    public Vector2 Look { get; private set; }

    /// <summary>
    /// Cached hotbar scroll input.
    /// </summary>
    public float HotbarScroll { get; private set; }

    /// <summary>
    /// Whether sprint is currently held.
    /// </summary>
    public bool IsSprintHeld { get; private set; }

    /// <summary>
    /// Whether primary use is currently held.
    /// </summary>
    public bool IsUsePrimaryHeld { get; private set; }

    /// <summary>
    /// Whether secondary use is currently held.
    /// </summary>
    public bool IsUseSecondaryHeld { get; private set; }

    /// <summary>
    /// Whether crouch is currently held.
    /// </summary>
    public bool IsCrouchHeld { get; private set; }

    /// <summary>
    /// Fired when jump is pressed this frame.
    /// </summary>
    public event Action JumpPerformed;

    /// <summary>
    /// Fired when crouch is pressed this frame.
    /// </summary>
    public event Action CrouchPerformed;

    /// <summary>
    /// Fired when interact is pressed this frame.
    /// </summary>
    public event Action InteractPerformed;

    /// <summary>
    /// Fired when primary use is pressed this frame.
    /// </summary>
    public event Action UsePrimaryPerformed;

    /// <summary>
    /// Fired when secondary use is pressed this frame.
    /// </summary>
    public event Action UseSecondaryPerformed;

    /// <summary>
    /// Fired when a direct hotbar slot is selected.
    /// </summary>
    public event Action<int> SlotPerformed;

    /// <summary>
    /// Fired when previous is pressed this frame.
    /// </summary>
    public event Action PreviousPerformed;

    /// <summary>
    /// Fired when next is pressed this frame.
    /// </summary>
    public event Action NextPerformed;

    /// <summary>
    /// Fired when drop item is pressed this frame.
    /// </summary>
    public event Action DropItemPerformed;

    [Header("Cached References")]
    [Tooltip("Cached PlayerInput component used as the single source of truth for actions.")]
    [SerializeField] private PlayerInput PlayerInput;

    [Tooltip("Cached movement action.")]
    [SerializeField] private InputAction MoveAction;

    [Tooltip("Cached look action.")]
    [SerializeField] private InputAction LookAction;

    [Tooltip("Cached primary use action.")]
    [SerializeField] private InputAction UsePrimaryAction;

    [Tooltip("Cached secondary use action.")]
    [SerializeField] private InputAction UseSecondaryAction;

    [Tooltip("Cached interact action.")]
    [SerializeField] private InputAction InteractAction;

    [Tooltip("Cached crouch action.")]
    [SerializeField] private InputAction CrouchAction;

    [Tooltip("Cached jump action.")]
    [SerializeField] private InputAction JumpAction;

    [Tooltip("Cached previous action.")]
    [SerializeField] private InputAction PreviousAction;

    [Tooltip("Cached next action.")]
    [SerializeField] private InputAction NextAction;

    [Tooltip("Cached sprint action.")]
    [SerializeField] private InputAction SprintAction;

    [Tooltip("Cached drop item action.")]
    [SerializeField] private InputAction DropItemAction;

    [Tooltip("Cached hotbar scroll action.")]
    [SerializeField] private InputAction HotbarScrollAction;

    [Tooltip("Cached slot 1 action.")]
    [SerializeField] private InputAction Slot1Action;

    [Tooltip("Cached slot 2 action.")]
    [SerializeField] private InputAction Slot2Action;

    [Tooltip("Cached slot 3 action.")]
    [SerializeField] private InputAction Slot3Action;

    [Tooltip("Cached slot 4 action.")]
    [SerializeField] private InputAction Slot4Action;

    [Tooltip("Cached slot 5 action.")]
    [SerializeField] private InputAction Slot5Action;

    [Tooltip("Cached slot 6 action.")]
    [SerializeField] private InputAction Slot6Action;

    [Tooltip("Cached slot 7 action.")]
    [SerializeField] private InputAction Slot7Action;

    [Tooltip("Cached slot 8 action.")]
    [SerializeField] private InputAction Slot8Action;

    [Tooltip("Cached slot 9 action.")]
    [SerializeField] private InputAction Slot9Action;

    /// <summary>
    /// Whether the action cache has already been built.
    /// </summary>
    private bool AreActionsCached;

    /// <summary>
    /// Caches PlayerInput and resolves every action from its active action asset.
    /// </summary>
    private void Awake()
    {
        EnsureActionsCached();
    }

    /// <summary>
    /// Reads both continuous and discrete inputs every frame.
    /// </summary>
    private void Update()
    {
        EnsureActionsCached();

        if (!AreActionsCached)
        {
            return;
        }

        ReadContinuousInputs();
        ReadDiscreteInputs();
    }

    /// <summary>
    /// Clears cached runtime values when the component is disabled.
    /// </summary>
    private void OnDisable()
    {
        ResetRuntimeValues();
    }

    /// <summary>
    /// Resolves all actions from the current PlayerInput instance.
    /// This method is idempotent and safe to call multiple times.
    /// </summary>
    private void EnsureActionsCached()
    {
        if (AreActionsCached)
        {
            return;
        }

        if (PlayerInput == null)
        {
            PlayerInput = GetComponent<PlayerInput>();
        }

        if (PlayerInput == null || PlayerInput.actions == null)
        {
            Debug.LogError($"[{nameof(PlayerInputReader)}] Missing PlayerInput or InputActionAsset.", this);
            return;
        }

        MoveAction = PlayerInput.actions[MoveActionName];
        LookAction = PlayerInput.actions[LookActionName];
        UsePrimaryAction = PlayerInput.actions[UsePrimaryActionName];
        UseSecondaryAction = PlayerInput.actions[UseSecondaryActionName];
        InteractAction = PlayerInput.actions[InteractActionName];
        CrouchAction = PlayerInput.actions[CrouchActionName];
        JumpAction = PlayerInput.actions[JumpActionName];
        PreviousAction = PlayerInput.actions[PreviousActionName];
        NextAction = PlayerInput.actions[NextActionName];
        SprintAction = PlayerInput.actions[SprintActionName];
        DropItemAction = PlayerInput.actions[DropItemActionName];
        HotbarScrollAction = PlayerInput.actions[HotbarScrollActionName];
        Slot1Action = PlayerInput.actions[Slot1ActionName];
        Slot2Action = PlayerInput.actions[Slot2ActionName];
        Slot3Action = PlayerInput.actions[Slot3ActionName];
        Slot4Action = PlayerInput.actions[Slot4ActionName];
        Slot5Action = PlayerInput.actions[Slot5ActionName];
        Slot6Action = PlayerInput.actions[Slot6ActionName];
        Slot7Action = PlayerInput.actions[Slot7ActionName];
        Slot8Action = PlayerInput.actions[Slot8ActionName];
        Slot9Action = PlayerInput.actions[Slot9ActionName];

        AreActionsCached = true;

        Log(
            "Actions cached. " +
            "InteractAction=" + (InteractAction != null) +
            " | JumpAction=" + (JumpAction != null) +
            " | MoveAction=" + (MoveAction != null)
        );
    }

    /// <summary>
    /// Reads every continuous input value from the currently active PlayerInput actions.
    /// </summary>
    private void ReadContinuousInputs()
    {
        Move = MoveAction != null ? MoveAction.ReadValue<Vector2>() : Vector2.zero;
        Look = LookAction != null ? LookAction.ReadValue<Vector2>() : Vector2.zero;
        HotbarScroll = HotbarScrollAction != null ? HotbarScrollAction.ReadValue<float>() : 0f;
        IsSprintHeld = SprintAction != null && SprintAction.IsPressed();
        IsCrouchHeld = CrouchAction != null && CrouchAction.IsPressed();
        IsUsePrimaryHeld = UsePrimaryAction != null && UsePrimaryAction.IsPressed();
        IsUseSecondaryHeld = UseSecondaryAction != null && UseSecondaryAction.IsPressed();
    }

    /// <summary>
    /// Detects discrete input edges and raises the corresponding high-level events.
    /// This intentionally mirrors the polling pattern already used by the other player systems.
    /// </summary>
    private void ReadDiscreteInputs()
    {
        if (JumpAction != null && JumpAction.WasPressedThisFrame())
        {
            JumpPerformed?.Invoke();
            Log("Jump pressed.");
        }

        if (CrouchAction != null && CrouchAction.WasPressedThisFrame())
        {
            CrouchPerformed?.Invoke();
            Log("Crouch pressed.");
        }

        if (InteractAction != null && InteractAction.WasPressedThisFrame())
        {
            InteractPerformed?.Invoke();
            Log("Interact pressed.");
        }

        if (UsePrimaryAction != null && UsePrimaryAction.WasPressedThisFrame())
        {
            UsePrimaryPerformed?.Invoke();
            Log("UsePrimary pressed.");
        }

        if (UseSecondaryAction != null && UseSecondaryAction.WasPressedThisFrame())
        {
            UseSecondaryPerformed?.Invoke();
            Log("UseSecondary pressed.");
        }

        if (PreviousAction != null && PreviousAction.WasPressedThisFrame())
        {
            PreviousPerformed?.Invoke();
            Log("Previous pressed.");
        }

        if (NextAction != null && NextAction.WasPressedThisFrame())
        {
            NextPerformed?.Invoke();
            Log("Next pressed.");
        }

        if (DropItemAction != null && DropItemAction.WasPressedThisFrame())
        {
            DropItemPerformed?.Invoke();
            Log("DropItem pressed.");
        }

        if (Slot1Action != null && Slot1Action.WasPressedThisFrame()) SlotPerformed?.Invoke(1);
        if (Slot2Action != null && Slot2Action.WasPressedThisFrame()) SlotPerformed?.Invoke(2);
        if (Slot3Action != null && Slot3Action.WasPressedThisFrame()) SlotPerformed?.Invoke(3);
        if (Slot4Action != null && Slot4Action.WasPressedThisFrame()) SlotPerformed?.Invoke(4);
        if (Slot5Action != null && Slot5Action.WasPressedThisFrame()) SlotPerformed?.Invoke(5);
        if (Slot6Action != null && Slot6Action.WasPressedThisFrame()) SlotPerformed?.Invoke(6);
        if (Slot7Action != null && Slot7Action.WasPressedThisFrame()) SlotPerformed?.Invoke(7);
        if (Slot8Action != null && Slot8Action.WasPressedThisFrame()) SlotPerformed?.Invoke(8);
        if (Slot9Action != null && Slot9Action.WasPressedThisFrame()) SlotPerformed?.Invoke(9);
    }

    /// <summary>
    /// Clears cached runtime values when the component is disabled.
    /// </summary>
    private void ResetRuntimeValues()
    {
        Move = Vector2.zero;
        Look = Vector2.zero;
        HotbarScroll = 0f;
        IsSprintHeld = false;
        IsUsePrimaryHeld = false;
        IsUseSecondaryHeld = false;
        IsCrouchHeld = false;
    }

    /// <summary>
    /// Writes a reader-specific debug message.
    /// </summary>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[PlayerInputReader] " + Message, this);
    }
}