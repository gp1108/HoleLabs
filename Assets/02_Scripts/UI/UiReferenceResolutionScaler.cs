using UnityEngine;

/// <summary>
/// Keeps a UI root in a fixed reference design size and scales it uniformly
/// so all child RectTransforms preserve their authored layout across aspect ratios.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class UiReferenceResolutionScaler : MonoBehaviour
{
    [Header("References")]
    [Tooltip("RectTransform that defines the available area where the scaled UI must fit. Usually MenuSafeRoot.")]
    [SerializeField] private RectTransform AvailableArea;

    [Tooltip("RectTransform that will keep the fixed reference size and receive the uniform scale. Usually this transform.")]
    [SerializeField] private RectTransform ScaledRoot;

    [Header("Reference Design")]
    [Tooltip("Reference width used when the UI was authored correctly.")]
    [SerializeField] private float ReferenceWidth = 1920f;

    [Tooltip("Reference height used when the UI was authored correctly.")]
    [SerializeField] private float ReferenceHeight = 1080f;

    [Header("Fitting")]
    [Tooltip("Extra multiplier applied after fitting. Use values below 1 to add breathing room.")]
    [SerializeField] private float FitPaddingMultiplier = 0.96f;

    [Tooltip("Minimum allowed scale for the menu root.")]
    [SerializeField] private float MinimumScale = 0.25f;

    [Tooltip("Maximum allowed scale for the menu root.")]
    [SerializeField] private float MaximumScale = 1.25f;

    [Tooltip("If true, the scaled root is kept centered in the available area.")]
    [SerializeField] private bool KeepCentered = true;

    /// <summary>
    /// Applies the fixed reference size and uniform scale whenever the component is enabled.
    /// </summary>
    private void OnEnable()
    {
        ApplyScale();
    }

    /// <summary>
    /// Keeps the menu stable in edit mode and after resolution changes.
    /// </summary>
    private void LateUpdate()
    {
        ApplyScale();
    }

    /// <summary>
    /// Recalculates scale when this RectTransform changes dimensions.
    /// </summary>
    private void OnRectTransformDimensionsChange()
    {
        ApplyScale();
    }

    /// <summary>
    /// Fits the scaled root inside the available area while preserving its reference design aspect.
    /// </summary>
    private void ApplyScale()
    {
        if (AvailableArea == null)
        {
            return;
        }

        if (ScaledRoot == null)
        {
            ScaledRoot = transform as RectTransform;
        }

        if (ScaledRoot == null)
        {
            return;
        }

        float SafeReferenceWidth = Mathf.Max(1f, ReferenceWidth);
        float SafeReferenceHeight = Mathf.Max(1f, ReferenceHeight);

        ScaledRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, SafeReferenceWidth);
        ScaledRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, SafeReferenceHeight);

        float AvailableWidth = Mathf.Max(1f, AvailableArea.rect.width);
        float AvailableHeight = Mathf.Max(1f, AvailableArea.rect.height);

        float WidthScale = AvailableWidth / SafeReferenceWidth;
        float HeightScale = AvailableHeight / SafeReferenceHeight;
        float TargetScale = Mathf.Min(WidthScale, HeightScale) * Mathf.Max(0.01f, FitPaddingMultiplier);

        TargetScale = Mathf.Clamp(TargetScale, MinimumScale, MaximumScale);
        ScaledRoot.localScale = new Vector3(TargetScale, TargetScale, 1f);

        if (KeepCentered)
        {
            ScaledRoot.anchorMin = new Vector2(0.5f, 0.5f);
            ScaledRoot.anchorMax = new Vector2(0.5f, 0.5f);
            ScaledRoot.pivot = new Vector2(0.5f, 0.5f);
            ScaledRoot.anchoredPosition = Vector2.zero;
        }
    }
}