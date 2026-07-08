using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Global authority for research state.
/// It stores the active research, paid activation states and partial ore progress independently from any physical researcher machine.
/// </summary>
[DisallowMultipleComponent]
public sealed class ResearchRuntimeService : MonoBehaviour
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
        MissingResearchId = 10,
        MissingScannerRuntimeService = 11,
        MissingDiscoveredOreRequirement = 12,
        MissingResearchTier = 13
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
        /// Adds processed ore count to a requirement index.
        /// </summary>
        public void AddProcessedAmount(int RequirementIndex, int Amount)
        {
            EnsureRequirementIndex(RequirementIndex);
            ProcessedAmounts[RequirementIndex] = Mathf.Max(0, ProcessedAmounts[RequirementIndex] + Mathf.Max(0, Amount));
        }

        /// <summary>
        /// Gets all processed amounts by requirement index.
        /// </summary>
        public IReadOnlyList<int> GetProcessedAmounts()
        {
            return ProcessedAmounts;
        }

        /// <summary>
        /// Replaces the processed amount list with sanitized saved values.
        /// </summary>
        public void SetProcessedAmounts(IReadOnlyList<int> SavedAmounts)
        {
            ProcessedAmounts.Clear();

            if (SavedAmounts == null)
            {
                return;
            }

            for (int Index = 0; Index < SavedAmounts.Count; Index++)
            {
                ProcessedAmounts.Add(Mathf.Max(0, SavedAmounts[Index]));
            }
        }

        /// <summary>
        /// Creates a deep copy of this progress state.
        /// </summary>
        public ResearchProgressState Clone()
        {
            ResearchProgressState Result = new ResearchProgressState(ResearchId);
            Result.SetIsActivationPaid(IsActivationPaid);
            Result.SetProcessedAmounts(ProcessedAmounts);
            return Result;
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

    /// <summary>
    /// Serializable save payload for one research progress entry.
    /// </summary>
    [Serializable]
    public sealed class ResearchProgressSaveData
    {
        [Tooltip("Research id this progress belongs to.")]
        [SerializeField] private string ResearchId;

        [Tooltip("True after the activation credit cost has been paid once.")]
        [SerializeField] private bool IsActivationPaid;

        [Tooltip("Processed ore counts by requirement index.")]
        [SerializeField] private List<int> ProcessedAmounts = new();

        /// <summary>
        /// Creates an empty save payload for serialization.
        /// </summary>
        public ResearchProgressSaveData()
        {
        }

        /// <summary>
        /// Creates a save payload from one runtime progress state.
        /// </summary>
        public ResearchProgressSaveData(ResearchProgressState RuntimeState)
        {
            if (RuntimeState == null)
            {
                ResearchId = string.Empty;
                IsActivationPaid = false;
                return;
            }

            ResearchId = RuntimeState.GetResearchId();
            IsActivationPaid = RuntimeState.GetIsActivationPaid();

            IReadOnlyList<int> RuntimeAmounts = RuntimeState.GetProcessedAmounts();

            for (int Index = 0; Index < RuntimeAmounts.Count; Index++)
            {
                ProcessedAmounts.Add(Mathf.Max(0, RuntimeAmounts[Index]));
            }
        }

        /// <summary>
        /// Gets the saved research id.
        /// </summary>
        public string GetResearchId()
        {
            return ResearchId;
        }

        /// <summary>
        /// Converts this save payload back into a runtime progress state.
        /// </summary>
        public ResearchProgressState ToRuntimeState()
        {
            ResearchProgressState Result = new ResearchProgressState(ResearchId);
            Result.SetIsActivationPaid(IsActivationPaid);
            Result.SetProcessedAmounts(ProcessedAmounts);
            return Result;
        }
    }

    /// <summary>
    /// Serializable save payload for the global research runtime state.
    /// </summary>
    [Serializable]
    public sealed class ResearchRuntimeSaveData
    {
        [Tooltip("Research id that was active when the game was saved.")]
        [SerializeField] private string ActiveResearchId;

        [Tooltip("Saved progress states for paid or partially processed researches.")]
        [SerializeField] private List<ResearchProgressSaveData> ProgressStates = new();

        /// <summary>
        /// Creates an empty save payload for serialization.
        /// </summary>
        public ResearchRuntimeSaveData()
        {
        }

        /// <summary>
        /// Creates a save payload with the provided active research id.
        /// </summary>
        public ResearchRuntimeSaveData(string ActiveResearchIdValue)
        {
            ActiveResearchId = ActiveResearchIdValue;
        }

        /// <summary>
        /// Gets the saved active research id.
        /// </summary>
        public string GetActiveResearchId()
        {
            return ActiveResearchId;
        }

        /// <summary>
        /// Gets the saved research progress entries.
        /// </summary>
        public List<ResearchProgressSaveData> GetProgressStates()
        {
            return ProgressStates;
        }

        /// <summary>
        /// Adds one progress entry to this save payload.
        /// </summary>
        public void AddProgressState(ResearchProgressSaveData ProgressState)
        {
            if (ProgressState == null || string.IsNullOrWhiteSpace(ProgressState.GetResearchId()))
            {
                return;
            }

            ProgressStates.Add(ProgressState);
        }
    }

    [Header("References")]
    [Tooltip("Wallet used to spend research activation credit costs.")]
    [SerializeField] private CurrencyWallet CurrencyWallet;

    [Tooltip("Upgrade manager used for prerequisites and final research application.")]
    [SerializeField] private UpgradeManager UpgradeManager;

    [Tooltip("Scanner runtime service used to block activation until required ore types have been discovered.")]
    [SerializeField] private ScannerRuntimeService ScannerRuntimeService;

    [Header("Research Lookup")]
    [Tooltip("Explicit research definitions known by the runtime service. Register every research asset here for deterministic save/load.")]
    [SerializeField] private List<ResearchDefinition> ResearchDefinitions = new();

    [Header("Runtime State")]
    [Tooltip("Research currently active. Only this research consumes matching ores from placed research stations.")]
    [SerializeField] private ResearchDefinition ActiveResearchDefinition;

    [Tooltip("Runtime progress retained while switching active research and saved globally.")]
    [SerializeField] private List<ResearchProgressState> ResearchProgressStates = new();

    [Header("Debug")]
    [Tooltip("Logs global research flow and ore consumption.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Fired when research state, active research or progress may have changed.
    /// </summary>
    public event Action OnResearchStateChanged;

    /// <summary>
    /// Resolves missing references.
    /// </summary>
    private void Awake()
    {
        ResolveReferences();
        ApplyDefaultCompletedResearchDefinitions(false);
    }

    /// <summary>
    /// Gets the currently active research definition.
    /// </summary>
    public ResearchDefinition GetActiveResearchDefinition()
    {
        return ActiveResearchDefinition;
    }

    /// <summary>
    /// Registers a research definition in the global lookup table.
    /// </summary>
    public void RegisterResearchDefinition(ResearchDefinition ResearchDefinition)
    {
        RegisterResearchDefinitionInternal(ResearchDefinition, true);
    }

    /// <summary>
    /// Registers a research definition in the global lookup table and optionally applies default-completed state.
    /// </summary>
    /// <param name="ResearchDefinition">Research definition to register.</param>
    /// <param name="ApplyDefaultCompletion">If true, researches configured to start completed can apply their result immediately.</param>
    private void RegisterResearchDefinitionInternal(ResearchDefinition ResearchDefinition, bool ApplyDefaultCompletion)
    {
        if (ResearchDefinition == null || string.IsNullOrWhiteSpace(ResearchDefinition.GetResearchId()))
        {
            return;
        }

        string ResearchId = ResearchDefinition.GetResearchId();

        for (int Index = 0; Index < ResearchDefinitions.Count; Index++)
        {
            ResearchDefinition ExistingDefinition = ResearchDefinitions[Index];

            if (ExistingDefinition == null)
            {
                continue;
            }

            if (ExistingDefinition == ResearchDefinition ||
                string.Equals(ExistingDefinition.GetResearchId(), ResearchId, StringComparison.Ordinal))
            {
                ResearchDefinitions[Index] = ResearchDefinition;

                if (ApplyDefaultCompletion)
                {
                    TryApplyDefaultCompletedResearch(ResearchDefinition, true);
                }

                return;
            }
        }

        ResearchDefinitions.Add(ResearchDefinition);

        if (ApplyDefaultCompletion)
        {
            TryApplyDefaultCompletedResearch(ResearchDefinition, true);
        }
    }

    /// <summary>
    /// Creates a save snapshot containing the active research id and retained progress states.
    /// </summary>
    public ResearchRuntimeSaveData CreateSaveSnapshot()
    {
        string ActiveResearchId = ActiveResearchDefinition != null ? ActiveResearchDefinition.GetResearchId() : string.Empty;
        ResearchRuntimeSaveData Result = new ResearchRuntimeSaveData(ActiveResearchId);

        for (int Index = 0; Index < ResearchProgressStates.Count; Index++)
        {
            ResearchProgressState State = ResearchProgressStates[Index];

            if (State == null || string.IsNullOrWhiteSpace(State.GetResearchId()))
            {
                continue;
            }

            Result.AddProgressState(new ResearchProgressSaveData(State));
        }

        return Result;
    }

    /// <summary>
    /// Restores the active research and retained progress states from save data.
    /// Completed researches still come from UpgradeManager; this restores unfinished or paid progress.
    /// </summary>
    public void ApplySaveState(ResearchRuntimeSaveData SaveData)
    {
        ResolveReferences();
        ResearchProgressStates.Clear();
        ActiveResearchDefinition = null;

        if (SaveData == null)
        {
            ApplyDefaultCompletedResearchDefinitions(false);
            NotifyStateChanged();
            return;
        }

        List<ResearchProgressSaveData> SavedProgressStates = SaveData.GetProgressStates();

        if (SavedProgressStates != null)
        {
            for (int Index = 0; Index < SavedProgressStates.Count; Index++)
            {
                ResearchProgressSaveData SavedState = SavedProgressStates[Index];

                if (SavedState == null || string.IsNullOrWhiteSpace(SavedState.GetResearchId()))
                {
                    continue;
                }

                ResearchProgressStates.Add(SavedState.ToRuntimeState());
            }
        }

        string SavedActiveResearchId = SaveData.GetActiveResearchId();

        if (!string.IsNullOrWhiteSpace(SavedActiveResearchId))
        {
            ActiveResearchDefinition = ResolveResearchDefinitionById(SavedActiveResearchId);

            if (ActiveResearchDefinition == null)
            {
                Log("Saved active research could not be resolved: " + SavedActiveResearchId);
            }
        }

        ApplyDefaultCompletedResearchDefinitions(false);
        NotifyStateChanged();
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

        if (BlockReason != ResearchBlockReason.None &&
            BlockReason != ResearchBlockReason.NotEnoughCredits &&
            BlockReason != ResearchBlockReason.MissingDiscoveredOreRequirement)
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

        return ResearchViewState.Available;
    }

    /// <summary>
    /// Gets the current reason why a research entry cannot currently be activated.
    /// Ore discovery is checked here because unknown ore types must be scanned before the research can become active.
    /// </summary>
    public ResearchBlockReason GetResearchBlockReason(ResearchDefinition ResearchDefinition)
    {
        ResolveReferences();

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
        int TargetLevel = ResearchDefinition.GetStartsCompletedByDefault()
            ? ResearchDefinition.GetDefaultCompletedTargetLevel()
            : ResearchDefinition.GetResolvedTargetLevel(UpgradeManager);

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

        if (!IsResearchTierRequirementMet(ResearchDefinition))
        {
            return ResearchBlockReason.MissingResearchTier;
        }

        if (HasDiscoverableOreRequirements(ResearchDefinition) && ScannerRuntimeService == null)
        {
            return ResearchBlockReason.MissingScannerRuntimeService;
        }

        if (HasUndiscoveredOreRequirements(ResearchDefinition))
        {
            return ResearchBlockReason.MissingDiscoveredOreRequirement;
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
    public bool TryActivateResearch(ResearchDefinition ResearchDefinition)
    {
        RegisterResearchDefinition(ResearchDefinition);
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

        if (IsResearchProgressComplete(ResearchDefinition))
        {
            CompleteActiveResearch();
            Log("Research activated and completed instantly: " + ResearchDefinition.GetDisplayName());
            return true;
        }

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
    /// Returns whether every valid ore requirement on the provided research has already been discovered by the scanner system.
    /// Research entries without valid ore requirements are considered fully discovered.
    /// </summary>
    /// <param name="ResearchDefinition">Research definition to evaluate.</param>
    /// <returns>True when there are no unknown ore requirements.</returns>
    public bool AreOreRequirementsDiscovered(ResearchDefinition ResearchDefinition)
    {
        if (ResearchDefinition == null)
        {
            return false;
        }

        IReadOnlyList<ResearchDefinition.OreRequirement> Requirements = ResearchDefinition.GetOreRequirements();

        if (Requirements == null || Requirements.Count <= 0)
        {
            return true;
        }

        for (int Index = 0; Index < Requirements.Count; Index++)
        {
            ResearchDefinition.OreRequirement Requirement = Requirements[Index];

            if (Requirement == null || !Requirement.IsValid())
            {
                continue;
            }

            if (!IsOreRequirementDiscovered(Requirement))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns whether the provided ore requirement references an ore type already discovered by the scanner system.
    /// Invalid requirements are considered unknown so authoring problems do not silently unlock activation.
    /// </summary>
    /// <param name="Requirement">Requirement to evaluate.</param>
    /// <returns>True when the required ore type is globally discovered.</returns>
    public bool IsOreRequirementDiscovered(ResearchDefinition.OreRequirement Requirement)
    {
        if (Requirement == null || !Requirement.IsValid())
        {
            return false;
        }

        ResolveReferences();

        if (ScannerRuntimeService == null)
        {
            return false;
        }

        return ScannerRuntimeService.IsOreDefinitionDiscovered(Requirement.GetOreDefinition());
    }

    /// <summary>
    /// Counts valid ore requirements whose ore type has not been discovered yet.
    /// </summary>
    /// <param name="ResearchDefinition">Research definition to evaluate.</param>
    /// <returns>Number of unknown ore requirements.</returns>
    public int CountUndiscoveredOreRequirements(ResearchDefinition ResearchDefinition)
    {
        if (ResearchDefinition == null)
        {
            return 0;
        }

        IReadOnlyList<ResearchDefinition.OreRequirement> Requirements = ResearchDefinition.GetOreRequirements();

        if (Requirements == null || Requirements.Count <= 0)
        {
            return 0;
        }

        int Count = 0;

        for (int Index = 0; Index < Requirements.Count; Index++)
        {
            ResearchDefinition.OreRequirement Requirement = Requirements[Index];

            if (Requirement == null || !Requirement.IsValid())
            {
                continue;
            }

            if (!IsOreRequirementDiscovered(Requirement))
            {
                Count++;
            }
        }

        return Count;
    }

    /// <summary>
    /// Attempts to process matching physical ore pickups into the active research.
    /// </summary>
    /// <param name="CandidatePickups">Candidate pickups currently inside a physical ore input zone.</param>
    /// <param name="MaxOresToProcess">Maximum number of matching pickups consumed this call.</param>
    /// <returns>Number of pickups consumed.</returns>
    public int ProcessOrePickups(IReadOnlyList<OrePickup> CandidatePickups, int MaxOresToProcess)
    {
        if (ActiveResearchDefinition == null || CandidatePickups == null || CandidatePickups.Count <= 0)
        {
            return 0;
        }

        ResearchBlockReason BlockReason = GetResearchBlockReason(ActiveResearchDefinition);

        if (BlockReason == ResearchBlockReason.AlreadyCompleted)
        {
            ActiveResearchDefinition = null;
            NotifyStateChanged();
            return 0;
        }

        if (BlockReason != ResearchBlockReason.None)
        {
            return 0;
        }

        List<OrePickup> AvailablePickups = new List<OrePickup>();

        for (int Index = 0; Index < CandidatePickups.Count; Index++)
        {
            OrePickup Pickup = CandidatePickups[Index];

            if (Pickup != null && Pickup.gameObject.activeInHierarchy)
            {
                AvailablePickups.Add(Pickup);
            }
        }

        int ProcessedCount = 0;
        int MaxCount = Mathf.Max(1, MaxOresToProcess);

        while (ProcessedCount < MaxCount && TryProcessOneMatchingOre(ActiveResearchDefinition, AvailablePickups))
        {
            ProcessedCount++;
        }

        if (ProcessedCount > 0)
        {
            NotifyStateChanged();
        }

        if (IsResearchProgressComplete(ActiveResearchDefinition))
        {
            CompleteActiveResearch();
        }

        return ProcessedCount;
    }

    /// <summary>
    /// Resolves a research definition by its stable id from the global lookup.
    /// </summary>
    public ResearchDefinition ResolveResearchDefinitionById(string ResearchId)
    {
        if (string.IsNullOrWhiteSpace(ResearchId))
        {
            return null;
        }

        RegisterLoadedResearchDefinitions();

        for (int Index = 0; Index < ResearchDefinitions.Count; Index++)
        {
            ResearchDefinition Definition = ResearchDefinitions[Index];

            if (Definition == null)
            {
                continue;
            }

            if (string.Equals(Definition.GetResearchId(), ResearchId, StringComparison.Ordinal))
            {
                return Definition;
            }
        }

        return null;
    }

    /// <summary>
    /// Attempts to consume one ore pickup that satisfies one remaining requirement of the active research.
    /// </summary>
    private bool TryProcessOneMatchingOre(ResearchDefinition ResearchDefinition, List<OrePickup> CandidatePickups)
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

            OrePickup MatchingPickup = FindMatchingPickupForRequirement(Requirement, CandidatePickups);

            if (MatchingPickup == null)
            {
                continue;
            }

            ConsumeOrePickup(MatchingPickup);
            CandidatePickups.Remove(MatchingPickup);
            ProgressState.AddProcessedAmount(RequirementIndex, 1);
            Log("Processed ore for research: " + ResearchDefinition.GetDisplayName() + " | Requirement=" + Requirement.BuildDisplayRequirementLabel());
            return true;
        }

        return false;
    }

    /// <summary>
    /// Finds one candidate ore pickup matching the provided requirement.
    /// </summary>
    private OrePickup FindMatchingPickupForRequirement(ResearchDefinition.OreRequirement Requirement, List<OrePickup> CandidatePickups)
    {
        if (Requirement == null || CandidatePickups == null)
        {
            return null;
        }

        for (int Index = 0; Index < CandidatePickups.Count; Index++)
        {
            OrePickup Pickup = CandidatePickups[Index];

            if (Pickup != null && Requirement.MatchesPickup(Pickup))
            {
                return Pickup;
            }
        }

        return null;
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
    /// Returns whether the provided research has at least one valid ore requirement that must be discovered before activation.
    /// </summary>
    /// <param name="ResearchDefinition">Research definition to inspect.</param>
    private bool HasDiscoverableOreRequirements(ResearchDefinition ResearchDefinition)
    {
        if (ResearchDefinition == null)
        {
            return false;
        }

        IReadOnlyList<ResearchDefinition.OreRequirement> Requirements = ResearchDefinition.GetOreRequirements();

        if (Requirements == null || Requirements.Count <= 0)
        {
            return false;
        }

        for (int Index = 0; Index < Requirements.Count; Index++)
        {
            ResearchDefinition.OreRequirement Requirement = Requirements[Index];

            if (Requirement != null && Requirement.IsValid())
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns whether the provided research has one or more valid ore requirements that are still unknown to the scanner system.
    /// </summary>
    /// <param name="ResearchDefinition">Research definition to inspect.</param>
    private bool HasUndiscoveredOreRequirements(ResearchDefinition ResearchDefinition)
    {
        return CountUndiscoveredOreRequirements(ResearchDefinition) > 0;
    }

    /// <summary>
    /// Returns whether the provided research tier requirement is currently satisfied.
    /// Research entries without a tier gate are considered available at the tier level.
    /// </summary>
    /// <param name="ResearchDefinition">Research definition being evaluated.</param>
    public bool IsResearchTierRequirementMet(ResearchDefinition ResearchDefinition)
    {
        ResolveReferences();

        if (ResearchDefinition == null)
        {
            return false;
        }

        if (!ResearchDefinition.GetRequiresResearchTier())
        {
            return true;
        }

        return IsResearchTierUnlocked(
            ResearchDefinition.GetRequiredResearchTierUpgradeDefinition(),
            ResearchDefinition.GetRequiredResearchTierLevel());
    }

    /// <summary>
    /// Returns whether the global research tier upgrade has reached the requested level.
    /// </summary>
    /// <param name="ResearchTierUpgradeDefinition">Upgrade definition used to store research tier progress.</param>
    /// <param name="RequiredTierLevel">Minimum required tier level.</param>
    public bool IsResearchTierUnlocked(UpgradeDefinition ResearchTierUpgradeDefinition, int RequiredTierLevel)
    {
        ResolveReferences();

        if (UpgradeManager == null || ResearchTierUpgradeDefinition == null)
        {
            return false;
        }

        UpgradeDefinition RegisteredUpgradeDefinition = UpgradeManager.GetUpgradeDefinition(ResearchTierUpgradeDefinition.GetUpgradeId());

        if (RegisteredUpgradeDefinition != ResearchTierUpgradeDefinition)
        {
            return false;
        }

        int CurrentTierLevel = UpgradeManager.GetUpgradeLevel(ResearchTierUpgradeDefinition);
        return CurrentTierLevel >= Mathf.Max(1, RequiredTierLevel);
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
    /// Permanently consumes one ore pickup for research.
    /// </summary>
    private void ConsumeOrePickup(OrePickup Pickup)
    {
        if (Pickup == null)
        {
            return;
        }

        if (Pickup.ReturnToPool())
        {
            return;
        }

        Transform Root = Pickup.GetRuntimeRoot();
        Destroy(Root != null ? Root.gameObject : Pickup.gameObject);
    }

    /// <summary>
    /// Resolves mandatory scene references if they were not assigned manually.
    /// </summary>
    private void ResolveReferences()
    {
        if (CurrencyWallet == null)
        {
            CurrencyWallet = FindFirstObjectByType<CurrencyWallet>();
        }

        if (UpgradeManager == null)
        {
            UpgradeManager = FindFirstObjectByType<UpgradeManager>();
        }

        if (ScannerRuntimeService == null)
        {
            ScannerRuntimeService = FindFirstObjectByType<ScannerRuntimeService>();
        }
    }

    /// <summary>
    /// Applies every serialized research configured to start completed by default.
    /// </summary>
    /// <param name="NotifyIfChanged">If true, listeners are notified when at least one default research applies an upgrade level.</param>
    private void ApplyDefaultCompletedResearchDefinitions(bool NotifyIfChanged)
    {
        bool Changed = false;

        for (int Index = 0; Index < ResearchDefinitions.Count; Index++)
        {
            Changed |= TryApplyDefaultCompletedResearch(ResearchDefinitions[Index], false);
        }

        if (Changed && NotifyIfChanged)
        {
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// Applies the result of a research that is configured to start completed, without consuming credits or ores.
    /// </summary>
    /// <param name="ResearchDefinition">Research definition being initialized.</param>
    /// <param name="NotifyIfChanged">If true, listeners are notified when the default completion changes runtime state.</param>
    /// <returns>True when the runtime upgrade level was changed.</returns>
    private bool TryApplyDefaultCompletedResearch(ResearchDefinition ResearchDefinition, bool NotifyIfChanged)
    {
        ResolveReferences();

        if (ResearchDefinition == null || !ResearchDefinition.GetStartsCompletedByDefault() || UpgradeManager == null)
        {
            return false;
        }

        UpgradeDefinition AppliedUpgradeDefinition = ResearchDefinition.GetAppliedUpgradeDefinition();

        if (AppliedUpgradeDefinition == null || string.IsNullOrWhiteSpace(AppliedUpgradeDefinition.GetUpgradeId()))
        {
            return false;
        }

        UpgradeDefinition RegisteredUpgradeDefinition = UpgradeManager.GetUpgradeDefinition(AppliedUpgradeDefinition.GetUpgradeId());

        if (RegisteredUpgradeDefinition != AppliedUpgradeDefinition)
        {
            return false;
        }

        int TargetLevel = ResearchDefinition.GetDefaultCompletedTargetLevel();
        int CurrentLevel = UpgradeManager.GetUpgradeLevel(AppliedUpgradeDefinition);

        if (TargetLevel <= 0 || CurrentLevel >= TargetLevel)
        {
            return false;
        }

        UpgradeManager.SetUpgradeLevel(AppliedUpgradeDefinition, TargetLevel);

        if (ActiveResearchDefinition == ResearchDefinition)
        {
            ActiveResearchDefinition = null;
        }

        Log("Default completed research applied: " + ResearchDefinition.GetDisplayName());

        if (NotifyIfChanged)
        {
            NotifyStateChanged();
        }

        return true;
    }

    /// <summary>
    /// Registers loaded research definition assets so save/load can resolve active research ids.
    /// </summary>
    private void RegisterLoadedResearchDefinitions()
    {
        ResearchDefinition[] LoadedDefinitions = Resources.FindObjectsOfTypeAll<ResearchDefinition>();

        for (int Index = 0; Index < LoadedDefinitions.Length; Index++)
        {
            RegisterResearchDefinitionInternal(LoadedDefinitions[Index], false);
        }
    }

    /// <summary>
    /// Notifies bound UI or station components that research state changed.
    /// </summary>
    private void NotifyStateChanged()
    {
        OnResearchStateChanged?.Invoke();
    }

    /// <summary>
    /// Writes a research-runtime-specific debug message.
    /// </summary>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[ResearchRuntimeService] " + Message, this);
    }
}
