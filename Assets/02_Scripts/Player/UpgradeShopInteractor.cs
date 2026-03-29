using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Player-side controller that opens and closes an upgrade shop station while in range.
/// It delegates modal ownership to PlayerModalStateController so the solution stays reusable
/// for future terminals, chests, crafting benches or dialogue screens.
/// </summary>
[DefaultExecutionOrder(-200)]
public sealed class UpgradeShopInteractor : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Input reader used to listen for interact input.")]
    [SerializeField] private PlayerInputReader PlayerInputReader;

    [Tooltip("Central modal state controller used to block gameplay and release the cursor.")]
    [SerializeField] private PlayerModalStateController PlayerModalStateController;

    [Header("Close Input")]
    [Tooltip("If true, Escape closes the currently open shop modal.")]
    [SerializeField] private bool CloseOnEscape = true;

    /// <summary>
    /// Shop station currently in range.
    /// </summary>
    private UpgradeShopStation NearbyStation;

    /// <summary>
    /// Shop station currently opened by this interactor.
    /// </summary>
    private UpgradeShopStation OpenedStation;

    /// <summary>
    /// Caches required references.
    /// </summary>
    private void Awake()
    {
        if (PlayerInputReader == null)
        {
            PlayerInputReader = GetComponent<PlayerInputReader>();
        }

        if (PlayerModalStateController == null)
        {
            PlayerModalStateController = GetComponent<PlayerModalStateController>();
        }
    }

    /// <summary>
    /// Subscribes input callbacks.
    /// </summary>
    private void OnEnable()
    {
        if (PlayerInputReader != null)
        {
            PlayerInputReader.InteractPerformed += HandleInteractPerformed;
        }
    }

    /// <summary>
    /// Unsubscribes input callbacks.
    /// </summary>
    private void OnDisable()
    {
        if (PlayerInputReader != null)
        {
            PlayerInputReader.InteractPerformed -= HandleInteractPerformed;
        }
    }

    /// <summary>
    /// Processes close input for the open station.
    /// </summary>
    private void Update()
    {
        if (!CloseOnEscape || OpenedStation == null)
        {
            return;
        }

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            CloseCurrentStation();
        }
    }

    /// <summary>
    /// Assigns the currently reachable station.
    /// </summary>
    public void SetNearbyStation(UpgradeShopStation Station)
    {
        NearbyStation = Station;
    }

    /// <summary>
    /// Clears the currently reachable station if it matches the provided one.
    /// Also closes the shop if the player leaves the active station trigger.
    /// </summary>
    public void ClearNearbyStation(UpgradeShopStation Station)
    {
        if (NearbyStation == Station)
        {
            NearbyStation = null;
        }

        if (OpenedStation == Station)
        {
            CloseCurrentStation();
        }
    }

    /// <summary>
    /// Opens the nearby station if possible.
    /// </summary>
    private void HandleInteractPerformed()
    {
        if (OpenedStation != null)
        {
            return;
        }

        if (NearbyStation == null)
        {
            return;
        }

        UpgradePanelUI Panel = NearbyStation.GetUpgradePanelUI();

        if (Panel == null || PlayerModalStateController == null)
        {
            return;
        }

        if (!PlayerModalStateController.TryOpenModal(this))
        {
            return;
        }

        Panel.ShowPanel();
        OpenedStation = NearbyStation;
    }

    /// <summary>
    /// Closes the currently open station and returns control to gameplay.
    /// </summary>
    private void CloseCurrentStation()
    {
        if (OpenedStation == null)
        {
            return;
        }

        UpgradePanelUI Panel = OpenedStation.GetUpgradePanelUI();

        if (Panel != null)
        {
            Panel.HidePanel();
        }

        if (PlayerModalStateController != null)
        {
            PlayerModalStateController.CloseModal(this);
        }

        OpenedStation = null;
    }
}