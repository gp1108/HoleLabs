using UnityEngine;

/// <summary>
/// World station that exposes one upgrade panel when the player interacts nearby.
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class UpgradeShopStation : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Upgrade panel controlled by this station.")]
    [SerializeField] private UpgradePanelUI UpgradePanelUI;

    [Tooltip("Optional prompt root enabled only while the player is inside the station range.")]
    [SerializeField] private GameObject PromptRoot;

    /// <summary>
    /// Player currently inside the station range.
    /// </summary>
    private UpgradeShopInteractor CurrentInteractor;

    /// <summary>
    /// Gets the panel owned by this station.
    /// </summary>
    public UpgradePanelUI GetUpgradePanelUI()
    {
        return UpgradePanelUI;
    }

    /// <summary>
    /// Returns whether the provided interactor is currently the registered nearby player.
    /// </summary>
    public bool IsInteractorRegistered(UpgradeShopInteractor Interactor)
    {
        return CurrentInteractor == Interactor;
    }

    /// <summary>
    /// Registers the player interactor entering the station range.
    /// </summary>
    private void OnTriggerEnter(Collider Other)
    {
        UpgradeShopInteractor Interactor = Other.GetComponentInParent<UpgradeShopInteractor>();

        if (Interactor == null)
        {
            return;
        }

        CurrentInteractor = Interactor;
        CurrentInteractor.SetNearbyStation(this);

        if (PromptRoot != null)
        {
            PromptRoot.SetActive(true);
        }
    }

    /// <summary>
    /// Unregisters the player interactor leaving the station range.
    /// </summary>
    private void OnTriggerExit(Collider Other)
    {
        UpgradeShopInteractor Interactor = Other.GetComponentInParent<UpgradeShopInteractor>();

        if (Interactor == null || CurrentInteractor != Interactor)
        {
            return;
        }

        CurrentInteractor.ClearNearbyStation(this);

        if (PromptRoot != null)
        {
            PromptRoot.SetActive(false);
        }

        CurrentInteractor = null;
    }
}