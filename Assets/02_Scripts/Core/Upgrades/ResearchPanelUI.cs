using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Panel that displays research entries and refreshes them from wallet, upgrade and active research processing state.
/// Entries are assigned manually in the hierarchy for predictable authoring.
/// </summary>
public sealed class ResearchPanelUI : MonoBehaviour
{
    [Header("Root")]
    [Tooltip("Root object enabled while the research panel is visible. If empty, this GameObject is used.")]
    [SerializeField] private GameObject PanelRoot;

    [Header("References")]
    [Tooltip("Wallet used to display current credits.")]
    [SerializeField] private CurrencyWallet CurrencyWallet;

    [Tooltip("Upgrade manager used to refresh research completion and unlock state.")]
    [SerializeField] private UpgradeManager UpgradeManager;

    [Tooltip("Text used to display current credits.")]
    [SerializeField] private TMP_Text CreditsAmountText;

    [Tooltip("Optional text used to display the currently active research.")]
    [SerializeField] private TMP_Text ActiveResearchText;

    [Header("Discovery")]
    [Tooltip("If true, research entries are discovered during Awake.")]
    [SerializeField] private bool DiscoverOnAwake = true;

    [Tooltip("If true, research entries are rediscovered whenever the panel is shown.")]
    [SerializeField] private bool RediscoverOnShow = true;

    /// <summary>
    /// Research station that owns this panel.
    /// </summary>
    private ResearchStation OwnerStation;

    /// <summary>
    /// Manual research entries currently registered under this panel.
    /// </summary>
    private readonly List<ResearchListEntryUI> RegisteredResearchEntries = new();

    /// <summary>
    /// Resolves references and optionally discovers manual entries.
    /// </summary>
    private void Awake()
    {
        if (PanelRoot == null)
        {
            PanelRoot = gameObject;
        }

        if (CurrencyWallet == null)
        {
            CurrencyWallet = FindFirstObjectByType<CurrencyWallet>();
        }

        if (UpgradeManager == null)
        {
            UpgradeManager = FindFirstObjectByType<UpgradeManager>();
        }

        SubscribeToEvents();

        if (DiscoverOnAwake)
        {
            DiscoverManualUi();
        }

        PanelRoot.SetActive(false);
    }

    /// <summary>
    /// Unsubscribes runtime events.
    /// </summary>
    private void OnDestroy()
    {
        UnsubscribeFromEvents();

        if (OwnerStation != null)
        {
            OwnerStation.OnResearchStationStateChanged -= HandleResearchStationStateChanged;
        }
    }

    /// <summary>
    /// Initializes this panel with the owning research station.
    /// </summary>
    public void Initialize(ResearchStation Station)
    {
        if (OwnerStation != null)
        {
            OwnerStation.OnResearchStationStateChanged -= HandleResearchStationStateChanged;
        }

        OwnerStation = Station;

        if (OwnerStation != null)
        {
            OwnerStation.OnResearchStationStateChanged += HandleResearchStationStateChanged;
        }

        DiscoverManualUi();
        InitializeManualUi();
        RefreshAll();
    }

    /// <summary>
    /// Shows this panel.
    /// </summary>
    public void ShowPanel()
    {
        SetVisible(true);
    }

    /// <summary>
    /// Hides this panel.
    /// </summary>
    public void HidePanel()
    {
        SetVisible(false);
    }

    /// <summary>
    /// Shows or hides the panel root.
    /// </summary>
    public void SetVisible(bool IsVisible)
    {
        if (PanelRoot == null)
        {
            return;
        }

        PanelRoot.SetActive(IsVisible);

        if (IsVisible)
        {
            if (RediscoverOnShow)
            {
                DiscoverManualUi();
                InitializeManualUi();
            }

            RefreshAll();
        }
    }

