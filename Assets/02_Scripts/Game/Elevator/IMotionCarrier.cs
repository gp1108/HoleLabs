using UnityEngine;

/// <summary>
/// Provides incremental carrier motion data for systems that need to inherit platform movement.
/// </summary>
public interface IMotionCarrier
{
    /// <summary>
    /// World displacement applied by the carrier root during the latest frame.
    /// </summary>
    Vector3 DeltaPosition { get; }

    /// <summary>
    /// World rotation delta applied by the carrier during the latest frame.
    /// </summary>
    Quaternion DeltaRotation { get; }

    /// <summary>
    /// Returns how much a given world point was displaced by the carrier between the previous
    /// and current frame poses.
    /// </summary>
    /// <param name="WorldPoint">Point to evaluate in world space.</param>
    /// <returns>World-space displacement of that point caused by the carrier motion.</returns>
    Vector3 GetWorldPointDelta(Vector3 WorldPoint);
}