using UnityEngine;

/// <summary>
/// Provides frame delta information for systems that need to inherit platform motion.
/// </summary>
public interface IMotionCarrier
{
    /// <summary>
    /// World displacement applied by the carrier during the latest frame.
    /// </summary>
    Vector3 DeltaPosition { get; }
}