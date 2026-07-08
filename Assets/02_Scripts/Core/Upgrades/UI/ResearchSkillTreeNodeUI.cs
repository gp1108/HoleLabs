using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Visual and interactive UI node for one ResearchDefinition inside the researcher skill tree.
/// It does not own research state; it only renders the state provided by ResearchRuntimeService.
/// </summary>
[DisallowMultipleComponent]
public sealed class ResearchSkillTreeNodeUI : MonoBehaviour
{
    [Header("Data")]
    [Tooltip("Research definition represented by this skill tree node.")]
    [SerializeField] private ResearchDefinition ResearchDefinition;

    [Tooltip("Optional visual set used to resolve default state sprites and colors.")]
    [SerializeField] private ResearchSkillTreeVisualSet VisualSet;

    [Header("References")]
    [Tooltip("Button that receives clicks on this node. Keep this button interactable even when locked so locked clicks can close the tooltip.")]
    [SerializeField] private Button SelectionButton;

    [Tooltip("Root rect transform scaled during selection and completion feedback.")]
    [SerializeField] private RectTransform AnimatedRoot;

    [Tooltip("Image used for the circular node frame.")]
    [SerializeField] private Image FrameImage;

    [Tooltip("Image used for the research icon.")]
    [SerializeField] private Image IconImage;

    [Tooltip("Image used for the research name plate.")]
    [SerializeField] private Image TitlePlateImage;

    [Tooltip("Text used to display the research name.")]
    [SerializeField] private TMP_Text NameText;

    [Tooltip("Optional text used to display the current state for debugging or temporary UI work.")]
    [SerializeField] private TMP_Text StateText;

    [Tooltip("Image enabled while this research is locked.")]
    [SerializeField] private Image LockImage;

    [Tooltip("Image enabled while this research is the active research.")]
    [SerializeField] private Image ActiveMarkerImage;

    [Tooltip("Image enabled while this node is selected.")]
    [SerializeField] private Image SelectionRimImage;

    [Tooltip("Optional canvas group used for a completion pulse overlay.")]
    [SerializeField] private CanvasGroup CompletionPulseCanvasGroup;

    [Header("Icon Overrides")]
    [Tooltip("Icon used while the research is currently active. If empty, the ResearchDefinition icon is used.")]
    [SerializeField] private Sprite ActiveIconOverride;

    [Tooltip("Icon used after the research has been completed. If empty, the ResearchDefinition icon is used.")]
    [SerializeField] private Sprite CompletedIconOverride;

    [Header("Frame Overrides")]
    [Tooltip("Node frame sprite used while locked. If empty, the visual set is used.")]
    [SerializeField] private Sprite LockedFrameOverride;

    [Tooltip("Node frame sprite used while available or paid inactive. If empty, the visual set is used.")]
    [SerializeField] private Sprite AvailableFrameOverride;

    [Tooltip("Node frame sprite used while active. If empty, Available Frame Override or the visual set is used.")]
    [SerializeField] private Sprite ActiveFrameOverride;

    [Tooltip("Node frame sprite used while completed. If empty, the visual set is used.")]
    [SerializeField] private Sprite CompletedFrameOverride;

    [Header("Title Plate Overrides")]
    [Tooltip("Title plate sprite used while locked. If empty, the visual set is used.")]
    [SerializeField] private Sprite LockedTitlePlateOverride;

    [Tooltip("Title plate sprite used while available or paid inactive. If empty, the visual set is used.")]
    [SerializeField] private Sprite AvailableTitlePlateOverride;

    [Tooltip("Title plate sprite used while active. If empty, Available Title Plate Override or the visual set is used.")]
    [SerializeField] private Sprite ActiveTitlePlateOverride;

    [Tooltip("Title plate sprite used while completed. If empty, the visual set is used.")]
    [SerializeField] private Sprite CompletedTitlePlateOverride;

    [Header("Interaction")]
    [Tooltip("If true, locked nodes can be clicked but will close the tooltip instead of opening it.")]
    [SerializeField] private bool CloseTooltipWhenLockedNodeClicked = true;

