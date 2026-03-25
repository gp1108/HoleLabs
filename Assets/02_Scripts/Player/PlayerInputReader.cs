using System;
using UnityEngine;
using UnityEngine.InputSystem;


/// <summary>
/// Clase centralizada para leer y exponer todos los inputs del jugador.
/// No contiene lógica de movimiento ni gameplay; solo lectura de entrada.
/// </summary>
public sealed class PlayerInputReader : MonoBehaviour
{
    [Header("Input Asset")]

    [SerializeField]
    [Tooltip("Input Action Asset que contiene el Action Map del jugador.")]
    private InputActionAsset InputActionsAsset;

    [SerializeField]
    [Tooltip("Nombre exacto del Action Map del jugador dentro del Input Action Asset.")]
    private string PlayerActionMapName = "Player";

    [Header("Action Names")]

    [SerializeField] private string MoveActionName = "Move";
    [SerializeField] private string LookActionName = "Look";
    [SerializeField] private string UsePrimaryActionName = "UsePrimary";
    [SerializeField] private string UseSecondaryActionName = "UseSecondary";
    [SerializeField] private string InteractActionName = "Interact";
    [SerializeField] private string CrouchActionName = "Crouch";
    [SerializeField] private string JumpActionName = "Jump";
    [SerializeField] private string PreviousActionName = "Previous";
    [SerializeField] private string NextActionName = "Next";
    [SerializeField] private string SprintActionName = "Sprint";
    [SerializeField] private string DropItemActionName = "DropItem";
    [SerializeField] private string HotbarScrollActionName = "HotbarScroll";
    [SerializeField] private string Slot1ActionName = "Slot1";
    [SerializeField] private string Slot2ActionName = "Slot2";
    [SerializeField] private string Slot3ActionName = "Slot3";
    [SerializeField] private string Slot4ActionName = "Slot4";
    [SerializeField] private string Slot5ActionName = "Slot5";
    [SerializeField] private string Slot6ActionName = "Slot6";
    [SerializeField] private string Slot7ActionName = "Slot7";
    [SerializeField] private string Slot8ActionName = "Slot8";
    [SerializeField] private string Slot9ActionName = "Slot9";

    /// <summary>
    /// Input de movimiento en X/Y.
    /// </summary>
    public Vector2 Move { get; private set; }

    /// <summary>
    /// Input de cámara en X/Y.
    /// </summary>
    public Vector2 Look { get; private set; }

    /// <summary>
    /// Valor del scroll de hotbar en el frame actual.
    /// </summary>
    public float HotbarScroll { get; private set; }

    /// <summary>
    /// Indica si el input de sprint está mantenido.
    /// </summary>
    public bool IsSprintHeld { get; private set; }

    /// <summary>
    /// Indica si el input primario está mantenido.
    /// </summary>
    public bool IsUsePrimaryHeld { get; private set; }

    /// <summary>
    /// Indica si el input secundario está mantenido.
    /// </summary>
    public bool IsUseSecondaryHeld { get; private set; }

    /// <summary>
    /// Evento lanzado cuando se pulsa salto.
    /// </summary>
    public event Action JumpPerformed;

    /// <summary>
    /// Evento lanzado cuando se pulsa crouch.
    /// </summary>
    public event Action CrouchPerformed;
    /// <summary>
    /// Indicates whether the crouch input is currently being held.
    /// </summary>
    public bool IsCrouchHeld { get; private set; }

    /// <summary>
    /// Evento lanzado cuando se pulsa interactuar.
    /// </summary>
    public event Action InteractPerformed;

    /// <summary>
    /// Evento lanzado cuando se pulsa acción primaria.
    /// </summary>
    public event Action UsePrimaryPerformed;

    /// <summary>
    /// Evento lanzado cuando se pulsa acción secundaria.
    /// </summary>
    public event Action UseSecondaryPerformed;

    /// <summary>
    /// Evento lanzado cuando se pulsa el acceso rápido a slot.
    /// </summary>
    public event Action<int> SlotPerformed;

    /// <summary>
    /// Evento lanzado al pulsar previous.
    /// </summary>
    public event Action PreviousPerformed;

    /// <summary>
    /// Evento lanzado al pulsar next.
    /// </summary>
    public event Action NextPerformed;

    /// <summary>
    /// Evento lanzado al pulsar drop item.
    /// </summary>
    public event Action DropItemPerformed;

