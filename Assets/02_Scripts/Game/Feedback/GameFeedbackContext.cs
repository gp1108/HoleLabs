using UnityEngine;

/// <summary>
/// Runtime context passed to a feedback emitter when a gameplay event happens.
/// It carries optional world position, surface normal, source, target and intensity data.
/// </summary>
public readonly struct GameFeedbackContext
{
    /// <summary>
    /// Whether this context contains a meaningful world position.
    /// </summary>
    public readonly bool HasPosition;

    /// <summary>
    /// World position where feedback should be emitted.
    /// </summary>
    public readonly Vector3 Position;

    /// <summary>
    /// Whether this context contains a meaningful surface normal.
    /// </summary>
    public readonly bool HasNormal;

    /// <summary>
    /// Surface normal used to align particles or directional feedback.
    /// </summary>
    public readonly Vector3 Normal;

    /// <summary>
    /// Transform that caused the feedback event.
    /// </summary>
    public readonly Transform SourceTransform;

    /// <summary>
    /// Transform affected by the feedback event.
    /// </summary>
    public readonly Transform TargetTransform;

    /// <summary>
    /// Optional parent used when feedback entries explicitly request parented particles.
    /// </summary>
    public readonly Transform ParentTransform;

    /// <summary>
    /// Intensity multiplier used by Feel feedbacks and other scalable effects.
    /// </summary>
    public readonly float Intensity;

    /// <summary>
    /// Creates a complete feedback context.
    /// </summary>
    /// <param name="HasPositionValue">Whether the position should be used.</param>
    /// <param name="PositionValue">World position of the event.</param>
    /// <param name="HasNormalValue">Whether the normal should be used.</param>
    /// <param name="NormalValue">Surface normal of the event.</param>
    /// <param name="SourceTransformValue">Transform that caused the event.</param>
    /// <param name="TargetTransformValue">Transform affected by the event.</param>
    /// <param name="ParentTransformValue">Optional parent for parented effects.</param>
    /// <param name="IntensityValue">Feedback intensity multiplier.</param>
    public GameFeedbackContext(
        bool HasPositionValue,
        Vector3 PositionValue,
        bool HasNormalValue,
        Vector3 NormalValue,
        Transform SourceTransformValue,
        Transform TargetTransformValue,
        Transform ParentTransformValue,
        float IntensityValue)
    {
        HasPosition = HasPositionValue;
        Position = PositionValue;
        HasNormal = HasNormalValue && NormalValue.sqrMagnitude > 0.0001f;
        Normal = HasNormal ? NormalValue.normalized : Vector3.up;
        SourceTransform = SourceTransformValue;
        TargetTransform = TargetTransformValue;
        ParentTransform = ParentTransformValue;
        Intensity = Mathf.Max(0f, IntensityValue);
    }

    /// <summary>
    /// Creates a feedback context from a raycast hit.
    /// </summary>
    /// <param name="HitInfo">Raycast hit that produced the feedback.</param>
    /// <param name="SourceTransform">Transform that caused the event.</param>
    /// <param name="Intensity">Feedback intensity multiplier.</param>
    /// <returns>Context containing hit position, normal and target transform.</returns>
    public static GameFeedbackContext FromRaycastHit(RaycastHit HitInfo, Transform SourceTransform, float Intensity = 1f)
    {
        Transform TargetTransform = HitInfo.collider != null ? HitInfo.collider.transform : null;

        return new GameFeedbackContext(
            true,
            HitInfo.point,
            true,
            HitInfo.normal,
            SourceTransform,
            TargetTransform,
            null,
            Intensity);
    }

    /// <summary>
    /// Creates a feedback context from a world position.
    /// </summary>
    /// <param name="Position">World position where feedback should be emitted.</param>
    /// <param name="SourceTransform">Transform that caused the event.</param>
    /// <param name="Intensity">Feedback intensity multiplier.</param>
    /// <returns>Context containing a world position without a specific normal.</returns>
    public static GameFeedbackContext FromPosition(Vector3 Position, Transform SourceTransform, float Intensity = 1f)
    {
        return new GameFeedbackContext(
            true,
            Position,
            false,
            Vector3.up,
            SourceTransform,
            null,
            null,
            Intensity);
    }

    /// <summary>
    /// Creates a feedback context from a transform.
    /// </summary>
    /// <param name="SourceTransform">Transform used as source and fallback position.</param>
    /// <param name="Intensity">Feedback intensity multiplier.</param>
    /// <returns>Context using the transform position.</returns>
    public static GameFeedbackContext FromTransform(Transform SourceTransform, float Intensity = 1f)
    {
        Vector3 Position = SourceTransform != null ? SourceTransform.position : Vector3.zero;

        return new GameFeedbackContext(
            SourceTransform != null,
            Position,
            false,
            Vector3.up,
            SourceTransform,
            null,
            null,
            Intensity);
    }
}
