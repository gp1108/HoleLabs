using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Runtime controller for the installed purity machine.
/// It validates the target and sacrifice input zones, previews the final purity, consumes sacrifice ores one by one and applies purity gains to the target ore.
/// </summary>
public sealed class PurityMachineController : MonoBehaviour
{
    /// <summary>
    /// Current high-level machine state.
    /// </summary>
    public enum PurityMachineState
    {
        Idle = 0,
        Invalid = 1,
        Ready = 2,
        Processing = 3,
        Blocked = 4,
        Completed = 5,
        Cancelled = 6
    }

    /// <summary>
    /// Describes why the input validation is being executed.
    /// Passive preview avoids showing delayed player-facing errors until the machine button is pressed.
    /// </summary>
    private enum InputValidationContext
    {
        PassivePreview = 0,
        ButtonPress = 1,
        Processing = 2
    }

    [Header("References")]
    [Tooltip("Input zone that should contain exactly one ore to improve.")]
    [SerializeField] private PurityMachineInputZone TargetInputZone;

    [Tooltip("Input zone that contains ores sacrificed to improve the target ore.")]
    [SerializeField] private PurityMachineInputZone SacrificeInputZone;

    [Tooltip("Screen presenter used to show simple state, error and preview text.")]
    [SerializeField] private PurityMachineDisplayUI DisplayUI;

    [Tooltip("Runtime ore service used to recalculate value and weight after purity changes.")]
    [SerializeField] private OreRuntimeService OreRuntimeService;

    [Tooltip("Upgrade manager used to resolve conversion ratio and sacrifice capacity upgrades.")]
    [SerializeField] private UpgradeManager UpgradeManager;

    [Tooltip("Optional animator driven by the purity machine runtime state.")]
    [SerializeField] private Animator MachineAnimator;

    [Header("Processing")]
    [Tooltip("Base conversion ratio applied to each consumed sacrifice ore purity. Example: 0.25 means a 40% sacrifice ore gives +10% target purity.")]
    [SerializeField] private float BaseConversionRatio = 0.25f;

    [Tooltip("Base maximum amount of sacrifice ores allowed inside the sacrifice input zone.")]
    [SerializeField] private int BaseMaxSacrificeOreCount = 3;

    [Tooltip("Seconds required to consume each sacrifice ore while the machine is processing.")]
    [SerializeField] private float SacrificeConsumeInterval = 1f;

    [Tooltip("If true, ores already processed by the purity machine can still be used as sacrifices for other ores.")]
    [SerializeField] private bool AllowProcessedOresAsSacrifice = true;

    [Header("Upgrades")]
    [Tooltip("If true, UpgradeManager modifiers affect conversion ratio and sacrifice capacity.")]
    [SerializeField] private bool UseUpgradeModifiedValues = true;

    [Tooltip("Upgrade stat used to modify the purity conversion ratio.")]
    [SerializeField] private UpgradeStatType ConversionRatioStat = UpgradeStatType.PurityMachineConversionRatio;

    [Tooltip("Upgrade stat used to modify maximum sacrifice capacity.")]
    [SerializeField] private UpgradeStatType SacrificeCapacityStat = UpgradeStatType.PurityMachineSacrificeCapacity;

    [Header("Animator")]
    [Tooltip("Animator bool set while the machine is actively processing.")]
    [SerializeField] private string IsProcessingBoolName = "IsProcessing";

    [Tooltip("Animator bool set while the machine is blocked by invalid runtime contents.")]
    [SerializeField] private string IsBlockedBoolName = "IsBlocked";

    [Tooltip("Animator trigger fired when processing starts.")]
    [SerializeField] private string StartTriggerName = "Start";

    [Tooltip("Animator trigger fired every time one sacrifice ore is consumed.")]
    [SerializeField] private string ConsumeTriggerName = "Consume";

    [Tooltip("Animator trigger fired when processing completes.")]
    [SerializeField] private string CompleteTriggerName = "Complete";

    [Tooltip("Animator trigger fired when processing is cancelled.")]
    [SerializeField] private string CancelTriggerName = "Cancel";

