using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Fixed-position details tooltip for the researcher skill tree.
/// It displays title, description, costs, ore progress, state and the research activation button for the selected node.
/// </summary>
[DisallowMultipleComponent]
public sealed class ResearchSkillTreeTooltipUI : MonoBehaviour
{
    [Header("Root")]
    [Tooltip("Root object enabled while the tooltip is visible. If empty, this GameObject is used.")]
    [SerializeField] private GameObject TooltipRoot;

    [Tooltip("Canvas group faded when showing or hiding the tooltip.")]
    [SerializeField] private CanvasGroup TooltipCanvasGroup;

    [Tooltip("Rect transform animated when showing or hiding the tooltip.")]
    [SerializeField] private RectTransform AnimatedRoot;

    [Header("References")]
    [Tooltip("Image used to display the selected research icon.")]
    [SerializeField] private Image IconImage;

    [Tooltip("Text used to display the selected research title.")]
    [SerializeField] private TMP_Text TitleText;

    [Tooltip("Text used to display the selected research description.")]
    [SerializeField] private TMP_Text DescriptionText;

    [Tooltip("Text used to display current research state.")]
    [SerializeField] private TMP_Text StateText;

    [Tooltip("Text used to display activation credit cost.")]
    [SerializeField] private TMP_Text CreditCostText;

    [Tooltip("Text used to display ore requirements and processed progress.")]
    [SerializeField] private TMP_Text RequirementsText;

    [Tooltip("Button used to start or resume the selected research.")]
    [SerializeField] private Button ResearchButton;

    [Tooltip("Text label inside the research button.")]
    [SerializeField] private TMP_Text ResearchButtonText;

    [Header("Requirement Text")]
    [Tooltip("Label shown when an ore requirement references an ore type that has not been discovered by the scanner yet.")]
    [SerializeField] private string UnknownOreRequirementLabel = "???";

    [Tooltip("If true, undiscovered ore requirements hide amount and filters. If false, only the ore name is hidden.")]
    [SerializeField] private bool HideUnknownOreRequirementDetails = true;

    [Tooltip("Color used when a requirement is completed.")]
    [SerializeField] private Color CompletedRequirementColor = new Color(0.55f, 1f, 0.55f, 1f);

    [Tooltip("Color used when a requirement is missing or incomplete.")]
    [SerializeField] private Color MissingRequirementColor = new Color(1f, 0.45f, 0.45f, 1f);

    [Tooltip("Color used when a requirement is hidden because the ore is unknown.")]
    [SerializeField] private Color UnknownRequirementColor = new Color(1f, 0.25f, 0.25f, 1f);

    [Header("Animation")]
    [Tooltip("Unscaled seconds used by the tooltip show and hide animation.")]
    [SerializeField] private float FadeDuration = 0.16f;

    [Tooltip("Local anchored offset used when the tooltip is hidden before sliding into place.")]
    [SerializeField] private Vector2 HiddenAnchoredOffset = new Vector2(24f, 0f);

