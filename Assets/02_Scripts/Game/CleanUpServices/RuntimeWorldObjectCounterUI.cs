using TMPro;
using UnityEngine;

/// <summary>
/// Lightweight UI bridge that displays runtime physics object counts from RuntimeWorldObjectRegistry.
/// This can be placed inside an options or emergency menu without coupling the registry to a specific UI layout.
/// </summary>
[DisallowMultipleComponent]
public sealed class RuntimeWorldObjectCounterUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Registry used to read active object counts. If empty, the first registry in the scene is used.")]
    [SerializeField] private RuntimeWorldObjectRegistry Registry;

    [Tooltip("Text used to show active ore count.")]
    [SerializeField] private TMP_Text ActiveOreCountText;

    [Tooltip("Text used to show active money pickup count.")]
    [SerializeField] private TMP_Text ActiveMoneyCountText;

    [Tooltip("Text used to show active runtime world item count.")]
    [SerializeField] private TMP_Text RuntimeWorldItemCountText;

    [Tooltip("Text used to show total tracked physics object count.")]
    [SerializeField] private TMP_Text TotalTrackedObjectCountText;

    [Header("Format")]
    [Tooltip("Prefix written before the ore count.")]
    [SerializeField] private string OrePrefix = "Ores: ";

    [Tooltip("Prefix written before the money pickup count.")]
    [SerializeField] private string MoneyPrefix = "Money: ";

    [Tooltip("Prefix written before the runtime world item count.")]
    [SerializeField] private string RuntimeWorldItemPrefix = "Runtime Items: ";

    [Tooltip("Prefix written before the total tracked object count.")]
    [SerializeField] private string TotalPrefix = "Physics Objects: ";

    [Header("Refresh")]
    [Tooltip("If true, the UI refreshes automatically while enabled.")]
    [SerializeField] private bool RefreshAutomatically = true;

    [Tooltip("Seconds between automatic refreshes.")]
    [SerializeField] private float RefreshInterval = 0.25f;

    private float NextRefreshTime;

    /// <summary>
    /// Resolves missing references when the UI becomes active.
    /// </summary>
    private void OnEnable()
    {
        ResolveReferences();
        Refresh();
    }

    /// <summary>
    /// Refreshes displayed counts at a configurable interval.
    /// </summary>
    private void Update()
    {
        if (!RefreshAutomatically)
        {
            return;
        }

        if (Time.unscaledTime < NextRefreshTime)
        {
            return;
        }

        Refresh();
    }

    /// <summary>
    /// Refreshes the displayed registry counts immediately.
    /// This is suitable for Unity UI button OnClick events.
    /// </summary>
    public void Refresh()
    {
        ResolveReferences();
        NextRefreshTime = Time.unscaledTime + Mathf.Max(0.05f, RefreshInterval);

        if (Registry == null)
        {
            SetText(ActiveOreCountText, OrePrefix + "--");
            SetText(ActiveMoneyCountText, MoneyPrefix + "--");
            SetText(RuntimeWorldItemCountText, RuntimeWorldItemPrefix + "--");
            SetText(TotalTrackedObjectCountText, TotalPrefix + "--");
            return;
        }

        SetText(ActiveOreCountText, OrePrefix + Registry.GetActiveOreCount());
        SetText(ActiveMoneyCountText, MoneyPrefix + Registry.GetActiveMoneyPickupCount());
        SetText(RuntimeWorldItemCountText, RuntimeWorldItemPrefix + Registry.GetActiveRuntimeWorldItemCount());
        SetText(TotalTrackedObjectCountText, TotalPrefix + Registry.GetTrackedPhysicsObjectCount());
    }

    /// <summary>
    /// Assigns text only when the target label exists.
    /// </summary>
    private void SetText(TMP_Text TargetText, string Value)
    {
        if (TargetText == null)
        {
            return;
        }

        TargetText.text = Value;
    }

    /// <summary>
    /// Resolves missing references allowed to be auto-bound.
    /// </summary>
    private void ResolveReferences()
    {
        if (Registry == null)
        {
            Registry = RuntimeWorldObjectRegistry.Instance;

            if (Registry == null)
            {
                Registry = FindFirstObjectByType<RuntimeWorldObjectRegistry>();
            }
        }
    }
}
