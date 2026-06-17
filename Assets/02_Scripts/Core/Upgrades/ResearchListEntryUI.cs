using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manual UI entry used to display and activate one research definition.
/// Ore progress is processed by the owning ResearchStation while the research is active.
/// Unknown ore requirements are resolved through scanner knowledge before activation is allowed.
/// </summary>
public sealed class ResearchListEntryUI : MonoBehaviour
{
    [Header("Data")]
    [Tooltip("Research definition manually assigned to this entry.")]
    [SerializeField] private ResearchDefinition ResearchDefinition;

    [Header("References")]
    [Tooltip("Icon image used to display research artwork.")]
    [SerializeField] private Image IconImage;

    [Tooltip("Text used to display the research name.")]
    [SerializeField] private TMP_Text NameText;

    [Tooltip("Text used to display the research description.")]
    [SerializeField] private TMP_Text DescriptionText;

    [Tooltip("Text used to display the activation credit cost.")]
    [SerializeField] private TMP_Text CreditCostText;

    [Tooltip("Text used to display required ores and processed progress.")]
    [SerializeField] private TMP_Text OreRequirementsText;

    [Tooltip("Text used to display current research state.")]
    [SerializeField] private TMP_Text StateText;

    [Tooltip("Button used to activate this research.")]
    [SerializeField] private Button ActivateButton;

    [Tooltip("Optional text label inside the activate button.")]
    [SerializeField] private TMP_Text ActivateButtonText;

    [Header("Unknown Requirements")]
    [Tooltip("Label shown when an ore requirement references an ore type that has not been discovered by the scanner yet.")]
    [SerializeField] private string UnknownOreRequirementLabel = "???";

    [Tooltip("If true, undiscovered ore requirements hide their required amount and filters. If false, only the ore name is hidden.")]
    [SerializeField] private bool HideUnknownOreRequirementDetails = true;

    [Tooltip("If true, descriptions are replaced by Locked Description when the research is hard-locked by feature flags or prerequisites.")]
    [SerializeField] private bool HideDescriptionWhenHardLocked = false;

    [Tooltip("Description text shown while Hide Description When Hard Locked is true and this research is hard-locked.")]
    [TextArea]
    [SerializeField] private string LockedDescription = "Locked research.";

    [Header("Colors")]
    [Tooltip("Color used when research can be activated.")]
    [SerializeField] private Color AvailableColor = Color.white;

    [Tooltip("Color used when research is blocked.")]
    [SerializeField] private Color BlockedColor = new Color(1f, 0.55f, 0.55f, 1f);

    [Tooltip("Color used when research is currently active.")]
    [SerializeField] private Color ActiveColor = new Color(0.55f, 0.75f, 1f, 1f);

    [Tooltip("Color used when research is already completed.")]
    [SerializeField] private Color CompletedColor = new Color(0.55f, 1f, 0.55f, 1f);

    [Tooltip("Color used for unknown ore requirements shown as question marks.")]
    [SerializeField] private Color UnknownRequirementColor = new Color(1f, 0.25f, 0.25f, 1f);

    [Tooltip("Color used for known but incomplete ore or credit requirements.")]
    [SerializeField] private Color MissingRequirementColor = new Color(1f, 0.45f, 0.45f, 1f);

    [Tooltip("Color used for completed ore requirements.")]
    [SerializeField] private Color CompletedRequirementColor = new Color(0.55f, 1f, 0.55f, 1f);

    /// <summary>
    /// Station used to execute research activation.
    /// </summary>
    private ResearchStation OwnerStation;

    /// <summary>
    /// Reusable string builder for requirement text.
    /// </summary>
    private readonly StringBuilder RequirementsBuilder = new();

    /// <summary>
    /// Initializes this entry with the owning research station.
    /// </summary>
    /// <param name="Station">Research station that owns this entry.</param>
    public void Initialize(ResearchStation Station)
    {
        OwnerStation = Station;

        if (OwnerStation != null)
        {
            OwnerStation.RegisterResearchDefinition(ResearchDefinition);
        }

        EnableRichText();

        if (ActivateButton != null)
        {
            ActivateButton.onClick.RemoveListener(HandleActivateButtonClicked);
            ActivateButton.onClick.AddListener(HandleActivateButtonClicked);
        }

        RefreshView();
    }

    /// <summary>
    /// Gets the research definition assigned to this manual UI entry.
    /// </summary>
    public ResearchDefinition GetResearchDefinition()
    {
        return ResearchDefinition;
    }

