using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Manual controller for the upgrade UI.
/// This panel does not generate entries dynamically.
/// Instead, it discovers manually placed list entries and tree groups,
/// initializes them and refreshes their state when currency or upgrade data changes.
/// </summary>
public sealed class UpgradePanelUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Central runtime upgrade manager used by this panel.")]
    [SerializeField] private UpgradeManager UpgradeManager;

    [Tooltip("Central wallet used to display current balances.")]
    [SerializeField] private CurrencyWallet CurrencyWallet;

    [Tooltip("Root object toggled on and off when showing or hiding this panel.")]
    [SerializeField] private GameObject PanelRoot;

    [Header("Currencies")]
    [Tooltip("Text used to display the current gold balance.")]
    [SerializeField] private TMP_Text GoldAmountText;

    [Tooltip("Text used to display the current research balance.")]
    [SerializeField] private TMP_Text ResearchAmountText;

    [Header("Discovery")]
    [Tooltip("If true, manual entries and tree groups are discovered during Awake.")]
    [SerializeField] private bool DiscoverOnAwake = true;

    [Tooltip("If true, manual entries and tree groups are rediscovered whenever the panel is shown.")]
    [SerializeField] private bool RediscoverOnShow = true;

    /// <summary>
    /// Manual list entries currently registered under this panel.
    /// </summary>
    private readonly List<UpgradeListEntryUI> RegisteredListEntries = new();

    /// <summary>
    /// Manual tree groups currently registered under this panel.
    /// </summary>
    private readonly List<UpgradeTreeGroupUI> RegisteredTreeGroups = new();

    /// <summary>
    /// Resolves references, subscribes to runtime events and optionally discovers manual UI elements.
    /// </summary>
    private void Awake()
    {
        if (UpgradeManager == null)
        {
            UpgradeManager = FindFirstObjectByType<UpgradeManager>();
        }

        if (CurrencyWallet == null)
        {
            CurrencyWallet = FindFirstObjectByType<CurrencyWallet>();
        }

        if (PanelRoot == null)
        {
            PanelRoot = gameObject;
        }

        SubscribeToEvents();

        if (DiscoverOnAwake)
        {
            DiscoverManualUi();
            InitializeManualUi();
            RefreshAll();
        }

        PanelRoot.SetActive(false);
    }

    /// <summary>
    /// Unsubscribes from runtime events when this panel is destroyed.
    /// </summary>
    private void OnDestroy()
    {
        UnsubscribeFromEvents();
    }

    /// <summary>
    /// Shows or hides this panel and refreshes its contents when opened.
    /// </summary>
    public void SetVisible(bool IsVisible)
    {
        if (PanelRoot == null)
        {
            return;
        }

        PanelRoot.SetActive(IsVisible);

        if (!IsVisible)
        {
            return;
        }

        if (RediscoverOnShow)
        {
            DiscoverManualUi();
        }

        InitializeManualUi();
        RefreshAll();
        RebuildTreeGroups();
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
    /// Refreshes wallet labels and all registered manual entries.
    /// </summary>
    public void RefreshAll()
    {
        RefreshCurrencyTexts();

        for (int Index = 0; Index < RegisteredListEntries.Count; Index++)
        {
            if (RegisteredListEntries[Index] != null)
            {
                RegisteredListEntries[Index].RefreshView();
            }
        }

        for (int Index = 0; Index < RegisteredTreeGroups.Count; Index++)
        {
            if (RegisteredTreeGroups[Index] != null)
            {
                RegisteredTreeGroups[Index].RefreshAllEntries();
            }
        }
    }

    /// <summary>
    /// Refreshes the wallet amounts displayed by this panel.
    /// </summary>
    public void RefreshCurrencyTexts()
    {
        if (CurrencyWallet == null)
        {
            return;
        }

        if (GoldAmountText != null)
        {
            GoldAmountText.text = CurrencyWallet.GetBalance(CurrencyWallet.CurrencyType.Gold).ToString("0.00");
        }

        if (ResearchAmountText != null)
        {
            ResearchAmountText.text = CurrencyWallet.GetBalance(CurrencyWallet.CurrencyType.Research).ToString("0.00");
        }
    }

    /// <summary>
    /// Discovers all manual list entries and tree groups under this panel, including inactive children.
    /// </summary>
    public void DiscoverManualUi()
    {
        RegisteredListEntries.Clear();
        RegisteredTreeGroups.Clear();

        UpgradeListEntryUI[] ListEntries = GetComponentsInChildren<UpgradeListEntryUI>(true);
        UpgradeTreeGroupUI[] TreeGroups = GetComponentsInChildren<UpgradeTreeGroupUI>(true);

        for (int Index = 0; Index < ListEntries.Length; Index++)
        {
            if (ListEntries[Index] != null)
            {
                RegisteredListEntries.Add(ListEntries[Index]);
            }
        }

        for (int Index = 0; Index < TreeGroups.Length; Index++)
        {
            if (TreeGroups[Index] != null)
            {
                RegisteredTreeGroups.Add(TreeGroups[Index]);
            }
        }
    }

    /// <summary>
    /// Initializes every discovered manual UI component with the runtime references required to function.
    /// </summary>
    public void InitializeManualUi()
    {
        for (int Index = 0; Index < RegisteredListEntries.Count; Index++)
        {
            if (RegisteredListEntries[Index] != null)
            {
                RegisteredListEntries[Index].Initialize(UpgradeManager);
            }
        }

        for (int Index = 0; Index < RegisteredTreeGroups.Count; Index++)
        {
            if (RegisteredTreeGroups[Index] != null)
            {
                RegisteredTreeGroups[Index].Initialize(UpgradeManager);
            }
        }
    }

    /// <summary>
    /// Rebuilds all registered tree groups so their visual connections match the current manual layout.
    /// </summary>
    public void RebuildTreeGroups()
    {
        for (int Index = 0; Index < RegisteredTreeGroups.Count; Index++)
        {
            if (RegisteredTreeGroups[Index] != null)
            {
                RegisteredTreeGroups[Index].RebuildConnections();
            }
        }
    }

    /// <summary>
    /// Subscribes to wallet and upgrade events so the panel stays synchronized at runtime.
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
    /// Refreshes the panel when any currency amount changes.
    /// </summary>
    private void HandleCurrencyChanged(CurrencyWallet.CurrencyType CurrencyTypeValue, float NewAmount)
    {
        RefreshAll();
    }

    /// <summary>
    /// Refreshes all manual entries when upgrade state changes.
    /// </summary>
    private void HandleUpgradeStateChanged()
    {
        RefreshAll();
    }
}