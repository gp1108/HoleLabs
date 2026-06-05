using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Manual product shop panel.
/// It discovers manually placed product entries and refreshes them when currency or upgrade state changes.
/// </summary>
public sealed class ShopProductPanelUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Root object toggled when this panel is shown or hidden.")]
    [SerializeField] private GameObject PanelRoot;

    [Tooltip("Wallet used to display current credit balance.")]
    [SerializeField] private CurrencyWallet CurrencyWallet;

    [Tooltip("Text used to display the current credit balance.")]
    [SerializeField] private TMP_Text CreditsAmountText;

    [Header("Discovery")]
    [Tooltip("If true, product entries are discovered during Awake.")]
    [SerializeField] private bool DiscoverOnAwake = true;

    [Tooltip("If true, product entries are rediscovered whenever the panel is shown.")]
    [SerializeField] private bool RediscoverOnShow = true;

    /// <summary>
    /// Product station that owns this panel.
    /// </summary>
    private ShopProductStation OwnerStation;

    /// <summary>
    /// Manual product entries currently registered under this panel.
    /// </summary>
    private readonly List<ShopProductListEntryUI> RegisteredProductEntries = new();

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

        SubscribeToEvents();

        if (DiscoverOnAwake)
        {
            DiscoverManualUi();
        }

        PanelRoot.SetActive(false);
    }

    /// <summary>
    /// Unsubscribes from runtime events.
    /// </summary>
    private void OnDestroy()
    {
        UnsubscribeFromEvents();
    }

    /// <summary>
    /// Initializes this panel with the owning product station.
    /// </summary>
    /// <param name="Station">Product station that owns this panel.</param>
    public void Initialize(ShopProductStation Station)
    {
        OwnerStation = Station;
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
    /// <param name="IsVisible">True to show the panel.</param>
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
    /// Refreshes wallet labels and product entries.
    /// </summary>
    public void RefreshAll()
    {
        RefreshCurrencyTexts();

        for (int Index = 0; Index < RegisteredProductEntries.Count; Index++)
        {
            if (RegisteredProductEntries[Index] != null)
            {
                RegisteredProductEntries[Index].RefreshView();
            }
        }
    }

    /// <summary>
    /// Discovers all manual product entries under this panel.
    /// </summary>
    public void DiscoverManualUi()
    {
        RegisteredProductEntries.Clear();

        ShopProductListEntryUI[] Entries = GetComponentsInChildren<ShopProductListEntryUI>(true);

        for (int Index = 0; Index < Entries.Length; Index++)
        {
            if (Entries[Index] != null)
            {
                RegisteredProductEntries.Add(Entries[Index]);
            }
        }
    }

    /// <summary>
    /// Initializes every discovered product entry with the owning station.
    /// </summary>
    public void InitializeManualUi()
    {
        for (int Index = 0; Index < RegisteredProductEntries.Count; Index++)
        {
            if (RegisteredProductEntries[Index] != null)
            {
                RegisteredProductEntries[Index].Initialize(OwnerStation);
            }
        }
    }

    /// <summary>
    /// Refreshes the displayed credit amount.
    /// </summary>
    private void RefreshCurrencyTexts()
    {
        if (CurrencyWallet == null || CreditsAmountText == null)
        {
            return;
        }

        CreditsAmountText.text = CurrencyWallet.GetCredits().ToString("0.00") + " C";
    }

    /// <summary>
    /// Subscribes to wallet events.
    /// </summary>
    private void SubscribeToEvents()
    {
        if (CurrencyWallet != null)
        {
            CurrencyWallet.OnCurrencyChanged += HandleCurrencyChanged;
        }
    }

    /// <summary>
    /// Unsubscribes from wallet events.
    /// </summary>
    private void UnsubscribeFromEvents()
    {
        if (CurrencyWallet != null)
        {
            CurrencyWallet.OnCurrencyChanged -= HandleCurrencyChanged;
        }
    }

    /// <summary>
    /// Refreshes the panel when currency changes.
    /// </summary>
    private void HandleCurrencyChanged(CurrencyWallet.CurrencyType CurrencyTypeValue, float NewAmount)
    {
        RefreshAll();
    }
}
