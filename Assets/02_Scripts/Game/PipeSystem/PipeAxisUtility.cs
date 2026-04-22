using UnityEngine;

/// <summary>
/// Shared geometric helpers used by the cave pipe graph, pathfinding and transport systems.
/// The entire solution assumes a central build axis that represents the elevator line.
/// </summary>
public static class PipeAxisUtility
{
    /// <summary>
    /// Gets the closest point on the infinite axis line to the provided world position.
    /// </summary>
    /// <param name="AxisTransform">Axis transform that defines the origin and up direction.</param>
    /// <param name="WorldPoint">World point projected onto the axis.</param>
    /// <returns>Closest point on the infinite axis line.</returns>
    public static Vector3 GetClosestPointOnAxis(Transform AxisTransform, Vector3 WorldPoint)
    {
        if (AxisTransform == null)
        {
            return WorldPoint;
        }

        Vector3 AxisOrigin = AxisTransform.position;
        Vector3 AxisDirection = AxisTransform.up.normalized;
        float DistanceOnAxis = Vector3.Dot(WorldPoint - AxisOrigin, AxisDirection);
        return AxisOrigin + (AxisDirection * DistanceOnAxis);
    }

    /// <summary>
    /// Returns the radial direction that points from the axis towards the provided world point.
    /// </summary>
    /// <param name="AxisTransform">Axis transform that defines the cave center line.</param>
    /// <param name="WorldPoint">World point used to compute the radial direction.</param>
    /// <returns>Normalized radial direction. Falls back to the axis right vector when degenerate.</returns>
    public static Vector3 GetRadialDirectionFromAxis(Transform AxisTransform, Vector3 WorldPoint)
    {
        if (AxisTransform == null)
        {
            return Vector3.right;
        }

        Vector3 AxisPoint = GetClosestPointOnAxis(AxisTransform, WorldPoint);
        Vector3 Radial = WorldPoint - AxisPoint;

        if (Radial.sqrMagnitude <= 0.000001f)
        {
            return AxisTransform.right.sqrMagnitude > 0.000001f ? AxisTransform.right.normalized : Vector3.right;
        }

        return Radial.normalized;
    }

    /// <summary>
    /// Returns the inward support direction used to offset the pipe away from the wall and toward the cave interior.
    /// </summary>
    /// <param name="AxisTransform">Axis transform that defines the cave center line.</param>
    /// <param name="SurfacePoint">Wall contact point.</param>
    /// <returns>Normalized inward direction from the wall toward the axis.</returns>
    public static Vector3 GetInwardDirectionToAxis(Transform AxisTransform, Vector3 SurfacePoint)
    {
        return -GetRadialDirectionFromAxis(AxisTransform, SurfacePoint);
    }

    /// <summary>
    /// Returns the radial distance from the axis to the provided point.
    /// </summary>
    /// <param name="AxisTransform">Axis transform that defines the cave center line.</param>
    /// <param name="WorldPoint">World point whose radial distance should be measured.</param>
    /// <returns>Distance from the point to the axis line.</returns>
    public static float GetDistanceToAxis(Transform AxisTransform, Vector3 WorldPoint)
    {
        if (AxisTransform == null)
        {
            return 0f;
        }

        Vector3 AxisPoint = GetClosestPointOnAxis(AxisTransform, WorldPoint);
        return Vector3.Distance(AxisPoint, WorldPoint);
    }

    /// <summary>
    /// Builds a stable segment up vector that stays orthogonal to the travel direction while remaining wall-aware.
    /// </summary>
    /// <param name="Forward">Segment travel direction.</param>
    /// <param name="PreferredUp">Preferred wall-based up or support direction.</param>
    /// <returns>Orthogonal up vector suitable for LookRotation.</returns>
    public static Vector3 BuildFrameUp(Vector3 Forward, Vector3 PreferredUp)
    {
        Vector3 NormalizedForward = Forward.sqrMagnitude > 0.000001f ? Forward.normalized : Vector3.forward;
        Vector3 RawUp = PreferredUp.sqrMagnitude > 0.000001f ? PreferredUp.normalized : Vector3.up;

        Vector3 Right = Vector3.Cross(RawUp, NormalizedForward);
        if (Right.sqrMagnitude <= 0.000001f)
        {
            Right = Vector3.Cross(Vector3.up, NormalizedForward);
        }

        if (Right.sqrMagnitude <= 0.000001f)
        {
            Right = Vector3.Cross(Vector3.right, NormalizedForward);
        }

        Right.Normalize();
        return Vector3.Cross(NormalizedForward, Right).normalized;
    }
}
