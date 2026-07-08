using UnityEngine;

/// <summary>
/// Central visual configuration used by research skill tree nodes, pipes and tooltip widgets.
/// It keeps state colors and default state sprites consistent across the whole researcher UI.
/// </summary>
[CreateAssetMenu(fileName = "ResearchSkillTreeVisualSet", menuName = "Game/Research/UI/Skill Tree Visual Set")]
public sealed class ResearchSkillTreeVisualSet : ScriptableObject
{
    [Header("Node Frames")]
    [Tooltip("Frame sprite used when a research node is locked.")]
    [SerializeField] private Sprite LockedNodeFrameSprite;

    [Tooltip("Frame sprite used when a research node is available but not completed.")]
    [SerializeField] private Sprite AvailableNodeFrameSprite;

    [Tooltip("Frame sprite used when a research node is currently the active research.")]
    [SerializeField] private Sprite ActiveNodeFrameSprite;

    [Tooltip("Frame sprite used when a research node has already been completed.")]
    [SerializeField] private Sprite CompletedNodeFrameSprite;

    [Tooltip("Frame sprite used when a research node has invalid configuration.")]
    [SerializeField] private Sprite InvalidNodeFrameSprite;

    [Header("Title Plates")]
    [Tooltip("Title plate sprite used when a research node is locked.")]
    [SerializeField] private Sprite LockedTitlePlateSprite;

    [Tooltip("Title plate sprite used when a research node is available but not completed.")]
    [SerializeField] private Sprite AvailableTitlePlateSprite;

    [Tooltip("Title plate sprite used when a research node is currently the active research.")]
    [SerializeField] private Sprite ActiveTitlePlateSprite;

    [Tooltip("Title plate sprite used when a research node has already been completed.")]
    [SerializeField] private Sprite CompletedTitlePlateSprite;

    [Tooltip("Title plate sprite used when a research node has invalid configuration.")]
    [SerializeField] private Sprite InvalidTitlePlateSprite;

    [Header("Pipes")]
    [Tooltip("Pipe sprite used for locked pipe segments.")]
    [SerializeField] private Sprite LockedPipeSprite;

    [Tooltip("Pipe sprite used for available but incomplete pipe segments.")]
    [SerializeField] private Sprite AvailablePipeSprite;

    [Tooltip("Pipe sprite used for currently active pipe segments.")]
    [SerializeField] private Sprite ActivePipeSprite;

    [Tooltip("Pipe sprite used for completed pipe segments.")]
    [SerializeField] private Sprite CompletedPipeSprite;

    [Tooltip("Pipe sprite used for invalid pipe segments.")]
    [SerializeField] private Sprite InvalidPipeSprite;

    [Header("Colors")]
    [Tooltip("Tint color used when a research element is locked.")]
    [SerializeField] private Color LockedColor = new Color(0.45f, 0.45f, 0.45f, 1f);

    [Tooltip("Tint color used when a research element is available but not completed.")]
    [SerializeField] private Color AvailableColor = new Color(0.75f, 0.43f, 0.24f, 1f);

    [Tooltip("Tint color used when a research element is currently active.")]
    [SerializeField] private Color ActiveColor = new Color(0.95f, 0.72f, 0.32f, 1f);

    [Tooltip("Tint color used when a research element has already been completed.")]
    [SerializeField] private Color CompletedColor = new Color(1f, 0.77f, 0.18f, 1f);

    [Tooltip("Tint color used when a research element has invalid configuration.")]
    [SerializeField] private Color InvalidColor = new Color(1f, 0.15f, 0.15f, 1f);

    [Tooltip("Text color used for readable labels on the skill tree.")]
    [SerializeField] private Color TextColor = Color.white;

    [Tooltip("Text color used when a label belongs to a locked research node.")]
    [SerializeField] private Color LockedTextColor = new Color(0.72f, 0.72f, 0.72f, 1f);

