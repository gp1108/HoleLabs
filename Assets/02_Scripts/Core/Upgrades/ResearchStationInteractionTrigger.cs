using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Player interaction trigger for a ResearchStation.
/// This collider is intentionally separated from ore input zones so UI range and ore assimilation volume can be authored independently.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class ResearchStationInteractionTrigger : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Research station opened by this interaction trigger. If empty, the nearest parent ResearchStation is used.")]
    [SerializeField] private ResearchStation OwnerStation;

    [Header("Debug")]
    [Tooltip("Logs interaction trigger registration events.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Interactor currently registered through this trigger.
    /// </summary>
    private UpgradeShopInteractor CurrentInteractor;

    /// <summary>
    /// Player colliders currently inside this trigger. This avoids clearing interaction while another player collider is still inside.
    /// </summary>
    private readonly HashSet<Collider> RegisteredInteractorColliders = new();

    /// <summary>
    /// Ensures the interaction collider behaves as a trigger.
    /// </summary>
    private void Reset()
    {
        Collider TriggerCollider = GetComponent<Collider>();

        if (TriggerCollider != null)
        {
            TriggerCollider.isTrigger = true;
        }

        OwnerStation = GetComponentInParent<ResearchStation>();
    }

    /// <summary>
    /// Resolves the owner station.
    /// </summary>
    private void Awake()
    {
        if (OwnerStation == null)
        {
            OwnerStation = GetComponentInParent<ResearchStation>();
        }
    }

    /// <summary>
    /// Clears the interactor if this trigger is disabled while the player is inside it.
    /// </summary>
    private void OnDisable()
    {
        if (OwnerStation != null && CurrentInteractor != null)
        {
            OwnerStation.ClearInteractor(CurrentInteractor);
        }

        RegisteredInteractorColliders.Clear();
        CurrentInteractor = null;
    }

    /// <summary>
    /// Registers the player interactor when it enters the interaction trigger.
    /// </summary>
    private void OnTriggerEnter(Collider Other)
    {
        if (OwnerStation == null || Other == null)
        {
            return;
        }

        UpgradeShopInteractor Interactor = Other.GetComponentInParent<UpgradeShopInteractor>();

        if (Interactor == null)
        {
            return;
        }

        if (!RegisteredInteractorColliders.Add(Other))
        {
            return;
        }

        if (CurrentInteractor == null)
        {
            CurrentInteractor = Interactor;
            OwnerStation.RegisterInteractor(Interactor);
            Log("Interactor entered: " + Interactor.name);
        }
    }

    /// <summary>
    /// Clears the player interactor when it leaves the interaction trigger.
    /// </summary>
    private void OnTriggerExit(Collider Other)
    {
        if (OwnerStation == null || Other == null || CurrentInteractor == null)
        {
            return;
        }

        UpgradeShopInteractor Interactor = Other.GetComponentInParent<UpgradeShopInteractor>();

        if (Interactor == null || Interactor != CurrentInteractor)
        {
            return;
        }

        RegisteredInteractorColliders.Remove(Other);

        if (HasAnyColliderForInteractor(Interactor))
        {
            return;
        }

        OwnerStation.ClearInteractor(Interactor);
        CurrentInteractor = null;
        Log("Interactor exited: " + Interactor.name);
    }

    /// <summary>
    /// Returns whether any registered collider still belongs to the provided interactor.
    /// </summary>
    private bool HasAnyColliderForInteractor(UpgradeShopInteractor Interactor)
    {
        RegisteredInteractorColliders.RemoveWhere(ColliderValue =>
            ColliderValue == null ||
            !ColliderValue.enabled ||
            !ColliderValue.gameObject.activeInHierarchy);

        foreach (Collider RegisteredCollider in RegisteredInteractorColliders)
        {
            if (RegisteredCollider == null)
            {
                continue;
            }

            if (RegisteredCollider.GetComponentInParent<UpgradeShopInteractor>() == Interactor)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Writes an interaction-trigger-specific debug message.
    /// </summary>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[ResearchStationInteractionTrigger] " + Message, this);
    }
}
