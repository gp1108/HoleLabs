using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Main controller for the upgrade UI.
/// It builds upgrade entries, listens to wallet and upgrade events,
/// and keeps the panel synchronized with runtime progression data.
/// This panel can optionally filter definitions by ShopId so multiple stores
/// can reuse the same central UpgradeManager without duplicating systems.
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

    [Tooltip("Container where upgrade entry instances are spawned.")]
    [SerializeField] private Transform EntryContainer;

    [Tooltip("Prefab used to create one UI entry per visible upgrade definition.")]
    [SerializeField] private UpgradeEntryUI EntryPrefab;

    [Header("Currencies")]
    [Tooltip("Text used to display the current gold balance.")]
    [SerializeField] private TMP_Text GoldAmountText;

    [Tooltip("Text used to display the current research balance.")]
    [SerializeField] private TMP_Text ResearchAmountText;

    [Header("Shop Filter")]
    [Tooltip("If true, this panel shows every upgrade definition regardless of ShopId.")]
    [SerializeField] private bool ShowAllUpgrades = true;

    [Tooltip("Logical shop id shown by this panel when ShowAllUpgrades is disabled.")]
    [SerializeField] private string TargetShopId;

    [Tooltip("If true, upgrades with an empty ShopId are also shown when filtering by TargetShopId.")]
    [SerializeField] private bool IncludeEmptyShopIdWhenFiltering = false;

    [Header("Behaviour")]
    [Tooltip("If true, entries are rebuilt during Awake.")]
    [SerializeField] private bool RebuildOnAwake = true;

    [Tooltip("If true, entries are rebuilt during Start.")]
    [SerializeField] private bool RebuildOnStart = true;

    /// <summary>
    /// Runtime list of entry instances currently spawned by this panel.
    /// </summary>
    private readonly List<UpgradeEntryUI> SpawnedEntries = new();

    /// <summary>
    /// Resolves references, subscribes to runtime events and optionally builds the panel immediately.
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

        if (RebuildOnAwake)
        {
            RebuildEntries();
            RefreshAll();
        }

        PanelRoot.SetActive(false);
    }

    /// <summary>
    /// Optionally rebuilds the panel after all scene objects have finished initialization.
    /// </summary>
    private void Start()
    {
        if (RebuildOnStart)
        {
            RebuildEntries();
            RefreshAll();
        }
    }

    /// <summary>
    /// Unsubscribes from wallet and upgrade events when this panel is destroyed.
    /// </summary>
    private void OnDestroy()
    {
        UnsubscribeFromEvents();
    }

    /// <summary>
    /// Rebuilds every entry shown by this panel using the configured shop filter.
    /// </summary>
    public void RebuildEntries()
    {
        ClearEntries();

        if (UpgradeManager == null || EntryContainer == null || EntryPrefab == null)
        {
            return;
        }

        IReadOnlyList<UpgradeDefinition> UpgradeDefinitions = UpgradeManager.GetAllUpgradeDefinitions();

        for (int Index = 0; Index < UpgradeDefinitions.Count; Index++)
        {
            UpgradeDefinition Definition = UpgradeDefinitions[Index];

            if (Definition == null)
            {
                continue;
            }

            if (!ShouldDisplayUpgrade(Definition))
            {
                continue;
            }

            UpgradeEntryUI Entry = Instantiate(EntryPrefab, EntryContainer);
            Entry.Initialize(UpgradeManager, CurrencyWallet, Definition);
            SpawnedEntries.Add(Entry);
        }
    }

    /// <summary>
    /// Refreshes wallet labels and all currently spawned entries.
    /// </summary>
    public void RefreshAll()
    {
        RefreshCurrencyTexts();

        for (int Index = 0; Index < SpawnedEntries.Count; Index++)
        {
            if (SpawnedEntries[Index] != null)
            {
                SpawnedEntries[Index].RefreshView();
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
    /// Shows or hides this panel and refreshes its entries when opened.
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
            RebuildEntries();
            RefreshAll();
        }
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
    /// Sets the target shop id used by this panel and rebuilds the visible entries.
    /// </summary>
    public void SetTargetShopId(string NewTargetShopId)
    {
        TargetShopId = NewTargetShopId;
        RebuildEntries();
        RefreshAll();
    }

    /// <summary>
    /// Sets whether this panel should ignore shop filtering and show all upgrades.
    /// </summary>
    public void SetShowAllUpgrades(bool ShouldShowAllUpgrades)
    {
        ShowAllUpgrades = ShouldShowAllUpgrades;
        RebuildEntries();
        RefreshAll();
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
    /// Destroys every currently spawned entry instance.
    /// </summary>
    private void ClearEntries()
    {
        for (int Index = 0; Index < SpawnedEntries.Count; Index++)
        {
            if (SpawnedEntries[Index] != null)
            {
                Destroy(SpawnedEntries[Index].gameObject);
            }
        }

        SpawnedEntries.Clear();
    }

    /// <summary>
    /// Returns whether the provided upgrade definition belongs to this panel according to the current shop filter.
    /// </summary>
    private bool ShouldDisplayUpgrade(UpgradeDefinition Definition)
    {
        if (Definition == null)
        {
            return false;
        }

        if (ShowAllUpgrades)
        {
            return true;
        }

        string DefinitionShopId = Definition.GetShopId();

        if (string.IsNullOrWhiteSpace(DefinitionShopId))
        {
            return IncludeEmptyShopIdWhenFiltering;
        }

        return string.Equals(DefinitionShopId, TargetShopId, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Refreshes the panel when any currency amount changes.
    /// </summary>
    private void HandleCurrencyChanged(CurrencyWallet.CurrencyType CurrencyTypeValue, float NewAmount)
    {
        RefreshAll();
    }

    /// <summary>
    /// Refreshes the panel when upgrade state changes.
    /// </summary>
    private void HandleUpgradeStateChanged()
    {
        RefreshAll();
    }
}