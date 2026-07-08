using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Blocks a rectangular skill tree area until a configured research tier upgrade level has been unlocked.
/// This component is intentionally visual and input-focused; the authoritative tier requirement is still checked by ResearchRuntimeService through ResearchDefinition tier gates.
/// </summary>
[DisallowMultipleComponent]
[ExecuteAlways]
public sealed class ResearchSkillTreeTierGateUI : MonoBehaviour
{
    [Header("Tier Requirement")]
    [Tooltip("Upgrade definition that stores the unlocked research tier level. This should be the same tier upgrade used by the gated ResearchDefinition assets.")]
    [SerializeField] private UpgradeDefinition RequiredResearchTierUpgradeDefinition;

    [Tooltip("Minimum research tier level required before this blocker stops intercepting clicks.")]
    [SerializeField] private int RequiredResearchTierLevel = 1;

    [Header("References")]
    [Tooltip("Root object shown while this tier is locked. If empty, this GameObject is used.")]
    [SerializeField] private GameObject LockedRoot;

    [Tooltip("Canvas group used to fade and block raycasts on the locked overlay.")]
    [SerializeField] private CanvasGroup LockedCanvasGroup;

    [Tooltip("Optional button used as the click-blocking panel. It does not need an OnClick action.")]
    [SerializeField] private Button BlockingButton;

    [Tooltip("Optional text used to display the locked tier title.")]
    [SerializeField] private TMP_Text TitleText;

    [Tooltip("Optional text used to display the locked tier requirement message.")]
    [SerializeField] private TMP_Text RequirementText;

    [Header("Display")]
    [Tooltip("If true, the locked overlay object is disabled when the tier is unlocked. If false, only alpha and raycast blocking are changed.")]
    [SerializeField] private bool HideRootWhenUnlocked = true;

    [Tooltip("Alpha applied to the canvas group while this tier is locked.")]
    [SerializeField, Range(0f, 1f)] private float LockedAlpha = 1f;

    [Tooltip("Alpha applied to the canvas group while this tier is unlocked and Hide Root When Unlocked is false.")]
    [SerializeField, Range(0f, 1f)] private float UnlockedAlpha = 0f;

    [Tooltip("Title format used while the tier is locked. {0} is replaced by the required tier level.")]
    [SerializeField] private string LockedTitleFormat = "Tier {0} Locked";

    [Tooltip("Requirement format used while the tier is locked. {0} is replaced by the required tier level.")]
    [SerializeField] private string LockedRequirementFormat = "Unlock research tier {0} to access these upgrades.";

    [Header("Editor Preview")]
    [Tooltip("If true, the overlay can be previewed in edit mode without entering Play Mode.")]
    [SerializeField] private bool PreviewInEditMode = true;

    [Tooltip("Preview state used in edit mode when Preview In Edit Mode is enabled.")]
    [SerializeField] private bool EditorPreviewLocked = true;

