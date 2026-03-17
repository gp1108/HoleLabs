using UnityEngine;

/// <summary>
/// Small example component that shows how a gameplay system can read upgraded values
/// without knowing anything about upgrade purchasing or currencies.
/// This file is only a reference example and can be removed if not needed.
/// </summary>
public sealed class UpgradeableStatReaderExample : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Upgrade manager used to resolve the final gameplay value.")]
    [SerializeField] private UpgradeManager UpgradeManager;

    [Header("Base Stat")]
    [Tooltip("Base elevator down speed before upgrades are applied.")]
    [SerializeField] private float BaseElevatorDownSpeed = 2f;

    /// <summary>
    /// Returns the final elevator down speed after applying upgrades.
    /// </summary>
    public float GetElevatorDownSpeed()
    {
        if (UpgradeManager == null)
        {
            return BaseElevatorDownSpeed;
        }

        return UpgradeManager.GetModifiedFloatStat(UpgradeStatType.ElevatorDownSpeed, BaseElevatorDownSpeed);
    }
}


/// <summary>
/// Simple debug helper to grant test currencies on start.
/// </summary>
public sealed class CurrencyDebugGrant : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Wallet that will receive the debug currencies.")]
    [SerializeField] private CurrencyWallet CurrencyWallet;

    [Header("Debug Amounts")]
    [Tooltip("Gold granted on Start.")]
    [SerializeField] private int GoldAmount = 500;

    [Tooltip("Research granted on Start.")]
    [SerializeField] private int ResearchAmount = 1000;

    /// <summary>
    /// Grants test currencies when the scene starts.
    /// </summary>
    private void Start()
    {
        if (CurrencyWallet == null)
        {
            return;
        }

        CurrencyWallet.AddCurrency(CurrencyWallet.CurrencyType.Gold, GoldAmount);
        CurrencyWallet.AddCurrency(CurrencyWallet.CurrencyType.Research, ResearchAmount);
    }
}


/// <summary>
/// Example component that purchases one specific upgrade for testing.
/// </summary>
public sealed class UpgradePurchaseExample : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Upgrade manager used to purchase upgrades.")]
    [SerializeField] private UpgradeManager UpgradeManager;

    [Tooltip("Upgrade definition that will be purchased.")]
    [SerializeField] private UpgradeDefinition UpgradeDefinition;

    /// <summary>
    /// Attempts to purchase the configured upgrade.
    /// </summary>

    //public void PurchaseUpgrade()
    //{
    //    if (UpgradeManager == null || UpgradeDefinition == null)
    //    {
    //        return;
    //    }

    //    bool wasPurchased = UpgradeManager.TryPurchaseUpgrade(UpgradeDefinition);

    //    Debug.Log("Purchase result: " + wasPurchased);
    //}

    //Opcion con comprobacion previa
    [ContextMenu("Purchase Upgrade")]
    public void PurchaseUpgrade()
    {
        if (UpgradeManager == null || UpgradeDefinition == null)
        {
            return;
        }

        if (!UpgradeManager.CanPurchaseUpgrade(UpgradeDefinition))
        {
            Debug.Log("Cannot purchase upgrade.");
            return;
        }

        bool wasPurchased = UpgradeManager.TryPurchaseUpgrade(UpgradeDefinition);

        if (wasPurchased)
        {
            Debug.Log("Upgrade purchased successfully.");
        }
    }
}

/// <summary>
/// Example elevator component that resolves its final down speed through the upgrade system.
/// </summary>
public sealed class ElevatorUpgradeReader : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Upgrade manager used to resolve the final elevator speed.")]
    [SerializeField] private UpgradeManager UpgradeManager;

    [Header("Base Values")]
    [Tooltip("Base elevator down speed before upgrades are applied.")]
    [SerializeField] private float BaseDownSpeed = 2f;

    /// <summary>
    /// Gets the current final elevator down speed.
    /// </summary>
    public float GetCurrentDownSpeed()
    {
        if (UpgradeManager == null)
        {
            return BaseDownSpeed;
        }

        return UpgradeManager.GetModifiedFloatStat(
            UpgradeStatType.ElevatorDownSpeed,
            BaseDownSpeed
        );
    }

    //Cuando el ascensor se quira mover float downSpeed = GetCurrentDownSpeed();
}

/// <summary>
/// Example mining node component that resolves required hits from upgrades.
/// </summary>
public sealed class MiningNodeUpgradeReader : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Upgrade manager used to resolve mining values.")]
    [SerializeField] private UpgradeManager UpgradeManager;

    [Header("Base Values")]
    [Tooltip("Base amount of hits required to mine this node.")]
    [SerializeField] private int BaseHitsRequired = 5;

    /// <summary>
    /// Gets the final amount of hits required to mine this node.
    /// </summary>
    public int GetHitsRequired()
    {
        if (UpgradeManager == null)
        {
            return BaseHitsRequired;
        }

        return UpgradeManager.GetModifiedIntStat(
            UpgradeStatType.MiningHitsRequired,
            BaseHitsRequired
        );
    }
}

/// <summary>
/// Example ore yield reader using the upgrade system.
/// </summary>
public sealed class OreYieldUpgradeReader : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Upgrade manager used to resolve ore yield.")]
    [SerializeField] private UpgradeManager UpgradeManager;

    [Header("Base Values")]
    [Tooltip("Base amount of ore pieces produced.")]
    [SerializeField] private int BaseOreYield = 1;

    /// <summary>
    /// Gets the final ore yield amount.
    /// </summary>
    public int GetOreYield()
    {
        if (UpgradeManager == null)
        {
            return BaseOreYield;
        }

        return UpgradeManager.GetModifiedIntStat(
            UpgradeStatType.OreYieldAmount,
            BaseOreYield
        );
    }
}

/// <summary>
/// Example component that enables a visual effect only if the related upgrade reward is unlocked.
/// </summary>
public sealed class MiningVFXUnlockReader : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Upgrade manager used to resolve visual unlocks.")]
    [SerializeField] private UpgradeManager UpgradeManager;

    [Tooltip("Visual GameObject enabled when the unlock is active.")]
    [SerializeField] private GameObject AdvancedMiningVFX;


    //Cualquier sistema puede preguntar
    //bool isUnlocked = UpgradeManager.IsVisualEffectUnlocked("AdvancedMiningSpark");

    /// <summary>
    /// Refreshes the active state of the visual effect.
    /// </summary>
    public void RefreshVisualState()
    {
        if (AdvancedMiningVFX == null)
        {
            return;
        }

        bool isUnlocked = UpgradeManager != null &&
                          UpgradeManager.IsVisualEffectUnlocked("AdvancedMiningSpark");

        AdvancedMiningVFX.SetActive(isUnlocked);
    }
}