    [Tooltip("If true, the node button remains interactable for every state so background click handling is deterministic.")]
    [SerializeField] private bool KeepButtonInteractable = true;

    [Header("Feedback")]
    [Tooltip("Scale applied at the peak of the selection pulse.")]
    [SerializeField] private float SelectionPulseScale = 1.08f;

    [Tooltip("Scale applied at the peak of the activation pulse.")]
    [SerializeField] private float ActivationPulseScale = 1.12f;

    [Tooltip("Scale applied at the peak of the completion pulse.")]
    [SerializeField] private float CompletionPulseScale = 1.18f;

    [Tooltip("Unscaled seconds used by short selection and activation pulses.")]
    [SerializeField] private float ShortPulseDuration = 0.16f;

    [Tooltip("Unscaled seconds used by the completion pulse.")]
    [SerializeField] private float CompletionPulseDuration = 0.34f;

    [Header("Events")]
    [Tooltip("Invoked when this node becomes selected. Use it for audio or particles.")]
    [SerializeField] private UnityEvent OnSelectedFeedback = new UnityEvent();

    [Tooltip("Invoked when this node successfully starts or resumes research.")]
    [SerializeField] private UnityEvent OnActivatedFeedback = new UnityEvent();

    [Tooltip("Invoked when this node changes into the completed state after initialization.")]
    [SerializeField] private UnityEvent OnCompletedFeedback = new UnityEvent();

    /// <summary>
    /// Fired when this node button is clicked.
    /// </summary>
    public event Action<ResearchSkillTreeNodeUI> OnNodeClicked;

    /// <summary>
    /// Station used to query research state.
    /// </summary>
    private ResearchStation OwnerStation;

    /// <summary>
    /// Last state rendered by this node.
    /// </summary>
    private ResearchRuntimeService.ResearchViewState LastViewState = ResearchRuntimeService.ResearchViewState.Invalid;

    /// <summary>
    /// True after the first state refresh has been applied.
    /// </summary>
    private bool HasRenderedInitialState;

    /// <summary>
    /// Coroutine currently animating the node scale.
    /// </summary>
    private Coroutine PulseRoutine;

    /// <summary>
    /// Caches references and binds click events.
    /// </summary>
    private void Awake()
    {
        if (AnimatedRoot == null)
        {
            AnimatedRoot = transform as RectTransform;
        }

        if (SelectionButton == null)
        {
            SelectionButton = GetComponent<Button>();
        }

        if (SelectionButton != null)
        {
            SelectionButton.onClick.RemoveListener(HandleSelectionButtonClicked);
            SelectionButton.onClick.AddListener(HandleSelectionButtonClicked);
        }

        SetSelected(false);
        SetCompletionPulseAlpha(0f);
    }

    /// <summary>
    /// Unbinds click events.
    /// </summary>
    private void OnDestroy()
    {
        if (SelectionButton != null)
        {
            SelectionButton.onClick.RemoveListener(HandleSelectionButtonClicked);
        }
    }

    /// <summary>
    /// Initializes this node with the owning research station.
    /// </summary>
    /// <param name="Station">Research station used to query runtime state.</param>
    public void Initialize(ResearchStation Station)
    {
        OwnerStation = Station;
    }

    /// <summary>
    /// Gets the research definition represented by this node.
    /// </summary>
    public ResearchDefinition GetResearchDefinition()
    {
        return ResearchDefinition;
    }

    /// <summary>
    /// Gets the current resolved view state for this node.
    /// </summary>
    public ResearchRuntimeService.ResearchViewState GetViewState()
    {
        if (OwnerStation == null || ResearchDefinition == null)
        {
            return ResearchRuntimeService.ResearchViewState.Invalid;
        }

        return OwnerStation.GetResearchViewState(ResearchDefinition);
    }

    /// <summary>
    /// Returns whether this node should open the details tooltip when clicked.
    /// </summary>
    public bool CanOpenTooltip()
    {
        ResearchRuntimeService.ResearchViewState ViewState = GetViewState();
        return ViewState == ResearchRuntimeService.ResearchViewState.Available ||
               ViewState == ResearchRuntimeService.ResearchViewState.PaidInactive ||
               ViewState == ResearchRuntimeService.ResearchViewState.Active ||
               ViewState == ResearchRuntimeService.ResearchViewState.Completed;
    }