    /// <summary>
    /// Refreshes all visual fields.
    /// </summary>
    public void RefreshView()
    {
        if (ResearchDefinition == null)
        {
            return;
        }

        EnableRichText();

        ResearchRuntimeService.ResearchBlockReason BlockReason = OwnerStation != null
            ? OwnerStation.GetResearchBlockReason(ResearchDefinition)
            : ResearchRuntimeService.ResearchBlockReason.MissingResearch;

        ResearchRuntimeService.ResearchViewState ViewState = OwnerStation != null
            ? OwnerStation.GetResearchViewState(ResearchDefinition)
            : ResearchRuntimeService.ResearchViewState.Invalid;

        bool CanActivate = BlockReason == ResearchRuntimeService.ResearchBlockReason.None &&
                           ViewState != ResearchRuntimeService.ResearchViewState.Completed &&
                           ViewState != ResearchRuntimeService.ResearchViewState.Active;

        if (IconImage != null)
        {
            Sprite Icon = ResearchDefinition.GetIcon();
            IconImage.sprite = Icon;
            IconImage.enabled = Icon != null;
        }

        if (NameText != null)
        {
            NameText.text = ResearchDefinition.GetDisplayName();
        }

        if (DescriptionText != null)
        {
            DescriptionText.text = ResolveDescriptionText(BlockReason);
        }

        if (CreditCostText != null)
        {
            CreditCostText.text = BuildCreditCostText(BlockReason, ViewState);
        }

        if (OreRequirementsText != null)
        {
            OreRequirementsText.text = BuildOreRequirementsText();
        }

        if (StateText != null)
        {
            StateText.text = BuildStateText(BlockReason, ViewState);
            StateText.color = ResolveStateColor(ViewState, CanActivate);
        }

        if (ActivateButton != null)
        {
            ActivateButton.interactable = CanActivate;
        }

        if (ActivateButtonText != null)
        {
            ActivateButtonText.text = ViewState == ResearchRuntimeService.ResearchViewState.PaidInactive ? "Resume" : "Activate";
        }
    }

    /// <summary>
    /// Handles the activate button click.
    /// </summary>
    private void HandleActivateButtonClicked()
    {
        if (OwnerStation == null || ResearchDefinition == null)
        {
            return;
        }

        OwnerStation.TryActivateResearch(ResearchDefinition);
        RefreshView();
    }

    /// <summary>
    /// Builds the credit cost label, coloring it when credits are the current blocker.
    /// </summary>
    /// <param name="BlockReason">Current activation block reason.</param>
    /// <param name="ViewState">Current research view state.</param>
    private string BuildCreditCostText(ResearchRuntimeService.ResearchBlockReason BlockReason, ResearchRuntimeService.ResearchViewState ViewState)
    {
        if (ViewState == ResearchRuntimeService.ResearchViewState.Active ||
            ViewState == ResearchRuntimeService.ResearchViewState.PaidInactive ||
            ViewState == ResearchRuntimeService.ResearchViewState.Completed)
        {
            return "Activation Cost: Paid";
        }

        string Label = "Activation Cost: " + ResearchDefinition.GetCreditCost().ToString("0.00") + " C";
        return BlockReason == ResearchRuntimeService.ResearchBlockReason.NotEnoughCredits ? Colorize(Label, MissingRequirementColor) : Label;
    }

    /// <summary>
    /// Builds the ore requirement text using processed station progress and scanner discovery state.
    /// </summary>
    private string BuildOreRequirementsText()
    {
        if (OwnerStation == null || ResearchDefinition == null)
        {
            return "Ores: -";
        }

        List<ResearchRuntimeService.OreRequirementProgress> ProgressEntries = OwnerStation.GetOreRequirementProgress(ResearchDefinition);

        if (ProgressEntries.Count <= 0)
        {
            return "Ores: None";
        }

        RequirementsBuilder.Clear();
        RequirementsBuilder.Append("Ores:");

        for (int Index = 0; Index < ProgressEntries.Count; Index++)
        {
            ResearchRuntimeService.OreRequirementProgress Progress = ProgressEntries[Index];
            ResearchDefinition.OreRequirement Requirement = Progress.Requirement;
            bool IsRequirementKnown = OwnerStation.IsOreRequirementDiscovered(Requirement);
            bool IsRequirementSatisfied = Progress.IsSatisfied();

            RequirementsBuilder.Append('\n');

            if (!IsRequirementKnown)
            {
                RequirementsBuilder.Append(BuildUnknownRequirementText(Progress));
                continue;
            }

            string OreLabel = Requirement != null ? Requirement.BuildDisplayRequirementLabel() : "Missing Ore";
            string ProgressLabel = Progress.ProcessedAmount + "/" + Progress.RequiredAmount;
            string FullLabel = OreLabel + " -> " + ProgressLabel;

            RequirementsBuilder.Append(IsRequirementSatisfied
                ? Colorize(FullLabel, CompletedRequirementColor)
                : Colorize(FullLabel, MissingRequirementColor));
        }

        return RequirementsBuilder.ToString();
    }

