using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Authoritative elevator weight evaluator.
/// Free carryables count while physically inside the trigger.
/// Held or magnetized carryables count as player-transferred weight
/// while the player remains inside the elevator.
/// Upgrade integration is resolved through UpgradeManager without coupling
/// purchasing logic to this system.
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
    /// Carryables currently overlapping the elevator trigger.
    /// </summary>
    private readonly HashSet<PhysicsCarryable> OverlappingCarryables = new HashSet<PhysicsCarryable>();

    /// <summary>
    /// Player weight actors currently overlapping the elevator trigger.
    /// </summary>
    private readonly HashSet<ElevatorWeightActor> OverlappingActors = new HashSet<ElevatorWeightActor>();

    /// <summary>
    /// Cached trigger collider.
    /// </summary>
    private Collider TriggerCollider;

    /// <summary>
    /// Returns whether at least one weight actor is currently inside the elevator trigger.
    /// </summary>
    public bool HasAnyWeightActorInside()
    {
        CleanupNullReferences();
        return OverlappingActors.Count > 0;
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
    /// Validates trigger setup.
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
    /// Initializes the UI.
    /// </summary>
    private void Start()
    {
        RefreshWeight();
    }

    /// <summary>
    /// Recomputes elevator weight in the physics loop.
    /// </summary>
    private void FixedUpdate()
    {
        RefreshWeight();
    }

    /// <summary>
    /// Registers overlapping actors and carryables.
    /// </summary>
    private void OnTriggerEnter(Collider Other)
    {
        RegisterOverlap(Other);
    }

    /// <summary>
    /// Keeps overlap registration stable on moving platform edge cases.
    /// </summary>
    private void OnTriggerStay(Collider Other)
    {
        RegisterOverlap(Other);
    }

    /// <summary>
    /// Removes overlapping actors and carryables when they leave the trigger.
    /// </summary>
    private void OnTriggerExit(Collider Other)
    {
        PhysicsCarryable Carryable = ResolveCarryable(Other);
        if (Carryable != null)
        {
            OverlappingCarryables.Remove(Carryable);
        }

        ElevatorWeightActor Actor = ResolveWeightActor(Other);
        if (Actor != null)
        {
            OverlappingActors.Remove(Actor);
        }
    }

    /// <summary>
    /// Registers a carryable or actor found in the provided collider hierarchy.
    /// </summary>
    private void RegisterOverlap(Collider Other)
    {
        PhysicsCarryable Carryable = ResolveCarryable(Other);
        if (Carryable != null)
        {
            OverlappingCarryables.Add(Carryable);
        }

        ElevatorWeightActor Actor = ResolveWeightActor(Other);
        if (Actor != null)
        {
            OverlappingActors.Add(Actor);
        }
    }

    /// <summary>
    /// Recomputes the full authoritative weight.
    /// </summary>
    private void RefreshWeight()
    {
        CleanupNullReferences();

        RuntimeMaxAllowedWeight = ResolveMaxAllowedWeight();

        float FreeCarryableWeight = EvaluateFreeCarryablesInsideWeight();
        float ActorWeight = EvaluateActorsInsideWeight();

        CurrentWeight = Mathf.Max(0f, FreeCarryableWeight + ActorWeight);
        IsOverweighted = CurrentWeight > RuntimeMaxAllowedWeight;

        if (DebugLogs)
        {
            Debug.Log(
                "[ElevatorWeightSystem] CurrentWeight=" + CurrentWeight.ToString("F2") +
                " | FreeCarryables=" + FreeCarryableWeight.ToString("F2") +
                " | ActorWeight=" + ActorWeight.ToString("F2") +
                " | MaxAllowed=" + RuntimeMaxAllowedWeight.ToString("F2"),
                this);
        }

        ShowWeightOnUI();
    }

    /// <summary>
    /// Sums all carryables physically inside the elevator that are not currently held or magnetized.
    /// </summary>
    private float EvaluateFreeCarryablesInsideWeight()
    {
        float TotalWeight = 0f;

        foreach (PhysicsCarryable Carryable in OverlappingCarryables)
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
    /// Sums base player weight plus every held or magnetized carryable while the player is inside the elevator.
    /// </summary>
    private float EvaluateActorsInsideWeight()
    {
        if (OverlappingActors.Count == 0)
        {
            return 0f;
        }

        float TotalWeight = 0f;

        foreach (ElevatorWeightActor Actor in OverlappingActors)
        {
            if (Actor == null)
            {
                continue;
            }

            TotalWeight += Actor.GetBaseWeight();
            TotalWeight += EvaluateTransferredCarryableWeight();
        }

        return TotalWeight;
    }

    /// <summary>
    /// Sums every carryable currently controlled by the player through hold or magnet.
    /// Because only one player can own these states, explicit ownership tracking is not required.
    /// </summary>
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
    /// Resolves the gameplay weight of a carryable from its ore item data.
    /// </summary>
    private float GetCarryableWeight(PhysicsCarryable Carryable)
    {
        if (Carryable == null)
        {
            return 0f;
        }

        OrePickup OrePickupComponent = Carryable.GetComponent<OrePickup>();
        if (OrePickupComponent == null)
        {
            OrePickupComponent = Carryable.GetComponentInChildren<OrePickup>();
        }

        if (OrePickupComponent == null)
        {
            return 0f;
        }

        OreItemData OreData = OrePickupComponent.GetOreItemData();
        if (OreData == null)
        {
            Debug.LogWarning("Carryable has no OreItemData assigned.", Carryable);
            return 0f;
        }

        return Mathf.Max(0f, OreData.GetWeightValue());
    }

    /// <summary>
    /// Resolves the final maximum allowed weight after upgrades.
    /// </summary>
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
    /// Removes null references from tracked sets.
    /// </summary>
    private void CleanupNullReferences()
    {
        OverlappingCarryables.RemoveWhere(Item => Item == null);
        OverlappingActors.RemoveWhere(Item => Item == null);
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
    private ElevatorWeightActor ResolveWeightActor(Collider Other)
    {
        if (Other == null)
        {
            return null;
        }

        return Other.GetComponentInParent<ElevatorWeightActor>();
    }
}