    /// <summary>
    /// Refreshes all panel labels and registered entries.
    /// </summary>
    public void RefreshAll()
    {
        RefreshCurrencyText();
        RefreshActiveResearchText();

        for (int Index = 0; Index < RegisteredResearchEntries.Count; Index++)
        {
            if (RegisteredResearchEntries[Index] != null)
            {
                RegisteredResearchEntries[Index].RefreshView();
            }
        }
    }

    /// <summary>
    /// Discovers all manual research entries under this panel.
    /// </summary>
    public void DiscoverManualUi()
    {
        RegisteredResearchEntries.Clear();

        ResearchListEntryUI[] Entries = GetComponentsInChildren<ResearchListEntryUI>(true);

        for (int Index = 0; Index < Entries.Length; Index++)
        {
            if (Entries[Index] != null)
            {
                RegisteredResearchEntries.Add(Entries[Index]);
            }
        }
    }

    /// <summary>
    /// Gets the research definitions currently assigned to registered entries.
    /// </summary>
    public List<ResearchDefinition> GetRegisteredResearchDefinitions()
    {
        List<ResearchDefinition> Result = new();

        for (int Index = 0; Index < RegisteredResearchEntries.Count; Index++)
        {
            if (RegisteredResearchEntries[Index] == null)
            {
                continue;
            }

            ResearchDefinition Definition = RegisteredResearchEntries[Index].GetResearchDefinition();

            if (Definition != null && !Result.Contains(Definition))
            {
                Result.Add(Definition);
            }
        }

        return Result;
    }

    /// <summary>
    /// Initializes every discovered research entry with the owning station.
    /// </summary>
    public void InitializeManualUi()
    {
        for (int Index = 0; Index < RegisteredResearchEntries.Count; Index++)
        {
            if (RegisteredResearchEntries[Index] != null)
            {
                RegisteredResearchEntries[Index].Initialize(OwnerStation);
            }
        }
    }

    /// <summary>
    /// Refreshes the current credit display.
    /// </summary>
    private void RefreshCurrencyText()
    {
        if (CurrencyWallet == null || CreditsAmountText == null)
        {
            return;
        }

        CreditsAmountText.text = CurrencyWallet.GetCredits().ToString("0.00") + " C";
    }

    /// <summary>
    /// Refreshes the optional active research label.
    /// </summary>
    private void RefreshActiveResearchText()
    {
        if (ActiveResearchText == null)
        {
            return;
        }

        ResearchDefinition ActiveResearch = OwnerStation != null ? OwnerStation.GetActiveResearchDefinition() : null;
        ActiveResearchText.text = ActiveResearch != null ? "Active: " + ActiveResearch.GetDisplayName() : "Active: None";
    }

    /// <summary>
    /// Subscribes to wallet and upgrade events.
    /// </summary>
    private void SubscribeToEvents()
    {
        if (CurrencyWallet != null)
        {
            CurrencyWallet.OnCurrencyChanged += HandleCurrencyChanged;
        }

        if (UpgradeManager != null)
        {
            UpgradeManager.OnUpgradeStateChanged += HandleUpgradeStateChanged;
        }
    }

    /// <summary>
    /// Unsubscribes from wallet and upgrade events.
    /// </summary>
    private void UnsubscribeFromEvents()
    {
        if (CurrencyWallet != null)
        {
            CurrencyWallet.OnCurrencyChanged -= HandleCurrencyChanged;
        }

        if (UpgradeManager != null)
        {
            UpgradeManager.OnUpgradeStateChanged -= HandleUpgradeStateChanged;
        }
    }

    /// <summary>
    /// Refreshes UI when wallet values change.
    /// </summary>
    private void HandleCurrencyChanged(CurrencyWallet.CurrencyType CurrencyType, float Amount)
    {
        RefreshAll();
    }

    /// <summary>
    /// Refreshes UI when upgrade state changes.
    /// </summary>
    private void HandleUpgradeStateChanged()
    {
        RefreshAll();
    }

    /// <summary>
    /// Refreshes UI when station state changes.
    /// </summary>
    private void HandleResearchStationStateChanged()
    {
        RefreshAll();
    }
}