    /// <summary>
    /// Returns whether a locked click should close the active tooltip.
    /// </summary>
    public bool ShouldCloseTooltipWhenClicked()
    {
        return CloseTooltipWhenLockedNodeClicked && !CanOpenTooltip();
    }

    /// <summary>
    /// Refreshes the complete visual state of this node.
    /// </summary>
    /// <param name="PlayStateFeedback">If true, state transition feedback can be played.</param>
    public void RefreshView(bool PlayStateFeedback)
    {
        ResearchRuntimeService.ResearchViewState ViewState = GetViewState();
        bool BecameCompleted = HasRenderedInitialState && LastViewState != ViewState && ViewState == ResearchRuntimeService.ResearchViewState.Completed;

        ApplyFrame(ViewState);
        ApplyIcon(ViewState);
        ApplyTitlePlate(ViewState);
        ApplyText(ViewState);
        ApplyStateMarkers(ViewState);
        ApplyButtonState(ViewState);

        LastViewState = ViewState;
        HasRenderedInitialState = true;

        if (PlayStateFeedback && BecameCompleted)
        {
            PlayCompletionFeedback();
        }
    }

    /// <summary>
    /// Enables or disables the selected rim around this node.
    /// </summary>
    /// <param name="IsSelected">True when this node is the current details selection.</param>
    public void SetSelected(bool IsSelected)
    {
        if (SelectionRimImage != null)
        {
            SelectionRimImage.enabled = IsSelected;
        }

        if (IsSelected)
        {
            OnSelectedFeedback?.Invoke();
            PlayScalePulse(SelectionPulseScale, ShortPulseDuration);
        }
    }

    /// <summary>
    /// Plays feedback used when research activation succeeds from the tooltip button.
    /// </summary>
    public void PlayActivationFeedback()
    {
        OnActivatedFeedback?.Invoke();
        PlayScalePulse(ActivationPulseScale, ShortPulseDuration);
    }

    /// <summary>
    /// Plays feedback used when this research becomes completed.
    /// </summary>
    public void PlayCompletionFeedback()
    {
        OnCompletedFeedback?.Invoke();
        PlayScalePulse(CompletionPulseScale, CompletionPulseDuration);

        if (CompletionPulseCanvasGroup != null)
        {
            StartCoroutine(AnimateCompletionPulse());
        }
    }

    /// <summary>
    /// Handles the Unity UI button click.
    /// </summary>
    private void HandleSelectionButtonClicked()
    {
        OnNodeClicked?.Invoke(this);
    }

    /// <summary>
    /// Applies the correct node frame sprite and tint.
    /// </summary>
    /// <param name="ViewState">Current runtime view state.</param>
    private void ApplyFrame(ResearchRuntimeService.ResearchViewState ViewState)
    {
        if (FrameImage == null)
        {
            return;
        }

        Sprite FrameSprite = ResolveFrameSprite(ViewState);

        if (FrameSprite != null)
        {
            FrameImage.sprite = FrameSprite;
            FrameImage.enabled = true;
        }

        if (VisualSet != null)
        {
            FrameImage.color = VisualSet.GetStateColor(ViewState);
        }
    }

    /// <summary>
    /// Applies the correct research icon sprite.
    /// </summary>
    /// <param name="ViewState">Current runtime view state.</param>
    private void ApplyIcon(ResearchRuntimeService.ResearchViewState ViewState)
    {
        if (IconImage == null)
        {
            return;
        }

        Sprite IconSprite = ResearchDefinition != null ? ResearchDefinition.GetIcon() : null;

        if (ViewState == ResearchRuntimeService.ResearchViewState.Active && ActiveIconOverride != null)
        {
            IconSprite = ActiveIconOverride;
        }
        else if (ViewState == ResearchRuntimeService.ResearchViewState.Completed && CompletedIconOverride != null)
        {
            IconSprite = CompletedIconOverride;
        }

        IconImage.sprite = IconSprite;
        IconImage.enabled = IconSprite != null;

        if (VisualSet != null)
        {
            IconImage.color = ViewState == ResearchRuntimeService.ResearchViewState.Locked
                ? VisualSet.GetStateColor(ViewState)
                : Color.white;
        }
    }