    [Tooltip("Curve used by tooltip fade and slide animation.")]
    [SerializeField] private AnimationCurve FadeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("If true, selecting a different visible node replays the open animation. If false, the tooltip swaps content in place without sliding from its hidden offset again.")]
    [SerializeField] private bool ReplayShowAnimationWhenAlreadyVisible = false;

    [Header("Debug")]
    [Tooltip("Logs tooltip activation flow.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Skill tree view that owns this tooltip.
    /// </summary>
    private ResearchSkillTreeViewUI OwnerView;

    /// <summary>
    /// Station used to query and activate research state.
    /// </summary>
    private ResearchStation OwnerStation;

    /// <summary>
    /// Node currently displayed by the tooltip.
    /// </summary>
    private ResearchSkillTreeNodeUI CurrentNode;

    /// <summary>
    /// Reusable builder for requirement text.
    /// </summary>
    private readonly StringBuilder TextBuilder = new();

    /// <summary>
    /// Shown anchored position captured from the tooltip rect transform.
    /// </summary>
    private Vector2 ShownAnchoredPosition;

    /// <summary>
    /// Coroutine currently animating the tooltip.
    /// </summary>
    private Coroutine FadeRoutine;

    /// <summary>
    /// True after the shown anchored position has been captured.
    /// </summary>
    private bool HasCapturedShownPosition;

    /// <summary>
    /// True while the tooltip is intended to be visible.
    /// </summary>
    private bool IsVisible;

    /// <summary>
    /// Caches references and starts hidden.
    /// </summary>
    private void Awake()
    {
        if (TooltipRoot == null)
        {
            TooltipRoot = gameObject;
        }

        if (TooltipCanvasGroup == null)
        {
            TooltipCanvasGroup = GetComponent<CanvasGroup>();
        }

        if (AnimatedRoot == null)
        {
            AnimatedRoot = transform as RectTransform;
        }

        CaptureShownPosition();
        BindButton();
        Hide(true);
    }

    /// <summary>
    /// Unbinds button events.
    /// </summary>
    private void OnDestroy()
    {
        if (ResearchButton != null)
        {
            ResearchButton.onClick.RemoveListener(HandleResearchButtonClicked);
        }
    }

    /// <summary>
    /// Shows this tooltip for a selected skill tree node.
    /// </summary>
    /// <param name="View">Owning skill tree view.</param>
    /// <param name="Station">Research station used to query runtime state.</param>
    /// <param name="Node">Selected research node.</param>
    public void Show(ResearchSkillTreeViewUI View, ResearchStation Station, ResearchSkillTreeNodeUI Node)
    {
        bool WasAlreadyVisible = IsVisible && TooltipRoot != null && TooltipRoot.activeSelf;

        OwnerView = View;
        OwnerStation = Station;
        CurrentNode = Node;

        CaptureShownPosition();
        RefreshView();

        if (WasAlreadyVisible && !ReplayShowAnimationWhenAlreadyVisible)
        {
            ApplyVisibilityInstant(true);
            return;
        }

        PlayVisibilityAnimation(true, false);
    }

    /// <summary>
    /// Hides this tooltip.
    /// </summary>
    /// <param name="Immediate">If true, visibility is applied instantly.</param>
    public void Hide(bool Immediate)
    {
        CurrentNode = null;
        PlayVisibilityAnimation(false, Immediate);
    }

    /// <summary>
    /// Refreshes all tooltip fields using the current selected node.
    /// </summary>
    public void RefreshView()
    {
        if (CurrentNode == null || CurrentNode.GetResearchDefinition() == null)
        {
            return;
        }

        ResearchDefinition Definition = CurrentNode.GetResearchDefinition();
        ResearchRuntimeService.ResearchViewState ViewState = ResolveViewState(Definition);
        ResearchRuntimeService.ResearchBlockReason BlockReason = ResolveBlockReason(Definition);

        if (IconImage != null)
        {
            Sprite Icon = Definition.GetIcon();
            IconImage.sprite = Icon;
            IconImage.enabled = Icon != null;
        }

        if (TitleText != null)
        {
            TitleText.text = Definition.GetDisplayName();
        }

        if (DescriptionText != null)
        {
            DescriptionText.text = Definition.GetDescription();
        }

        if (StateText != null)
        {
            StateText.text = BuildStateText(BlockReason, ViewState);
        }

        if (CreditCostText != null)
        {
            CreditCostText.text = BuildCreditCostText(Definition, BlockReason, ViewState);
        }

        if (RequirementsText != null)
        {
            RequirementsText.text = BuildRequirementsText(Definition);
        }

        RefreshButton(Definition, BlockReason, ViewState);
    }

    /// <summary>
    /// Handles the research button click.
    /// </summary>
    private void HandleResearchButtonClicked()
    {
        if (OwnerView == null)
        {
            return;
        }

        bool Activated = OwnerView.TryActivateSelectedResearch();
        RefreshView();
        Log(Activated ? "Research button activated selected research." : "Research button failed to activate selected research.");
    }

    /// <summary>
    /// Refreshes research button state and label.
    /// </summary>
    /// <param name="Definition">Selected research definition.</param>
    /// <param name="BlockReason">Current activation block reason.</param>
    /// <param name="ViewState">Current view state.</param>
    private void RefreshButton(ResearchDefinition Definition, ResearchRuntimeService.ResearchBlockReason BlockReason, ResearchRuntimeService.ResearchViewState ViewState)
    {
        if (ResearchButton == null)
        {
            return;
        }

        bool CanActivate = BlockReason == ResearchRuntimeService.ResearchBlockReason.None &&
                           ViewState != ResearchRuntimeService.ResearchViewState.Completed &&
                           ViewState != ResearchRuntimeService.ResearchViewState.Active;

        ResearchButton.interactable = CanActivate;

        if (ResearchButtonText == null)
        {
            return;
        }

        if (ViewState == ResearchRuntimeService.ResearchViewState.Completed)
        {
            ResearchButtonText.text = "Completed";
        }
        else if (ViewState == ResearchRuntimeService.ResearchViewState.Active)
        {
            ResearchButtonText.text = "Researching";
        }
        else if (ViewState == ResearchRuntimeService.ResearchViewState.PaidInactive)
        {
            ResearchButtonText.text = "Resume";
        }
        else if (BlockReason == ResearchRuntimeService.ResearchBlockReason.NotEnoughCredits)
        {
            ResearchButtonText.text = "Missing Credits";
        }
        else if (BlockReason == ResearchRuntimeService.ResearchBlockReason.MissingDiscoveredOreRequirement)
        {
            ResearchButtonText.text = "Scan Required";
        }
        else if (BlockReason == ResearchRuntimeService.ResearchBlockReason.MissingResearchTier)
        {
            ResearchButtonText.text = "Tier Locked";
        }
        else if (BlockReason != ResearchRuntimeService.ResearchBlockReason.None)
        {
            ResearchButtonText.text = "Locked";
        }
        else
        {
            ResearchButtonText.text = "Research";
        }
    }

    /// <summary>
    /// Builds the activation credit cost label.
    /// </summary>
    /// <param name="Definition">Selected research definition.</param>
    /// <param name="BlockReason">Current activation block reason.</param>
    /// <param name="ViewState">Current view state.</param>
    private string BuildCreditCostText(ResearchDefinition Definition, ResearchRuntimeService.ResearchBlockReason BlockReason, ResearchRuntimeService.ResearchViewState ViewState)
    {
        if (Definition == null)
        {
            return "Cost: -";
        }

        if (ViewState == ResearchRuntimeService.ResearchViewState.Active ||
            ViewState == ResearchRuntimeService.ResearchViewState.PaidInactive ||
            ViewState == ResearchRuntimeService.ResearchViewState.Completed)
        {
            return "Activation Cost: Paid";
        }

        string Label = "Activation Cost: " + Definition.GetCreditCost().ToString("0.##") + " C";
        return BlockReason == ResearchRuntimeService.ResearchBlockReason.NotEnoughCredits
            ? Colorize(Label, MissingRequirementColor)
            : Label;
    }

    /// <summary>
    /// Builds the requirements and processed progress label.
    /// </summary>
    /// <param name="Definition">Selected research definition.</param>
    private string BuildRequirementsText(ResearchDefinition Definition)
    {
        if (OwnerStation == null || Definition == null)
        {
            return "Ores: -";
        }

        List<ResearchRuntimeService.OreRequirementProgress> ProgressEntries = OwnerStation.GetOreRequirementProgress(Definition);

        if (ProgressEntries.Count <= 0)
        {
            return "Ores: None";
        }

        TextBuilder.Clear();
        TextBuilder.Append("Ores:");

        for (int Index = 0; Index < ProgressEntries.Count; Index++)
        {
            ResearchRuntimeService.OreRequirementProgress Progress = ProgressEntries[Index];
            ResearchDefinition.OreRequirement Requirement = Progress.Requirement;

            if (Requirement == null)
            {
                continue;
            }

            bool IsKnown = OwnerStation.IsOreRequirementDiscovered(Requirement);
            bool IsSatisfied = Progress.IsSatisfied();
            string RequirementLabel;

            if (!IsKnown && HideUnknownOreRequirementDetails)
            {
                RequirementLabel = UnknownOreRequirementLabel;
            }
            else if (!IsKnown)
            {
                RequirementLabel = UnknownOreRequirementLabel + " x" + Requirement.GetAmount();
            }
            else
            {
                RequirementLabel = Requirement.BuildDisplayRequirementLabel();
            }

            string ProgressLabel = IsKnown ? " (" + Progress.ProcessedAmount + "/" + Progress.RequiredAmount + ")" : string.Empty;
            Color LabelColor = !IsKnown ? UnknownRequirementColor : IsSatisfied ? CompletedRequirementColor : MissingRequirementColor;

            TextBuilder.Append("\n- ");
            TextBuilder.Append(Colorize(RequirementLabel + ProgressLabel, LabelColor));
        }

        return TextBuilder.ToString();
    }

    /// <summary>
    /// Builds the current high-level state text.
    /// </summary>
    /// <param name="BlockReason">Current activation block reason.</param>
    /// <param name="ViewState">Current view state.</param>
    private string BuildStateText(ResearchRuntimeService.ResearchBlockReason BlockReason, ResearchRuntimeService.ResearchViewState ViewState)
    {
        switch (ViewState)
        {
            case ResearchRuntimeService.ResearchViewState.Completed:
                return "State: Completed";
            case ResearchRuntimeService.ResearchViewState.Active:
                return "State: Active Research";
            case ResearchRuntimeService.ResearchViewState.PaidInactive:
                return "State: Paid, inactive";
            case ResearchRuntimeService.ResearchViewState.Available:
                if (BlockReason == ResearchRuntimeService.ResearchBlockReason.MissingDiscoveredOreRequirement)
                {
                    return "State: Available - scan required ores";
                }

                if (BlockReason == ResearchRuntimeService.ResearchBlockReason.NotEnoughCredits)
                {
                    return "State: Available - missing credits";
                }

                return "State: Available";
            case ResearchRuntimeService.ResearchViewState.Locked:
                return "State: Locked - " + BuildBlockReasonText(BlockReason);
            default:
                return "State: Invalid";
        }
    }

    /// <summary>
    /// Builds a readable label for a block reason.
    /// </summary>
    /// <param name="BlockReason">Block reason resolved by the runtime service.</param>
    private string BuildBlockReasonText(ResearchRuntimeService.ResearchBlockReason BlockReason)
    {
        switch (BlockReason)
        {
            case ResearchRuntimeService.ResearchBlockReason.NotEnoughCredits:
                return "not enough credits";
            case ResearchRuntimeService.ResearchBlockReason.MissingFeatureFlag:
                return "missing feature";
            case ResearchRuntimeService.ResearchBlockReason.MissingPrerequisite:
                return "missing prerequisite";
            case ResearchRuntimeService.ResearchBlockReason.MissingDiscoveredOreRequirement:
                return "unknown ore";
            case ResearchRuntimeService.ResearchBlockReason.MissingResearchTier:
                return "missing research tier";
            case ResearchRuntimeService.ResearchBlockReason.AlreadyCompleted:
                return "completed";
            case ResearchRuntimeService.ResearchBlockReason.None:
                return "none";
            default:
                return BlockReason.ToString();
        }
    }

    /// <summary>
    /// Resolves current view state for a research definition.
    /// </summary>
    /// <param name="Definition">Research definition being evaluated.</param>
    private ResearchRuntimeService.ResearchViewState ResolveViewState(ResearchDefinition Definition)
    {
        return OwnerStation != null ? OwnerStation.GetResearchViewState(Definition) : ResearchRuntimeService.ResearchViewState.Invalid;
    }

    /// <summary>
    /// Resolves current block reason for a research definition.
    /// </summary>
    /// <param name="Definition">Research definition being evaluated.</param>
    private ResearchRuntimeService.ResearchBlockReason ResolveBlockReason(ResearchDefinition Definition)
    {
        return OwnerStation != null ? OwnerStation.GetResearchBlockReason(Definition) : ResearchRuntimeService.ResearchBlockReason.MissingResearch;
    }

    /// <summary>
    /// Wraps text in a TMP rich-text color tag.
    /// </summary>
    /// <param name="Text">Text being colored.</param>
    /// <param name="ColorValue">Color applied to the text.</param>
    private string Colorize(string Text, Color ColorValue)
    {
        return "<color=#" + ColorUtility.ToHtmlStringRGBA(ColorValue) + ">" + Text + "</color>";
    }

    /// <summary>
    /// Binds the research button.
    /// </summary>
    private void BindButton()
    {
        if (ResearchButton == null)
        {
            return;
        }

        ResearchButton.onClick.RemoveListener(HandleResearchButtonClicked);
        ResearchButton.onClick.AddListener(HandleResearchButtonClicked);
    }

    /// <summary>
    /// Captures the shown tooltip position once.
    /// </summary>
    private void CaptureShownPosition()
    {
        if (HasCapturedShownPosition || AnimatedRoot == null)
        {
            return;
        }

        ShownAnchoredPosition = AnimatedRoot.anchoredPosition;
        HasCapturedShownPosition = true;
    }

    /// <summary>
    /// Plays or applies tooltip visibility animation.
    /// </summary>
    /// <param name="ShowTooltip">True to show, false to hide.</param>
    /// <param name="Immediate">If true, visibility changes instantly.</param>
    private void PlayVisibilityAnimation(bool ShowTooltip, bool Immediate)
    {
        if (TooltipRoot == null)
        {
            return;
        }

        IsVisible = ShowTooltip;

        if (FadeRoutine != null)
        {
            StopCoroutine(FadeRoutine);
            FadeRoutine = null;
        }

        if (Immediate || !gameObject.activeInHierarchy)
        {
            ApplyVisibilityInstant(ShowTooltip);
            return;
        }

        if (ShowTooltip)
        {
            TooltipRoot.SetActive(true);
        }

        FadeRoutine = StartCoroutine(AnimateVisibility(ShowTooltip));
    }

    /// <summary>
    /// Applies tooltip visibility without animation.
    /// </summary>
    /// <param name="ShowTooltip">True to show, false to hide.</param>
    private void ApplyVisibilityInstant(bool ShowTooltip)
    {
        IsVisible = ShowTooltip;
        TooltipRoot.SetActive(ShowTooltip);

        if (TooltipCanvasGroup != null)
        {
            TooltipCanvasGroup.alpha = ShowTooltip ? 1f : 0f;
            TooltipCanvasGroup.interactable = ShowTooltip;
            TooltipCanvasGroup.blocksRaycasts = ShowTooltip;
        }

        if (AnimatedRoot != null)
        {
            AnimatedRoot.anchoredPosition = ShowTooltip ? ShownAnchoredPosition : ShownAnchoredPosition + HiddenAnchoredOffset;
        }
    }

    /// <summary>
    /// Animates tooltip fade and slide.
    /// </summary>
    /// <param name="ShowTooltip">True to show, false to hide.</param>
    private IEnumerator AnimateVisibility(bool ShowTooltip)
    {
        float Duration = Mathf.Max(0.01f, FadeDuration);
        float Elapsed = 0f;
        float StartAlpha = TooltipCanvasGroup != null ? TooltipCanvasGroup.alpha : ShowTooltip ? 0f : 1f;
        float TargetAlpha = ShowTooltip ? 1f : 0f;
        Vector2 StartPosition = AnimatedRoot != null ? AnimatedRoot.anchoredPosition : ShownAnchoredPosition;
        Vector2 TargetPosition = ShowTooltip ? ShownAnchoredPosition : ShownAnchoredPosition + HiddenAnchoredOffset;

        if (TooltipCanvasGroup != null)
        {
            TooltipCanvasGroup.interactable = ShowTooltip;
            TooltipCanvasGroup.blocksRaycasts = ShowTooltip;
        }

        while (Elapsed < Duration)
        {
            Elapsed += Time.unscaledDeltaTime;
            float NormalizedTime = Mathf.Clamp01(Elapsed / Duration);
            float EvaluatedTime = FadeCurve != null ? FadeCurve.Evaluate(NormalizedTime) : NormalizedTime;

            if (TooltipCanvasGroup != null)
            {
                TooltipCanvasGroup.alpha = Mathf.Lerp(StartAlpha, TargetAlpha, EvaluatedTime);
            }

            if (AnimatedRoot != null)
            {
                AnimatedRoot.anchoredPosition = Vector2.LerpUnclamped(StartPosition, TargetPosition, EvaluatedTime);
            }

            yield return null;
        }

        ApplyVisibilityInstant(ShowTooltip);
        FadeRoutine = null;
    }

    /// <summary>
    /// Writes a tooltip-specific debug message.
    /// </summary>
    /// <param name="Message">Message written to the Unity console.</param>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[ResearchSkillTreeTooltipUI] " + Message, this);
    }
}