    /// <summary>
    /// Gets the default node frame sprite for a runtime research view state.
    /// </summary>
    /// <param name="ViewState">Research view state resolved by the runtime service.</param>
    public Sprite GetNodeFrameSprite(ResearchRuntimeService.ResearchViewState ViewState)
    {
        switch (ViewState)
        {
            case ResearchRuntimeService.ResearchViewState.Locked:
                return LockedNodeFrameSprite;
            case ResearchRuntimeService.ResearchViewState.Available:
            case ResearchRuntimeService.ResearchViewState.PaidInactive:
                return AvailableNodeFrameSprite;
            case ResearchRuntimeService.ResearchViewState.Active:
                return ActiveNodeFrameSprite != null ? ActiveNodeFrameSprite : AvailableNodeFrameSprite;
            case ResearchRuntimeService.ResearchViewState.Completed:
                return CompletedNodeFrameSprite != null ? CompletedNodeFrameSprite : AvailableNodeFrameSprite;
            default:
                return InvalidNodeFrameSprite != null ? InvalidNodeFrameSprite : LockedNodeFrameSprite;
        }
    }

    /// <summary>
    /// Gets the default title plate sprite for a runtime research view state.
    /// </summary>
    /// <param name="ViewState">Research view state resolved by the runtime service.</param>
    public Sprite GetTitlePlateSprite(ResearchRuntimeService.ResearchViewState ViewState)
    {
        switch (ViewState)
        {
            case ResearchRuntimeService.ResearchViewState.Locked:
                return LockedTitlePlateSprite;
            case ResearchRuntimeService.ResearchViewState.Available:
            case ResearchRuntimeService.ResearchViewState.PaidInactive:
                return AvailableTitlePlateSprite;
            case ResearchRuntimeService.ResearchViewState.Active:
                return ActiveTitlePlateSprite != null ? ActiveTitlePlateSprite : AvailableTitlePlateSprite;
            case ResearchRuntimeService.ResearchViewState.Completed:
                return CompletedTitlePlateSprite != null ? CompletedTitlePlateSprite : AvailableTitlePlateSprite;
            default:
                return InvalidTitlePlateSprite != null ? InvalidTitlePlateSprite : LockedTitlePlateSprite;
        }
    }

    /// <summary>
    /// Gets the default pipe sprite for a runtime research view state.
    /// </summary>
    /// <param name="ViewState">Research view state resolved by the runtime service.</param>
    public Sprite GetPipeSprite(ResearchRuntimeService.ResearchViewState ViewState)
    {
        switch (ViewState)
        {
            case ResearchRuntimeService.ResearchViewState.Locked:
                return LockedPipeSprite;
            case ResearchRuntimeService.ResearchViewState.Available:
            case ResearchRuntimeService.ResearchViewState.PaidInactive:
                return AvailablePipeSprite;
            case ResearchRuntimeService.ResearchViewState.Active:
                return ActivePipeSprite != null ? ActivePipeSprite : AvailablePipeSprite;
            case ResearchRuntimeService.ResearchViewState.Completed:
                return CompletedPipeSprite != null ? CompletedPipeSprite : AvailablePipeSprite;
            default:
                return InvalidPipeSprite != null ? InvalidPipeSprite : LockedPipeSprite;
        }
    }

    /// <summary>
    /// Gets the tint color for a runtime research view state.
    /// </summary>
    /// <param name="ViewState">Research view state resolved by the runtime service.</param>
    public Color GetStateColor(ResearchRuntimeService.ResearchViewState ViewState)
    {
        switch (ViewState)
        {
            case ResearchRuntimeService.ResearchViewState.Locked:
                return LockedColor;
            case ResearchRuntimeService.ResearchViewState.Available:
            case ResearchRuntimeService.ResearchViewState.PaidInactive:
                return AvailableColor;
            case ResearchRuntimeService.ResearchViewState.Active:
                return ActiveColor;
            case ResearchRuntimeService.ResearchViewState.Completed:
                return CompletedColor;
            default:
                return InvalidColor;
        }
    }

    /// <summary>
    /// Gets the text color for a runtime research view state.
    /// </summary>
    /// <param name="ViewState">Research view state resolved by the runtime service.</param>
    public Color GetTextColor(ResearchRuntimeService.ResearchViewState ViewState)
    {
        return ViewState == ResearchRuntimeService.ResearchViewState.Locked ? LockedTextColor : TextColor;
    }
}