    private InputActionMap PlayerMap;

    private InputAction MoveAction;
    private InputAction LookAction;
    private InputAction UsePrimaryAction;
    private InputAction UseSecondaryAction;
    private InputAction InteractAction;
    private InputAction CrouchAction;
    private InputAction JumpAction;
    private InputAction PreviousAction;
    private InputAction NextAction;
    private InputAction SprintAction;
    private InputAction DropItemAction;
    private InputAction HotbarScrollAction;
    private InputAction Slot1Action;
    private InputAction Slot2Action;
    private InputAction Slot3Action;
    private InputAction Slot4Action;
    private InputAction Slot5Action;
    private InputAction Slot6Action;
    private InputAction Slot7Action;
    private InputAction Slot8Action;
    private InputAction Slot9Action;

    private void Awake()
    {
        CacheActions();
    }

    private void OnEnable()
    {
        EnableActions();
        SubscribeActions();
    }

    private void OnDisable()
    {
        UnsubscribeActions();
        DisableActions();
        ResetRuntimeValues();
    }

    private void Update()
    {
        ReadContinuousInputs();
    }

    /// <summary>
    /// Busca y cachea todas las acciones necesarias.
    /// </summary>
    private void CacheActions()
    {
        if (InputActionsAsset == null)
        {
            Debug.LogError($"[{nameof(PlayerInputReader)}] Falta asignar el InputActionAsset.", this);
            return;
        }

        PlayerMap = InputActionsAsset.FindActionMap(PlayerActionMapName, true);

        MoveAction = PlayerMap.FindAction(MoveActionName, true);
        LookAction = PlayerMap.FindAction(LookActionName, true);
        UsePrimaryAction = PlayerMap.FindAction(UsePrimaryActionName, true);
        UseSecondaryAction = PlayerMap.FindAction(UseSecondaryActionName, true);
        InteractAction = PlayerMap.FindAction(InteractActionName, true);
        CrouchAction = PlayerMap.FindAction(CrouchActionName, true);
        JumpAction = PlayerMap.FindAction(JumpActionName, true);
        PreviousAction = PlayerMap.FindAction(PreviousActionName, true);
        NextAction = PlayerMap.FindAction(NextActionName, true);
        SprintAction = PlayerMap.FindAction(SprintActionName, true);
        DropItemAction = PlayerMap.FindAction(DropItemActionName, true);
        HotbarScrollAction = PlayerMap.FindAction(HotbarScrollActionName, true);
        Slot1Action = PlayerMap.FindAction(Slot1ActionName, true);
        Slot2Action = PlayerMap.FindAction(Slot2ActionName, true);
        Slot3Action = PlayerMap.FindAction(Slot3ActionName, true);
        Slot4Action = PlayerMap.FindAction(Slot4ActionName, true);
        Slot5Action = PlayerMap.FindAction(Slot5ActionName, true);
        Slot6Action = PlayerMap.FindAction(Slot6ActionName, true);
        Slot7Action = PlayerMap.FindAction(Slot7ActionName, true);
        Slot8Action = PlayerMap.FindAction(Slot8ActionName, true);
        Slot9Action = PlayerMap.FindAction(Slot9ActionName, true);
    }

    /// <summary>
    /// Activa el action map del jugador.
    /// </summary>
    private void EnableActions()
    {
        PlayerMap?.Enable();
    }

    /// <summary>
    /// Desactiva el action map del jugador.
    /// </summary>
    private void DisableActions()
    {
        PlayerMap?.Disable();
    }

