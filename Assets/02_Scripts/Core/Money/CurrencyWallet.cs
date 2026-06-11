using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stores and manages the player credit balance.
/// Credits are the only authoritative currency in the current HoleLabs economy.
/// </summary>
public sealed class CurrencyWallet : MonoBehaviour
{
    /// <summary>
    /// Defines the supported gameplay currency.
    /// </summary>
    public enum CurrencyType
    {
        Credits = 0
    }

    [Serializable]
    private sealed class CurrencyEntry
    {
        [Tooltip("Currency type stored by this entry. Credits is the only supported runtime currency.")]
        [SerializeField] private CurrencyType Type = CurrencyType.Credits;

        [Tooltip("Current amount owned for this currency type.")]
        [SerializeField] private float Amount;

        /// <summary>
        /// Gets the currency type represented by this entry.
        /// </summary>
        public CurrencyType GetTypeValue()
        {
            return NormalizeCurrencyType(Type);
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
        /// <param name="AmountValue">New amount assigned to this entry.</param>
        public void SetAmount(float AmountValue)
        {
            Amount = CurrencyMath.RoundCurrency(Mathf.Max(0f, AmountValue));
        }
    }

    [Header("Defaults")]
    [Tooltip("Optional starting values assigned on Awake. Use Credits for the current economy.")]
    [SerializeField] private List<CurrencyEntry> DefaultCurrencies = new();

    [Header("Debug")]
    [Tooltip("Logs wallet operations to the console.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Runtime balances indexed by normalized currency type.
    /// </summary>
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

            CurrencyType NormalizedType = NormalizeCurrencyType(Entry.GetTypeValue());
            Balances[NormalizedType] = CurrencyMath.RoundCurrency(Mathf.Max(0f, Entry.GetAmount()));
        }
    }

    /// <summary>
    /// Logs the current wallet balances for debug validation.
    /// </summary>
    [ContextMenu("Debug Currency")]
    public void DebugCurrency()
    {
        Debug.Log(GetCredits().ToString("0.00") + " credits", this);
    }

    /// <summary>
    /// Gets the current credit balance.
    /// </summary>
    /// <returns>Current amount of credits owned by the player.</returns>
    public float GetCredits()
    {
        return GetBalance(CurrencyType.Credits);
    }

    /// <summary>
    /// Adds credits to the wallet.
    /// </summary>
    /// <param name="Amount">Credit amount to add.</param>
    public void AddCredits(float Amount)
    {
        AddCurrency(CurrencyType.Credits, Amount);
    }

    /// <summary>
    /// Checks whether the wallet has enough credits.
    /// </summary>
    /// <param name="Amount">Credit amount required.</param>
    /// <returns>True when the wallet contains enough credits.</returns>
    public bool HasEnoughCredits(float Amount)
    {
        return HasEnough(CurrencyType.Credits, Amount);
    }

    /// <summary>
    /// Attempts to spend credits from the wallet.
    /// </summary>
    /// <param name="Amount">Credit amount to spend.</param>
    /// <returns>True when the spend operation succeeded.</returns>
    public bool TrySpendCredits(float Amount)
    {
        return TrySpendCurrency(CurrencyType.Credits, Amount);
    }

    /// <summary>
    /// Sets the exact credit balance.
    /// Useful for loading save data or debugging.
    /// </summary>
    /// <param name="Amount">New credit amount.</param>
    public void SetCredits(float Amount)
    {
        SetBalance(CurrencyType.Credits, Amount);
    }

    /// <summary>
    /// Gets the current balance for the provided currency type.
    /// </summary>
    /// <param name="CurrencyTypeValue">Currency type to read.</param>
    /// <returns>Current rounded balance.</returns>
    public float GetBalance(CurrencyType CurrencyTypeValue)
    {
        CurrencyType NormalizedType = NormalizeCurrencyType(CurrencyTypeValue);

        if (Balances.TryGetValue(NormalizedType, out float Amount))
        {
            return Amount;
        }

        return 0f;
    }

    /// <summary>
    /// Adds currency to the wallet.
    /// </summary>
    /// <param name="CurrencyTypeValue">Currency type to add.</param>
    /// <param name="Amount">Amount to add.</param>
    public void AddCurrency(CurrencyType CurrencyTypeValue, float Amount)
    {
        if (Amount <= 0f)
        {
            return;
        }

        CurrencyType NormalizedType = NormalizeCurrencyType(CurrencyTypeValue);
        float NewAmount = CurrencyMath.RoundCurrency(GetBalance(NormalizedType) + Amount);
        Balances[NormalizedType] = NewAmount;

        NotifyCurrencyChanged(NormalizedType, NewAmount);
        Log("Added " + Amount.ToString("0.00") + " " + NormalizedType + ". New balance: " + NewAmount.ToString("0.00"));
    }

