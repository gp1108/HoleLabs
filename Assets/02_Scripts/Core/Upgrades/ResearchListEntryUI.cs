using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manual UI entry used to display and activate one research definition.
/// Ore progress is processed by the owning ResearchStation while the research is active.
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

    [Header("Colors")]
    [Tooltip("Color used when research can be activated.")]
    [SerializeField] private Color AvailableColor = Color.white;

    [Tooltip("Color used when research is blocked.")]
    [SerializeField] private Color BlockedColor = new Color(1f, 0.55f, 0.55f, 1f);

    [Tooltip("Color used when research is currently active.")]
    [SerializeField] private Color ActiveColor = new Color(0.55f, 0.75f, 1f, 1f);

    [Tooltip("Color used when research is already completed.")]
    [SerializeField] private Color CompletedColor = new Color(0.55f, 1f, 0.55f, 1f);

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
    public void Initialize(ResearchStation Station)
    {
        OwnerStation = Station;

        if (ActivateButton != null)
        {
            ActivateButton.onClick.RemoveListener(HandleActivateButtonClicked);
            ActivateButton.onClick.AddListener(HandleActivateButtonClicked);
        }

        RefreshView();
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

        ResearchStation.ResearchBlockReason BlockReason = OwnerStation != null
            ? OwnerStation.GetResearchBlockReason(ResearchDefinition)
            : ResearchStation.ResearchBlockReason.MissingResearch;

        ResearchStation.ResearchViewState ViewState = OwnerStation != null
            ? OwnerStation.GetResearchViewState(ResearchDefinition)
            : ResearchStation.ResearchViewState.Invalid;

        bool CanActivate = BlockReason == ResearchStation.ResearchBlockReason.None &&
                           ViewState != ResearchStation.ResearchViewState.Completed &&
                           ViewState != ResearchStation.ResearchViewState.Active;

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
            DescriptionText.text = ResearchDefinition.GetDescription();
        }

        if (CreditCostText != null)
        {
            CreditCostText.text = "Activation Cost: " + ResearchDefinition.GetCreditCost().ToString("0.00") + " C";
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
            ActivateButtonText.text = ViewState == ResearchStation.ResearchViewState.PaidInactive ? "Resume" : "Activate";
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
    /// Builds the ore requirement text using processed station progress.
    /// </summary>
    private string BuildOreRequirementsText()
    {
        if (OwnerStation == null || ResearchDefinition == null)
        {
            return "Ores: -";
        }

        List<ResearchStation.OreRequirementProgress> ProgressEntries = OwnerStation.GetOreRequirementProgress(ResearchDefinition);

        if (ProgressEntries.Count <= 0)
        {
            return "Ores: None";
        }

        RequirementsBuilder.Clear();
        RequirementsBuilder.Append("Ores:");

        for (int Index = 0; Index < ProgressEntries.Count; Index++)
        {
            ResearchStation.OreRequirementProgress Progress = ProgressEntries[Index];
            string OreLabel = Progress.Requirement != null ? Progress.Requirement.BuildDisplayRequirementLabel() : "Missing Ore";

            RequirementsBuilder.Append('\n');
            RequirementsBuilder.Append(OreLabel);
            RequirementsBuilder.Append(" -> ");
            RequirementsBuilder.Append(Progress.ProcessedAmount);
            RequirementsBuilder.Append("/");
            RequirementsBuilder.Append(Progress.RequiredAmount);
        }

        return RequirementsBuilder.ToString();
    }

    /// <summary>
    /// Builds a compact state label for the current block reason and view state.
    /// </summary>
    private string BuildStateText(ResearchStation.ResearchBlockReason BlockReason, ResearchStation.ResearchViewState ViewState)
    {
        switch (ViewState)
        {
            case ResearchStation.ResearchViewState.Completed:
                return "Completed";
            case ResearchStation.ResearchViewState.Active:
                return "Active";
            case ResearchStation.ResearchViewState.PaidInactive:
                return "Paid - inactive";
            case ResearchStation.ResearchViewState.Available:
                return "Available";
        }

        switch (BlockReason)
        {
            case ResearchStation.ResearchBlockReason.NotEnoughCredits:
                return "Not enough credits";
            case ResearchStation.ResearchBlockReason.MissingFeatureFlag:
                return "Locked";
            case ResearchStation.ResearchBlockReason.MissingPrerequisite:
                return "Prerequisite missing";
            case ResearchStation.ResearchBlockReason.AppliedUpgradeNotRegistered:
                return "Upgrade not registered";
            case ResearchStation.ResearchBlockReason.MissingAppliedUpgrade:
                return "Missing result upgrade";
            case ResearchStation.ResearchBlockReason.MissingWallet:
                return "Missing wallet";
            case ResearchStation.ResearchBlockReason.MissingUpgradeManager:
                return "Missing upgrade manager";
            case ResearchStation.ResearchBlockReason.MissingResearchId:
                return "Missing research id";
            default:
                return BlockReason.ToString();
        }
    }

    /// <summary>
    /// Resolves the visual color used by the state text.
    /// </summary>
    private Color ResolveStateColor(ResearchStation.ResearchViewState ViewState, bool CanActivate)
    {
        switch (ViewState)
        {
            case ResearchStation.ResearchViewState.Completed:
                return CompletedColor;
            case ResearchStation.ResearchViewState.Active:
                return ActiveColor;
            case ResearchStation.ResearchViewState.Available:
            case ResearchStation.ResearchViewState.PaidInactive:
                return CanActivate ? AvailableColor : BlockedColor;
            default:
                return BlockedColor;
        }
    }
}