    /// <summary>
    /// Builds the hidden label shown for one undiscovered ore requirement.
    /// </summary>
    /// <param name="Progress">Progress entry associated with the hidden requirement.</param>
    private string BuildUnknownRequirementText(ResearchRuntimeService.OreRequirementProgress Progress)
    {
        string SafeLabel = string.IsNullOrWhiteSpace(UnknownOreRequirementLabel) ? "???" : UnknownOreRequirementLabel;

        if (HideUnknownOreRequirementDetails)
        {
            return Colorize(SafeLabel, UnknownRequirementColor);
        }

        return Colorize(SafeLabel + " x" + Progress.RequiredAmount, UnknownRequirementColor);
    }

    /// <summary>
    /// Resolves the description text, optionally hiding it for hard-locked research entries.
    /// </summary>
    /// <param name="BlockReason">Current activation block reason.</param>
    private string ResolveDescriptionText(ResearchRuntimeService.ResearchBlockReason BlockReason)
    {
        if (HideDescriptionWhenHardLocked && IsHardLockedByProgression(BlockReason))
        {
            return string.IsNullOrWhiteSpace(LockedDescription) ? "Locked research." : LockedDescription;
        }

        return ResearchDefinition.GetDescription();
    }

    /// <summary>
    /// Builds a compact state label for the current block reason and view state.
    /// </summary>
    private string BuildStateText(ResearchRuntimeService.ResearchBlockReason BlockReason, ResearchRuntimeService.ResearchViewState ViewState)
    {
        switch (ViewState)
        {
            case ResearchRuntimeService.ResearchViewState.Completed:
                return "Completed";
            case ResearchRuntimeService.ResearchViewState.Active:
                return "Active";
            case ResearchRuntimeService.ResearchViewState.PaidInactive:
                return "Paid - inactive";
            case ResearchRuntimeService.ResearchViewState.Available:
                return "Available";
        }

        switch (BlockReason)
        {
            case ResearchRuntimeService.ResearchBlockReason.NotEnoughCredits:
                return "Not enough credits";
            case ResearchRuntimeService.ResearchBlockReason.MissingDiscoveredOreRequirement:
                return "Unknown requirement";
            case ResearchRuntimeService.ResearchBlockReason.MissingScannerRuntimeService:
                return "Missing scanner service";
            case ResearchRuntimeService.ResearchBlockReason.MissingFeatureFlag:
                return "Locked";
            case ResearchRuntimeService.ResearchBlockReason.MissingPrerequisite:
                return "Prerequisite missing";
            case ResearchRuntimeService.ResearchBlockReason.AppliedUpgradeNotRegistered:
                return "Upgrade not registered";
            case ResearchRuntimeService.ResearchBlockReason.MissingAppliedUpgrade:
                return "Missing result upgrade";
            case ResearchRuntimeService.ResearchBlockReason.MissingWallet:
                return "Missing wallet";
            case ResearchRuntimeService.ResearchBlockReason.MissingUpgradeManager:
                return "Missing upgrade manager";
            case ResearchRuntimeService.ResearchBlockReason.MissingResearchId:
                return "Missing research id";
            default:
                return BlockReason.ToString();
        }
    }

    /// <summary>
    /// Resolves the visual color used by the state text.
    /// </summary>
    private Color ResolveStateColor(ResearchRuntimeService.ResearchViewState ViewState, bool CanActivate)
    {
        switch (ViewState)
        {
            case ResearchRuntimeService.ResearchViewState.Completed:
                return CompletedColor;
            case ResearchRuntimeService.ResearchViewState.Active:
                return ActiveColor;
            case ResearchRuntimeService.ResearchViewState.Available:
            case ResearchRuntimeService.ResearchViewState.PaidInactive:
                return CanActivate ? AvailableColor : BlockedColor;
            default:
                return BlockedColor;
        }
    }

    /// <summary>
    /// Returns whether a block reason should hide the full description when configured to do so.
    /// </summary>
    /// <param name="BlockReason">Activation block reason to evaluate.</param>
    private bool IsHardLockedByProgression(ResearchRuntimeService.ResearchBlockReason BlockReason)
    {
        return BlockReason == ResearchRuntimeService.ResearchBlockReason.MissingFeatureFlag ||
               BlockReason == ResearchRuntimeService.ResearchBlockReason.MissingPrerequisite;
    }

    /// <summary>
    /// Wraps text in a TMP rich text color tag.
    /// </summary>
    /// <param name="Text">Text to wrap.</param>
    /// <param name="ColorValue">Color to apply.</param>
    private string Colorize(string Text, Color ColorValue)
    {
        string Hex = ColorUtility.ToHtmlStringRGBA(ColorValue);
        return "<color=#" + Hex + ">" + Text + "</color>";
    }

    /// <summary>
    /// Ensures optional TMP fields can render colored requirement labels.
    /// </summary>
    private void EnableRichText()
    {
        if (CreditCostText != null)
        {
            CreditCostText.richText = true;
        }

        if (OreRequirementsText != null)
        {
            OreRequirementsText.richText = true;
        }
    }
}
