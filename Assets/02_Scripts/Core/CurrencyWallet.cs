using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stores and manages every gameplay currency owned by the player.
/// This wallet is intentionally generic so both shops and research stations
/// can consume different currencies through the same API.
/// </summary>
public sealed class CurrencyWallet : MonoBehaviour
{
    /// <summary>
    /// Defines every supported currency type used by the game.
    /// This enum lives inside the wallet so currency ownership
    /// and currency identification stay grouped together.
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
        [SerializeField] private int Amount;

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
        public int GetAmount()
        {
            return Amount;
        }

        /// <summary>
        /// Sets the amount currently stored by this entry.
        /// </summary>
        public void SetAmount(int amount)
        {
            Amount = Mathf.Max(0, amount);
        }
    }

    [Header("Defaults")]
    [Tooltip("Optional starting values assigned on Awake.")]
    [SerializeField] private List<CurrencyEntry> DefaultCurrencies = new();

    [Header("Debug")]
    [Tooltip("Logs wallet operations to the console.")]
    [SerializeField] private bool DebugLogs = false;

    private readonly Dictionary<CurrencyType, int> Balances = new();

    /// <summary>
    /// Fired whenever a currency amount changes.
    /// The first argument is the affected currency type and the second argument is the new amount.
    /// </summary>
    public event Action<CurrencyType, int> OnCurrencyChanged;

    /// <summary>
    /// Initializes balances from the configured default values.
    /// </summary>
    private void Awake()
    {
        Balances.Clear();

        foreach (CurrencyEntry entry in DefaultCurrencies)
        {
            if (entry == null)
            {
                continue;
            }

            Balances[entry.GetTypeValue()] = Mathf.Max(0, entry.GetAmount());
        }
    }

    /// <summary>
    /// Gets the current balance for the provided currency type.
    /// </summary>
    public int GetBalance(CurrencyType currencyType)
    {
        if (Balances.TryGetValue(currencyType, out int amount))
        {
            return amount;
        }

        return 0;
    }

    /// <summary>
    /// Adds currency to the wallet.
    /// </summary>
    public void AddCurrency(CurrencyType currencyType, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        int newAmount = GetBalance(currencyType) + amount;
        Balances[currencyType] = newAmount;

        NotifyCurrencyChanged(currencyType, newAmount);
        Log("Added " + amount + " " + currencyType + ". New balance: " + newAmount);
    }

    /// <summary>
    /// Checks whether the wallet contains enough of the provided currency.
    /// </summary>
    public bool HasEnough(CurrencyType currencyType, int amount)
    {
        if (amount <= 0)
        {
            return true;
        }

        return GetBalance(currencyType) >= amount;
    }

    /// <summary>
    /// Attempts to spend currency from the wallet.
    /// </summary>
    public bool TrySpendCurrency(CurrencyType currencyType, int amount)
    {
        if (amount <= 0)
        {
            return true;
        }

        int currentBalance = GetBalance(currencyType);

        if (currentBalance < amount)
        {
            Log("Failed to spend " + amount + " " + currencyType + ". Current balance: " + currentBalance);
            return false;
        }

        int newAmount = currentBalance - amount;
        Balances[currencyType] = newAmount;

        NotifyCurrencyChanged(currencyType, newAmount);
        Log("Spent " + amount + " " + currencyType + ". New balance: " + newAmount);
        return true;
    }

    /// <summary>
    /// Sets the exact balance for a currency type.
    /// Useful for loading save data or debugging.
    /// </summary>
    public void SetBalance(CurrencyType currencyType, int amount)
    {
        int clampedAmount = Mathf.Max(0, amount);
        Balances[currencyType] = clampedAmount;

        NotifyCurrencyChanged(currencyType, clampedAmount);
        Log("Set " + currencyType + " balance to " + clampedAmount);
    }

    /// <summary>
    /// Raises the currency changed event.
    /// </summary>
    private void NotifyCurrencyChanged(CurrencyType currencyType, int newAmount)
    {
        OnCurrencyChanged?.Invoke(currencyType, newAmount);
    }

    /// <summary>
    /// Logs wallet messages if debug logging is enabled.
    /// </summary>
    private void Log(string message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[CurrencyWallet] " + message);
    }
}