    /// <summary>
    /// Checks whether the wallet contains enough of the provided currency.
    /// </summary>
    /// <param name="CurrencyTypeValue">Currency type to check.</param>
    /// <param name="Amount">Required amount.</param>
    /// <returns>True when the wallet has enough currency.</returns>
    public bool HasEnough(CurrencyType CurrencyTypeValue, float Amount)
    {
        if (Amount <= 0f)
        {
            return true;
        }

        CurrencyType NormalizedType = NormalizeCurrencyType(CurrencyTypeValue);
        return GetBalance(NormalizedType) + CurrencyMath.CurrencyComparisonEpsilon >= CurrencyMath.RoundCurrency(Amount);
    }

    /// <summary>
    /// Attempts to spend currency from the wallet.
    /// </summary>
    /// <param name="CurrencyTypeValue">Currency type to spend.</param>
    /// <param name="Amount">Amount to spend.</param>
    /// <returns>True when the spend operation succeeded.</returns>
    public bool TrySpendCurrency(CurrencyType CurrencyTypeValue, float Amount)
    {
        if (Amount <= 0f)
        {
            return true;
        }

        CurrencyType NormalizedType = NormalizeCurrencyType(CurrencyTypeValue);
        float RoundedAmount = CurrencyMath.RoundCurrency(Amount);
        float CurrentBalance = GetBalance(NormalizedType);

        if (CurrentBalance + CurrencyMath.CurrencyComparisonEpsilon < RoundedAmount)
        {
            Log("Failed to spend " + RoundedAmount.ToString("0.00") + " " + NormalizedType + ". Current balance: " + CurrentBalance.ToString("0.00"));
            return false;
        }

        float NewAmount = CurrencyMath.RoundCurrency(CurrentBalance - RoundedAmount);
        Balances[NormalizedType] = Mathf.Max(0f, NewAmount);

        NotifyCurrencyChanged(NormalizedType, Balances[NormalizedType]);
        Log("Spent " + RoundedAmount.ToString("0.00") + " " + NormalizedType + ". New balance: " + Balances[NormalizedType].ToString("0.00"));
        return true;
    }

    /// <summary>
    /// Sets the exact balance for a currency type.
    /// Useful for loading save data or debugging.
    /// </summary>
    /// <param name="CurrencyTypeValue">Currency type to set.</param>
    /// <param name="Amount">New amount.</param>
    public void SetBalance(CurrencyType CurrencyTypeValue, float Amount)
    {
        CurrencyType NormalizedType = NormalizeCurrencyType(CurrencyTypeValue);
        float ClampedAmount = CurrencyMath.RoundCurrency(Mathf.Max(0f, Amount));
        Balances[NormalizedType] = ClampedAmount;

        NotifyCurrencyChanged(NormalizedType, ClampedAmount);
        Log("Set " + NormalizedType + " balance to " + ClampedAmount.ToString("0.00"));
    }

    /// <summary>
    /// Normalizes serialized currency values into the current authoritative runtime type.
    /// </summary>
    /// <param name="CurrencyTypeValue">Currency type to normalize.</param>
    /// <returns>Normalized currency type.</returns>
    public static CurrencyType NormalizeCurrencyType(CurrencyType CurrencyTypeValue)
    {
        return CurrencyType.Credits;
    }

    /// <summary>
    /// Raises the currency changed event using the normalized currency type.
    /// </summary>
    /// <param name="CurrencyTypeValue">Currency type that changed.</param>
    /// <param name="NewAmount">New currency amount.</param>
    private void NotifyCurrencyChanged(CurrencyType CurrencyTypeValue, float NewAmount)
    {
        OnCurrencyChanged?.Invoke(NormalizeCurrencyType(CurrencyTypeValue), NewAmount);
    }

    /// <summary>
    /// Logs wallet messages if debug logging is enabled.
    /// </summary>
    /// <param name="Message">Message to write.</param>
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
    /// <param name="Value">Value to round.</param>
    /// <returns>Currency value rounded to two decimals.</returns>
    public static float RoundCurrency(float Value)
    {
        return Mathf.Round(Value * 100f) / 100f;
    }

    /// <summary>
    /// Converts a currency float value to integer minor units.
    /// </summary>
    /// <param name="Value">Currency value.</param>
    /// <returns>Integer minor units.</returns>
    public static int CurrencyToCents(float Value)
    {
        return Mathf.RoundToInt(RoundCurrency(Value) * 100f);
    }

    /// <summary>
    /// Converts integer minor units back to a currency float value.
    /// </summary>
    /// <param name="Cents">Minor units.</param>
    /// <returns>Currency value.</returns>
    public static float CentsToCurrency(int Cents)
    {
        return Cents / 100f;
    }
}