    /// <summary>
    /// Applies the correct title plate sprite and tint.
    /// </summary>
    /// <param name="ViewState">Current runtime view state.</param>
    private void ApplyTitlePlate(ResearchRuntimeService.ResearchViewState ViewState)
    {
        if (TitlePlateImage == null)
        {
            return;
        }

        Sprite TitleSprite = ResolveTitlePlateSprite(ViewState);

        if (TitleSprite != null)
        {
            TitlePlateImage.sprite = TitleSprite;
            TitlePlateImage.enabled = true;
        }

        if (VisualSet != null)
        {
            TitlePlateImage.color = VisualSet.GetStateColor(ViewState);
        }
    }

    /// <summary>
    /// Applies name and optional state debug text.
    /// </summary>
    /// <param name="ViewState">Current runtime view state.</param>
    private void ApplyText(ResearchRuntimeService.ResearchViewState ViewState)
    {
        if (NameText != null)
        {
            NameText.text = ResearchDefinition != null ? ResearchDefinition.GetDisplayName() : "Missing Research";

            if (VisualSet != null)
            {
                NameText.color = VisualSet.GetTextColor(ViewState);
            }
        }

        if (StateText != null)
        {
            StateText.text = ViewState.ToString();
        }
    }

    /// <summary>
    /// Applies lock and active state overlays.
    /// </summary>
    /// <param name="ViewState">Current runtime view state.</param>
    private void ApplyStateMarkers(ResearchRuntimeService.ResearchViewState ViewState)
    {
        if (LockImage != null)
        {
            LockImage.enabled = ViewState == ResearchRuntimeService.ResearchViewState.Locked ||
                                ViewState == ResearchRuntimeService.ResearchViewState.Invalid;
        }

        if (ActiveMarkerImage != null)
        {
            ActiveMarkerImage.enabled = ViewState == ResearchRuntimeService.ResearchViewState.Active;
        }
    }

    /// <summary>
    /// Applies the interactable state of the node button.
    /// </summary>
    /// <param name="ViewState">Current runtime view state.</param>
    private void ApplyButtonState(ResearchRuntimeService.ResearchViewState ViewState)
    {
        if (SelectionButton == null)
        {
            return;
        }

        SelectionButton.interactable = KeepButtonInteractable || ViewState != ResearchRuntimeService.ResearchViewState.Invalid;
    }

    /// <summary>
    /// Resolves the frame sprite for a state, allowing per-node overrides.
    /// </summary>
    /// <param name="ViewState">Current runtime view state.</param>
    private Sprite ResolveFrameSprite(ResearchRuntimeService.ResearchViewState ViewState)
    {
        switch (ViewState)
        {
            case ResearchRuntimeService.ResearchViewState.Locked:
                return LockedFrameOverride != null ? LockedFrameOverride : VisualSet != null ? VisualSet.GetNodeFrameSprite(ViewState) : null;
            case ResearchRuntimeService.ResearchViewState.Available:
            case ResearchRuntimeService.ResearchViewState.PaidInactive:
                return AvailableFrameOverride != null ? AvailableFrameOverride : VisualSet != null ? VisualSet.GetNodeFrameSprite(ViewState) : null;
            case ResearchRuntimeService.ResearchViewState.Active:
                return ActiveFrameOverride != null ? ActiveFrameOverride : AvailableFrameOverride != null ? AvailableFrameOverride : VisualSet != null ? VisualSet.GetNodeFrameSprite(ViewState) : null;
            case ResearchRuntimeService.ResearchViewState.Completed:
                return CompletedFrameOverride != null ? CompletedFrameOverride : VisualSet != null ? VisualSet.GetNodeFrameSprite(ViewState) : null;
            default:
                return VisualSet != null ? VisualSet.GetNodeFrameSprite(ViewState) : null;
        }
    }

