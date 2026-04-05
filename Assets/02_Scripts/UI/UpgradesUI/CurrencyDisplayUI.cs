using TMPro;
using UnityEngine;

/// <summary>
/// Small standalone currency display that can be used outside the full upgrade panel if needed.
/// </summary>
public sealed class CurrencyDisplayUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Wallet used to read and react to currency changes.")]
    [SerializeField] private CurrencyWallet CurrencyWallet;

    [Tooltip("Text used to display the current gold amount.")]
    [SerializeField] private TMP_Text GoldAmountText;

    [Tooltip("Text used to display the current research amount.")]
    [SerializeField] private TMP_Text ResearchAmountText;

    /// <summary>
    /// Initializes references and refreshes the displayed values.
    /// </summary>
    private void Awake()
    {
        if (CurrencyWallet == null)
        {
            CurrencyWallet = FindFirstObjectByType<CurrencyWallet>();
        }

        RefreshView();
    }

    /// <summary>
    /// Subscribes to currency events.
    /// </summary>
    private void OnEnable()
    {
        if (CurrencyWallet != null)
        {
            CurrencyWallet.OnCurrencyChanged += HandleCurrencyChanged;
        }
    }

    /// <summary>
    /// Unsubscribes from currency events.
    /// </summary>
    private void OnDisable()
    {
        if (CurrencyWallet != null)
        {
            CurrencyWallet.OnCurrencyChanged -= HandleCurrencyChanged;
        }
    }

    /// <summary>
    /// Refreshes the displayed balances.
    /// </summary>
    public void RefreshView()
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
    /// Handles wallet changes by refreshing the displayed values.
    /// </summary>
    private void HandleCurrencyChanged(CurrencyWallet.CurrencyType CurrencyType, float NewAmount)
    {
        RefreshView();
    }
}