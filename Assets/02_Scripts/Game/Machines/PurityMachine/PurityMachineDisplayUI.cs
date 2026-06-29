using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Simple screen presenter for the purity machine.
/// It intentionally contains no processing rules; PurityMachineController sends already-resolved state text and values.
/// The primary feedback channel is the panel color so error states can be read quickly without relying only on text.
/// </summary>
public sealed class PurityMachineDisplayUI : MonoBehaviour
{
    /// <summary>
    /// Visual severity used to color the machine screen panel.
    /// </summary>
    public enum DisplaySeverity
    {
        Neutral = 0,
        Ready = 1,
        Processing = 2,
        Warning = 3,
        Error = 4,
        Complete = 5
    }

    [Header("Root")]
    [Tooltip("Optional screen root enabled while the display is active. If empty, this object is used.")]
    [SerializeField] private GameObject ScreenRoot;

    [Header("Panel Feedback")]
    [Tooltip("Optional UI graphic used as the main colored panel feedback. Assign the screen background Image here.")]
    [SerializeField] private Graphic PanelGraphic;

    [Tooltip("Optional mesh renderer used as the main colored panel feedback for world-space mesh screens.")]
    [SerializeField] private Renderer PanelRenderer;

    [Tooltip("Material color property driven on the optional panel renderer. URP lit materials usually use _BaseColor, legacy materials usually use _Color.")]
    [SerializeField] private string PanelRendererColorProperty = "_BaseColor";

    [Tooltip("If true, status and detail text also receive the severity color. Keep this disabled when the panel color should be the main feedback.")]
    [SerializeField] private bool ApplySeverityColorToText = false;

    [Header("Text Fields")]
    [Tooltip("Main status text shown at the top of the purity machine screen.")]
    [SerializeField] private TMP_Text StatusText;

    [Tooltip("Optional detail text used for explanations and validation errors.")]
    [SerializeField] private TMP_Text DetailText;

    [Tooltip("Optional target ore summary text.")]
    [SerializeField] private TMP_Text TargetText;

    [Tooltip("Optional sacrifice ore summary text.")]
    [SerializeField] private TMP_Text SacrificeText;

    [Tooltip("Optional preview text showing the predicted final purity.")]
    [SerializeField] private TMP_Text PreviewText;

    [Header("Panel Colors")]
    [Tooltip("Panel color used for neutral idle messages.")]
    [SerializeField] private Color NeutralPanelColor = new Color(0.08f, 0.10f, 0.12f, 1f);

    [Tooltip("Panel color used when the machine is ready.")]
    [SerializeField] private Color ReadyPanelColor = new Color(0.08f, 0.24f, 0.12f, 1f);

    [Tooltip("Panel color used while the machine is processing.")]
    [SerializeField] private Color ProcessingPanelColor = new Color(0.05f, 0.16f, 0.24f, 1f);

    [Tooltip("Panel color used for warning states.")]
    [SerializeField] private Color WarningPanelColor = new Color(0.32f, 0.24f, 0.05f, 1f);

    [Tooltip("Panel color used for hard errors.")]
    [SerializeField] private Color ErrorPanelColor = new Color(0.34f, 0.04f, 0.04f, 1f);

    [Tooltip("Panel color used when processing completes successfully.")]
    [SerializeField] private Color CompletePanelColor = new Color(0.08f, 0.24f, 0.12f, 1f);

    [Header("Text Colors")]
    [Tooltip("Text color used when Apply Severity Color To Text is disabled.")]
    [SerializeField] private Color DefaultTextColor = Color.white;

    [Tooltip("Text color used for neutral idle messages when text coloring is enabled.")]
    [SerializeField] private Color NeutralTextColor = Color.white;

    [Tooltip("Text color used when the machine is ready and text coloring is enabled.")]
    [SerializeField] private Color ReadyTextColor = Color.white;

    [Tooltip("Text color used while the machine is processing and text coloring is enabled.")]
    [SerializeField] private Color ProcessingTextColor = Color.white;

    [Tooltip("Text color used for warning states when text coloring is enabled.")]
    [SerializeField] private Color WarningTextColor = Color.white;

    [Tooltip("Text color used for hard errors when text coloring is enabled.")]
    [SerializeField] private Color ErrorTextColor = Color.white;

    [Tooltip("Text color used when processing completes and text coloring is enabled.")]
    [SerializeField] private Color CompleteTextColor = Color.white;

    [Header("Formatting")]
    [Tooltip("Suffix appended to purity values.")]
    [SerializeField] private string PuritySuffix = "%";

    /// <summary>
    /// Reusable material property block used to color mesh screen renderers without editing shared materials.
    /// </summary>
    private MaterialPropertyBlock PanelPropertyBlock;

    /// <summary>
    /// Shows or hides the display root.
    /// </summary>
    /// <param name="IsVisible">True to show the screen.</param>
    public void SetVisible(bool IsVisible)
    {
        if (ScreenRoot != null)
        {
            ScreenRoot.SetActive(IsVisible);
            return;
        }

        gameObject.SetActive(IsVisible);
    }

