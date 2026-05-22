using TMPro;
using UnityEngine;

/// <summary>
/// Fits a button width to a localized TMP label without rebuilding the surrounding UI layout.
/// This is intended for manually authored menus where the button must keep its current position
/// but expand when translated text becomes longer.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class LocalizedButtonWidthFitter : MonoBehaviour
{
    private enum WidthExpansionMode
    {
        PreserveLeftEdge = 0,
        PreserveCenter = 1,
        PreserveRightEdge = 2
    }

    [Header("References")]
    [Tooltip("RectTransform resized by this component. Usually the button root.")]
    [SerializeField] private RectTransform ButtonRoot;

    [Tooltip("TMP label used to calculate the required localized width.")]
    [SerializeField] private TMP_Text Label;

    [Header("Width")]
    [Tooltip("If true, the current button width captured on enable is used as the minimum width.")]
    [SerializeField] private bool UseCurrentWidthAsMinimum = false;

    [Tooltip("Manual minimum width used when Use Current Width As Minimum is false, or as an additional lower limit.")]
    [SerializeField] private float MinimumWidth = 0f;

    [Tooltip("Maximum width allowed for this button. Use 0 or lower to disable the maximum limit.")]
    [SerializeField] private float MaximumWidth = 0f;

    [Tooltip("If true, the script reads the label RectTransform left offset as the leading space before the text.")]
    [SerializeField] private bool UseLabelLeftOffsetAsLeadingPadding = true;

    [Tooltip("Manual space before the text when Use Label Left Offset As Leading Padding is disabled.")]
    [SerializeField] private float LeadingPadding = 70f;

    [Tooltip("Extra space added after the text.")]
    [SerializeField] private float TrailingPadding = 32f;

    [Header("Position")]
    [Tooltip("Defines which edge remains visually fixed when the button width changes.")]
    [SerializeField] private WidthExpansionMode ExpansionMode = WidthExpansionMode.PreserveLeftEdge;

    [Header("Update")]
    [Tooltip("If true, the fitter updates in edit mode as well as play mode.")]
    [SerializeField] private bool UpdateInEditMode = true;

    [Tooltip("If true, the fitter checks every frame. Disable if you only call FitWidth manually after localization changes.")]
    [SerializeField] private bool UpdateContinuously = true;

    private float CapturedInitialWidth;
    private string LastLabelText;
    private float LastAppliedWidth = -1f;

    /// <summary>
    /// Caches common references when the component is first added.
    /// </summary>
    private void Reset()
    {
        ButtonRoot = transform as RectTransform;
        Label = GetComponentInChildren<TMP_Text>(true);
        CaptureCurrentWidthAsMinimum();
    }

    /// <summary>
    /// Initializes the minimum width and fits the button once.
    /// </summary>
    private void OnEnable()
    {
        if (ButtonRoot == null)
        {
            ButtonRoot = transform as RectTransform;
        }

        if (Label == null)
        {
            Label = GetComponentInChildren<TMP_Text>(true);
        }

        CaptureCurrentWidthAsMinimum();
        FitWidth();
    }

    /// <summary>
    /// Keeps the width valid after text changes, localization changes or layout rebuilds.
    /// </summary>
    private void LateUpdate()
    {
        if (!Application.isPlaying && !UpdateInEditMode)
        {
            return;
        }

        if (!UpdateContinuously)
        {
            return;
        }

        if (Label == null)
        {
            return;
        }

        if (LastLabelText == Label.text && Mathf.Approximately(LastAppliedWidth, GetCurrentWidth()))
        {
            return;
        }

        FitWidth();
    }

    /// <summary>
    /// Captures the current button width as the base minimum width.
    /// </summary>
    [ContextMenu("Capture Current Width As Minimum")]
    public void CaptureCurrentWidthAsMinimum()
    {
        if (ButtonRoot == null)
        {
            ButtonRoot = transform as RectTransform;
        }

        if (ButtonRoot == null)
        {
            return;
        }

        CapturedInitialWidth = Mathf.Max(0f, ButtonRoot.rect.width);
    }

    /// <summary>
    /// Recalculates the required button width from the current localized label text.
    /// </summary>
    [ContextMenu("Fit Width")]
    public void FitWidth()
    {
        if (ButtonRoot == null || Label == null)
        {
            return;
        }

        Label.ForceMeshUpdate();

        float LabelPreferredWidth = Label.GetPreferredValues(Label.text, Mathf.Infinity, Mathf.Infinity).x;
        float LeadingSpace = ResolveLeadingPadding();
        float DesiredWidth = LeadingSpace + LabelPreferredWidth + Mathf.Max(0f, TrailingPadding);

        float EffectiveMinimumWidth = Mathf.Max(0f, MinimumWidth);

        if (UseCurrentWidthAsMinimum)
        {
            EffectiveMinimumWidth = Mathf.Max(EffectiveMinimumWidth, CapturedInitialWidth);
        }

        DesiredWidth = Mathf.Max(EffectiveMinimumWidth, DesiredWidth);

        if (MaximumWidth > 0f)
        {
            DesiredWidth = Mathf.Min(MaximumWidth, DesiredWidth);
        }

        ApplyWidth(DesiredWidth);
        LastLabelText = Label.text;
        LastAppliedWidth = DesiredWidth;
    }

    /// <summary>
    /// Gets the current button width.
    /// </summary>
    private float GetCurrentWidth()
    {
        return ButtonRoot != null ? ButtonRoot.rect.width : 0f;
    }

    /// <summary>
    /// Calculates the leading space before the text.
    /// </summary>
    private float ResolveLeadingPadding()
    {
        if (!UseLabelLeftOffsetAsLeadingPadding)
        {
            return Mathf.Max(0f, LeadingPadding);
        }

        RectTransform LabelRectTransform = Label.transform as RectTransform;

        if (LabelRectTransform == null)
        {
            return Mathf.Max(0f, LeadingPadding);
        }

        return Mathf.Max(0f, LabelRectTransform.offsetMin.x);
    }

    /// <summary>
    /// Applies a new width while preserving the configured visual edge.
    /// </summary>
    /// <param name="NewWidth">Target button width.</param>
    private void ApplyWidth(float NewWidth)
    {
        float OldWidth = ButtonRoot.rect.width;
        float WidthDelta = NewWidth - OldWidth;
        Vector2 AnchoredPosition = ButtonRoot.anchoredPosition;

        ButtonRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, NewWidth);

        switch (ExpansionMode)
        {
            case WidthExpansionMode.PreserveLeftEdge:
                AnchoredPosition.x += ButtonRoot.pivot.x * WidthDelta;
                break;

            case WidthExpansionMode.PreserveRightEdge:
                AnchoredPosition.x -= (1f - ButtonRoot.pivot.x) * WidthDelta;
                break;

            case WidthExpansionMode.PreserveCenter:
                break;
        }

        ButtonRoot.anchoredPosition = AnchoredPosition;
    }
}