    /// <summary>
    /// Suscribe callbacks de acciones discretas.
    /// </summary>
    private void SubscribeActions()
    {
        if (JumpAction != null) JumpAction.performed += OnJumpPerformed;
        if (CrouchAction != null) CrouchAction.performed += OnCrouchPerformed;
        if (InteractAction != null) InteractAction.performed += OnInteractPerformed;

        if (UsePrimaryAction != null)
        {
            UsePrimaryAction.performed += OnUsePrimaryPerformed;
            UsePrimaryAction.canceled += OnUsePrimaryCanceled;
        }

        if (UseSecondaryAction != null)
        {
            UseSecondaryAction.performed += OnUseSecondaryPerformed;
            UseSecondaryAction.canceled += OnUseSecondaryCanceled;
        }

        if (PreviousAction != null) PreviousAction.performed += OnPreviousPerformed;
        if (NextAction != null) NextAction.performed += OnNextPerformed;
        if (DropItemAction != null) DropItemAction.performed += OnDropItemPerformed;

        if (Slot1Action != null) Slot1Action.performed += _ => SlotPerformed?.Invoke(1);
        if (Slot2Action != null) Slot2Action.performed += _ => SlotPerformed?.Invoke(2);
        if (Slot3Action != null) Slot3Action.performed += _ => SlotPerformed?.Invoke(3);
        if (Slot4Action != null) Slot4Action.performed += _ => SlotPerformed?.Invoke(4);
        if (Slot5Action != null) Slot5Action.performed += _ => SlotPerformed?.Invoke(5);
        if (Slot6Action != null) Slot6Action.performed += _ => SlotPerformed?.Invoke(6);
        if (Slot7Action != null) Slot7Action.performed += _ => SlotPerformed?.Invoke(7);
        if (Slot8Action != null) Slot8Action.performed += _ => SlotPerformed?.Invoke(8);
        if (Slot9Action != null) Slot9Action.performed += _ => SlotPerformed?.Invoke(9);
    }

    /// <summary>
    /// Elimina callbacks de acciones discretas.
    /// </summary>
    private void UnsubscribeActions()
    {
        if (JumpAction != null) JumpAction.performed -= OnJumpPerformed;
        if (CrouchAction != null) CrouchAction.performed -= OnCrouchPerformed;
        if (InteractAction != null) InteractAction.performed -= OnInteractPerformed;

        if (UsePrimaryAction != null)
        {
            UsePrimaryAction.performed -= OnUsePrimaryPerformed;
            UsePrimaryAction.canceled -= OnUsePrimaryCanceled;
        }

        if (UseSecondaryAction != null)
        {
            UseSecondaryAction.performed -= OnUseSecondaryPerformed;
            UseSecondaryAction.canceled -= OnUseSecondaryCanceled;
        }

        if (PreviousAction != null) PreviousAction.performed -= OnPreviousPerformed;
        if (NextAction != null) NextAction.performed -= OnNextPerformed;
        if (DropItemAction != null) DropItemAction.performed -= OnDropItemPerformed;
    }
    /// <summary>
    /// Reads continuous inputs every frame.
    /// </summary>
    private void ReadContinuousInputs()
    {
        Move = MoveAction != null ? MoveAction.ReadValue<Vector2>() : Vector2.zero;
        Look = LookAction != null ? LookAction.ReadValue<Vector2>() : Vector2.zero;
        HotbarScroll = HotbarScrollAction != null ? HotbarScrollAction.ReadValue<float>() : 0f;
        IsSprintHeld = SprintAction != null && SprintAction.IsPressed();
        IsCrouchHeld = CrouchAction != null && CrouchAction.IsPressed();
    }

    /// <summary>
    /// Limpia los valores runtime al desactivar el componente.
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

    private void OnJumpPerformed(InputAction.CallbackContext Context)
    {
        JumpPerformed?.Invoke();
    }

    private void OnCrouchPerformed(InputAction.CallbackContext Context)
    {
        CrouchPerformed?.Invoke();
    }

    private void OnInteractPerformed(InputAction.CallbackContext Context)
    {
        InteractPerformed?.Invoke();
    }

    private void OnUsePrimaryPerformed(InputAction.CallbackContext Context)
    {
        IsUsePrimaryHeld = true;
        UsePrimaryPerformed?.Invoke();
    }

    private void OnUsePrimaryCanceled(InputAction.CallbackContext Context)
    {
        IsUsePrimaryHeld = false;
    }

    private void OnUseSecondaryPerformed(InputAction.CallbackContext Context)
    {
        IsUseSecondaryHeld = true;
        UseSecondaryPerformed?.Invoke();
    }

    private void OnUseSecondaryCanceled(InputAction.CallbackContext Context)
    {
        IsUseSecondaryHeld = false;
    }

    private void OnPreviousPerformed(InputAction.CallbackContext Context)
    {
        PreviousPerformed?.Invoke();
    }

    private void OnNextPerformed(InputAction.CallbackContext Context)
    {
        NextPerformed?.Invoke();
    }

    private void OnDropItemPerformed(InputAction.CallbackContext Context)
    {
        DropItemPerformed?.Invoke();
    }
}

