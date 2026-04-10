using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Player-side upgrade shop state holder.
/// This component no longer reads interact input directly.
/// It only tracks nearby stations, opens or closes the current shop on request,
/// and handles Escape while a shop is open.
/// </summary>
[DefaultExecutionOrder(-200)]
public sealed class UpgradeShopInteractor : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Central modal state controller used to block gameplay and release the cursor.")]
    [SerializeField] private PlayerModalStateController PlayerModalStateController;

    [Header("Close Input")]
    [Tooltip("If true, Escape closes the currently open shop modal.")]
    [SerializeField] private bool CloseOnEscape = true;

    [Header("Debug")]
    [Tooltip("Logs shop interaction flow for debugging.")]
    [SerializeField] private bool DebugLogs = false;

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
        if (PlayerModalStateController == null)
        {
            PlayerModalStateController = GetComponent<PlayerModalStateController>();
        }
    }

    /// <summary>
    /// Processes close input for the currently opened station.
    /// </summary>
    private void Update()
    {
        if (!CloseOnEscape || OpenedStation == null)
        {
            return;
        }

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Log("Escape pressed. Closing current station.");
            CloseCurrentStation();
        }
    }

    /// <summary>
    /// Returns whether the player is currently inside a shop trigger.
    /// </summary>
    public bool HasNearbyStation()
    {
        return NearbyStation != null;
    }

    /// <summary>
    /// Returns whether a shop is currently opened.
    /// </summary>
    public bool HasOpenedStation()
    {
        return OpenedStation != null;
    }

    /// <summary>
    /// Assigns the currently reachable station.
    /// </summary>
    public void SetNearbyStation(UpgradeShopStation Station)
    {
        NearbyStation = Station;
        Log("Nearby station assigned: " + (Station != null ? Station.name : "null"));
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
            Log("Nearby station cleared: " + (Station != null ? Station.name : "null"));
        }

        if (OpenedStation == Station)
        {
            Log("Left opened station trigger. Closing station.");
            CloseCurrentStation();
        }
    }

    /// <summary>
    /// Tries to open the currently nearby station.
    /// </summary>
    /// <returns>True when the shop was opened successfully.</returns>
    public bool TryOpenNearbyStation()
    {
        if (OpenedStation != null)
        {
            Log("Ignored open request because a station is already opened.");
            return false;
        }

        if (NearbyStation == null)
        {
            Log("Cannot open shop because NearbyStation is null.");
            return false;
        }

        UpgradePanelUI Panel = NearbyStation.GetUpgradePanelUI();

        if (Panel == null)
        {
            Log("Cannot open shop because UpgradePanelUI on the station is null.");
            return false;
        }

        if (PlayerModalStateController == null)
        {
            Log("Cannot open shop because PlayerModalStateController is null.");
            return false;
        }

        if (!PlayerModalStateController.TryOpenModal(this))
        {
            Log("Cannot open shop because TryOpenModal returned false.");
            return false;
        }

        Panel.ShowPanel();
        OpenedStation = NearbyStation;
        Log("Shop opened successfully.");
        return true;
    }

    /// <summary>
    /// Closes the currently opened station.
    /// </summary>
    public void CloseCurrentStation()
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

        Log("Shop closed successfully.");
        OpenedStation = null;
    }

    /// <summary>
    /// Writes a shop-interactor-specific debug message.
    /// </summary>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[UpgradeShopInteractor] " + Message, this);
    }
}