using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Renders one dependency pipe between two research nodes.
/// The pipe is split into two visual halves: the source half inherits the source node state and the target half inherits the target node state.
/// </summary>
[DisallowMultipleComponent]
public sealed class ResearchSkillTreeConnectionUI : MonoBehaviour
{
    /// <summary>
    /// Configures one pipe half or one group of pipe images that must share the same state.
    /// </summary>
    [Serializable]
    public sealed class PipeSegmentGroup
    {
        [Tooltip("Images that belong to this pipe half. Multiple images are supported for complex bends or decorative caps.")]
        [SerializeField] private List<Image> SegmentImages = new();

        [Tooltip("Sprite used when this pipe half is locked. If empty, the visual set is used.")]
        [SerializeField] private Sprite LockedSpriteOverride;

        [Tooltip("Sprite used when this pipe half is available but not completed. If empty, the visual set is used.")]
        [SerializeField] private Sprite AvailableSpriteOverride;

        [Tooltip("Sprite used when this pipe half is active. If empty, Available Sprite Override or the visual set is used.")]
        [SerializeField] private Sprite ActiveSpriteOverride;

        [Tooltip("Sprite used when this pipe half is completed. If empty, the visual set is used.")]
        [SerializeField] private Sprite CompletedSpriteOverride;

        [Tooltip("If true, the state color from the visual set is applied as image tint.")]
        [SerializeField] private bool ApplyStateTint = true;

        [Tooltip("If true, every image in this group is hidden when no sprite can be resolved.")]
        [SerializeField] private bool HideWhenMissingSprite = false;

        /// <summary>
        /// Applies a research state to every image in this pipe group.
        /// </summary>
        /// <param name="ViewState">State used by this pipe half.</param>
        /// <param name="VisualSet">Visual set used for default sprites and colors.</param>
        public void ApplyState(ResearchRuntimeService.ResearchViewState ViewState, ResearchSkillTreeVisualSet VisualSet)
        {
            Sprite ResolvedSprite = ResolveSprite(ViewState, VisualSet);
            Color ResolvedColor = VisualSet != null && ApplyStateTint ? VisualSet.GetStateColor(ViewState) : Color.white;

            for (int Index = 0; Index < SegmentImages.Count; Index++)
            {
                Image SegmentImage = SegmentImages[Index];

                if (SegmentImage == null)
                {
                    continue;
                }

                if (ResolvedSprite != null)
                {
                    SegmentImage.sprite = ResolvedSprite;
                    SegmentImage.enabled = true;
                }
                else if (HideWhenMissingSprite)
                {
                    SegmentImage.enabled = false;
                }

                SegmentImage.color = ResolvedColor;
            }
        }

        /// <summary>
        /// Resolves the pipe sprite for the requested state.
        /// </summary>
        /// <param name="ViewState">State used by this pipe half.</param>
        /// <param name="VisualSet">Visual set used as fallback.</param>
        private Sprite ResolveSprite(ResearchRuntimeService.ResearchViewState ViewState, ResearchSkillTreeVisualSet VisualSet)
        {
            switch (ViewState)
            {
                case ResearchRuntimeService.ResearchViewState.Locked:
                    return LockedSpriteOverride != null ? LockedSpriteOverride : VisualSet != null ? VisualSet.GetPipeSprite(ViewState) : null;
                case ResearchRuntimeService.ResearchViewState.Available:
                case ResearchRuntimeService.ResearchViewState.PaidInactive:
                    return AvailableSpriteOverride != null ? AvailableSpriteOverride : VisualSet != null ? VisualSet.GetPipeSprite(ViewState) : null;
                case ResearchRuntimeService.ResearchViewState.Active:
                    return ActiveSpriteOverride != null ? ActiveSpriteOverride : AvailableSpriteOverride != null ? AvailableSpriteOverride : VisualSet != null ? VisualSet.GetPipeSprite(ViewState) : null;
                case ResearchRuntimeService.ResearchViewState.Completed:
                    return CompletedSpriteOverride != null ? CompletedSpriteOverride : VisualSet != null ? VisualSet.GetPipeSprite(ViewState) : null;
                default:
                    return VisualSet != null ? VisualSet.GetPipeSprite(ViewState) : null;
            }
        }
    }

    [Header("Data")]
    [Tooltip("Visual set used to resolve default pipe sprites and colors.")]
    [SerializeField] private ResearchSkillTreeVisualSet VisualSet;

    [Tooltip("Node at the dependency source side of this pipe.")]
    [SerializeField] private ResearchSkillTreeNodeUI SourceNode;

