using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Physical researcher machine installed in the world.
/// It opens the research UI and processes ores through separate input zones, while global research state lives in ResearchRuntimeService.
/// </summary>
[DisallowMultipleComponent]
public sealed class ResearchStation : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Panel controlled by this research station. If empty, the first scene ResearchPanelUI is used.")]
    [SerializeField] private ResearchPanelUI ResearchPanelUI;

    [Tooltip("Global research runtime authority. It persists active research and partial progress independently from this physical machine.")]
    [SerializeField] private ResearchRuntimeService ResearchRuntimeServiceReference;

    [Tooltip("Optional prompt root enabled only while the player is inside this station interaction range.")]
    [SerializeField] private GameObject PromptRoot;

    [Header("Ore Processing")]
    [Tooltip("If true, held or magnetized ore pickups inside input zones are ignored and cannot be consumed.")]
    [SerializeField] private bool IgnoreControlledCarryables = true;

    [Tooltip("Seconds between ore processing attempts while a research is active.")]
    [SerializeField] private float ProcessingInterval = 0.35f;

    [Tooltip("Maximum number of matching ores consumed per processing tick.")]
    [SerializeField] private int MaxOresProcessedPerTick = 1;

    [Header("Debug")]
    [Tooltip("Logs research station interaction and ore-zone flow.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Player interactor currently inside this station interaction trigger.
    /// </summary>
    private UpgradeShopInteractor CurrentInteractor;

    /// <summary>
    /// Colliders currently overlapping one or more registered research ore input zones.
    /// The integer value is a reference count so overlapping input zones cannot prematurely unregister a collider.
    /// </summary>
    private readonly Dictionary<Collider, int> LiveInputColliderCounts = new();

    /// <summary>
    /// Reusable list used to resolve unique ore pickups inside input zones.
    /// </summary>
    private readonly List<OrePickup> ResolvedOrePickups = new();

    /// <summary>
    /// Reusable set used to deduplicate ore pickups with multiple colliders.
    /// </summary>
    private readonly HashSet<OrePickup> ResolvedOrePickupSet = new();

    /// <summary>
    /// Reusable list used to remove invalid input colliders from the counted dictionary safely.
    /// </summary>
    private readonly List<Collider> InvalidInputColliders = new();

    /// <summary>
    /// Runtime timer used to process active research ores at a controlled pace.
    /// </summary>
    private float ProcessingTimer;

    /// <summary>
    /// Fired when ore contents or research state may have changed.
    /// </summary>
    public event Action OnResearchStationStateChanged;

    /// <summary>
    /// Resolves missing references and initializes the panel.
    /// </summary>
    private void Awake()
    {
        ResolveReferences();

        if (ResearchPanelUI != null)
        {
            ResearchPanelUI.Initialize(this);
        }
    }

    /// <summary>
    /// Subscribes to global research state changes.
    /// </summary>
    private void OnEnable()
    {
        ResolveReferences();

        if (ResearchRuntimeServiceReference != null)
        {
            ResearchRuntimeServiceReference.OnResearchStateChanged -= HandleResearchRuntimeStateChanged;
            ResearchRuntimeServiceReference.OnResearchStateChanged += HandleResearchRuntimeStateChanged;
        }
    }

    /// <summary>
    /// Unsubscribes from global research state changes.
    /// </summary>
    private void OnDisable()
    {
        if (ResearchRuntimeServiceReference != null)
        {
            ResearchRuntimeServiceReference.OnResearchStateChanged -= HandleResearchRuntimeStateChanged;
        }

        if (CurrentInteractor != null)
        {
            CurrentInteractor.ClearNearbyResearchStation(this);
            CurrentInteractor = null;
        }

        LiveInputColliderCounts.Clear();
    }

    /// <summary>
    /// Cleans invalid collider references and processes active research over time.
    /// </summary>
    private void Update()
    {
        CleanupInvalidInputColliders();
        ProcessActiveResearch(Time.deltaTime);
    }

    /// <summary>
    /// Gets the panel owned by this station.
    /// </summary>
    public ResearchPanelUI GetResearchPanelUI()
    {
        return ResearchPanelUI;
    }

    /// <summary>
    /// Gets the global research runtime service used by this station.
    /// </summary>
    public ResearchRuntimeService GetResearchRuntimeService()
    {
        ResolveReferences();
        return ResearchRuntimeServiceReference;
    }

    /// <summary>
    /// Gets the currently active research definition.
    /// </summary>
    public ResearchDefinition GetActiveResearchDefinition()
    {
        ResearchRuntimeService RuntimeService = GetResearchRuntimeService();
        return RuntimeService != null ? RuntimeService.GetActiveResearchDefinition() : null;
    }

    /// <summary>
    /// Registers a research definition in the global runtime lookup.
    /// </summary>
    public void RegisterResearchDefinition(ResearchDefinition ResearchDefinition)
    {
        ResearchRuntimeService RuntimeService = GetResearchRuntimeService();

        if (RuntimeService != null)
        {
            RuntimeService.RegisterResearchDefinition(ResearchDefinition);
        }
    }

    /// <summary>
    /// Returns whether the provided interactor is currently registered inside this station interaction range.
    /// </summary>
    public bool IsInteractorRegistered(UpgradeShopInteractor Interactor)
    {
        return CurrentInteractor == Interactor;
    }

    /// <summary>
    /// Registers the player interactor that entered this station interaction trigger.
    /// Called by ResearchStationInteractionTrigger.
    /// </summary>
    public void RegisterInteractor(UpgradeShopInteractor Interactor)
    {
        if (Interactor == null)
        {
            return;
        }

        CurrentInteractor = Interactor;
        CurrentInteractor.SetNearbyResearchStation(this);

        if (PromptRoot != null)
        {
            PromptRoot.SetActive(true);
        }

        Log("Interactor registered: " + Interactor.name);
    }

    /// <summary>
    /// Clears the player interactor that left this station interaction trigger.
    /// Called by ResearchStationInteractionTrigger.
    /// </summary>
    public void ClearInteractor(UpgradeShopInteractor Interactor)
    {
        if (Interactor == null || CurrentInteractor != Interactor)
        {
            return;
        }

        CurrentInteractor.ClearNearbyResearchStation(this);
        CurrentInteractor = null;

        if (PromptRoot != null)
        {
            PromptRoot.SetActive(false);
        }

        Log("Interactor cleared: " + Interactor.name);
    }

    /// <summary>
    /// Registers a collider currently inside one of this station's ore input zones.
    /// Called by ResearchOreInputZone.
    /// </summary>
    public void RegisterOreInputCollider(Collider Other)
    {
        if (Other == null)
        {
            return;
        }

        if (Other.GetComponent<OrePickup>() == null && Other.GetComponentInParent<OrePickup>() == null)
        {
            return;
        }

        if (LiveInputColliderCounts.TryGetValue(Other, out int CurrentCount))
        {
            LiveInputColliderCounts[Other] = CurrentCount + 1;
            return;
        }

        LiveInputColliderCounts[Other] = 1;
        NotifyStateChanged();
    }

    /// <summary>
    /// Unregisters a collider that left one of this station's ore input zones.
    /// Called by ResearchOreInputZone.
    /// </summary>
    public void UnregisterOreInputCollider(Collider Other)
    {
        if (Other == null)
        {
            return;
        }

        if (!LiveInputColliderCounts.TryGetValue(Other, out int CurrentCount))
        {
            return;
        }

        CurrentCount--;

        if (CurrentCount > 0)
        {
            LiveInputColliderCounts[Other] = CurrentCount;
            return;
        }

        LiveInputColliderCounts.Remove(Other);
        NotifyStateChanged();
    }

    /// <summary>
    /// Returns whether the provided research is currently active.
    /// </summary>
    public bool IsResearchActive(ResearchDefinition ResearchDefinition)
    {
        ResearchRuntimeService RuntimeService = GetResearchRuntimeService();
        return RuntimeService != null && RuntimeService.IsResearchActive(ResearchDefinition);
    }

    /// <summary>
    /// Gets the current UI state for one research entry.
    /// </summary>
    public ResearchRuntimeService.ResearchViewState GetResearchViewState(ResearchDefinition ResearchDefinition)
    {
        ResearchRuntimeService RuntimeService = GetResearchRuntimeService();
        return RuntimeService != null
            ? RuntimeService.GetResearchViewState(ResearchDefinition)
            : ResearchRuntimeService.ResearchViewState.Invalid;
    }

    /// <summary>
    /// Gets the current reason why a research entry cannot currently be activated.
    /// </summary>
    public ResearchRuntimeService.ResearchBlockReason GetResearchBlockReason(ResearchDefinition ResearchDefinition)
    {
        ResearchRuntimeService RuntimeService = GetResearchRuntimeService();
        return RuntimeService != null
            ? RuntimeService.GetResearchBlockReason(ResearchDefinition)
            : ResearchRuntimeService.ResearchBlockReason.MissingUpgradeManager;
    }

    /// <summary>
    /// Attempts to activate the provided research entry through the global runtime service.
    /// </summary>
    public bool TryActivateResearch(ResearchDefinition ResearchDefinition)
    {
        ResearchRuntimeService RuntimeService = GetResearchRuntimeService();
        return RuntimeService != null && RuntimeService.TryActivateResearch(ResearchDefinition);
    }

    /// <summary>
    /// Clears the active research without deleting its paid state or processed ore progress.
    /// </summary>
    public void ClearActiveResearch()
    {
        ResearchRuntimeService RuntimeService = GetResearchRuntimeService();

        if (RuntimeService != null)
        {
            RuntimeService.ClearActiveResearch();
        }
    }

    /// <summary>
    /// Gets progress for every ore requirement of the provided research entry.
    /// </summary>
    public List<ResearchRuntimeService.OreRequirementProgress> GetOreRequirementProgress(ResearchDefinition ResearchDefinition)
    {
        ResearchRuntimeService RuntimeService = GetResearchRuntimeService();
        return RuntimeService != null
            ? RuntimeService.GetOreRequirementProgress(ResearchDefinition)
            : new List<ResearchRuntimeService.OreRequirementProgress>();
    }


    /// <summary>
    /// Returns whether every valid ore requirement on the provided research has been discovered by the scanner system.
    /// </summary>
    /// <param name="ResearchDefinition">Research definition to evaluate.</param>
    public bool AreOreRequirementsDiscovered(ResearchDefinition ResearchDefinition)
    {
        ResearchRuntimeService RuntimeService = GetResearchRuntimeService();
        return RuntimeService != null && RuntimeService.AreOreRequirementsDiscovered(ResearchDefinition);
    }

    /// <summary>
    /// Returns whether one ore requirement is known by the scanner system.
    /// </summary>
    /// <param name="Requirement">Requirement to evaluate.</param>
    public bool IsOreRequirementDiscovered(ResearchDefinition.OreRequirement Requirement)
    {
        ResearchRuntimeService RuntimeService = GetResearchRuntimeService();
        return RuntimeService != null && RuntimeService.IsOreRequirementDiscovered(Requirement);
    }

    /// <summary>
    /// Counts valid ore requirements that still reference undiscovered ore types.
    /// </summary>
    /// <param name="ResearchDefinition">Research definition to evaluate.</param>
    public int CountUndiscoveredOreRequirements(ResearchDefinition ResearchDefinition)
    {
        ResearchRuntimeService RuntimeService = GetResearchRuntimeService();
        return RuntimeService != null ? RuntimeService.CountUndiscoveredOreRequirements(ResearchDefinition) : 0;
    }

    /// <summary>
    /// Processes matching physical ore pickups from all registered ore input zones.
    /// </summary>
    private void ProcessActiveResearch(float DeltaTime)
    {
        ResearchRuntimeService RuntimeService = GetResearchRuntimeService();

        if (RuntimeService == null || RuntimeService.GetActiveResearchDefinition() == null)
        {
            return;
        }

        ProcessingTimer -= DeltaTime;

        if (ProcessingTimer > 0f)
        {
            return;
        }

        ProcessingTimer = Mathf.Max(0.01f, ProcessingInterval);
        ResolveCurrentOrePickups();

        int ProcessedCount = RuntimeService.ProcessOrePickups(ResolvedOrePickups, Mathf.Max(1, MaxOresProcessedPerTick));

        if (ProcessedCount > 0)
        {
            RemoveInvalidOrConsumedInputColliders();
            NotifyStateChanged();
            Log("Processed " + ProcessedCount + " ore pickup(s). ");
        }
    }

    /// <summary>
    /// Rebuilds the unique current ore pickup list from live input-zone colliders.
    /// </summary>
    private void ResolveCurrentOrePickups()
    {
        ResolvedOrePickups.Clear();
        ResolvedOrePickupSet.Clear();
        CleanupInvalidInputColliders();

        foreach (Collider LiveCollider in LiveInputColliderCounts.Keys)
        {
            if (LiveCollider == null || !LiveCollider.enabled || !LiveCollider.gameObject.activeInHierarchy)
            {
                continue;
            }

            OrePickup Pickup = LiveCollider.GetComponent<OrePickup>() ?? LiveCollider.GetComponentInParent<OrePickup>();

            if (Pickup == null)
            {
                continue;
            }

            if (Pickup.GetOreItemData() == null || Pickup.GetOreItemData().GetOreDefinition() == null)
            {
                continue;
            }

            if (IgnoreControlledCarryables && IsOreControlled(Pickup))
            {
                continue;
            }

            if (ResolvedOrePickupSet.Add(Pickup))
            {
                ResolvedOrePickups.Add(Pickup);
            }
        }
    }

    /// <summary>
    /// Returns whether a pickup is currently held or magnetized.
    /// </summary>
    private bool IsOreControlled(OrePickup Pickup)
    {
        PhysicsCarryable Carryable = Pickup.GetComponent<PhysicsCarryable>() ?? Pickup.GetComponentInParent<PhysicsCarryable>();

        if (Carryable == null)
        {
            return false;
        }

        return Carryable.GetIsHeld() || Carryable.GetIsMagnetized();
    }

    /// <summary>
    /// Removes invalid collider references from the input set.
    /// </summary>
    private void CleanupInvalidInputColliders()
    {
        InvalidInputColliders.Clear();

        foreach (Collider ColliderValue in LiveInputColliderCounts.Keys)
        {
            if (ColliderValue == null ||
                !ColliderValue.enabled ||
                !ColliderValue.gameObject.activeInHierarchy)
            {
                InvalidInputColliders.Add(ColliderValue);
            }
        }

        for (int Index = 0; Index < InvalidInputColliders.Count; Index++)
        {
            LiveInputColliderCounts.Remove(InvalidInputColliders[Index]);
        }

        if (InvalidInputColliders.Count > 0)
        {
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// Removes colliders belonging to pickups that have been consumed or disabled.
    /// </summary>
    private void RemoveInvalidOrConsumedInputColliders()
    {
        InvalidInputColliders.Clear();

        foreach (Collider ColliderValue in LiveInputColliderCounts.Keys)
        {
            if (ColliderValue == null ||
                !ColliderValue.enabled ||
                !ColliderValue.gameObject.activeInHierarchy ||
                ColliderValue.GetComponentInParent<OrePickup>() == null)
            {
                InvalidInputColliders.Add(ColliderValue);
            }
        }

        for (int Index = 0; Index < InvalidInputColliders.Count; Index++)
        {
            LiveInputColliderCounts.Remove(InvalidInputColliders[Index]);
        }

        if (InvalidInputColliders.Count > 0)
        {
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// Resolves scene references if they were not assigned manually.
    /// </summary>
    private void ResolveReferences()
    {
        if (ResearchRuntimeServiceReference == null)
        {
            ResearchRuntimeServiceReference = FindFirstObjectByType<ResearchRuntimeService>();
        }

        if (ResearchPanelUI == null)
        {
            ResearchPanelUI = FindFirstObjectByType<ResearchPanelUI>();
        }
    }

    /// <summary>
    /// Handles global research runtime changes and refreshes station-bound UI.
    /// </summary>
    private void HandleResearchRuntimeStateChanged()
    {
        NotifyStateChanged();
    }

    /// <summary>
    /// Notifies bound UI that station contents or research state changed.
    /// </summary>
    private void NotifyStateChanged()
    {
        OnResearchStationStateChanged?.Invoke();
    }

    /// <summary>
    /// Writes a station-specific debug message.
    /// </summary>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[ResearchStation] " + Message, this);
    }
}
