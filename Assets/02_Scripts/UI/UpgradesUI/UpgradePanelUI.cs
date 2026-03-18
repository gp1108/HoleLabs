using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Main controller for the upgrade research UI.
/// It builds all upgrade entries, listens to wallet and upgrade events,
/// and keeps the full panel synchronized with runtime progression data.
/// </summary>
public sealed class UpgradePanelUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Upgrade manager used as the main data source for upgrade levels and purchases.")]
    [SerializeField] private UpgradeManager UpgradeManager;

    [Tooltip("Currency wallet used to show current balances and react to currency changes.")]
    [SerializeField] private CurrencyWallet CurrencyWallet;

    [Tooltip("Container where upgrade entry instances will be spawned.")]
    [SerializeField] private Transform EntryContainer;

    [Tooltip("Prefab used to represent one upgrade entry.")]
    [SerializeField] private UpgradeEntryUI EntryPrefab;

    [Header("Currencies")]
    [Tooltip("Optional text field used to display the current gold amount.")]
    [SerializeField] private TMP_Text GoldAmountText;

    [Tooltip("Optional text field used to display the current research amount.")]
    [SerializeField] private TMP_Text ResearchAmountText;

    [Header("Behaviour")]
    [Tooltip("If true, the panel is fully rebuilt during Awake.")]
    [SerializeField] private bool RebuildOnAwake = true;
    [Tooltip("If true, the panel is fully rebuilt during Start.")]
    [SerializeField] private bool RebuildOnStart = true;

    private readonly List<UpgradeEntryUI> SpawnedEntries = new();

    /// <summary>
    /// Initializes references, subscribes to runtime events and optionally rebuilds the UI.
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

        SubscribeToEvents();

        if (RebuildOnAwake)
        {
            RebuildEntries();
            RefreshAll();
        }
    }

    private void Start()
    {
        if (RebuildOnStart)
        {
            RebuildEntries();
            RefreshAll();
        }
    }

    /// <summary>
    /// Unsubscribes from runtime events.
    /// </summary>
    private void OnDestroy()
    {
        UnsubscribeFromEvents();
    }

    /// <summary>
    /// Rebuilds all upgrade entries from the manager definitions.
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

            UpgradeEntryUI Entry = Instantiate(EntryPrefab, EntryContainer);
            Entry.Initialize(UpgradeManager, CurrencyWallet, Definition);
            SpawnedEntries.Add(Entry);
        }
    }

    /// <summary>
    /// Refreshes currencies and all upgrade entries.
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
    /// Refreshes the visible currency texts.
    /// </summary>
    public void RefreshCurrencyTexts()
    {
        if (CurrencyWallet == null)
        {
            return;
        }

        if (GoldAmountText != null)
        {
            GoldAmountText.text = CurrencyWallet.GetBalance(CurrencyWallet.CurrencyType.Gold).ToString();
        }

        if (ResearchAmountText != null)
        {
            ResearchAmountText.text = CurrencyWallet.GetBalance(CurrencyWallet.CurrencyType.Research).ToString();
        }
    }

    /// <summary>
    /// Subscribes to wallet and upgrade manager events.
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
    /// Unsubscribes from wallet and upgrade manager events.
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
    /// Destroys all currently spawned UI entries.
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
    /// Handles currency changes by refreshing currency texts and entry states.
    /// </summary>
    private void HandleCurrencyChanged(CurrencyWallet.CurrencyType CurrencyType, int NewAmount)
    {
        RefreshAll();
    }

    /// <summary>
    /// Handles upgrade state changes by refreshing the whole panel.
    /// </summary>
    private void HandleUpgradeStateChanged()
    {
        RefreshAll();
    }
}