    [Header("Debug")]
    [Tooltip("Logs tier gate refresh information.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Station used to resolve the runtime research service.
    /// </summary>
    private ResearchStation OwnerStation;

    /// <summary>
    /// Resolves optional child references in edit mode and applies preview state.
    /// </summary>
    private void OnValidate()
    {
        ResolveReferences();

        if (!Application.isPlaying && PreviewInEditMode)
        {
            ApplyVisualState(EditorPreviewLocked);
        }
    }

    /// <summary>
    /// Initializes this tier blocker with the owning researcher station.
    /// </summary>
    /// <param name="Station">Station used to query runtime tier state.</param>
    public void Initialize(ResearchStation Station)
    {
        OwnerStation = Station;
        ResolveReferences();
        RefreshView();
    }

    /// <summary>
    /// Gets the upgrade definition used by this tier gate.
    /// </summary>
    public UpgradeDefinition GetRequiredResearchTierUpgradeDefinition()
    {
        return RequiredResearchTierUpgradeDefinition;
    }

    /// <summary>
    /// Gets the tier level required by this gate.
    /// </summary>
    public int GetRequiredResearchTierLevel()
    {
        return Mathf.Max(1, RequiredResearchTierLevel);
    }

    /// <summary>
    /// Returns whether the required research tier is currently unlocked.
    /// </summary>
    public bool IsUnlocked()
    {
        if (!Application.isPlaying)
        {
            return !EditorPreviewLocked;
        }

        if (OwnerStation == null)
        {
            return false;
        }

        ResearchRuntimeService RuntimeService = OwnerStation.GetResearchRuntimeService();

        if (RuntimeService == null)
        {
            return false;
        }

        return RuntimeService.IsResearchTierUnlocked(RequiredResearchTierUpgradeDefinition, GetRequiredResearchTierLevel());
    }

    /// <summary>
    /// Refreshes the overlay visibility, raycast blocking and labels.
    /// </summary>
    public void RefreshView()
    {
        ResolveReferences();
        bool IsLocked = !IsUnlocked();
        ApplyVisualState(IsLocked);
        Log(IsLocked ? "Tier gate locked." : "Tier gate unlocked.");
    }

    /// <summary>
    /// Automatically assigns common child references by name.
    /// </summary>
    [ContextMenu("Auto Assign References From Children")]
    private void AutoAssignReferencesFromChildren()
    {
        ResolveReferences();
        ApplyVisualState(!IsUnlocked());
    }

    /// <summary>
    /// Applies the configured editor preview state manually.
    /// </summary>
    [ContextMenu("Apply Editor Preview")]
    private void ApplyEditorPreviewFromContextMenu()
    {
        ResolveReferences();
        ApplyVisualState(EditorPreviewLocked);
    }

    /// <summary>
    /// Applies the overlay state.
    /// </summary>
    /// <param name="IsLocked">True while the tier should block clicks.</param>
    private void ApplyVisualState(bool IsLocked)
    {
        GameObject ResolvedRoot = LockedRoot != null ? LockedRoot : gameObject;

        if (ResolvedRoot != null && HideRootWhenUnlocked)
        {
            ResolvedRoot.SetActive(IsLocked);
        }
        else if (ResolvedRoot != null && !ResolvedRoot.activeSelf)
        {
            ResolvedRoot.SetActive(true);
        }

        if (LockedCanvasGroup != null)
        {
            LockedCanvasGroup.alpha = IsLocked ? LockedAlpha : UnlockedAlpha;
            LockedCanvasGroup.interactable = IsLocked;
            LockedCanvasGroup.blocksRaycasts = IsLocked;
        }

        if (BlockingButton != null)
        {
            BlockingButton.interactable = IsLocked;
        }

        if (TitleText != null)
        {
            TitleText.text = string.Format(LockedTitleFormat, GetRequiredResearchTierLevel());
        }

        if (RequirementText != null)
        {
            RequirementText.text = string.Format(LockedRequirementFormat, GetRequiredResearchTierLevel());
        }
    }

    /// <summary>
    /// Resolves optional references from this object and its children.
    /// </summary>
    private void ResolveReferences()
    {
        if (LockedRoot == null)
        {
            LockedRoot = gameObject;
        }

        if (LockedCanvasGroup == null)
        {
            LockedCanvasGroup = GetComponent<CanvasGroup>();
        }

        if (BlockingButton == null)
        {
            BlockingButton = GetComponentInChildren<Button>(true);
        }

        TMP_Text[] ChildTexts = GetComponentsInChildren<TMP_Text>(true);

        for (int Index = 0; Index < ChildTexts.Length; Index++)
        {
            TMP_Text CurrentText = ChildTexts[Index];

            if (CurrentText == null)
            {
                continue;
            }

            string ChildName = CurrentText.gameObject.name.ToLowerInvariant();

            if (TitleText == null && (ChildName.Contains("title") || ChildName.Contains("tier")))
            {
                TitleText = CurrentText;
            }
            else if (RequirementText == null && (ChildName.Contains("requirement") || ChildName.Contains("description") || ChildName.Contains("locked")))
            {
                RequirementText = CurrentText;
            }
        }
    }

    /// <summary>
    /// Logs a message if debug logs are enabled.
    /// </summary>
    /// <param name="Message">Message to print.</param>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[ResearchSkillTreeTierGateUI] " + Message, this);
    }
}