    /// <summary>
    /// Resolves the title plate sprite for a state, allowing per-node overrides.
    /// </summary>
    /// <param name="ViewState">Current runtime view state.</param>
    private Sprite ResolveTitlePlateSprite(ResearchRuntimeService.ResearchViewState ViewState)
    {
        switch (ViewState)
        {
            case ResearchRuntimeService.ResearchViewState.Locked:
                return LockedTitlePlateOverride != null ? LockedTitlePlateOverride : VisualSet != null ? VisualSet.GetTitlePlateSprite(ViewState) : null;
            case ResearchRuntimeService.ResearchViewState.Available:
            case ResearchRuntimeService.ResearchViewState.PaidInactive:
                return AvailableTitlePlateOverride != null ? AvailableTitlePlateOverride : VisualSet != null ? VisualSet.GetTitlePlateSprite(ViewState) : null;
            case ResearchRuntimeService.ResearchViewState.Active:
                return ActiveTitlePlateOverride != null ? ActiveTitlePlateOverride : AvailableTitlePlateOverride != null ? AvailableTitlePlateOverride : VisualSet != null ? VisualSet.GetTitlePlateSprite(ViewState) : null;
            case ResearchRuntimeService.ResearchViewState.Completed:
                return CompletedTitlePlateOverride != null ? CompletedTitlePlateOverride : VisualSet != null ? VisualSet.GetTitlePlateSprite(ViewState) : null;
            default:
                return VisualSet != null ? VisualSet.GetTitlePlateSprite(ViewState) : null;
        }
    }

    /// <summary>
    /// Plays a scale pulse without relying on external tweening packages.
    /// </summary>
    /// <param name="TargetScale">Peak local scale multiplier.</param>
    /// <param name="Duration">Unscaled animation duration.</param>
    private void PlayScalePulse(float TargetScale, float Duration)
    {
        if (AnimatedRoot == null || !gameObject.activeInHierarchy)
        {
            return;
        }

        if (PulseRoutine != null)
        {
            StopCoroutine(PulseRoutine);
        }

        PulseRoutine = StartCoroutine(AnimateScalePulse(Mathf.Max(1f, TargetScale), Mathf.Max(0.01f, Duration)));
    }

    /// <summary>
    /// Animates node scale up and back to one.
    /// </summary>
    /// <param name="TargetScale">Peak local scale multiplier.</param>
    /// <param name="Duration">Unscaled animation duration.</param>
    private IEnumerator AnimateScalePulse(float TargetScale, float Duration)
    {
        float Elapsed = 0f;
        Vector3 BaseScale = Vector3.one;
        Vector3 PeakScale = Vector3.one * TargetScale;

        while (Elapsed < Duration)
        {
            Elapsed += Time.unscaledDeltaTime;
            float NormalizedTime = Mathf.Clamp01(Elapsed / Duration);
            float PingPong = NormalizedTime <= 0.5f ? NormalizedTime * 2f : (1f - NormalizedTime) * 2f;
            AnimatedRoot.localScale = Vector3.LerpUnclamped(BaseScale, PeakScale, PingPong);
            yield return null;
        }

        AnimatedRoot.localScale = BaseScale;
        PulseRoutine = null;
    }

    /// <summary>
    /// Animates the optional completion overlay alpha.
    /// </summary>
    private IEnumerator AnimateCompletionPulse()
    {
        float Duration = Mathf.Max(0.01f, CompletionPulseDuration);
        float Elapsed = 0f;

        while (Elapsed < Duration)
        {
            Elapsed += Time.unscaledDeltaTime;
            float NormalizedTime = Mathf.Clamp01(Elapsed / Duration);
            float Alpha = NormalizedTime <= 0.5f ? NormalizedTime * 2f : (1f - NormalizedTime) * 2f;
            SetCompletionPulseAlpha(Alpha);
            yield return null;
        }

        SetCompletionPulseAlpha(0f);
    }

    /// <summary>
    /// Sets the alpha of the optional completion overlay.
    /// </summary>
    /// <param name="Alpha">Canvas group alpha value.</param>
    private void SetCompletionPulseAlpha(float Alpha)
    {
        if (CompletionPulseCanvasGroup != null)
        {
            CompletionPulseCanvasGroup.alpha = Mathf.Clamp01(Alpha);
        }
    }
}