    /// <summary>
    /// Displays a complete purity machine state snapshot.
    /// </summary>
    /// <param name="Severity">Severity used to color the display panel.</param>
    /// <param name="Status">Main status message.</param>
    /// <param name="Detail">Optional detail message.</param>
    /// <param name="TargetSummary">Optional target ore summary.</param>
    /// <param name="SacrificeSummary">Optional sacrifice summary.</param>
    /// <param name="PreviewSummary">Optional preview summary.</param>
    public void ShowState(
        DisplaySeverity Severity,
        string Status,
        string Detail,
        string TargetSummary,
        string SacrificeSummary,
        string PreviewSummary)
    {
        SetText(StatusText, Status);
        SetText(DetailText, Detail);
        SetText(TargetText, TargetSummary);
        SetText(SacrificeText, SacrificeSummary);
        SetText(PreviewText, PreviewSummary);

        ApplyPanelColor(ResolvePanelColor(Severity));
        ApplyTextColor(ApplySeverityColorToText ? ResolveTextColor(Severity) : DefaultTextColor);
    }

    /// <summary>
    /// Displays an idle state with no valid current target.
    /// </summary>
    public void ShowIdle()
    {
        ShowState(
            DisplaySeverity.Neutral,
            "Idle",
            "Insert one target ore and sacrifice ores.",
            "Target: -",
            "Sacrifices: 0",
            "Preview: -");
    }

    /// <summary>
    /// Formats a purity value consistently for this screen.
    /// </summary>
    /// <param name="PurityPercent">Purity percent to format.</param>
    /// <returns>Formatted purity string.</returns>
    public string FormatPurity(float PurityPercent)
    {
        return Mathf.Clamp(PurityPercent, 0f, 100f).ToString("0.#") + PuritySuffix;
    }

    /// <summary>
    /// Assigns text if the target field exists.
    /// </summary>
    /// <param name="TextField">Text field to update.</param>
    /// <param name="Value">Value assigned to the text field.</param>
    private void SetText(TMP_Text TextField, string Value)
    {
        if (TextField != null)
        {
            TextField.text = Value ?? string.Empty;
        }
    }

    /// <summary>
    /// Applies one color to all text fields.
    /// </summary>
    /// <param name="ColorValue">Color to apply.</param>
    private void ApplyTextColor(Color ColorValue)
    {
        ApplyColor(StatusText, ColorValue);
        ApplyColor(DetailText, ColorValue);
        ApplyColor(TargetText, ColorValue);
        ApplyColor(SacrificeText, ColorValue);
        ApplyColor(PreviewText, ColorValue);
    }

    /// <summary>
    /// Applies a color if the target field exists.
    /// </summary>
    /// <param name="TextField">Text field to color.</param>
    /// <param name="ColorValue">Color to apply.</param>
    private void ApplyColor(TMP_Text TextField, Color ColorValue)
    {
        if (TextField != null)
        {
            TextField.color = ColorValue;
        }
    }

    /// <summary>
    /// Applies the resolved severity color to the configured screen panel targets.
    /// </summary>
    /// <param name="ColorValue">Panel color to apply.</param>
    private void ApplyPanelColor(Color ColorValue)
    {
        if (PanelGraphic != null)
        {
            PanelGraphic.color = ColorValue;
        }

        if (PanelRenderer == null || string.IsNullOrWhiteSpace(PanelRendererColorProperty))
        {
            return;
        }

        if (PanelPropertyBlock == null)
        {
            PanelPropertyBlock = new MaterialPropertyBlock();
        }

        PanelRenderer.GetPropertyBlock(PanelPropertyBlock);
        PanelPropertyBlock.SetColor(PanelRendererColorProperty, ColorValue);
        PanelRenderer.SetPropertyBlock(PanelPropertyBlock);
    }

    /// <summary>
    /// Resolves the panel color for a display severity.
    /// </summary>
    /// <param name="Severity">Severity to resolve.</param>
    /// <returns>Configured panel color.</returns>
    private Color ResolvePanelColor(DisplaySeverity Severity)
    {
        switch (Severity)
        {
            case DisplaySeverity.Ready:
                return ReadyPanelColor;
            case DisplaySeverity.Processing:
                return ProcessingPanelColor;
            case DisplaySeverity.Warning:
                return WarningPanelColor;
            case DisplaySeverity.Error:
                return ErrorPanelColor;
            case DisplaySeverity.Complete:
                return CompletePanelColor;
            default:
                return NeutralPanelColor;
        }
    }

    /// <summary>
    /// Resolves the text color for a display severity when text severity coloring is enabled.
    /// </summary>
    /// <param name="Severity">Severity to resolve.</param>
    /// <returns>Configured text color.</returns>
    private Color ResolveTextColor(DisplaySeverity Severity)
    {
        switch (Severity)
        {
            case DisplaySeverity.Ready:
                return ReadyTextColor;
            case DisplaySeverity.Processing:
                return ProcessingTextColor;
            case DisplaySeverity.Warning:
                return WarningTextColor;
            case DisplaySeverity.Error:
                return ErrorTextColor;
            case DisplaySeverity.Complete:
                return CompleteTextColor;
            default:
                return NeutralTextColor;
        }
    }
}
