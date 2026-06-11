using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Player-side station modal interactor.
/// This component tracks product shop stations and research stations, opens the appropriate panel on request,
/// and handles Escape while any supported station panel is open.
/// </summary>
[DefaultExecutionOrder(-200)]
public sealed class UpgradeShopInteractor : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Central modal state controller used to block gameplay and release the cursor while a station panel is open.")]
    [SerializeField] private PlayerModalStateController PlayerModalStateController;

    [Header("Close Input")]
    [Tooltip("If true, Escape closes the currently open station modal.")]
    [SerializeField] private bool CloseOnEscape = true;

    [Header("Debug")]
    [Tooltip("Logs station interaction flow for debugging.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Product shop station currently in range.
    /// </summary>
    private ShopProductStation NearbyProductStation;

    /// <summary>
    /// Research station currently in range.
    /// </summary>
    private ResearchStation NearbyResearchStation;

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
    /// <returns>True when a product shop or research station is currently reachable.</returns>
    public bool HasNearbyStation()
    {
        return NearbyProductStation != null || NearbyResearchStation != null;
    }

    /// <summary>
    /// Returns whether a station panel is currently opened.
    /// </summary>
    /// <returns>True when a product shop or research station panel is open.</returns>
    public bool HasOpenedStation()
    {
        return OpenedProductStation != null || OpenedResearchStation != null;
    }

    /// <summary>
    /// Assigns the currently reachable product station.
    /// </summary>
    /// <param name="Station">Product station that entered interaction range.</param>
    public void SetNearbyProductStation(ShopProductStation Station)
    {
        NearbyProductStation = Station;
        Log("Nearby product station assigned: " + (Station != null ? Station.name : "null"));
    }

    /// <summary>
    /// Assigns the currently reachable research station.
    /// </summary>
    /// <param name="Station">Research station that entered interaction range.</param>
    public void SetNearbyResearchStation(ResearchStation Station)
    {
        NearbyResearchStation = Station;
        Log("Nearby research station assigned: " + (Station != null ? Station.name : "null"));
    }

    /// <summary>
    /// Clears the reachable product station if it matches the provided one.
    /// Also closes the modal if the player leaves the opened product station trigger.
    /// </summary>
    /// <param name="Station">Product station that left interaction range.</param>
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
    /// Also closes the modal if the player leaves the opened research station trigger.
    /// </summary>
    /// <param name="Station">Research station that left interaction range.</param>
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
    /// Product stations are prioritized before research stations when both are reachable.
    /// </summary>
    /// <returns>True when a station panel was opened.</returns>
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

        Log("Cannot open station because no nearby station is available.");
        return false;
    }

    /// <summary>
    /// Closes the currently opened station, if any.
    /// </summary>
    public void CloseCurrentStation()
    {
        bool ClosedAnyStation = false;

        if (OpenedProductStation != null)
        {
            ShopProductPanelUI Panel = OpenedProductStation.GetProductPanelUI();

            if (Panel != null)
            {
                Panel.HidePanel();
            }

            Log("Product shop closed successfully.");
            OpenedProductStation = null;
            ClosedAnyStation = true;
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
            ClosedAnyStation = true;
        }

        if (ClosedAnyStation && PlayerModalStateController != null)
        {
            PlayerModalStateController.CloseModal(this);
        }
    }

    /// <summary>
    /// Attempts to open a product station.
    /// </summary>
    /// <param name="Station">Product station to open.</param>
    /// <returns>True when the product station panel was opened.</returns>
    private bool TryOpenProductStation(ShopProductStation Station)
    {
        if (Station == null)
        {
            return false;
        }

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
    /// <param name="Station">Research station to open.</param>
    /// <returns>True when the research station panel was opened.</returns>
    private bool TryOpenResearchStation(ResearchStation Station)
    {
        if (Station == null)
        {
            return false;
        }

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
    /// <returns>True when the modal state was opened.</returns>
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
    /// <param name="Message">Message written to the Unity console.</param>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[UpgradeShopInteractor] " + Message, this);
    }
}
