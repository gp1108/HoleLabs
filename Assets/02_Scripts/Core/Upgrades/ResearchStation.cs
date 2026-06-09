using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// World researcher station that activates one research at a time and progressively processes matching physical ores.
/// Activation spends credits once, progress is retained when switching active research, and completion applies an UpgradeDefinition.
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class ResearchStation : MonoBehaviour
{
    /// <summary>
    /// Describes why a research entry cannot currently be activated.
    /// </summary>
    public enum ResearchBlockReason
    {
        None = 0,
        MissingResearch = 1,
        MissingWallet = 2,
        MissingUpgradeManager = 3,
        MissingAppliedUpgrade = 4,
        AppliedUpgradeNotRegistered = 5,
        AlreadyCompleted = 6,
        MissingFeatureFlag = 7,
        MissingPrerequisite = 8,
        NotEnoughCredits = 9,
        MissingResearchId = 10
    }

    /// <summary>
    /// High level state used by UI entries.
    /// </summary>
    public enum ResearchViewState
    {
        Locked = 0,
        Available = 1,
        PaidInactive = 2,
        Active = 3,
        Completed = 4,
        Invalid = 5
    }

    /// <summary>
    /// Runtime progress for one configured ore requirement.
    /// </summary>
    public readonly struct OreRequirementProgress
    {
        /// <summary>
        /// Requirement configured by the research asset.
        /// </summary>
        public readonly ResearchDefinition.OreRequirement Requirement;

        /// <summary>
        /// Amount already processed into this research.
        /// </summary>
        public readonly int ProcessedAmount;

        /// <summary>
        /// Amount required by the research asset.
        /// </summary>
        public readonly int RequiredAmount;

        /// <summary>
        /// Creates one requirement progress value.
        /// </summary>
        public OreRequirementProgress(ResearchDefinition.OreRequirement RequirementValue, int ProcessedAmountValue, int RequiredAmountValue)
        {
            Requirement = RequirementValue;
            ProcessedAmount = Mathf.Max(0, ProcessedAmountValue);
            RequiredAmount = Mathf.Max(0, RequiredAmountValue);
        }

        /// <summary>
        /// Returns whether this requirement has been fully processed.
        /// </summary>
        public bool IsSatisfied()
        {
            return ProcessedAmount >= RequiredAmount;
        }
    }

    /// <summary>
    /// Serializable runtime progress retained while switching active research.
    /// </summary>
    [Serializable]
    public sealed class ResearchProgressState
    {
        [Tooltip("Research id this progress belongs to.")]
        [SerializeField] private string ResearchId;

        [Tooltip("True after the activation credit cost has been paid once.")]
        [SerializeField] private bool IsActivationPaid;

        [Tooltip("Processed ore counts by requirement index.")]
        [SerializeField] private List<int> ProcessedAmounts = new();

        /// <summary>
        /// Creates an empty progress state for serialization.
        /// </summary>
        public ResearchProgressState()
        {
        }

        /// <summary>
        /// Creates a new progress state for the provided research id.
        /// </summary>
        public ResearchProgressState(string ResearchIdValue)
        {
            ResearchId = ResearchIdValue;
        }

        /// <summary>
        /// Gets the research id.
        /// </summary>
        public string GetResearchId()
        {
            return ResearchId;
        }

        /// <summary>
        /// Gets whether activation credits have already been paid.
        /// </summary>
        public bool GetIsActivationPaid()
        {
            return IsActivationPaid;
        }

        /// <summary>
        /// Sets whether activation credits have already been paid.
        /// </summary>
        public void SetIsActivationPaid(bool Value)
        {
            IsActivationPaid = Value;
        }

        /// <summary>
        /// Gets the processed amount for a requirement index.
        /// </summary>
        public int GetProcessedAmount(int RequirementIndex)
        {
            if (RequirementIndex < 0 || RequirementIndex >= ProcessedAmounts.Count)
            {
                return 0;
            }

            return Mathf.Max(0, ProcessedAmounts[RequirementIndex]);
        }

        /// <summary>
        /// Adds one processed ore to a requirement index.
        /// </summary>
        public void AddProcessedAmount(int RequirementIndex, int Amount)
        {
            EnsureRequirementIndex(RequirementIndex);
            ProcessedAmounts[RequirementIndex] = Mathf.Max(0, ProcessedAmounts[RequirementIndex] + Mathf.Max(0, Amount));
        }

        /// <summary>
        /// Ensures the processed amount list can store the provided requirement index.
        /// </summary>
        private void EnsureRequirementIndex(int RequirementIndex)
        {
            if (RequirementIndex < 0)
            {
                return;
            }

            while (ProcessedAmounts.Count <= RequirementIndex)
            {
                ProcessedAmounts.Add(0);
            }
        }
    }

    [Header("References")]
    [Tooltip("Panel controlled by this research station.")]
    [SerializeField] private ResearchPanelUI ResearchPanelUI;

    [Tooltip("Wallet used to spend research activation credit costs.")]
    [SerializeField] private CurrencyWallet CurrencyWallet;

    [Tooltip("Upgrade manager used for prerequisites and final research application.")]
    [SerializeField] private UpgradeManager UpgradeManager;

    [Tooltip("Optional prompt root enabled only while the player is inside the station range.")]
    [SerializeField] private GameObject PromptRoot;

    [Header("Ore Input")]
    [Tooltip("Layer mask used by the station input zone. Keep this broad unless you have a dedicated ore layer.")]
    [SerializeField] private LayerMask OreInputLayers = ~0;

    [Tooltip("If true, held or magnetized ore pickups inside the trigger are ignored and cannot be consumed.")]
    [SerializeField] private bool IgnoreControlledCarryables = true;

    [Tooltip("Seconds between ore processing attempts while a research is active.")]
    [SerializeField] private float ProcessingInterval = 0.35f;

    [Tooltip("Maximum number of matching ores consumed per processing tick.")]
    [SerializeField] private int MaxOresProcessedPerTick = 1;

    [Header("Runtime State")]
    [Tooltip("Research currently active. Only this research consumes matching ores.")]
    [SerializeField] private ResearchDefinition ActiveResearchDefinition;

    [Tooltip("Runtime progress retained while switching active research. This is prepared for future save integration.")]
    [SerializeField] private List<ResearchProgressState> ResearchProgressStates = new();

    [Header("Debug")]
    [Tooltip("Logs research station flow and ore consumption.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Player currently inside this station trigger.
    /// </summary>
    private UpgradeShopInteractor CurrentInteractor;

    /// <summary>
    /// Colliders currently overlapping the research input trigger.
    /// </summary>
    private readonly HashSet<Collider> LiveInputColliders = new();

    /// <summary>
    /// Reusable list used to resolve unique ore pickups inside the input zone.
    /// </summary>
    private readonly List<OrePickup> ResolvedOrePickups = new();

    /// <summary>
    /// Reusable set used to deduplicate ore pickups with multiple colliders.
    /// </summary>
    private readonly HashSet<OrePickup> ResolvedOrePickupSet = new();

    /// <summary>
    /// Runtime timer used to process active research ores at a controlled pace.
    /// </summary>
    private float ProcessingTimer;

    /// <summary>
    /// Fired when ore contents or research state may have changed.
    /// </summary>
    public event Action OnResearchStationStateChanged;

    /// <summary>
    /// Resolves missing runtime references and initializes the panel.
    /// </summary>
    private void Awake()
    {
        if (CurrencyWallet == null)
        {
            CurrencyWallet = FindFirstObjectByType<CurrencyWallet>();
        }

        if (UpgradeManager == null)
        {
            UpgradeManager = FindFirstObjectByType<UpgradeManager>();
        }

        if (ResearchPanelUI != null)
        {
            ResearchPanelUI.Initialize(this);
        }
    }

    /// <summary>
    /// Cleans invalid collider references and processes the active research over time.
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
    /// Gets the currently active research definition.
    /// </summary>
    public ResearchDefinition GetActiveResearchDefinition()
    {
        return ActiveResearchDefinition;
    }

    /// <summary>
    /// Returns whether the provided interactor is currently registered inside this station range.
    /// </summary>
    public bool IsInteractorRegistered(UpgradeShopInteractor Interactor)
    {
        return CurrentInteractor == Interactor;
    }

    /// <summary>
    /// Returns whether the provided research is currently active.
    /// </summary>
    public bool IsResearchActive(ResearchDefinition ResearchDefinition)
    {
        return ResearchDefinition != null && ActiveResearchDefinition == ResearchDefinition;
    }

    /// <summary>
    /// Gets the current UI state for one research entry.
    /// </summary>
    public ResearchViewState GetResearchViewState(ResearchDefinition ResearchDefinition)
    {
        ResearchBlockReason BlockReason = GetResearchBlockReason(ResearchDefinition);

        if (BlockReason == ResearchBlockReason.AlreadyCompleted)
        {
            return ResearchViewState.Completed;
        }

        if (BlockReason != ResearchBlockReason.None && BlockReason != ResearchBlockReason.NotEnoughCredits)
        {
            return ResearchViewState.Locked;
        }

        if (ResearchDefinition == null)
        {
            return ResearchViewState.Invalid;
        }

        ResearchProgressState ProgressState = GetProgressState(ResearchDefinition, false);

        if (ActiveResearchDefinition == ResearchDefinition)
        {
            return ResearchViewState.Active;
        }

        if (ProgressState != null && ProgressState.GetIsActivationPaid())
        {
            return ResearchViewState.PaidInactive;
        }

        return BlockReason == ResearchBlockReason.NotEnoughCredits ? ResearchViewState.Locked : ResearchViewState.Available;
    }

    /// <summary>
    /// Gets the current reason why a research entry cannot currently be activated.
    /// Ores are not checked here because ores are processed progressively after activation.
    /// </summary>
    public ResearchBlockReason GetResearchBlockReason(ResearchDefinition ResearchDefinition)
    {
        if (ResearchDefinition == null)
        {
            return ResearchBlockReason.MissingResearch;
        }

        if (string.IsNullOrWhiteSpace(ResearchDefinition.GetResearchId()))
        {
            return ResearchBlockReason.MissingResearchId;
        }

        if (CurrencyWallet == null)
        {
            return ResearchBlockReason.MissingWallet;
        }

        if (UpgradeManager == null)
        {
            return ResearchBlockReason.MissingUpgradeManager;
        }

        UpgradeDefinition AppliedUpgradeDefinition = ResearchDefinition.GetAppliedUpgradeDefinition();

        if (AppliedUpgradeDefinition == null)
        {
            return ResearchBlockReason.MissingAppliedUpgrade;
        }

        UpgradeDefinition RegisteredUpgradeDefinition = UpgradeManager.GetUpgradeDefinition(AppliedUpgradeDefinition.GetUpgradeId());

        if (RegisteredUpgradeDefinition != AppliedUpgradeDefinition)
        {
            return ResearchBlockReason.AppliedUpgradeNotRegistered;
        }

        int CurrentLevel = UpgradeManager.GetUpgradeLevel(AppliedUpgradeDefinition);
        int TargetLevel = ResearchDefinition.GetResolvedTargetLevel(UpgradeManager);

        if (TargetLevel <= CurrentLevel)
        {
            return ResearchBlockReason.AlreadyCompleted;
        }

        if (ResearchDefinition.GetRequiresFeatureFlag() &&
            !UpgradeManager.IsFeatureUnlocked(ResearchDefinition.GetRequiredFeatureFlagId()))
        {
            return ResearchBlockReason.MissingFeatureFlag;
        }

        if (!ArePrerequisitesMet(ResearchDefinition))
        {
            return ResearchBlockReason.MissingPrerequisite;
        }

        ResearchProgressState ProgressState = GetProgressState(ResearchDefinition, false);

        if ((ProgressState == null || !ProgressState.GetIsActivationPaid()) &&
            !CurrencyWallet.HasEnoughCredits(ResearchDefinition.GetCreditCost()))
        {
            return ResearchBlockReason.NotEnoughCredits;
        }

        return ResearchBlockReason.None;
    }

    /// <summary>
    /// Attempts to activate the provided research entry.
    /// Credits are spent only the first time this research is activated; progress remains when switching away.
    /// </summary>
    /// <param name="ResearchDefinition">Research to activate.</param>
    /// <returns>True when the research became active.</returns>
    public bool TryActivateResearch(ResearchDefinition ResearchDefinition)
    {
        ResearchBlockReason BlockReason = GetResearchBlockReason(ResearchDefinition);

        if (BlockReason != ResearchBlockReason.None)
        {
            Log("Research activation blocked: " + BlockReason);
            NotifyStateChanged();
            return false;
        }

        ResearchProgressState ProgressState = GetProgressState(ResearchDefinition, true);

        if (ProgressState == null)
        {
            Log("Research activation failed because progress state could not be created.");
            NotifyStateChanged();
            return false;
        }

        if (!ProgressState.GetIsActivationPaid())
        {
            if (!CurrencyWallet.TrySpendCredits(ResearchDefinition.GetCreditCost()))
            {
                Log("Research activation failed while spending credits: " + ResearchDefinition.GetDisplayName());
                NotifyStateChanged();
                return false;
            }

            ProgressState.SetIsActivationPaid(true);
        }

        ActiveResearchDefinition = ResearchDefinition;
        ProcessingTimer = 0f;
        NotifyStateChanged();
        Log("Research activated: " + ResearchDefinition.GetDisplayName());
        return true;
    }

    /// <summary>
    /// Clears the active research without deleting its paid state or processed ore progress.
    /// </summary>
    public void ClearActiveResearch()
    {
        ActiveResearchDefinition = null;
        ProcessingTimer = 0f;
        NotifyStateChanged();
    }

    /// <summary>
    /// Gets progress for every ore requirement of the provided research entry.
    /// </summary>
    public List<OreRequirementProgress> GetOreRequirementProgress(ResearchDefinition ResearchDefinition)
    {
        List<OreRequirementProgress> Result = new();

        if (ResearchDefinition == null)
        {
            return Result;
        }

        ResearchProgressState ProgressState = GetProgressState(ResearchDefinition, false);
        IReadOnlyList<ResearchDefinition.OreRequirement> Requirements = ResearchDefinition.GetOreRequirements();

        for (int Index = 0; Index < Requirements.Count; Index++)
        {
            ResearchDefinition.OreRequirement Requirement = Requirements[Index];

            if (Requirement == null || !Requirement.IsValid())
            {
                continue;
            }

            int ProcessedAmount = ProgressState != null ? ProgressState.GetProcessedAmount(Index) : 0;
            Result.Add(new OreRequirementProgress(Requirement, ProcessedAmount, Requirement.GetAmount()));
        }

        return Result;
    }

    /// <summary>
    /// Processes matching physical ore pickups into the active research.
    /// </summary>
    private void ProcessActiveResearch(float DeltaTime)
    {
        if (ActiveResearchDefinition == null)
        {
            return;
        }

        ResearchBlockReason BlockReason = GetResearchBlockReason(ActiveResearchDefinition);

        if (BlockReason == ResearchBlockReason.AlreadyCompleted)
        {
            ActiveResearchDefinition = null;
            NotifyStateChanged();
            return;
        }

        if (BlockReason != ResearchBlockReason.None)
        {
            return;
        }

        ProcessingTimer -= DeltaTime;

        if (ProcessingTimer > 0f)
        {
            return;
        }

        ProcessingTimer = Mathf.Max(0.01f, ProcessingInterval);

        int ProcessedThisTick = 0;
        int MaxToProcess = Mathf.Max(1, MaxOresProcessedPerTick);

        while (ProcessedThisTick < MaxToProcess && TryProcessOneMatchingOre(ActiveResearchDefinition))
        {
            ProcessedThisTick++;
        }

        if (ProcessedThisTick > 0)
        {
            NotifyStateChanged();
        }

        if (IsResearchProgressComplete(ActiveResearchDefinition))
        {
            CompleteActiveResearch();
        }
    }

    /// <summary>
    /// Attempts to consume one ore pickup that satisfies one remaining requirement of the active research.
    /// </summary>
    private bool TryProcessOneMatchingOre(ResearchDefinition ResearchDefinition)
    {
        if (ResearchDefinition == null)
        {
            return false;
        }

        ResearchProgressState ProgressState = GetProgressState(ResearchDefinition, true);

        if (ProgressState == null || !ProgressState.GetIsActivationPaid())
        {
            return false;
        }

        ResolveCurrentOrePickups();

        IReadOnlyList<ResearchDefinition.OreRequirement> Requirements = ResearchDefinition.GetOreRequirements();

        for (int RequirementIndex = 0; RequirementIndex < Requirements.Count; RequirementIndex++)
        {
            ResearchDefinition.OreRequirement Requirement = Requirements[RequirementIndex];

            if (Requirement == null || !Requirement.IsValid())
            {
                continue;
            }

            if (ProgressState.GetProcessedAmount(RequirementIndex) >= Requirement.GetAmount())
            {
                continue;
            }

            OrePickup MatchingPickup = FindMatchingPickupForRequirement(Requirement);

            if (MatchingPickup == null)
            {
                continue;
            }

            ConsumeOrePickup(MatchingPickup);
            ProgressState.AddProcessedAmount(RequirementIndex, 1);
            Log("Processed ore for research: " + ResearchDefinition.GetDisplayName() + " | Requirement=" + Requirement.BuildDisplayRequirementLabel());
            return true;
        }

        return false;
    }

    /// <summary>
    /// Completes the currently active research and applies its configured upgrade result.
    /// </summary>
    private void CompleteActiveResearch()
    {
        if (ActiveResearchDefinition == null)
        {
            return;
        }

        ResearchDefinition CompletedResearch = ActiveResearchDefinition;
        ApplyResearchResult(CompletedResearch);
        ActiveResearchDefinition = null;
        ProcessingTimer = 0f;
        NotifyStateChanged();
        Log("Research completed: " + CompletedResearch.GetDisplayName());
    }

    /// <summary>
    /// Returns whether all ore requirements have been processed.
    /// </summary>
    private bool IsResearchProgressComplete(ResearchDefinition ResearchDefinition)
    {
        ResearchProgressState ProgressState = GetProgressState(ResearchDefinition, false);

        if (ResearchDefinition == null || ProgressState == null || !ProgressState.GetIsActivationPaid())
        {
            return false;
        }

        IReadOnlyList<ResearchDefinition.OreRequirement> Requirements = ResearchDefinition.GetOreRequirements();

        for (int Index = 0; Index < Requirements.Count; Index++)
        {
            ResearchDefinition.OreRequirement Requirement = Requirements[Index];

            if (Requirement == null || !Requirement.IsValid())
            {
                continue;
            }

            if (ProgressState.GetProcessedAmount(Index) < Requirement.GetAmount())
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Finds one currently available ore pickup matching the provided requirement.
    /// </summary>
    private OrePickup FindMatchingPickupForRequirement(ResearchDefinition.OreRequirement Requirement)
    {
        ResolveCurrentOrePickups();

        for (int Index = 0; Index < ResolvedOrePickups.Count; Index++)
        {
            OrePickup Pickup = ResolvedOrePickups[Index];

            if (Requirement != null && Requirement.MatchesPickup(Pickup))
            {
                return Pickup;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets or creates runtime progress for a research definition.
    /// </summary>
    private ResearchProgressState GetProgressState(ResearchDefinition ResearchDefinition, bool CreateIfMissing)
    {
        if (ResearchDefinition == null || string.IsNullOrWhiteSpace(ResearchDefinition.GetResearchId()))
        {
            return null;
        }

        string ResearchId = ResearchDefinition.GetResearchId();

        for (int Index = 0; Index < ResearchProgressStates.Count; Index++)
        {
            ResearchProgressState State = ResearchProgressStates[Index];

            if (State != null && string.Equals(State.GetResearchId(), ResearchId, StringComparison.Ordinal))
            {
                return State;
            }
        }

        if (!CreateIfMissing)
        {
            return null;
        }

        ResearchProgressState NewState = new ResearchProgressState(ResearchId);
        ResearchProgressStates.Add(NewState);
        return NewState;
    }

    /// <summary>
    /// Returns whether all upgrade prerequisites are currently met.
    /// </summary>
    private bool ArePrerequisitesMet(ResearchDefinition ResearchDefinition)
    {
        IReadOnlyList<ResearchDefinition.ResearchPrerequisite> Prerequisites = ResearchDefinition.GetPrerequisites();

        if (Prerequisites == null || Prerequisites.Count <= 0)
        {
            return true;
        }

        for (int Index = 0; Index < Prerequisites.Count; Index++)
        {
            ResearchDefinition.ResearchPrerequisite Prerequisite = Prerequisites[Index];

            if (Prerequisite == null || Prerequisite.GetRequiredUpgradeDefinition() == null)
            {
                return false;
            }

            int CurrentLevel = UpgradeManager.GetUpgradeLevel(Prerequisite.GetRequiredUpgradeDefinition());

            if (CurrentLevel < Prerequisite.GetRequiredLevel())
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Applies the upgrade level change configured by the research asset.
    /// </summary>
    private void ApplyResearchResult(ResearchDefinition ResearchDefinition)
    {
        UpgradeDefinition UpgradeDefinition = ResearchDefinition.GetAppliedUpgradeDefinition();
        int CurrentLevel = UpgradeManager.GetUpgradeLevel(UpgradeDefinition);
        int TargetLevel = CurrentLevel;

        switch (ResearchDefinition.GetApplyMode())
        {
            case ResearchDefinition.ResearchApplyMode.SetToLevel:
                TargetLevel = ResearchDefinition.GetTargetUpgradeLevel();
                break;

            case ResearchDefinition.ResearchApplyMode.AddLevels:
                TargetLevel = CurrentLevel + ResearchDefinition.GetUpgradeLevelIncrement();
                break;
        }

        UpgradeManager.SetUpgradeLevel(UpgradeDefinition, TargetLevel);
    }

    /// <summary>
    /// Rebuilds the unique current ore pickup list from live trigger colliders.
    /// </summary>
    private void ResolveCurrentOrePickups()
    {
        ResolvedOrePickups.Clear();
        ResolvedOrePickupSet.Clear();
        CleanupInvalidInputColliders();

        foreach (Collider LiveCollider in LiveInputColliders)
        {
            if (LiveCollider == null || !LiveCollider.enabled || !LiveCollider.gameObject.activeInHierarchy)
            {
                continue;
            }

            if ((OreInputLayers.value & (1 << LiveCollider.gameObject.layer)) == 0)
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
    /// Permanently consumes one ore pickup for research.
    /// </summary>
    private void ConsumeOrePickup(OrePickup Pickup)
    {
        if (Pickup == null)
        {
            return;
        }

        LiveInputColliders.RemoveWhere(ColliderValue => ColliderValue == null || ColliderValue.GetComponentInParent<OrePickup>() == Pickup);

        if (Pickup.ReturnToPool())
        {
            return;
        }

        Transform Root = Pickup.GetRuntimeRoot();
        Destroy(Root != null ? Root.gameObject : Pickup.gameObject);
    }

    /// <summary>
    /// Registers incoming colliders inside the research station input zone.
    /// </summary>
    private void OnTriggerEnter(Collider Other)
    {
        if (Other == null)
        {
            return;
        }

        UpgradeShopInteractor Interactor = Other.GetComponentInParent<UpgradeShopInteractor>();

        if (Interactor != null)
        {
            CurrentInteractor = Interactor;
            CurrentInteractor.SetNearbyResearchStation(this);

            if (PromptRoot != null)
            {
                PromptRoot.SetActive(true);
            }
        }

        if (Other.GetComponent<OrePickup>() != null || Other.GetComponentInParent<OrePickup>() != null)
        {
            LiveInputColliders.Add(Other);
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// Unregisters outgoing colliders from the research station input zone.
    /// </summary>
    private void OnTriggerExit(Collider Other)
    {
        if (Other == null)
        {
            return;
        }

        UpgradeShopInteractor Interactor = Other.GetComponentInParent<UpgradeShopInteractor>();

        if (Interactor != null && CurrentInteractor == Interactor)
        {
            CurrentInteractor.ClearNearbyResearchStation(this);

            if (PromptRoot != null)
            {
                PromptRoot.SetActive(false);
            }

            CurrentInteractor = null;
        }

        if (LiveInputColliders.Remove(Other))
        {
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// Removes invalid collider references from the input set.
    /// </summary>
    private void CleanupInvalidInputColliders()
    {
        int RemovedCount = LiveInputColliders.RemoveWhere(ColliderValue =>
            ColliderValue == null ||
            !ColliderValue.enabled ||
            !ColliderValue.gameObject.activeInHierarchy);

        if (RemovedCount > 0)
        {
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// Notifies bound UI that station contents or state changed.
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
