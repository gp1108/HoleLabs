using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Coordinates the complete researcher skill tree UI: nodes, dependency pipes, details tooltip, selection state and tree shifting.
/// It intentionally delegates research state to ResearchRuntimeService and keeps this class purely visual and interactive.
/// </summary>
[DisallowMultipleComponent]
public sealed class ResearchSkillTreeViewUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Research station used to activate researches and query runtime state. Usually assigned by ResearchPanelUI.Initialize.")]
    [SerializeField] private ResearchStation OwnerStation;

    [Tooltip("Root moved horizontally when the selected node would be hidden by the fixed tooltip.")]
    [SerializeField] private RectTransform MovableTreeRoot;

    [Tooltip("Tooltip shown at a fixed right-side position when a valid research node is selected.")]
    [SerializeField] private ResearchSkillTreeTooltipUI TooltipUI;

    [Tooltip("Transparent full-screen or full-panel button placed behind nodes. Clicking it closes the tooltip and resets the tree position.")]
    [SerializeField] private Button BackgroundCloseButton;

    [Header("Discovery")]
    [Tooltip("If true, nodes and connections are discovered under this view during Awake.")]
    [SerializeField] private bool DiscoverOnAwake = true;

    [Tooltip("If true, nodes and connections are rediscovered whenever Initialize is called.")]
    [SerializeField] private bool RediscoverOnInitialize = true;

    [Header("Tree Shift")]
    [Tooltip("If true, selecting a node near the right side shifts the tree left so the fixed tooltip does not cover it.")]
    [SerializeField] private bool ShiftTreeWhenTooltipWouldOverlap = true;

    [Tooltip("Normalized screen X threshold used to decide when the tree should shift left. 0 is left edge and 1 is right edge.")]
    [SerializeField, Range(0.1f, 0.95f)] private float RightSideShiftThreshold = 0.66f;

    [Tooltip("If true, shift decisions are measured against the original tree position, not the current animated position. This prevents right-side nodes from resetting the tree after it has already shifted left.")]
    [SerializeField] private bool MeasureShiftAgainstDefaultTreePosition = true;

    [Tooltip("If true, the selected node is compared against the actual tooltip screen rectangle before falling back to the normalized threshold.")]
    [SerializeField] private bool UseTooltipOverlapForShift = true;

    [Tooltip("Extra screen-space pixels reserved between the selected node and the tooltip before shifting the tree.")]
    [SerializeField] private float TooltipOverlapPadding = 24f;

    [Tooltip("Anchored offset added to the default tree position while a right-side node is selected.")]
    [SerializeField] private Vector2 ShiftedTreeOffset = new Vector2(-360f, 0f);

    [Tooltip("Unscaled seconds used to animate the tree when shifting or returning to its default position.")]
    [SerializeField] private float TreeShiftDuration = 0.22f;

    [Tooltip("Curve used by the tree shift animation.")]
    [SerializeField] private AnimationCurve TreeShiftCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Debug")]
    [Tooltip("Logs researcher skill tree selection and activation flow.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Nodes currently registered in this skill tree.
    /// </summary>
    private readonly List<ResearchSkillTreeNodeUI> RegisteredNodes = new();

    /// <summary>
    /// Dependency connections currently registered in this skill tree.
    /// </summary>
    private readonly List<ResearchSkillTreeConnectionUI> RegisteredConnections = new();

    /// <summary>
    /// Tier gates currently registered in this skill tree.
    /// </summary>
    private readonly List<ResearchSkillTreeTierGateUI> RegisteredTierGates = new();

    /// <summary>
    /// Currently selected research node.
    /// </summary>
    private ResearchSkillTreeNodeUI SelectedNode;

    /// <summary>
    /// Default anchored position captured from the movable tree root.
    /// </summary>
    private Vector2 DefaultTreeAnchoredPosition;

    /// <summary>
    /// Coroutine currently animating the tree position.
    /// </summary>
    private Coroutine TreeShiftRoutine;

    /// <summary>
    /// True after the default tree position has been captured.
    /// </summary>
    private bool HasCapturedDefaultTreePosition;

    /// <summary>
    /// Resolves references and optionally discovers child UI elements.
    /// </summary>
    private void Awake()
    {
        ResolveReferences();
        CaptureDefaultTreePosition();
        BindBackgroundButton();

        if (DiscoverOnAwake)
        {
            DiscoverTreeElements();
        }
    }

    /// <summary>
    /// Subscribes to station and runtime events when the view becomes active.
    /// </summary>
    private void OnEnable()
    {
        SubscribeToEvents();
        RefreshAll(false);
    }

    /// <summary>
    /// Unsubscribes from station and runtime events when the view becomes inactive.
    /// </summary>
    private void OnDisable()
    {
        UnsubscribeFromEvents();
    }

    /// <summary>
    /// Unbinds events before this object is destroyed.
    /// </summary>
    private void OnDestroy()
    {
        UnsubscribeFromEvents();

        if (BackgroundCloseButton != null)
        {
            BackgroundCloseButton.onClick.RemoveListener(HandleBackgroundClicked);
        }

        UnbindNodeClicks();
    }

    /// <summary>
    /// Initializes the skill tree view with the owning station.
    /// </summary>
    /// <param name="Station">Research station controlling this researcher panel.</param>
    public void Initialize(ResearchStation Station)
    {
        if (OwnerStation != Station)
        {
            UnsubscribeFromEvents();
            OwnerStation = Station;
            SubscribeToEvents();
        }

        ResolveReferences();
        CaptureDefaultTreePosition();

        if (RediscoverOnInitialize)
        {
            DiscoverTreeElements();
        }

        InitializeNodesAndConnections();
        RefreshAll(false);
    }

    /// <summary>
    /// Called by ResearchPanelUI when the parent panel is shown.
    /// </summary>
    public void HandlePanelShown()
    {
        RefreshAll(false);
    }

    /// <summary>
    /// Called by ResearchPanelUI when the parent panel is hidden.
    /// </summary>
    public void HandlePanelHidden()
    {
        ClearSelection(true);
    }

    /// <summary>
    /// Discovers all nodes and connections under this skill tree view.
    /// </summary>
    public void DiscoverTreeElements()
    {
        UnbindNodeClicks();
        RegisteredNodes.Clear();
        RegisteredConnections.Clear();
        RegisteredTierGates.Clear();

        ResearchSkillTreeNodeUI[] Nodes = GetComponentsInChildren<ResearchSkillTreeNodeUI>(true);
        ResearchSkillTreeConnectionUI[] Connections = GetComponentsInChildren<ResearchSkillTreeConnectionUI>(true);
        ResearchSkillTreeTierGateUI[] TierGates = GetComponentsInChildren<ResearchSkillTreeTierGateUI>(true);

        for (int Index = 0; Index < Nodes.Length; Index++)
        {
            if (Nodes[Index] != null && !RegisteredNodes.Contains(Nodes[Index]))
            {
                RegisteredNodes.Add(Nodes[Index]);
            }
        }

        for (int Index = 0; Index < Connections.Length; Index++)
        {
            if (Connections[Index] != null && !RegisteredConnections.Contains(Connections[Index]))
            {
                RegisteredConnections.Add(Connections[Index]);
            }
        }

        for (int Index = 0; Index < TierGates.Length; Index++)
        {
            if (TierGates[Index] != null && !RegisteredTierGates.Contains(TierGates[Index]))
            {
                RegisteredTierGates.Add(TierGates[Index]);
            }
        }

        BindNodeClicks();
    }

    /// <summary>
    /// Refreshes every node, every pipe and the tooltip if visible.
    /// </summary>
    /// <param name="PlayStateFeedback">If true, state transition feedback can be played.</param>
    public void RefreshAll(bool PlayStateFeedback)
    {
        InitializeNodesAndConnections();

        for (int Index = 0; Index < RegisteredNodes.Count; Index++)
        {
            if (RegisteredNodes[Index] != null)
            {
                RegisteredNodes[Index].RefreshView(PlayStateFeedback);
            }
        }

        for (int Index = 0; Index < RegisteredConnections.Count; Index++)
        {
            if (RegisteredConnections[Index] != null)
            {
                RegisteredConnections[Index].RefreshView(PlayStateFeedback);
            }
        }

        for (int Index = 0; Index < RegisteredTierGates.Count; Index++)
        {
            if (RegisteredTierGates[Index] != null)
            {
                RegisteredTierGates[Index].RefreshView();
            }
        }

        if (TooltipUI != null && SelectedNode != null)
        {
            TooltipUI.RefreshView();
        }
    }

    /// <summary>
    /// Gets the currently selected node, if any.
    /// </summary>
    public ResearchSkillTreeNodeUI GetSelectedNode()
    {
        return SelectedNode;
    }

    /// <summary>
    /// Attempts to activate the research currently selected in the tooltip.
    /// </summary>
    /// <returns>True when activation succeeded.</returns>
    public bool TryActivateSelectedResearch()
    {
        if (OwnerStation == null || SelectedNode == null || SelectedNode.GetResearchDefinition() == null)
        {
            return false;
        }

        bool WasActivated = OwnerStation.TryActivateResearch(SelectedNode.GetResearchDefinition());

        if (WasActivated)
        {
            SelectedNode.PlayActivationFeedback();
            RefreshAll(true);
        }

        Log(WasActivated ? "Research activation succeeded." : "Research activation failed.");
        return WasActivated;
    }

    /// <summary>
    /// Clears the current selection, hides the tooltip and returns the tree to its original position.
    /// </summary>
    /// <param name="Immediate">If true, tooltip and tree movement are applied instantly.</param>
    public void ClearSelection(bool Immediate)
    {
        if (SelectedNode != null)
        {
            SelectedNode.SetSelected(false);
            SelectedNode = null;
        }

        if (TooltipUI != null)
        {
            TooltipUI.Hide(Immediate);
        }

        MoveTreeTo(DefaultTreeAnchoredPosition, Immediate);
    }

    /// <summary>
    /// Selects the provided node if it is visible in the research flow, otherwise closes the current tooltip.
    /// </summary>
    /// <param name="Node">Clicked node.</param>
    private void SelectNode(ResearchSkillTreeNodeUI Node)
    {
        if (Node == null)
        {
            ClearSelection(false);
            return;
        }

        if (!Node.CanOpenTooltip())
        {
            if (Node.ShouldCloseTooltipWhenClicked())
            {
                ClearSelection(false);
            }

            return;
        }

        if (SelectedNode != null && SelectedNode != Node)
        {
            SelectedNode.SetSelected(false);
        }

        SelectedNode = Node;
        SelectedNode.SetSelected(true);

        if (TooltipUI != null)
        {
            TooltipUI.Show(this, OwnerStation, Node);
        }

        MoveTreeForNode(Node);
        Log("Selected research node: " + Node.GetResearchDefinition().GetDisplayName());
    }

    /// <summary>
    /// Handles a node click event.
    /// </summary>
    /// <param name="Node">Clicked node.</param>
    private void HandleNodeClicked(ResearchSkillTreeNodeUI Node)
    {
        SelectNode(Node);
    }

    /// <summary>
    /// Handles clicks on the transparent background close button.
    /// </summary>
    private void HandleBackgroundClicked()
    {
        ClearSelection(false);
    }

    /// <summary>
    /// Moves the tree if the selected node is close to the tooltip area.
    /// </summary>
    /// <param name="Node">Selected node.</param>
    private void MoveTreeForNode(ResearchSkillTreeNodeUI Node)
    {
        if (!ShiftTreeWhenTooltipWouldOverlap || Node == null)
        {
            MoveTreeTo(DefaultTreeAnchoredPosition, false);
            return;
        }

        Vector2 TargetPosition = ShouldShiftTreeForNode(Node)
            ? DefaultTreeAnchoredPosition + ShiftedTreeOffset
            : DefaultTreeAnchoredPosition;

        MoveTreeTo(TargetPosition, false);
    }

    /// <summary>
    /// Returns whether the tree should shift left for the provided node.
    /// </summary>
    /// <param name="Node">Selected node.</param>
    private bool ShouldShiftTreeForNode(ResearchSkillTreeNodeUI Node)
    {
        if (!ShiftTreeWhenTooltipWouldOverlap || Node == null)
        {
            return false;
        }

        if (UseTooltipOverlapForShift && WouldNodeOverlapTooltipAtDefaultPosition(Node))
        {
            return true;
        }

        float NormalizedX = GetNodeScreenNormalizedX(Node, MeasureShiftAgainstDefaultTreePosition);
        return NormalizedX >= RightSideShiftThreshold;
    }

    /// <summary>
    /// Returns whether the node would overlap the fixed tooltip when the tree is at its original position.
    /// </summary>
    /// <param name="Node">Selected node.</param>
    private bool WouldNodeOverlapTooltipAtDefaultPosition(ResearchSkillTreeNodeUI Node)
    {
        RectTransform NodeRectTransform = Node != null ? Node.transform as RectTransform : null;
        RectTransform TooltipRectTransform = TooltipUI != null ? TooltipUI.transform as RectTransform : null;

        if (NodeRectTransform == null || TooltipRectTransform == null || Screen.width <= 0 || Screen.height <= 0)
        {
            return false;
        }

        Canvas RootCanvas = GetComponentInParent<Canvas>();
        Camera UiCamera = GetUiCamera(RootCanvas);
        Vector2 DefaultOffset = GetDefaultTreeScreenOffset(RootCanvas);

        Rect NodeScreenRect = GetScreenRect(NodeRectTransform, UiCamera, DefaultOffset);
        Rect TooltipScreenRect = GetScreenRect(TooltipRectTransform, UiCamera, Vector2.zero);
        float Padding = Mathf.Max(0f, TooltipOverlapPadding);
        bool VerticallyOverlaps = NodeScreenRect.yMax >= TooltipScreenRect.yMin && NodeScreenRect.yMin <= TooltipScreenRect.yMax;
        bool HorizontallyOverlapsOrTouchesSafeArea = NodeScreenRect.xMax + Padding >= TooltipScreenRect.xMin;

        return VerticallyOverlaps && HorizontallyOverlapsOrTouchesSafeArea;
    }

    /// <summary>
    /// Gets the normalized screen X coordinate of a node center.
    /// </summary>
    /// <param name="Node">Node being measured.</param>
    /// <param name="MeasureAtDefaultTreePosition">If true, the current tree shift is compensated before measuring.</param>
    private float GetNodeScreenNormalizedX(ResearchSkillTreeNodeUI Node, bool MeasureAtDefaultTreePosition)
    {
        RectTransform NodeRectTransform = Node.transform as RectTransform;

        if (NodeRectTransform == null || Screen.width <= 0)
        {
            return 0f;
        }

        Canvas RootCanvas = GetComponentInParent<Canvas>();
        Camera UiCamera = GetUiCamera(RootCanvas);
        Vector3 ScreenPoint = RectTransformUtility.WorldToScreenPoint(UiCamera, NodeRectTransform.TransformPoint(NodeRectTransform.rect.center));

        if (MeasureAtDefaultTreePosition)
        {
            ScreenPoint += (Vector3)GetDefaultTreeScreenOffset(RootCanvas);
        }

        return Mathf.Clamp01(ScreenPoint.x / Screen.width);
    }

    /// <summary>
    /// Gets the UI camera required by the root canvas render mode.
    /// </summary>
    /// <param name="RootCanvas">Canvas containing this skill tree.</param>
    private Camera GetUiCamera(Canvas RootCanvas)
    {
        return RootCanvas != null && RootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? RootCanvas.worldCamera
            : null;
    }

    /// <summary>
    /// Gets the screen-space offset required to measure nodes as if the tree were still at its default position.
    /// </summary>
    /// <param name="RootCanvas">Canvas containing this skill tree.</param>
    private Vector2 GetDefaultTreeScreenOffset(Canvas RootCanvas)
    {
        if (!MeasureShiftAgainstDefaultTreePosition || MovableTreeRoot == null || !HasCapturedDefaultTreePosition)
        {
            return Vector2.zero;
        }

        float ScaleFactor = RootCanvas != null ? RootCanvas.scaleFactor : 1f;
        return (DefaultTreeAnchoredPosition - MovableTreeRoot.anchoredPosition) * ScaleFactor;
    }

    /// <summary>
    /// Gets a rect transform screen rectangle and applies an optional screen-space offset.
    /// </summary>
    /// <param name="RectTransform">Rect transform being measured.</param>
    /// <param name="UiCamera">Camera used by the canvas, or null for overlay canvases.</param>
    /// <param name="ScreenOffset">Additional screen-space offset in pixels.</param>
    private Rect GetScreenRect(RectTransform RectTransform, Camera UiCamera, Vector2 ScreenOffset)
    {
        Vector3[] Corners = new Vector3[4];
        RectTransform.GetWorldCorners(Corners);

        Vector2 Min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 Max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

        for (int Index = 0; Index < Corners.Length; Index++)
        {
            Vector2 ScreenPoint = RectTransformUtility.WorldToScreenPoint(UiCamera, Corners[Index]) + ScreenOffset;
            Min = Vector2.Min(Min, ScreenPoint);
            Max = Vector2.Max(Max, ScreenPoint);
        }

        return Rect.MinMaxRect(Min.x, Min.y, Max.x, Max.y);
    }

    /// <summary>
    /// Moves the tree root to the requested anchored position.
    /// </summary>
    /// <param name="TargetPosition">Target anchored position.</param>
    /// <param name="Immediate">If true, movement is applied without animation.</param>
    private void MoveTreeTo(Vector2 TargetPosition, bool Immediate)
    {
        if (MovableTreeRoot == null)
        {
            return;
        }

        if (TreeShiftRoutine != null)
        {
            StopCoroutine(TreeShiftRoutine);
            TreeShiftRoutine = null;
        }

        if (Immediate || !gameObject.activeInHierarchy)
        {
            MovableTreeRoot.anchoredPosition = TargetPosition;
            return;
        }

        TreeShiftRoutine = StartCoroutine(AnimateTreePosition(MovableTreeRoot.anchoredPosition, TargetPosition));
    }

    /// <summary>
    /// Animates the tree root anchored position.
    /// </summary>
    /// <param name="StartPosition">Starting anchored position.</param>
    /// <param name="TargetPosition">Target anchored position.</param>
    private IEnumerator AnimateTreePosition(Vector2 StartPosition, Vector2 TargetPosition)
    {
        float Duration = Mathf.Max(0.01f, TreeShiftDuration);
        float Elapsed = 0f;

        while (Elapsed < Duration)
        {
            Elapsed += Time.unscaledDeltaTime;
            float NormalizedTime = Mathf.Clamp01(Elapsed / Duration);
            float EvaluatedTime = TreeShiftCurve != null ? TreeShiftCurve.Evaluate(NormalizedTime) : NormalizedTime;
            MovableTreeRoot.anchoredPosition = Vector2.LerpUnclamped(StartPosition, TargetPosition, EvaluatedTime);
            yield return null;
        }

        MovableTreeRoot.anchoredPosition = TargetPosition;
        TreeShiftRoutine = null;
    }

    /// <summary>
    /// Initializes nodes and connections with the current station.
    /// </summary>
    private void InitializeNodesAndConnections()
    {
        for (int Index = 0; Index < RegisteredNodes.Count; Index++)
        {
            if (RegisteredNodes[Index] != null)
            {
                RegisteredNodes[Index].Initialize(OwnerStation);

                if (OwnerStation != null && RegisteredNodes[Index].GetResearchDefinition() != null)
                {
                    OwnerStation.RegisterResearchDefinition(RegisteredNodes[Index].GetResearchDefinition());
                }
            }
        }

        for (int Index = 0; Index < RegisteredConnections.Count; Index++)
        {
            if (RegisteredConnections[Index] != null)
            {
                RegisteredConnections[Index].Initialize(OwnerStation);
            }
        }

        for (int Index = 0; Index < RegisteredTierGates.Count; Index++)
        {
            if (RegisteredTierGates[Index] != null)
            {
                RegisteredTierGates[Index].Initialize(OwnerStation);
            }
        }
    }

    /// <summary>
    /// Resolves missing references from the current hierarchy.
    /// </summary>
    private void ResolveReferences()
    {
        if (MovableTreeRoot == null)
        {
            MovableTreeRoot = transform as RectTransform;
        }

        if (TooltipUI == null)
        {
            TooltipUI = GetComponentInChildren<ResearchSkillTreeTooltipUI>(true);
        }
    }

    /// <summary>
    /// Captures the default tree position once.
    /// </summary>
    private void CaptureDefaultTreePosition()
    {
        if (HasCapturedDefaultTreePosition || MovableTreeRoot == null)
        {
            return;
        }

        DefaultTreeAnchoredPosition = MovableTreeRoot.anchoredPosition;
        HasCapturedDefaultTreePosition = true;
    }

    /// <summary>
    /// Binds the background close button.
    /// </summary>
    private void BindBackgroundButton()
    {
        if (BackgroundCloseButton == null)
        {
            return;
        }

        BackgroundCloseButton.onClick.RemoveListener(HandleBackgroundClicked);
        BackgroundCloseButton.onClick.AddListener(HandleBackgroundClicked);
    }

    /// <summary>
    /// Binds node click callbacks.
    /// </summary>
    private void BindNodeClicks()
    {
        for (int Index = 0; Index < RegisteredNodes.Count; Index++)
        {
            if (RegisteredNodes[Index] != null)
            {
                RegisteredNodes[Index].OnNodeClicked -= HandleNodeClicked;
                RegisteredNodes[Index].OnNodeClicked += HandleNodeClicked;
            }
        }
    }

    /// <summary>
    /// Unbinds node click callbacks.
    /// </summary>
    private void UnbindNodeClicks()
    {
        for (int Index = 0; Index < RegisteredNodes.Count; Index++)
        {
            if (RegisteredNodes[Index] != null)
            {
                RegisteredNodes[Index].OnNodeClicked -= HandleNodeClicked;
            }
        }
    }

    /// <summary>
    /// Subscribes to runtime and station state events.
    /// </summary>
    private void SubscribeToEvents()
    {
        if (OwnerStation == null)
        {
            return;
        }

        OwnerStation.OnResearchStationStateChanged -= HandleResearchStateChanged;
        OwnerStation.OnResearchStationStateChanged += HandleResearchStateChanged;

        ResearchRuntimeService RuntimeService = OwnerStation.GetResearchRuntimeService();

        if (RuntimeService != null)
        {
            RuntimeService.OnResearchStateChanged -= HandleResearchStateChanged;
            RuntimeService.OnResearchStateChanged += HandleResearchStateChanged;
        }
    }

    /// <summary>
    /// Unsubscribes from runtime and station state events.
    /// </summary>
    private void UnsubscribeFromEvents()
    {
        if (OwnerStation == null)
        {
            return;
        }

        OwnerStation.OnResearchStationStateChanged -= HandleResearchStateChanged;

        ResearchRuntimeService RuntimeService = OwnerStation.GetResearchRuntimeService();

        if (RuntimeService != null)
        {
            RuntimeService.OnResearchStateChanged -= HandleResearchStateChanged;
        }
    }

    /// <summary>
    /// Handles any runtime state change that should refresh the tree.
    /// </summary>
    private void HandleResearchStateChanged()
    {
        RefreshAll(true);
    }

    /// <summary>
    /// Prints every registered node state and block reason to the Unity console.
    /// </summary>
    [ContextMenu("Print Registered Node State Report")]
    private void PrintRegisteredNodeStateReport()
    {
        DiscoverTreeElements();
        InitializeNodesAndConnections();

        if (OwnerStation == null)
        {
            Debug.LogWarning("[ResearchSkillTreeViewUI] Missing OwnerStation. Nodes will render as Invalid and will not open tooltips.", this);
            return;
        }

        for (int Index = 0; Index < RegisteredNodes.Count; Index++)
        {
            ResearchSkillTreeNodeUI Node = RegisteredNodes[Index];

            if (Node == null)
            {
                continue;
            }

            ResearchDefinition Definition = Node.GetResearchDefinition();
            string Name = Definition != null ? Definition.GetDisplayName() : "Missing ResearchDefinition";
            ResearchRuntimeService.ResearchViewState State = Node.GetViewState();
            ResearchRuntimeService.ResearchBlockReason BlockReason = Definition != null
                ? OwnerStation.GetResearchBlockReason(Definition)
                : ResearchRuntimeService.ResearchBlockReason.MissingResearch;

            Debug.Log("[ResearchSkillTreeViewUI] Node report | Node=" + Node.name + " | Research=" + Name + " | State=" + State + " | BlockReason=" + BlockReason, Node);
        }

        for (int Index = 0; Index < RegisteredTierGates.Count; Index++)
        {
            ResearchSkillTreeTierGateUI TierGate = RegisteredTierGates[Index];

            if (TierGate == null)
            {
                continue;
            }

            bool IsUnlocked = TierGate.IsUnlocked();
            string TierUpgradeName = TierGate.GetRequiredResearchTierUpgradeDefinition() != null
                ? TierGate.GetRequiredResearchTierUpgradeDefinition().GetDisplayName()
                : "Missing Tier Upgrade";

            Debug.Log("[ResearchSkillTreeViewUI] Tier gate report | Gate=" + TierGate.name + " | TierUpgrade=" + TierUpgradeName + " | RequiredLevel=" + TierGate.GetRequiredResearchTierLevel() + " | Unlocked=" + IsUnlocked, TierGate);
        }
    }

    /// <summary>
    /// Writes a skill-tree-specific debug message.
    /// </summary>
    /// <param name="Message">Message written to the Unity console.</param>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[ResearchSkillTreeViewUI] " + Message, this);
    }
}
