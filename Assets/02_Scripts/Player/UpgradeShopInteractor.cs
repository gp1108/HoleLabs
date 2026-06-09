using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Player-side station state holder.
/// This component tracks legacy upgrade stations, product shop stations and research stations,
/// opens or closes the current modal panel on request, and handles Escape while a station is open.
/// </summary>
[DefaultExecutionOrder(-200)]
public sealed class UpgradeShopInteractor : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Central modal state controller used to block gameplay and release the cursor.")]
    [SerializeField] private PlayerModalStateController PlayerModalStateController;

    [Header("Close Input")]
    [Tooltip("If true, Escape closes the currently open station modal.")]
    [SerializeField] private bool CloseOnEscape = true;

    [Header("Debug")]
    [Tooltip("Logs station interaction flow for debugging.")]
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
    /// Research station currently in range.
    /// </summary>
    private ResearchStation NearbyResearchStation;

    /// <summary>
    /// Legacy upgrade station currently opened by this interactor.
    /// </summary>
    private UpgradeShopStation OpenedUpgradeStation;

    /// <summary>
    /// Product shop station currently opened by this interactor.
    /// </summary>
    private ShopProductStation OpenedProductStation;

    /// <summary>
    /// Research station currently opened by this interactor.
    /// </summary>
    private ResearchStation OpenedResearchStation;

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
    /// Returns whether the player is currently inside any supported station trigger.
    /// </summary>
    public bool HasNearbyStation()
    {
        return NearbyProductStation != null || NearbyResearchStation != null || NearbyUpgradeStation != null;
    }

    /// <summary>
    /// Returns whether a station panel is currently opened.
    /// </summary>
    public bool HasOpenedStation()
    {
        return OpenedProductStation != null || OpenedResearchStation != null || OpenedUpgradeStation != null;
    }

    /// <summary>
    /// Assigns the currently reachable legacy upgrade station.
    /// </summary>
    public void SetNearbyStation(UpgradeShopStation Station)
    {
        NearbyUpgradeStation = Station;
        Log("Nearby upgrade station assigned: " + (Station != null ? Station.name : "null"));
    }

    /// <summary>
    /// Assigns the currently reachable product station.
    /// </summary>
    public void SetNearbyProductStation(ShopProductStation Station)
    {
        NearbyProductStation = Station;
        Log("Nearby product station assigned: " + (Station != null ? Station.name : "null"));
    }

    /// <summary>
    /// Assigns the currently reachable research station.
    /// </summary>
    public void SetNearbyResearchStation(ResearchStation Station)
    {
        NearbyResearchStation = Station;
        Log("Nearby research station assigned: " + (Station != null ? Station.name : "null"));
    }

    /// <summary>
    /// Clears the reachable legacy upgrade station if it matches the provided one.
    /// Also closes the modal if the player leaves the opened station trigger.
    /// </summary>
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
    /// Also closes the modal if the player leaves the opened station trigger.
    /// </summary>
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
    /// Clears the reachable research station if it matches the provided one.
    /// Also closes the modal if the player leaves the opened station trigger.
    /// </summary>
    public void ClearNearbyResearchStation(ResearchStation Station)
    {
        if (NearbyResearchStation == Station)
        {
            NearbyResearchStation = null;
            Log("Nearby research station cleared: " + (Station != null ? Station.name : "null"));
        }

        if (OpenedResearchStation == Station)
        {
            Log("Left opened research station trigger. Closing station.");
            CloseCurrentStation();
        }
    }

    /// <summary>
    /// Tries to open the currently nearby station.
    /// Product stations are prioritized first, then research, then legacy upgrade stations.
    /// </summary>
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

        if (NearbyResearchStation != null)
        {
            return TryOpenResearchStation(NearbyResearchStation);
        }

        if (NearbyUpgradeStation != null)
        {
            return TryOpenUpgradeStation(NearbyUpgradeStation);
        }

        Log("Cannot open station because no nearby station is available.");
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

        if (OpenedResearchStation != null)
        {
            ResearchPanelUI Panel = OpenedResearchStation.GetResearchPanelUI();

            if (Panel != null)
            {
                Panel.HidePanel();
            }

            Log("Research station closed successfully.");
            OpenedResearchStation = null;
        }

        if (PlayerModalStateController != null)
        {
            PlayerModalStateController.CloseModal(this);
        }
    }

    /// <summary>
    /// Attempts to open a legacy upgrade station.
    /// </summary>
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
    /// Attempts to open a research station.
    /// </summary>
    private bool TryOpenResearchStation(ResearchStation Station)
    {
        ResearchPanelUI Panel = Station.GetResearchPanelUI();

        if (Panel == null)
        {
            Log("Cannot open research station because ResearchPanelUI is null.");
            return false;
        }

        if (!TryOpenModal())
        {
            return false;
        }

        Panel.Initialize(Station);
        Panel.ShowPanel();
        OpenedResearchStation = Station;
        Log("Research station opened successfully.");
        return true;
    }

    /// <summary>
    /// Attempts to open the player modal state for station UI.
    /// </summary>
    private bool TryOpenModal()
    {
        if (PlayerModalStateController == null)
        {
            Log("Cannot open station because PlayerModalStateController is null.");
            return false;
        }

        if (!PlayerModalStateController.TryOpenModal(this))
        {
            Log("Cannot open station because TryOpenModal returned false.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Writes a station-interactor-specific debug message.
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
