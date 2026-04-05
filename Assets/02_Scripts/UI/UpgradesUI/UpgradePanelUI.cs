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
    [SerializeField] private UpgradeManager UpgradeManager;
    [SerializeField] private CurrencyWallet CurrencyWallet;
    [SerializeField] private GameObject PanelRoot;
    [SerializeField] private Transform EntryContainer;
    [SerializeField] private UpgradeEntryUI EntryPrefab;

    [Header("Currencies")]
    [SerializeField] private TMP_Text GoldAmountText;
    [SerializeField] private TMP_Text ResearchAmountText;

    [Header("Behaviour")]
    [SerializeField] private bool RebuildOnAwake = true;
    [SerializeField] private bool RebuildOnStart = true;

    private readonly List<UpgradeEntryUI> SpawnedEntries = new();

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

    private void Start()
    {
        if (RebuildOnStart)
        {
            RebuildEntries();
            RefreshAll();
        }
    }

    private void OnDestroy()
    {
        UnsubscribeFromEvents();
    }

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

    public void ShowPanel()
    {
        SetVisible(true);
    }

    public void HidePanel()
    {
        SetVisible(false);
    }

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

    private void HandleCurrencyChanged(CurrencyWallet.CurrencyType CurrencyTypeValue, float NewAmount)
    {
        RefreshAll();
    }

    private void HandleUpgradeStateChanged()
    {
        RefreshAll();
    }
}