    [Tooltip("Animator trigger fired when the player attempts to start processing with invalid inputs.")]
    [SerializeField] private string ErrorTriggerName = "Error";

    [Header("Events")]
    [Tooltip("Invoked when the machine starts processing.")]
    [SerializeField] private UnityEvent OnProcessingStarted;

    [Tooltip("Invoked every time one sacrifice ore is consumed.")]
    [SerializeField] private UnityEvent OnSacrificeConsumed;

    [Tooltip("Invoked when the machine becomes blocked by invalid runtime contents.")]
    [SerializeField] private UnityEvent OnProcessingBlocked;

    [Tooltip("Invoked when processing completes successfully.")]
    [SerializeField] private UnityEvent OnProcessingCompleted;

    [Tooltip("Invoked when processing is cancelled because the target ore was removed.")]
    [SerializeField] private UnityEvent OnProcessingCancelled;

    [Header("Display Timing")]
    [Tooltip("Seconds that completed or cancelled messages remain on screen before idle preview refreshes again.")]
    [SerializeField] private float StatusHoldDuration = 1.5f;

    [Tooltip("Seconds that button-triggered validation errors remain visible before passive preview refreshes again.")]
    [SerializeField] private float ButtonErrorHoldDuration = 1.25f;

    [Header("Debug")]
    [Tooltip("Logs validation, preview and processing operations.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Current machine state.
    /// </summary>
    private PurityMachineState CurrentState = PurityMachineState.Idle;

    /// <summary>
    /// Target ore captured when processing starts.
    /// </summary>
    private OrePickup CurrentProcessingTarget;

    /// <summary>
    /// Remaining time before the next sacrifice ore is consumed.
    /// </summary>
    private float RemainingConsumeTimer;

    /// <summary>
    /// Amount of sacrifice ores consumed during the current processing run.
    /// </summary>
    private int CurrentConsumedSacrificeCount;

    /// <summary>
    /// Remaining time during which the screen should keep a terminal message instead of refreshing idle preview.
    /// </summary>
    private float StatusHoldTimer;

    /// <summary>
    /// Cached valid target ore list reused by validation.
    /// </summary>
    private readonly List<OrePickup> TargetOres = new();

    /// <summary>
    /// Cached sacrifice ore list reused by validation.
    /// </summary>
    private readonly List<OrePickup> SacrificeOres = new();

    /// <summary>
    /// Cached valid sacrifice ore list reused by validation.
    /// </summary>
    private readonly List<OrePickup> ValidSacrificeOres = new();


    /// <summary>
    /// Resolves optional references and initializes the display.
    /// </summary>
    private void Awake()
    {
        ResolveReferences();
        SetState(PurityMachineState.Idle);

        if (DisplayUI != null)
        {
            DisplayUI.ShowIdle();
        }
    }

    /// <summary>
    /// Updates idle preview or active processing according to the current machine state.
    /// </summary>
    private void Update()
    {
        if (CurrentState == PurityMachineState.Processing || CurrentState == PurityMachineState.Blocked)
        {
            UpdateProcessing();
            return;
        }

        if (StatusHoldTimer > 0f)
        {
            StatusHoldTimer -= Time.deltaTime;
            return;
        }

        RefreshIdlePreview();
    }

    /// <summary>
    /// Called by input zones when tracked ore contents change.
    /// </summary>
    /// <param name="InputZone">Input zone whose contents changed.</param>
    public void NotifyInputZoneContentsChanged(PurityMachineInputZone InputZone)
    {
        if (CurrentState == PurityMachineState.Processing || CurrentState == PurityMachineState.Blocked)
        {
            return;
        }

        RefreshIdlePreview();
    }

    /// <summary>
    /// Attempts to start processing from an external button or debug action.
    /// </summary>
    /// <returns>True when processing started.</returns>
    public bool TryStartProcessing()
    {
        if (CurrentState == PurityMachineState.Processing || CurrentState == PurityMachineState.Blocked)
        {
            ShowProcessingSnapshot(PurityMachineDisplayUI.DisplaySeverity.Processing, "Processing", "Machine is already running.");
            return false;
        }

        if (!EvaluateInputs(InputValidationContext.ButtonPress, out OrePickup TargetOre, out string StatusMessage, out string DetailMessage, out PurityMachineDisplayUI.DisplaySeverity DisplaySeverity))
        {
            SetState(PurityMachineState.Invalid);
            TriggerAnimator(ErrorTriggerName);
            ShowValidationSnapshot(DisplaySeverity, StatusMessage, DetailMessage);
            StatusHoldTimer = Mathf.Max(0f, ButtonErrorHoldDuration);
            return false;
        }

        CurrentProcessingTarget = TargetOre;
        CurrentConsumedSacrificeCount = 0;
        RemainingConsumeTimer = Mathf.Max(0.01f, SacrificeConsumeInterval);
        SetState(PurityMachineState.Processing);
        SetAnimatorBool(IsProcessingBoolName, true);
        SetAnimatorBool(IsBlockedBoolName, false);
        TriggerAnimator(StartTriggerName);
        OnProcessingStarted?.Invoke();
        ShowProcessingSnapshot(PurityMachineDisplayUI.DisplaySeverity.Processing, "Processing", "Absorbing sacrifice purity...");
        Log("Started processing target ore: " + CurrentProcessingTarget.name);
        return true;
    }

    /// <summary>
    /// Cancels the current processing run if one is active.
    /// Already consumed sacrifices remain consumed and already applied purity remains on the target ore.
    /// </summary>
    public void CancelProcessing()
    {
        if (CurrentState != PurityMachineState.Processing && CurrentState != PurityMachineState.Blocked)
        {
            return;
        }

        CancelProcessingInternal("Cancelled", "Processing cancelled.");
    }

    /// <summary>
    /// Gets the current resolved conversion ratio after upgrades.
    /// </summary>
    /// <returns>Resolved conversion ratio.</returns>
    public float GetResolvedConversionRatio()
    {
        float Ratio = Mathf.Max(0f, BaseConversionRatio);

        if (UseUpgradeModifiedValues && UpgradeManager != null)
        {
            Ratio = UpgradeManager.GetModifiedFloatStat(ConversionRatioStat, Ratio);
        }

        return Mathf.Max(0f, Ratio);
    }

    /// <summary>
    /// Gets the current resolved sacrifice capacity after upgrades.
    /// </summary>
    /// <returns>Resolved maximum sacrifice ore count.</returns>
    public int GetResolvedMaxSacrificeOreCount()
    {
        int Capacity = Mathf.Max(0, BaseMaxSacrificeOreCount);

        if (UseUpgradeModifiedValues && UpgradeManager != null)
        {
            Capacity = UpgradeManager.GetModifiedIntStat(SacrificeCapacityStat, Capacity);
        }

        return Mathf.Max(0, Capacity);
    }

    /// <summary>
    /// Refreshes the machine screen while idle, invalid, ready, completed or cancelled.
    /// </summary>
    private void RefreshIdlePreview()
    {
        if (EvaluateInputs(InputValidationContext.PassivePreview, out _, out string StatusMessage, out string DetailMessage, out PurityMachineDisplayUI.DisplaySeverity DisplaySeverity))
        {
            SetState(PurityMachineState.Ready);
            ShowValidationSnapshot(DisplaySeverity, StatusMessage, DetailMessage);
            return;
        }

        SetState(DisplaySeverity == PurityMachineDisplayUI.DisplaySeverity.Neutral ? PurityMachineState.Idle : PurityMachineState.Invalid);
        ShowValidationSnapshot(DisplaySeverity, StatusMessage, DetailMessage);
    }

    /// <summary>
    /// Advances active processing, including runtime blocking, target removal cancellation and sacrifice consumption.
    /// </summary>
    private void UpdateProcessing()
    {
        if (CurrentProcessingTarget == null || TargetInputZone == null || !TargetInputZone.ContainsOre(CurrentProcessingTarget))
        {
            CancelProcessingInternal("Cancelled", "Target removed.");
            return;
        }

        if (!EvaluateInputs(InputValidationContext.Processing, out OrePickup TargetOre, out string StatusMessage, out string DetailMessage, out PurityMachineDisplayUI.DisplaySeverity DisplaySeverity))
        {
            if (CurrentProcessingTarget == null || !TargetInputZone.ContainsOre(CurrentProcessingTarget))
            {
                CancelProcessingInternal("Cancelled", "Target removed.");
                return;
            }

            SetState(PurityMachineState.Blocked);
            SetAnimatorBool(IsProcessingBoolName, true);
            SetAnimatorBool(IsBlockedBoolName, true);
            OnProcessingBlocked?.Invoke();
            ShowValidationSnapshot(DisplaySeverity, StatusMessage, DetailMessage);
            return;
        }

        if (TargetOre != CurrentProcessingTarget)
        {
            CancelProcessingInternal("Cancelled", "Target changed.");
            return;
        }

        if (ValidSacrificeOres.Count <= 0)
        {
            CompleteProcessingInternal();
            return;
        }

        SetState(PurityMachineState.Processing);
        SetAnimatorBool(IsProcessingBoolName, true);
        SetAnimatorBool(IsBlockedBoolName, false);
        ShowProcessingSnapshot(PurityMachineDisplayUI.DisplaySeverity.Processing, "Processing", "Absorbing sacrifice purity...");

        RemainingConsumeTimer -= Time.deltaTime;

        if (RemainingConsumeTimer > 0f)
        {
            return;
        }

        RemainingConsumeTimer = Mathf.Max(0.01f, SacrificeConsumeInterval);
        ConsumeNextSacrificeOre();
    }

    /// <summary>
    /// Consumes one currently valid sacrifice ore and applies its converted purity to the active target.
    /// </summary>
    private void ConsumeNextSacrificeOre()
    {
        if (CurrentProcessingTarget == null || CurrentProcessingTarget.GetOreItemData() == null)
        {
            CancelProcessingInternal("Cancelled", "Target removed.");
            return;
        }

        if (ValidSacrificeOres.Count <= 0)
        {
            CompleteProcessingInternal();
            return;
        }

        OrePickup SacrificeOre = ValidSacrificeOres[0];

        if (SacrificeOre == null || SacrificeOre.GetOreItemData() == null)
        {
            return;
        }

        float ConversionRatio = GetResolvedConversionRatio();
        float PurityGainPercent = SacrificeOre.GetOreItemData().GetPurityPercent() * ConversionRatio;

        if (OreRuntimeService == null)
        {
            OreRuntimeService = FindFirstObjectByType<OreRuntimeService>();
        }

        if (OreRuntimeService != null)
        {
            OreRuntimeService.ApplyPurityMachineGain(CurrentProcessingTarget.GetOreItemData(), PurityGainPercent, true);
        }
        else
        {
            OreItemData TargetData = CurrentProcessingTarget.GetOreItemData();
            TargetData.SetPurityPercent(TargetData.GetPurityPercent() + PurityGainPercent);
            TargetData.SetHasBeenPurityProcessed(true);
        }

        CurrentProcessingTarget.RefreshPurityProcessedVisualState();
        CurrentConsumedSacrificeCount++;
        TriggerAnimator(ConsumeTriggerName);
        OnSacrificeConsumed?.Invoke();
        Log("Consumed sacrifice ore " + SacrificeOre.name + " for +" + PurityGainPercent.ToString("0.##") + "% purity.");
        ConsumeOrePickup(SacrificeOre);

        if (!EvaluateInputs(InputValidationContext.Processing, out _, out _, out _, out _))
        {
            if (CurrentProcessingTarget == null || !TargetInputZone.ContainsOre(CurrentProcessingTarget))
            {
                CancelProcessingInternal("Cancelled", "Target removed.");
                return;
            }
        }

        if (ValidSacrificeOres.Count <= 0)
        {
            CompleteProcessingInternal();
        }
    }

    /// <summary>
    /// Evaluates the current machine inputs and populates the cached target and sacrifice lists.
    /// </summary>
    /// <param name="ValidationContext">Context that controls whether delayed player-facing errors are shown now.</param>
    /// <param name="TargetOre">Resolved target ore.</param>
    /// <param name="StatusMessage">Validation status message.</param>
    /// <param name="DetailMessage">Validation detail message.</param>
    /// <param name="DisplaySeverity">Visual severity sent to the display panel.</param>
    /// <returns>True when the machine can process with the current inputs.</returns>
    private bool EvaluateInputs(
        InputValidationContext ValidationContext,
        out OrePickup TargetOre,
        out string StatusMessage,
        out string DetailMessage,
        out PurityMachineDisplayUI.DisplaySeverity DisplaySeverity)
    {
        TargetOre = null;
        StatusMessage = "Idle";
        DetailMessage = "Insert one target ore and sacrifice ores.";
        DisplaySeverity = PurityMachineDisplayUI.DisplaySeverity.Neutral;

        TargetOres.Clear();
        SacrificeOres.Clear();
        ValidSacrificeOres.Clear();

        if (TargetInputZone != null)
        {
            TargetInputZone.AppendValidOrePickups(TargetOres);
        }

        if (SacrificeInputZone != null)
        {
            SacrificeInputZone.AppendValidOrePickups(SacrificeOres);
        }

        if (TargetOres.Count <= 0)
        {
            return false;
        }

        if (TargetOres.Count > 1)
        {
            if (ValidationContext == InputValidationContext.PassivePreview)
            {
                StatusMessage = "Idle";
                DetailMessage = "Multiple target ores detected.";
                DisplaySeverity = PurityMachineDisplayUI.DisplaySeverity.Neutral;
                return false;
            }

            StatusMessage = "Too many target ores";
            DetailMessage = "Only one target ore allowed.";
            DisplaySeverity = PurityMachineDisplayUI.DisplaySeverity.Error;
            return false;
        }

        TargetOre = TargetOres[0];
        OreItemData TargetData = TargetOre != null ? TargetOre.GetOreItemData() : null;

        if (TargetOre == null || TargetData == null)
        {
            StatusMessage = "Invalid target ore";
            DetailMessage = "Target ore has no runtime data.";
            DisplaySeverity = PurityMachineDisplayUI.DisplaySeverity.Error;
            return false;
        }

        bool IsCurrentProcessingTarget = CurrentProcessingTarget != null && TargetOre == CurrentProcessingTarget;
        bool AllowCurrentProcessedTarget = ValidationContext == InputValidationContext.Processing;

        if (TargetData.GetHasBeenPurityProcessed() && (!AllowCurrentProcessedTarget || !IsCurrentProcessingTarget))
        {
            StatusMessage = "Target already processed";
            DetailMessage = "Processed ores cannot be improved again.";
            DisplaySeverity = PurityMachineDisplayUI.DisplaySeverity.Error;
            return false;
        }

        OreDefinition TargetDefinition = TargetData.GetOreDefinition();

        if (TargetDefinition == null)
        {
            StatusMessage = "Invalid target ore";
            DetailMessage = "Target ore definition is missing.";
            DisplaySeverity = PurityMachineDisplayUI.DisplaySeverity.Error;
            return false;
        }

        for (int Index = 0; Index < SacrificeOres.Count; Index++)
        {
            OrePickup SacrificeOre = SacrificeOres[Index];

            if (SacrificeOre == null || SacrificeOre.GetOreItemData() == null)
            {
                continue;
            }

            if (SacrificeOre == TargetOre)
            {
                StatusMessage = "Target cannot be sacrificed";
                DetailMessage = "Move the target ore out of the sacrifice input.";
                DisplaySeverity = PurityMachineDisplayUI.DisplaySeverity.Error;
                return false;
            }

            OreItemData SacrificeData = SacrificeOre.GetOreItemData();

            if (!AreOreDefinitionsEquivalent(TargetDefinition, SacrificeData.GetOreDefinition()))
            {
                StatusMessage = "Sacrifice type mismatch";
                DetailMessage = "Sacrifice ores must match the target ore type.";
                DisplaySeverity = PurityMachineDisplayUI.DisplaySeverity.Error;
                return false;
            }

            if (SacrificeData.GetHasBeenPurityProcessed() && !AllowProcessedOresAsSacrifice)
            {
                StatusMessage = "Processed sacrifice not allowed";
                DetailMessage = "Remove processed sacrifice ores.";
                DisplaySeverity = PurityMachineDisplayUI.DisplaySeverity.Error;
                return false;
            }

            ValidSacrificeOres.Add(SacrificeOre);
        }

        int MaxSacrificeCount = GetResolvedMaxSacrificeOreCount();

        if (ValidSacrificeOres.Count > MaxSacrificeCount)
        {
            StatusMessage = "Too many sacrifice ores";
            DetailMessage = "Capacity: " + MaxSacrificeCount + " | Current: " + ValidSacrificeOres.Count;
            DisplaySeverity = PurityMachineDisplayUI.DisplaySeverity.Error;
            return false;
        }

        if (ValidSacrificeOres.Count <= 0)
        {
            StatusMessage = "Idle";
            DetailMessage = "Insert at least one sacrifice ore.";
            DisplaySeverity = PurityMachineDisplayUI.DisplaySeverity.Neutral;
            return false;
        }

        StatusMessage = "Ready";
        DetailMessage = "Press the machine button to start.";
        DisplaySeverity = PurityMachineDisplayUI.DisplaySeverity.Ready;
        return true;
    }

    /// <summary>
    /// Returns whether two ore definitions represent the same sacrifice-compatible mineral type.
    /// Ore id matching is preferred so duplicated asset references still behave correctly when their stable id is the same.
    /// </summary>
    /// <param name="TargetDefinition">Target ore definition.</param>
    /// <param name="SacrificeDefinition">Sacrifice ore definition.</param>
    /// <returns>True when both definitions represent the same ore type.</returns>
    private bool AreOreDefinitionsEquivalent(OreDefinition TargetDefinition, OreDefinition SacrificeDefinition)
    {
        if (TargetDefinition == null || SacrificeDefinition == null)
        {
            return false;
        }

        if (TargetDefinition == SacrificeDefinition)
        {
            return true;
        }

        string TargetOreId = TargetDefinition.GetOreId();
        string SacrificeOreId = SacrificeDefinition.GetOreId();

        if (string.IsNullOrWhiteSpace(TargetOreId) || string.IsNullOrWhiteSpace(SacrificeOreId))
        {
            return false;
        }

        return string.Equals(TargetOreId, SacrificeOreId, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds and displays a snapshot from the current validation lists.
    /// </summary>
    /// <param name="Severity">Display severity.</param>
    /// <param name="StatusMessage">Main status message.</param>
    /// <param name="DetailMessage">Detail status message.</param>
    private void ShowValidationSnapshot(PurityMachineDisplayUI.DisplaySeverity Severity, string StatusMessage, string DetailMessage)
    {
        if (DisplayUI == null)
        {
            return;
        }

        string TargetSummary = BuildTargetSummary();
        string SacrificeSummary = BuildSacrificeSummary();
        string PreviewSummary = BuildPreviewSummary();
        DisplayUI.ShowState(Severity, StatusMessage, DetailMessage, TargetSummary, SacrificeSummary, PreviewSummary);
    }

    /// <summary>
    /// Displays processing information while the machine is running.
    /// </summary>
    /// <param name="Severity">Display severity.</param>
    /// <param name="StatusMessage">Main status message.</param>
    /// <param name="DetailMessage">Detail status message.</param>
    private void ShowProcessingSnapshot(PurityMachineDisplayUI.DisplaySeverity Severity, string StatusMessage, string DetailMessage)
    {
        if (DisplayUI == null)
        {
            return;
        }

        string TargetSummary = BuildTargetSummary();
        string SacrificeSummary = BuildSacrificeSummary() + " | Consumed: " + CurrentConsumedSacrificeCount;
        string PreviewSummary = BuildPreviewSummary();
        DisplayUI.ShowState(Severity, StatusMessage, DetailMessage, TargetSummary, SacrificeSummary, PreviewSummary);
    }

    /// <summary>
    /// Builds the target ore summary text.
    /// </summary>
    /// <returns>Target summary string.</returns>
    private string BuildTargetSummary()
    {
        if (TargetOres.Count > 1)
        {
            return "Targets: " + TargetOres.Count;
        }

        OrePickup TargetOre = TargetOres.Count == 1 ? TargetOres[0] : CurrentProcessingTarget;
        OreItemData TargetData = TargetOre != null ? TargetOre.GetOreItemData() : null;

        if (TargetData == null || TargetData.GetOreDefinition() == null)
        {
            return "Target: -";
        }

        string OreName = TargetData.GetOreDefinition().GetDisplayName();
        return "Target: " + OreName + " | " + FormatPurity(TargetData.GetPurityPercent());
    }

    /// <summary>
    /// Builds the sacrifice ore summary text.
    /// </summary>
    /// <returns>Sacrifice summary string.</returns>
    private string BuildSacrificeSummary()
    {
        int VisibleSacrificeCount = SacrificeOres.Count > 0 ? SacrificeOres.Count : ValidSacrificeOres.Count;
        return "Sacrifices: " + VisibleSacrificeCount + " / " + GetResolvedMaxSacrificeOreCount();
    }

    /// <summary>
    /// Builds the current preview text.
    /// </summary>
    /// <returns>Preview summary string.</returns>
    private string BuildPreviewSummary()
    {
        OrePickup TargetOre = TargetOres.Count == 1 ? TargetOres[0] : CurrentProcessingTarget;
        OreItemData TargetData = TargetOre != null ? TargetOre.GetOreItemData() : null;

        if (TargetData == null)
        {
            return "Preview: -";
        }

        float PreviewPurity = ResolvePreviewFinalPurity(TargetData.GetPurityPercent());
        return "Preview: " + FormatPurity(TargetData.GetPurityPercent()) + " -> " + FormatPurity(PreviewPurity);
    }

    /// <summary>
    /// Resolves the final purity preview from the current sacrifice list.
    /// </summary>
    /// <param name="StartingPurityPercent">Target starting purity percent.</param>
    /// <returns>Predicted final purity percent.</returns>
    private float ResolvePreviewFinalPurity(float StartingPurityPercent)
    {
        float FinalPurity = Mathf.Clamp(StartingPurityPercent, 0f, 100f);
        float ConversionRatio = GetResolvedConversionRatio();

        for (int Index = 0; Index < ValidSacrificeOres.Count; Index++)
        {
            OrePickup SacrificeOre = ValidSacrificeOres[Index];

            if (SacrificeOre == null || SacrificeOre.GetOreItemData() == null)
            {
                continue;
            }

            FinalPurity += SacrificeOre.GetOreItemData().GetPurityPercent() * ConversionRatio;
        }

        return Mathf.Clamp(FinalPurity, 0f, 100f);
    }

    /// <summary>
    /// Permanently consumes one sacrifice ore pickup.
    /// </summary>
    /// <param name="OrePickup">Ore pickup to consume.</param>
    private void ConsumeOrePickup(OrePickup OrePickup)
    {
        if (OrePickup == null)
        {
            return;
        }

        if (SacrificeInputZone != null)
        {
            SacrificeInputZone.ForgetOre(OrePickup);
        }

        if (TargetInputZone != null)
        {
            TargetInputZone.ForgetOre(OrePickup);
        }

        if (OrePickup.ReturnToPool())
        {
            return;
        }

        Transform RuntimeRoot = OrePickup.GetRuntimeRoot();
        Destroy(RuntimeRoot != null ? RuntimeRoot.gameObject : OrePickup.gameObject);
    }

    /// <summary>
    /// Completes the current processing run.
    /// </summary>
    private void CompleteProcessingInternal()
    {
        SetState(PurityMachineState.Completed);
        SetAnimatorBool(IsProcessingBoolName, false);
        SetAnimatorBool(IsBlockedBoolName, false);
        TriggerAnimator(CompleteTriggerName);
        OnProcessingCompleted?.Invoke();
        ShowValidationSnapshot(PurityMachineDisplayUI.DisplaySeverity.Complete, "Completed", "Purity absorption complete.");
        StatusHoldTimer = Mathf.Max(0f, StatusHoldDuration);
        Log("Processing completed. Sacrifices consumed: " + CurrentConsumedSacrificeCount);
        CurrentProcessingTarget = null;
        CurrentConsumedSacrificeCount = 0;
    }

    /// <summary>
    /// Cancels the current processing run.
    /// </summary>
    /// <param name="StatusMessage">Main status message.</param>
    /// <param name="DetailMessage">Detail status message.</param>
    private void CancelProcessingInternal(string StatusMessage, string DetailMessage)
    {
        SetState(PurityMachineState.Cancelled);
        SetAnimatorBool(IsProcessingBoolName, false);
        SetAnimatorBool(IsBlockedBoolName, false);
        TriggerAnimator(CancelTriggerName);
        OnProcessingCancelled?.Invoke();
        ShowValidationSnapshot(PurityMachineDisplayUI.DisplaySeverity.Warning, StatusMessage, DetailMessage);
        StatusHoldTimer = Mathf.Max(0f, StatusHoldDuration);
        Log("Processing cancelled: " + DetailMessage);
        CurrentProcessingTarget = null;
        CurrentConsumedSacrificeCount = 0;
    }

    /// <summary>
    /// Resolves optional scene references.
    /// </summary>
    private void ResolveReferences()
    {
        if (OreRuntimeService == null)
        {
            OreRuntimeService = FindFirstObjectByType<OreRuntimeService>();
        }

        if (UpgradeManager == null)
        {
            UpgradeManager = FindFirstObjectByType<UpgradeManager>();
        }

        if (MachineAnimator == null)
        {
            MachineAnimator = GetComponentInChildren<Animator>(true);
        }
    }

    /// <summary>
    /// Sets the current high-level machine state.
    /// </summary>
    /// <param name="NewState">New state.</param>
    private void SetState(PurityMachineState NewState)
    {
        CurrentState = NewState;
    }

    /// <summary>
    /// Sets an animator bool if the animator and parameter name are valid.
    /// </summary>
    /// <param name="ParameterName">Animator bool parameter.</param>
    /// <param name="Value">Value to assign.</param>
    private void SetAnimatorBool(string ParameterName, bool Value)
    {
        if (MachineAnimator == null || string.IsNullOrWhiteSpace(ParameterName))
        {
            return;
        }

        MachineAnimator.SetBool(ParameterName, Value);
    }

    /// <summary>
    /// Fires an animator trigger if the animator and parameter name are valid.
    /// </summary>
    /// <param name="TriggerName">Animator trigger parameter.</param>
    private void TriggerAnimator(string TriggerName)
    {
        if (MachineAnimator == null || string.IsNullOrWhiteSpace(TriggerName))
        {
            return;
        }

        MachineAnimator.SetTrigger(TriggerName);
    }

    /// <summary>
    /// Formats purity values through the display when available.
    /// </summary>
    /// <param name="PurityPercent">Purity percent to format.</param>
    /// <returns>Formatted purity value.</returns>
    private string FormatPurity(float PurityPercent)
    {
        if (DisplayUI != null)
        {
            return DisplayUI.FormatPurity(PurityPercent);
        }

        return Mathf.Clamp(PurityPercent, 0f, 100f).ToString("0.#") + "%";
    }

    /// <summary>
    /// Keeps inspector values valid.
    /// </summary>
    private void OnValidate()
    {
        BaseConversionRatio = Mathf.Max(0f, BaseConversionRatio);
        BaseMaxSacrificeOreCount = Mathf.Max(0, BaseMaxSacrificeOreCount);
        SacrificeConsumeInterval = Mathf.Max(0.01f, SacrificeConsumeInterval);
        StatusHoldDuration = Mathf.Max(0f, StatusHoldDuration);
        ButtonErrorHoldDuration = Mathf.Max(0f, ButtonErrorHoldDuration);
    }

    /// <summary>
    /// Debug helper used from the inspector to start processing.
    /// </summary>
    [ContextMenu("Start Purity Processing")]
    private void DebugStartProcessing()
    {
        TryStartProcessing();
    }

    /// <summary>
    /// Logs controller messages when debug logging is enabled.
    /// </summary>
    /// <param name="Message">Message to write.</param>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[PurityMachineController] " + Message, this);
    }
}
