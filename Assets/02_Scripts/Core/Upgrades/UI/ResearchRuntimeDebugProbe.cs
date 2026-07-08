using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Small editor/runtime diagnostic helper for researcher UI setup.
/// It prints the exact state and block reason for manually assigned research definitions.
/// </summary>
[DisallowMultipleComponent]
public sealed class ResearchRuntimeDebugProbe : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Station used to query the same runtime service that the researcher UI uses.")]
    [SerializeField] private ResearchStation ResearchStation;

    [Tooltip("Fallback runtime service used when no station is available.")]
    [SerializeField] private ResearchRuntimeService ResearchRuntimeService;

    [Header("Definitions")]
    [Tooltip("Research definitions that should be diagnosed from the inspector context menu.")]
    [SerializeField] private List<ResearchDefinition> ResearchDefinitions = new();

    /// <summary>
    /// Prints a diagnostic report for every assigned research definition.
    /// </summary>
    [ContextMenu("Print Research State Report")]
    public void PrintResearchStateReport()
    {
        ResolveReferences();

        if (ResearchStation == null && ResearchRuntimeService == null)
        {
            Debug.LogWarning("[ResearchRuntimeDebugProbe] Missing ResearchStation and ResearchRuntimeService. The UI cannot resolve research states.", this);
            return;
        }

        for (int Index = 0; Index < ResearchDefinitions.Count; Index++)
        {
            ResearchDefinition Definition = ResearchDefinitions[Index];

            if (Definition == null)
            {
                Debug.LogWarning("[ResearchRuntimeDebugProbe] Missing ResearchDefinition at index " + Index + ".", this);
                continue;
            }

            ResearchRuntimeService.ResearchViewState ViewState = GetViewState(Definition);
            ResearchRuntimeService.ResearchBlockReason BlockReason = GetBlockReason(Definition);
            UpgradeDefinition AppliedUpgrade = Definition.GetAppliedUpgradeDefinition();

            Debug.Log(
                "[ResearchRuntimeDebugProbe] Research report | Name=" + Definition.GetDisplayName() +
                " | Id=" + Definition.GetResearchId() +
                " | CreditCost=" + Definition.GetCreditCost() +
                " | AppliedUpgrade=" + (AppliedUpgrade != null ? AppliedUpgrade.GetUpgradeId() : "NULL") +
                " | State=" + ViewState +
                " | BlockReason=" + BlockReason,
                Definition);
        }
    }

    /// <summary>
    /// Resolves missing references from the scene.
    /// </summary>
    private void ResolveReferences()
    {
        if (ResearchStation == null)
        {
            ResearchStation = FindFirstObjectByType<ResearchStation>();
        }

        if (ResearchRuntimeService == null)
        {
            ResearchRuntimeService = FindFirstObjectByType<ResearchRuntimeService>();
        }
    }

    /// <summary>
    /// Gets the current state of a research definition from the configured station or runtime service.
    /// </summary>
    /// <param name="Definition">Research definition to query.</param>
    private ResearchRuntimeService.ResearchViewState GetViewState(ResearchDefinition Definition)
    {
        if (ResearchStation != null)
        {
            return ResearchStation.GetResearchViewState(Definition);
        }

        return ResearchRuntimeService != null
            ? ResearchRuntimeService.GetResearchViewState(Definition)
            : ResearchRuntimeService.ResearchViewState.Invalid;
    }

    /// <summary>
    /// Gets the exact block reason of a research definition from the configured station or runtime service.
    /// </summary>
    /// <param name="Definition">Research definition to query.</param>
    private ResearchRuntimeService.ResearchBlockReason GetBlockReason(ResearchDefinition Definition)
    {
        if (ResearchStation != null)
        {
            return ResearchStation.GetResearchBlockReason(Definition);
        }

        return ResearchRuntimeService != null
            ? ResearchRuntimeService.GetResearchBlockReason(Definition)
            : ResearchRuntimeService.ResearchBlockReason.MissingResearch;
    }
}