    [Tooltip("Node at the dependency target side of this pipe.")]
    [SerializeField] private ResearchSkillTreeNodeUI TargetNode;

    [Header("Pipe Halves")]
    [Tooltip("Pipe half visually owned by the source node.")]
    [SerializeField] private PipeSegmentGroup SourceSegment = new PipeSegmentGroup();

    [Tooltip("Pipe half visually owned by the target node.")]
    [SerializeField] private PipeSegmentGroup TargetSegment = new PipeSegmentGroup();

    [Header("Feedback")]
    [Tooltip("Optional canvas group used to flash this connection when either pipe half changes state.")]
    [SerializeField] private CanvasGroup PulseCanvasGroup;

    [Tooltip("Unscaled seconds used by the pipe state-change pulse.")]
    [SerializeField] private float PulseDuration = 0.22f;

    /// <summary>
    /// Station used to query fallback research state if node references are missing.
    /// </summary>
    private ResearchStation OwnerStation;

    /// <summary>
    /// Last rendered source state.
    /// </summary>
    private ResearchRuntimeService.ResearchViewState LastSourceState = ResearchRuntimeService.ResearchViewState.Invalid;

    /// <summary>
    /// Last rendered target state.
    /// </summary>
    private ResearchRuntimeService.ResearchViewState LastTargetState = ResearchRuntimeService.ResearchViewState.Invalid;

    /// <summary>
    /// True after the first state refresh has been applied.
    /// </summary>
    private bool HasRenderedInitialState;

    /// <summary>
    /// Coroutine currently animating this connection pulse.
    /// </summary>
    private Coroutine PulseRoutine;

    /// <summary>
    /// Initializes this connection with the owning station.
    /// </summary>
    /// <param name="Station">Research station used to query state.</param>
    public void Initialize(ResearchStation Station)
    {
        OwnerStation = Station;
    }

    /// <summary>
    /// Refreshes both visual pipe halves.
    /// </summary>
    /// <param name="PlayStateFeedback">If true, the pipe can flash when a state changes after initialization.</param>
    public void RefreshView(bool PlayStateFeedback)
    {
        ResearchRuntimeService.ResearchViewState SourceState = ResolveNodeState(SourceNode);
        ResearchRuntimeService.ResearchViewState TargetState = ResolveNodeState(TargetNode);

        SourceSegment.ApplyState(SourceState, VisualSet);
        TargetSegment.ApplyState(TargetState, VisualSet);

        bool StateChanged = HasRenderedInitialState && (SourceState != LastSourceState || TargetState != LastTargetState);
        LastSourceState = SourceState;
        LastTargetState = TargetState;
        HasRenderedInitialState = true;

        if (PlayStateFeedback && StateChanged)
        {
            PlayPulse();
        }
    }

    /// <summary>
    /// Resolves the state of a node safely.
    /// </summary>
    /// <param name="Node">Node being evaluated.</param>
    private ResearchRuntimeService.ResearchViewState ResolveNodeState(ResearchSkillTreeNodeUI Node)
    {
        if (Node == null)
        {
            return ResearchRuntimeService.ResearchViewState.Invalid;
        }

        if (OwnerStation == null)
        {
            return Node.GetViewState();
        }

        ResearchDefinition Definition = Node.GetResearchDefinition();
        return Definition != null ? OwnerStation.GetResearchViewState(Definition) : ResearchRuntimeService.ResearchViewState.Invalid;
    }

    /// <summary>
    /// Plays a short pulse on the optional pulse canvas group.
    /// </summary>
    private void PlayPulse()
    {
        if (PulseCanvasGroup == null || !gameObject.activeInHierarchy)
        {
            return;
        }

        if (PulseRoutine != null)
        {
            StopCoroutine(PulseRoutine);
        }

        PulseRoutine = StartCoroutine(AnimatePulse());
    }

    /// <summary>
    /// Animates the pulse canvas group alpha.
    /// </summary>
    private IEnumerator AnimatePulse()
    {
        float Duration = Mathf.Max(0.01f, PulseDuration);
        float Elapsed = 0f;

        while (Elapsed < Duration)
        {
            Elapsed += Time.unscaledDeltaTime;
            float NormalizedTime = Mathf.Clamp01(Elapsed / Duration);
            PulseCanvasGroup.alpha = NormalizedTime <= 0.5f ? NormalizedTime * 2f : (1f - NormalizedTime) * 2f;
            yield return null;
        }

        PulseCanvasGroup.alpha = 0f;
        PulseRoutine = null;
    }
}
