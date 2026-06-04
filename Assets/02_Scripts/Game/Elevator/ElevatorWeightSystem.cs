using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Authoritative elevator weight evaluator.
/// The system tracks live overlapping colliders and resolves unique weight sources every physics step.
/// This prevents stale world items from accumulating weight when they are picked into the hotbar,
/// destroyed, pooled or hidden by scene persistence without a reliable trigger exit event.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class ElevatorWeightSystem : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Upgrade manager used to resolve final upgraded elevator values.")]
    [SerializeField] private UpgradeManager UpgradeManager;

    [Header("Limits")]
    [Tooltip("Base maximum total weight allowed before the elevator becomes overweighted.")]
    [SerializeField] private float BaseMaxAllowedWeight = 200f;

    [Header("Runtime")]
    [Tooltip("Runtime resolved maximum allowed weight after upgrades.")]
    [SerializeField] private float RuntimeMaxAllowedWeight;

    [Tooltip("Current authoritative evaluated weight.")]
    [SerializeField] private float CurrentWeight;

    [Tooltip("Whether the elevator is currently overweighted.")]
    [SerializeField] private bool IsOverweighted;

    [Header("UI")]
    [Tooltip("Optional text used to display current and maximum weight.")]
    [SerializeField] private TMP_Text WeightTMP;

    [Header("Debug")]
    [Tooltip("Logs evaluated weight composition.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Live colliders currently believed to overlap the elevator trigger.
    /// This is intentionally collider-based instead of provider-based so disabled, destroyed or replaced items
    /// can be cleaned safely before resolving unique weight contributors.
    /// </summary>
    private readonly HashSet<Collider> OverlappingColliders = new HashSet<Collider>();

    /// <summary>
    /// Unique carryables resolved from the current live overlapping collider set.
    /// </summary>
    private readonly HashSet<PhysicsCarryable> CurrentCarryablesInside = new HashSet<PhysicsCarryable>();

    /// <summary>
    /// Unique weight actors resolved from the current live overlapping collider set.
    /// </summary>
    private readonly HashSet<ElevatorWeightActor> CurrentActorsInside = new HashSet<ElevatorWeightActor>();

    /// <summary>
    /// Unique direct weight providers resolved from live colliders that do not belong to a PhysicsCarryable.
    /// </summary>
    private readonly HashSet<IWeightProvider> CurrentDirectWeightProvidersInside = new HashSet<IWeightProvider>();

    /// <summary>
    /// Cached trigger collider.
    /// </summary>
    private Collider TriggerCollider;

    /// <summary>
    /// Returns whether at least one weight actor is currently inside the elevator trigger.
    /// </summary>
    public bool HasAnyWeightActorInside()
    {
        CleanupInvalidOverlappingColliders();
        RefreshResolvedOverlapSets();
        return CurrentActorsInside.Count > 0;
    }

    /// <summary>
    /// Gets the current elevator weight.
    /// </summary>
    public float GetCurrentWeight()
    {
        return CurrentWeight;
    }

    /// <summary>
    /// Gets the current runtime maximum allowed weight after upgrades.
    /// </summary>
    public float GetCurrentMaxAllowedWeight()
    {
        return RuntimeMaxAllowedWeight;
    }

    /// <summary>
    /// Returns whether the elevator is overweighted.
    /// </summary>
    public bool IsElevatorOverweighted()
    {
        return IsOverweighted;
    }

    /// <summary>
    /// Validates trigger setup and resolves optional dependencies.
    /// </summary>
    private void Awake()
    {
        TriggerCollider = GetComponent<Collider>();

        if (TriggerCollider != null)
        {
            TriggerCollider.isTrigger = true;
        }

        if (UpgradeManager == null)
        {
            UpgradeManager = FindFirstObjectByType<UpgradeManager>();
        }
    }

    /// <summary>
    /// Initializes the UI with the first evaluated weight state.
    /// </summary>
    private void Start()
    {
        RefreshWeight();
    }

    /// <summary>
    /// Clears transient overlap state when this evaluator is disabled.
    /// </summary>
    private void OnDisable()
    {
        OverlappingColliders.Clear();
        CurrentCarryablesInside.Clear();
        CurrentActorsInside.Clear();
        CurrentDirectWeightProvidersInside.Clear();
    }

    /// <summary>
    /// Recomputes elevator weight in the physics loop.
    /// </summary>
    private void FixedUpdate()
    {
        RefreshWeight();
    }

    /// <summary>
    /// Registers a collider that entered the elevator trigger.
    /// </summary>
    /// <param name="Other">Collider that entered the elevator trigger.</param>
    private void OnTriggerEnter(Collider Other)
    {
        RegisterCollider(Other);
    }

    /// <summary>
    /// Keeps overlap registration stable on moving platform edge cases.
    /// </summary>
    /// <param name="Other">Collider that stayed inside the elevator trigger.</param>
    private void OnTriggerStay(Collider Other)
    {
        RegisterCollider(Other);
    }

    /// <summary>
    /// Removes a collider that exited the elevator trigger.
    /// </summary>
    /// <param name="Other">Collider that exited the elevator trigger.</param>
    private void OnTriggerExit(Collider Other)
    {
        if (Other == null)
        {
            return;
        }

        OverlappingColliders.Remove(Other);
    }

    /// <summary>
    /// Registers a currently overlapping collider if it is still a valid live collider.
    /// </summary>
    /// <param name="Other">Collider currently overlapping the elevator trigger.</param>
    private void RegisterCollider(Collider Other)
    {
        if (!IsColliderValidForWeightEvaluation(Other))
        {
            return;
        }

        OverlappingColliders.Add(Other);
    }

    /// <summary>
    /// Recomputes the full authoritative weight from live unique weight contributors.
    /// </summary>
    private void RefreshWeight()
    {
        CleanupInvalidOverlappingColliders();
        RefreshResolvedOverlapSets();

        RuntimeMaxAllowedWeight = ResolveMaxAllowedWeight();

        float FreeCarryableWeight = EvaluateFreeCarryablesInsideWeight();
        float DirectWeightedObjectWeight = EvaluateDirectWeightedObjectsInsideWeight();
        float ActorWeight = EvaluateActorsInsideWeight();
        float TransferredCarryableWeight = CurrentActorsInside.Count > 0 ? EvaluateTransferredCarryableWeight() : 0f;

        CurrentWeight = Mathf.Max(0f, FreeCarryableWeight + DirectWeightedObjectWeight + ActorWeight + TransferredCarryableWeight);
        IsOverweighted = CurrentWeight > RuntimeMaxAllowedWeight;

        if (DebugLogs)
        {
            Debug.Log(
                "[ElevatorWeightSystem] CurrentWeight=" + CurrentWeight.ToString("F2") +
                " | FreeCarryables=" + FreeCarryableWeight.ToString("F2") +
                " | DirectWeightedObjects=" + DirectWeightedObjectWeight.ToString("F2") +
                " | ActorWeight=" + ActorWeight.ToString("F2") +
                " | TransferredCarryables=" + TransferredCarryableWeight.ToString("F2") +
                " | LiveColliders=" + OverlappingColliders.Count +
                " | MaxAllowed=" + RuntimeMaxAllowedWeight.ToString("F2"),
                this);
        }

        ShowWeightOnUI();
    }

    /// <summary>
    /// Resolves unique current actors, carryables and direct providers from the live collider set.
    /// </summary>
    private void RefreshResolvedOverlapSets()
    {
        CurrentCarryablesInside.Clear();
        CurrentActorsInside.Clear();
        CurrentDirectWeightProvidersInside.Clear();

        foreach (Collider Collider in OverlappingColliders)
        {
            if (!IsColliderValidForWeightEvaluation(Collider))
            {
                continue;
            }

            PhysicsCarryable Carryable = ResolveCarryable(Collider);
            if (Carryable != null)
            {
                CurrentCarryablesInside.Add(Carryable);
            }

            ElevatorWeightActor Actor = ResolveWeightActor(Collider);
            if (Actor != null)
            {
                CurrentActorsInside.Add(Actor);
            }

            IWeightProvider DirectWeightProvider = ResolveDirectWeightProvider(Collider, Carryable);
            if (IsWeightProviderValid(DirectWeightProvider))
            {
                CurrentDirectWeightProvidersInside.Add(DirectWeightProvider);
            }
        }
    }

    /// <summary>
    /// Sums all carryables physically inside the elevator that are not currently held or magnetized.
    /// </summary>
    /// <returns>Total free carryable weight inside the elevator.</returns>
    private float EvaluateFreeCarryablesInsideWeight()
    {
        float TotalWeight = 0f;

        foreach (PhysicsCarryable Carryable in CurrentCarryablesInside)
        {
            if (Carryable == null)
            {
                continue;
            }

            if (Carryable.GetIsHeld() || Carryable.GetIsMagnetized())
            {
                continue;
            }

            TotalWeight += GetCarryableWeight(Carryable);
        }

        return TotalWeight;
    }

    /// <summary>
    /// Sums every direct weighted object inside the elevator that is not represented by a PhysicsCarryable.
    /// </summary>
    /// <returns>Total direct weighted object weight inside the elevator.</returns>
    private float EvaluateDirectWeightedObjectsInsideWeight()
    {
        float TotalWeight = 0f;

        foreach (IWeightProvider WeightProvider in CurrentDirectWeightProvidersInside)
        {
            if (!IsWeightProviderValid(WeightProvider))
            {
                continue;
            }

            TotalWeight += Mathf.Max(0f, WeightProvider.GetWeight());
        }

        return TotalWeight;
    }

    /// <summary>
    /// Sums every actor currently inside the elevator.
    /// Actor weight includes body weight and optional hotbar item weight.
    /// </summary>
    /// <returns>Total actor weight inside the elevator.</returns>
    private float EvaluateActorsInsideWeight()
    {
        float TotalWeight = 0f;

        foreach (ElevatorWeightActor Actor in CurrentActorsInside)
        {
            if (Actor == null)
            {
                continue;
            }

            TotalWeight += Actor.GetBaseWeight();
        }

        return TotalWeight;
    }

    /// <summary>
    /// Sums every carryable currently controlled by the player through hold or magnet.
    /// Because only one player can own these states, explicit ownership tracking is not required.
    /// </summary>
    /// <returns>Total transferred carryable weight.</returns>
    private float EvaluateTransferredCarryableWeight()
    {
        float TotalWeight = 0f;
        PhysicsCarryable[] AllCarryables = FindObjectsByType<PhysicsCarryable>(FindObjectsSortMode.None);

        for (int Index = 0; Index < AllCarryables.Length; Index++)
        {
            PhysicsCarryable Carryable = AllCarryables[Index];
            if (Carryable == null)
            {
                continue;
            }

            if (!Carryable.GetIsHeld() && !Carryable.GetIsMagnetized())
            {
                continue;
            }

            TotalWeight += GetCarryableWeight(Carryable);
        }

        return TotalWeight;
    }

    /// <summary>
    /// Resolves the gameplay weight of a carryable from its highest-priority weight provider.
    /// Priority order is explicit PhysicsWeight, ore pickup data, world item data, then any custom provider.
    /// </summary>
    /// <param name="Carryable">Carryable being evaluated.</param>
    /// <returns>Resolved non-negative carryable weight.</returns>
    private float GetCarryableWeight(PhysicsCarryable Carryable)
    {
        if (Carryable == null)
        {
            return 0f;
        }

        IWeightProvider WeightProvider = ResolveWeightProvider(Carryable);

        if (WeightProvider == null)
        {
            return 0f;
        }

        return Mathf.Max(0f, WeightProvider.GetWeight());
    }

    /// <summary>
    /// Resolves the authoritative weight provider for a carryable hierarchy.
    /// </summary>
    /// <param name="Carryable">Carryable being evaluated.</param>
    /// <returns>Resolved weight provider, or null when the carryable has no weight.</returns>
    private IWeightProvider ResolveWeightProvider(PhysicsCarryable Carryable)
    {
        if (Carryable == null)
        {
            return null;
        }

        PhysicsWeight ExplicitWeight = Carryable.GetComponent<PhysicsWeight>();
        if (ExplicitWeight == null)
        {
            ExplicitWeight = Carryable.GetComponentInChildren<PhysicsWeight>(true);
        }

        if (ExplicitWeight != null)
        {
            return ExplicitWeight;
        }

        OrePickup OrePickup = Carryable.GetComponent<OrePickup>();
        if (OrePickup == null)
        {
            OrePickup = Carryable.GetComponentInChildren<OrePickup>(true);
        }

        if (OrePickup != null)
        {
            return OrePickup;
        }

        WorldItem WorldItem = Carryable.GetComponent<WorldItem>();
        if (WorldItem == null)
        {
            WorldItem = Carryable.GetComponentInChildren<WorldItem>(true);
        }

        if (WorldItem != null)
        {
            return WorldItem;
        }

        MonoBehaviour[] Behaviours = Carryable.GetComponentsInChildren<MonoBehaviour>(true);

        for (int Index = 0; Index < Behaviours.Length; Index++)
        {
            if (Behaviours[Index] is IWeightProvider Provider)
            {
                return Provider;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves a direct weighted object from an overlapping collider.
    /// Direct weighted objects are only used when the collider does not belong to a PhysicsCarryable,
    /// preventing double counting for ore pickups and fully carryable props.
    /// </summary>
    /// <param name="Other">Collider overlapping the elevator trigger.</param>
    /// <param name="ResolvedCarryable">Carryable already resolved for the same collider.</param>
    /// <returns>Resolved direct weight provider, or null when none is found.</returns>
    private IWeightProvider ResolveDirectWeightProvider(Collider Other, PhysicsCarryable ResolvedCarryable)
    {
        if (Other == null || ResolvedCarryable != null)
        {
            return null;
        }

        PhysicsWeight ExplicitWeight = Other.GetComponentInParent<PhysicsWeight>();
        if (ExplicitWeight != null)
        {
            return ExplicitWeight;
        }

        OrePickup OrePickup = Other.GetComponentInParent<OrePickup>();
        if (OrePickup != null)
        {
            return OrePickup;
        }

        WorldItem WorldItem = Other.GetComponentInParent<WorldItem>();
        if (WorldItem != null)
        {
            return WorldItem;
        }

        MonoBehaviour[] ParentBehaviours = Other.GetComponentsInParent<MonoBehaviour>(true);

        for (int Index = 0; Index < ParentBehaviours.Length; Index++)
        {
            if (ParentBehaviours[Index] is IWeightProvider Provider)
            {
                return Provider;
            }
        }

        MonoBehaviour[] ChildBehaviours = Other.GetComponentsInChildren<MonoBehaviour>(true);

        for (int Index = 0; Index < ChildBehaviours.Length; Index++)
        {
            if (ChildBehaviours[Index] is IWeightProvider Provider)
            {
                return Provider;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the final maximum allowed weight after upgrades.
    /// </summary>
    /// <returns>Final maximum allowed weight.</returns>
    private float ResolveMaxAllowedWeight()
    {
        float BaseValue = Mathf.Max(0f, BaseMaxAllowedWeight);

        if (UpgradeManager == null)
        {
            return BaseValue;
        }

        return Mathf.Max(
            0f,
            UpgradeManager.GetModifiedFloatStat(UpgradeStatType.ElevatorMaxAllowedWeight, BaseValue)
        );
    }

    /// <summary>
    /// Removes invalid, disabled or inactive colliders from the live overlap set.
    /// This is required because picked world items can be destroyed or hidden without receiving OnTriggerExit.
    /// </summary>
    private void CleanupInvalidOverlappingColliders()
    {
        List<Collider> CollidersToRemove = null;

        foreach (Collider Collider in OverlappingColliders)
        {
            if (IsColliderValidForWeightEvaluation(Collider))
            {
                continue;
            }

            CollidersToRemove ??= new List<Collider>();
            CollidersToRemove.Add(Collider);
        }

        if (CollidersToRemove == null)
        {
            return;
        }

        for (int Index = 0; Index < CollidersToRemove.Count; Index++)
        {
            OverlappingColliders.Remove(CollidersToRemove[Index]);
        }
    }

    /// <summary>
    /// Returns whether a collider can still contribute to weight evaluation.
    /// </summary>
    /// <param name="Collider">Collider to validate.</param>
    /// <returns>True when the collider is alive, enabled and active in hierarchy.</returns>
    private bool IsColliderValidForWeightEvaluation(Collider Collider)
    {
        if (Collider == null)
        {
            return false;
        }

        if (!Collider.enabled)
        {
            return false;
        }

        if (!Collider.gameObject.activeInHierarchy)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns whether the weight provider reference still points to a valid active runtime object.
    /// </summary>
    /// <param name="WeightProvider">Weight provider to validate.</param>
    /// <returns>True when the provider is valid.</returns>
    private bool IsWeightProviderValid(IWeightProvider WeightProvider)
    {
        if (WeightProvider == null)
        {
            return false;
        }

        if (WeightProvider is Component Component)
        {
            return Component != null && Component.gameObject.activeInHierarchy;
        }

        if (WeightProvider is Object UnityObject)
        {
            return UnityObject != null;
        }

        return true;
    }

    /// <summary>
    /// Updates the optional UI text.
    /// </summary>
    public void ShowWeightOnUI()
    {
        if (WeightTMP == null)
        {
            return;
        }

        WeightTMP.text = CurrentWeight.ToString("F0") + " / " + RuntimeMaxAllowedWeight.ToString("F0") + " KG";
    }

    /// <summary>
    /// Resolves the root PhysicsCarryable from an overlapping collider.
    /// </summary>
    /// <param name="Other">Collider overlapping the elevator trigger.</param>
    /// <returns>Resolved carryable, or null when none exists.</returns>
    private PhysicsCarryable ResolveCarryable(Collider Other)
    {
        if (Other == null)
        {
            return null;
        }

        return Other.GetComponentInParent<PhysicsCarryable>();
    }

    /// <summary>
    /// Resolves the player weight actor from an overlapping collider.
    /// </summary>
    /// <param name="Other">Collider overlapping the elevator trigger.</param>
    /// <returns>Resolved weight actor, or null when none exists.</returns>
    private ElevatorWeightActor ResolveWeightActor(Collider Other)
    {
        if (Other == null)
        {
            return null;
        }

        return Other.GetComponentInParent<ElevatorWeightActor>();
    }
}
