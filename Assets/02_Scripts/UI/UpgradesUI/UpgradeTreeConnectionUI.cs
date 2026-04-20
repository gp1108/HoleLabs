using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Simple UI connection line used to link two research tree nodes.
/// This component is intentionally lightweight so it can be replaced later by a more stylized implementation.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public sealed class UpgradeTreeConnectionUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Optional image used as the rendered line body.")]
    [SerializeField] private Image LineImage;

    [Tooltip("Optional explicit RectTransform used as the line root. If null, this component RectTransform is used.")]
    [SerializeField] private RectTransform LineRectTransform;

    [Header("Appearance")]
    [Tooltip("Thickness applied to the generated line.")]
    [SerializeField] private float Thickness = 4f;

    /// <summary>
    /// Updates the line so it visually connects the provided local points.
    /// </summary>
    public void SetEndpoints(Vector2 StartLocalPosition, Vector2 EndLocalPosition)
    {
        RectTransform RectTransform = GetLineRectTransform();

        if (RectTransform == null)
        {
            return;
        }

        Vector2 Delta = EndLocalPosition - StartLocalPosition;
        float Length = Delta.magnitude;
        float Angle = Mathf.Atan2(Delta.y, Delta.x) * Mathf.Rad2Deg;

        RectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        RectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        RectTransform.pivot = new Vector2(0f, 0.5f);
        RectTransform.anchoredPosition = StartLocalPosition;
        RectTransform.sizeDelta = new Vector2(Length, Thickness);
        RectTransform.localRotation = Quaternion.Euler(0f, 0f, Angle);

        if (LineImage != null)
        {
            LineImage.raycastTarget = false;
        }
    }

    /// <summary>
    /// Gets the RectTransform used as the rendered line root.
    /// </summary>
    private RectTransform GetLineRectTransform()
    {
        if (LineRectTransform != null)
        {
            return LineRectTransform;
        }

        return transform as RectTransform;
    }
}