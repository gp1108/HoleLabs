using UnityEngine;

/// <summary>
/// Defines the forbidden runtime volume reserved for the central elevator.
/// The assigned trigger collider is treated as the authoritative exclusion shape for pipe placement.
/// The path bake and runtime build validation query this volume geometrically instead of relying on trigger callbacks.
/// </summary>
[DisallowMultipleComponent]
public sealed class PipeExclusionVolume : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Trigger collider that defines the maximum forbidden space occupied by the elevator.")]
    [SerializeField] private Collider TriggerCollider;

    [Header("Validation")]
    [Tooltip("If true, the component forces the configured collider to remain a trigger during validation.")]
    [SerializeField] private bool ForceTriggerCollider = true;

    [Header("Debug")]
    [Tooltip("Draws the trigger collider bounds in the Scene view when selected.")]
    [SerializeField] private bool DrawBoundsGizmo = true;

    [Tooltip("Color used to draw the exclusion bounds gizmo.")]
    [SerializeField] private Color BoundsGizmoColor = new Color(1f, 0.25f, 0.25f, 0.18f);

    /// <summary>
    /// Gets the configured trigger collider.
    /// </summary>
    public Collider GetTriggerCollider()
    {
        return TriggerCollider;
    }

    /// <summary>
    /// Returns true when the exclusion volume is ready to be queried.
    /// </summary>
    public bool IsConfigured()
    {
        return TriggerCollider != null;
    }

    /// <summary>
    /// Returns true when the provided world point is inside the exclusion volume or too close to its surface.
    /// </summary>
    /// <param name="WorldPoint">Pipe center point being validated.</param>
    /// <param name="Clearance">Additional clearance required outside the exclusion surface.</param>
    public bool IsPointBlocked(Vector3 WorldPoint, float Clearance)
    {
        if (TriggerCollider == null)
        {
            return false;
        }

        Vector3 ClosestPoint = TriggerCollider.ClosestPoint(WorldPoint);
        float SurfaceDistance = Vector3.Distance(WorldPoint, ClosestPoint);
        return SurfaceDistance <= Mathf.Max(0f, Clearance);
    }

    /// <summary>
    /// Returns true when any sampled point along the provided segment is inside the exclusion volume or too close to it.
    /// </summary>
    /// <param name="StartPoint">Segment start.</param>
    /// <param name="EndPoint">Segment end.</param>
    /// <param name="Clearance">Additional clearance required outside the exclusion surface.</param>
    /// <param name="SampleStep">Sampling step used along the segment.</param>
    public bool IsSegmentBlocked(Vector3 StartPoint, Vector3 EndPoint, float Clearance, float SampleStep)
    {
        float Distance = Vector3.Distance(StartPoint, EndPoint);
        int SampleCount = Mathf.Max(2, Mathf.CeilToInt(Distance / Mathf.Max(0.05f, SampleStep)));

        for (int SampleIndex = 0; SampleIndex <= SampleCount; SampleIndex++)
        {
            float T = SampleCount <= 0 ? 0f : (float)SampleIndex / SampleCount;
            Vector3 SamplePoint = Vector3.Lerp(StartPoint, EndPoint, T);

            if (IsPointBlocked(SamplePoint, Clearance))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Auto-resolves the local collider and keeps it configured as a trigger when requested.
    /// </summary>
    private void OnValidate()
    {
        if (TriggerCollider == null)
        {
            TriggerCollider = GetComponent<Collider>();
        }

        if (ForceTriggerCollider && TriggerCollider != null)
        {
            TriggerCollider.isTrigger = true;
        }
    }

    /// <summary>
    /// Draws the exclusion bounds for scene debugging.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!DrawBoundsGizmo || TriggerCollider == null)
        {
            return;
        }

        Gizmos.color = BoundsGizmoColor;
        Bounds TriggerBounds = TriggerCollider.bounds;
        Gizmos.DrawCube(TriggerBounds.center, TriggerBounds.size);
        Gizmos.color = new Color(BoundsGizmoColor.r, BoundsGizmoColor.g, BoundsGizmoColor.b, 1f);
        Gizmos.DrawWireCube(TriggerBounds.center, TriggerBounds.size);
    }
}
