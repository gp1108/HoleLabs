using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Player-side shop state holder.
/// This component tracks both legacy upgrade stations and new product stations,
/// opens or closes the current shop on request, and handles Escape while a shop is open.
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
    /// Legacy upgrade station currently in range.
    /// </summary>
    private UpgradeShopStation NearbyUpgradeStation;

    /// <summary>
    /// Product shop station currently in range.
    /// </summary>
    private ShopProductStation NearbyProductStation;

    /// <summary>
    /// Legacy upgrade station currently opened by this interactor.
    /// </summary>
    private UpgradeShopStation OpenedUpgradeStation;

    /// <summary>
    /// Product shop station currently opened by this interactor.
    /// </summary>
    private ShopProductStation OpenedProductStation;

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
        if (!CloseOnEscape || !HasOpenedStation())
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
    /// Returns whether the player is currently inside any supported shop trigger.
    /// </summary>
    public bool HasNearbyStation()
    {
        return NearbyUpgradeStation != null || NearbyProductStation != null;
    }

    /// <summary>
    /// Returns whether a shop is currently opened.
    /// </summary>
    public bool HasOpenedStation()
    {
        return OpenedUpgradeStation != null || OpenedProductStation != null;
    }

    /// <summary>
    /// Assigns the currently reachable legacy upgrade station.
    /// </summary>
    /// <param name="Station">Upgrade station in range.</param>
    public void SetNearbyStation(UpgradeShopStation Station)
    {
        NearbyUpgradeStation = Station;
        Log("Nearby upgrade station assigned: " + (Station != null ? Station.name : "null"));
    }

    /// <summary>
    /// Assigns the currently reachable product station.
    /// </summary>
    /// <param name="Station">Product station in range.</param>
    public void SetNearbyProductStation(ShopProductStation Station)
    {
        NearbyProductStation = Station;
        Log("Nearby product station assigned: " + (Station != null ? Station.name : "null"));
    }

    /// <summary>
    /// Clears the reachable legacy upgrade station if it matches the provided one.
    /// Also closes the shop if the player leaves the active station trigger.
    /// </summary>
    /// <param name="Station">Upgrade station leaving range.</param>
    public void ClearNearbyStation(UpgradeShopStation Station)
    {
        if (NearbyUpgradeStation == Station)
        {
            NearbyUpgradeStation = null;
            Log("Nearby upgrade station cleared: " + (Station != null ? Station.name : "null"));
        }

        if (OpenedUpgradeStation == Station)
        {
            Log("Left opened upgrade station trigger. Closing station.");
            CloseCurrentStation();
        }
    }

    /// <summary>
    /// Clears the reachable product station if it matches the provided one.
    /// Also closes the shop if the player leaves the active station trigger.
    /// </summary>
    /// <param name="Station">Product station leaving range.</param>
    public void ClearNearbyProductStation(ShopProductStation Station)
    {
        if (NearbyProductStation == Station)
        {
            NearbyProductStation = null;
            Log("Nearby product station cleared: " + (Station != null ? Station.name : "null"));
        }

        if (OpenedProductStation == Station)
        {
            Log("Left opened product station trigger. Closing station.");
            CloseCurrentStation();
        }
    }

    /// <summary>
    /// Tries to open the currently nearby station.
    /// Product stations are prioritized over legacy upgrade stations when both are in range.
    /// </summary>
    /// <returns>True when a shop was opened successfully.</returns>
    public bool TryOpenNearbyStation()
    {
        if (HasOpenedStation())
        {
            Log("Ignored open request because a station is already opened.");
            return false;
        }

        if (NearbyProductStation != null)
        {
            return TryOpenProductStation(NearbyProductStation);
        }

        if (NearbyUpgradeStation != null)
        {
            return TryOpenUpgradeStation(NearbyUpgradeStation);
        }

        Log("Cannot open shop because no nearby station is available.");
        return false;
    }

    /// <summary>
    /// Closes the currently opened station.
    /// </summary>
    public void CloseCurrentStation()
    {
        if (OpenedUpgradeStation != null)
        {
            UpgradePanelUI Panel = OpenedUpgradeStation.GetUpgradePanelUI();

            if (Panel != null)
            {
                Panel.HidePanel();
            }

            Log("Upgrade shop closed successfully.");
            OpenedUpgradeStation = null;
        }

        if (OpenedProductStation != null)
        {
            ShopProductPanelUI Panel = OpenedProductStation.GetProductPanelUI();

            if (Panel != null)
            {
                Panel.HidePanel();
            }

            Log("Product shop closed successfully.");
            OpenedProductStation = null;
        }

        if (PlayerModalStateController != null)
        {
            PlayerModalStateController.CloseModal(this);
        }
    }

    /// <summary>
    /// Attempts to open a legacy upgrade station.
    /// </summary>
    /// <param name="Station">Station to open.</param>
    /// <returns>True when opened successfully.</returns>
    private bool TryOpenUpgradeStation(UpgradeShopStation Station)
    {
        UpgradePanelUI Panel = Station.GetUpgradePanelUI();

        if (Panel == null)
        {
            Log("Cannot open upgrade shop because UpgradePanelUI is null.");
            return false;
        }

        if (!TryOpenModal())
        {
            return false;
        }

        Panel.ShowPanel();
        OpenedUpgradeStation = Station;
        Log("Upgrade shop opened successfully.");
        return true;
    }

    /// <summary>
    /// Attempts to open a product station.
    /// </summary>
    /// <param name="Station">Station to open.</param>
    /// <returns>True when opened successfully.</returns>
    private bool TryOpenProductStation(ShopProductStation Station)
    {
        ShopProductPanelUI Panel = Station.GetProductPanelUI();

        if (Panel == null)
        {
            Log("Cannot open product shop because ProductPanelUI is null.");
            return false;
        }

        if (!TryOpenModal())
        {
            return false;
        }

        Panel.Initialize(Station);
        Panel.ShowPanel();
        OpenedProductStation = Station;
        Log("Product shop opened successfully.");
        return true;
    }

    /// <summary>
    /// Attempts to open the player modal state for shop UI.
    /// </summary>
    /// <returns>True when modal state was opened.</returns>
    private bool TryOpenModal()
    {
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

        return true;
    }

    /// <summary>
    /// Writes a shop-interactor-specific debug message.
    /// </summary>
    /// <param name="Message">Message to write.</param>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[UpgradeShopInteractor] " + Message, this);
    }
}
