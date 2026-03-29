using UnityEngine;

/// <summary>
/// Central authority that owns player modal focus.
/// It decides when gameplay input must be blocked because a UI panel is taking control.
/// </summary>
public sealed class PlayerModalStateController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Player controller that will receive movement and look blocking.")]
    [SerializeField] private PlayerController PlayerController;

    [Tooltip("Interaction controller blocked while a modal is open.")]
    [SerializeField] private PlayerInteractionController PlayerInteractionController;

    [Tooltip("Money collector blocked while a modal is open.")]
    [SerializeField] private MoneyCollector MoneyCollector;

    [Tooltip("Hotbar controller blocked while a modal is open.")]
    [SerializeField] private HotbarController HotbarController;

    [Header("Cursor")]
    [Tooltip("Cursor lock mode used during gameplay.")]
    [SerializeField] private CursorLockMode GameplayCursorLockMode = CursorLockMode.Locked;

    [Tooltip("Cursor lock mode used while a modal is open.")]
    [SerializeField] private CursorLockMode ModalCursorLockMode = CursorLockMode.None;

    /// <summary>
    /// Current modal owner. It is null when no modal is open.
    /// </summary>
    private Object CurrentModalOwner;

    /// <summary>
    /// Gets whether any modal currently owns player focus.
    /// </summary>
    public bool IsModalOpen => CurrentModalOwner != null;

    /// <summary>
    /// Tries to open a modal for the provided owner.
    /// Returns false when another modal already owns the focus.
    /// </summary>
    public bool TryOpenModal(Object ModalOwner)
    {
        if (ModalOwner == null)
        {
            return false;
        }

        if (CurrentModalOwner != null && CurrentModalOwner != ModalOwner)
        {
            return false;
        }

        CurrentModalOwner = ModalOwner;
        ApplyModalState(true);
        return true;
    }

    /// <summary>
    /// Closes the modal only if the provided owner currently owns the focus.
    /// </summary>
    public void CloseModal(Object ModalOwner)
    {
        if (ModalOwner == null)
        {
            return;
        }

        if (CurrentModalOwner != ModalOwner)
        {
            return;
        }

        CurrentModalOwner = null;
        ApplyModalState(false);
    }

    /// <summary>
    /// Forces the current modal to close regardless of owner.
    /// </summary>
    public void ForceCloseCurrentModal()
    {
        if (CurrentModalOwner == null)
        {
            return;
        }

        CurrentModalOwner = null;
        ApplyModalState(false);
    }

    /// <summary>
    /// Returns whether gameplay input should currently be ignored.
    /// </summary>
    public bool IsGameplayInputBlocked()
    {
        return CurrentModalOwner != null;
    }

    /// <summary>
    /// Caches missing references automatically.
    /// </summary>
    private void Awake()
    {
        if (PlayerController == null)
        {
            PlayerController = GetComponent<PlayerController>();
        }

        if (PlayerInteractionController == null)
        {
            PlayerInteractionController = GetComponent<PlayerInteractionController>();
        }

        if (MoneyCollector == null)
        {
            MoneyCollector = GetComponent<MoneyCollector>();
        }

        if (HotbarController == null)
        {
            HotbarController = GetComponent<HotbarController>();
        }

        ApplyModalState(false);
    }

    /// <summary>
    /// Applies the runtime blocked or unblocked state across cursor and gameplay systems.
    /// </summary>
    private void ApplyModalState(bool IsModalActive)
    {
        if (PlayerController != null)
        {
            PlayerController.SetExternalLookBlocked(IsModalActive);
            PlayerController.SetExternalMovementBlocked(IsModalActive);
        }

        if (PlayerInteractionController != null)
        {
            PlayerInteractionController.SetExternalInteractionBlocked(IsModalActive);
        }

        if (MoneyCollector != null)
        {
            MoneyCollector.SetExternalCollectionBlocked(IsModalActive);
        }

        if (HotbarController != null)
        {
            HotbarController.SetExternalHotbarInputBlocked(IsModalActive);
        }

        Cursor.visible = IsModalActive;
        Cursor.lockState = IsModalActive ? ModalCursorLockMode : GameplayCursorLockMode;
    }
}