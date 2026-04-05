using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stores and manages every gameplay currency owned by the player.
/// This wallet uses float balances rounded to currency precision so gameplay
/// can support cents while UI and purchases remain deterministic.
/// </summary>
public sealed class CurrencyWallet : MonoBehaviour
{
    /// <summary>
    /// Defines every supported currency type used by the game.
    /// </summary>
    public enum CurrencyType
    {
        Gold = 0,
        Research = 1
    }

    [Serializable]
    private sealed class CurrencyEntry
    {
        [Tooltip("Type of currency stored by this entry.")]
        [SerializeField] private CurrencyType Type;

        [Tooltip("Current amount owned for this currency type.")]
        [SerializeField] private float Amount;

        /// <summary>
        /// Gets the currency type represented by this entry.
        /// </summary>
        public CurrencyType GetTypeValue()
        {
            return Type;
        }

        /// <summary>
        /// Gets the amount currently stored by this entry.
        /// </summary>
        public float GetAmount()
        {
            return Amount;
        }

        /// <summary>
        /// Sets the amount currently stored by this entry.
        /// </summary>
        public void SetAmount(float AmountValue)
        {
            Amount = CurrencyMath.RoundCurrency(Mathf.Max(0f, AmountValue));
        }
    }

    [Header("Defaults")]
    [Tooltip("Optional starting values assigned on Awake.")]
    [SerializeField] private List<CurrencyEntry> DefaultCurrencies = new();

    [Header("Debug")]
    [Tooltip("Logs wallet operations to the console.")]
    [SerializeField] private bool DebugLogs = false;

    private readonly Dictionary<CurrencyType, float> Balances = new();

    /// <summary>
    /// Fired whenever a currency amount changes.
    /// The first argument is the affected currency type and the second argument is the new amount.
    /// </summary>
    public event Action<CurrencyType, float> OnCurrencyChanged;

    /// <summary>
    /// Initializes balances from the configured default values.
    /// </summary>
    private void Awake()
    {
        Balances.Clear();

        foreach (CurrencyEntry Entry in DefaultCurrencies)
        {
            if (Entry == null)
            {
                continue;
            }

            Balances[Entry.GetTypeValue()] = CurrencyMath.RoundCurrency(Mathf.Max(0f, Entry.GetAmount()));
        }
    }

    [ContextMenu("DebugCurrency")]
    public void DebugCurrency()
    {
        Debug.Log(GetBalance(CurrencyType.Gold).ToString("0.00") + " gold");
        Debug.Log(GetBalance(CurrencyType.Research).ToString("0.00") + " research");
    }

    /// <summary>
    /// Gets the current balance for the provided currency type.
    /// </summary>
    public float GetBalance(CurrencyType CurrencyTypeValue)
    {
        if (Balances.TryGetValue(CurrencyTypeValue, out float Amount))
        {
            return Amount;
        }

        return 0f;
    }

    /// <summary>
    /// Adds currency to the wallet.
    /// </summary>
    public void AddCurrency(CurrencyType CurrencyTypeValue, float Amount)
    {
        if (Amount <= 0f)
        {
            return;
        }

        float NewAmount = CurrencyMath.RoundCurrency(GetBalance(CurrencyTypeValue) + Amount);
        Balances[CurrencyTypeValue] = NewAmount;

        NotifyCurrencyChanged(CurrencyTypeValue, NewAmount);
        Log("Added " + Amount.ToString("0.00") + " " + CurrencyTypeValue + ". New balance: " + NewAmount.ToString("0.00"));
    }

    /// <summary>
    /// Checks whether the wallet contains enough of the provided currency.
    /// </summary>
    public bool HasEnough(CurrencyType CurrencyTypeValue, float Amount)
    {
        if (Amount <= 0f)
        {
            return true;
        }

        return GetBalance(CurrencyTypeValue) + CurrencyMath.CurrencyComparisonEpsilon >= CurrencyMath.RoundCurrency(Amount);
    }

    /// <summary>
    /// Attempts to spend currency from the wallet.
    /// </summary>
    public bool TrySpendCurrency(CurrencyType CurrencyTypeValue, float Amount)
    {
        if (Amount <= 0f)
        {
            return true;
        }

        float RoundedAmount = CurrencyMath.RoundCurrency(Amount);
        float CurrentBalance = GetBalance(CurrencyTypeValue);

        if (CurrentBalance + CurrencyMath.CurrencyComparisonEpsilon < RoundedAmount)
        {
            Log("Failed to spend " + RoundedAmount.ToString("0.00") + " " + CurrencyTypeValue + ". Current balance: " + CurrentBalance.ToString("0.00"));
            return false;
        }

        float NewAmount = CurrencyMath.RoundCurrency(CurrentBalance - RoundedAmount);
        Balances[CurrencyTypeValue] = Mathf.Max(0f, NewAmount);

        NotifyCurrencyChanged(CurrencyTypeValue, Balances[CurrencyTypeValue]);
        Log("Spent " + RoundedAmount.ToString("0.00") + " " + CurrencyTypeValue + ". New balance: " + Balances[CurrencyTypeValue].ToString("0.00"));
        return true;
    }

    /// <summary>
    /// Sets the exact balance for a currency type.
    /// Useful for loading save data or debugging.
    /// </summary>
    public void SetBalance(CurrencyType CurrencyTypeValue, float Amount)
    {
        float ClampedAmount = CurrencyMath.RoundCurrency(Mathf.Max(0f, Amount));
        Balances[CurrencyTypeValue] = ClampedAmount;

        NotifyCurrencyChanged(CurrencyTypeValue, ClampedAmount);
        Log("Set " + CurrencyTypeValue + " balance to " + ClampedAmount.ToString("0.00"));
    }

    /// <summary>
    /// Raises the currency changed event.
    /// </summary>
    private void NotifyCurrencyChanged(CurrencyType CurrencyTypeValue, float NewAmount)
    {
        OnCurrencyChanged?.Invoke(CurrencyTypeValue, NewAmount);
    }

    /// <summary>
    /// Logs wallet messages if debug logging is enabled.
    /// </summary>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[CurrencyWallet] " + Message, this);
    }
}

/// <summary>
/// Shared helpers for deterministic currency rounding and conversion.
/// </summary>
public static class CurrencyMath
{
    /// <summary>
    /// Small epsilon used to compare rounded currency values safely.
    /// </summary>
    public const float CurrencyComparisonEpsilon = 0.0001f;

    /// <summary>
    /// Rounds a currency value to two decimals.
    /// </summary>
    public static float RoundCurrency(float Value)
    {
        return Mathf.Round(Value * 100f) / 100f;
    }

    /// <summary>
    /// Converts a currency float value to integer cents.
    /// </summary>
    public static int CurrencyToCents(float Value)
    {
        return Mathf.RoundToInt(RoundCurrency(Value) * 100f);
    }

    /// <summary>
    /// Converts integer cents back to a currency float value.
    /// </summary>
    public static float CentsToCurrency(int Cents)
    {
        return Cents / 100f;
    }
}