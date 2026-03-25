using UnityEngine;

/// <summary>
/// Visual follower that mirrors the authoritative physical elevator pose.
/// It also exposes frame delta so CharacterController passengers can inherit visual platform motion.
/// </summary>
[DefaultExecutionOrder(100)]
public sealed class ElevatorVisualFollower : MonoBehaviour, IMotionCarrier
{
    [Header("References")]
    [Tooltip("Authoritative physical elevator motor.")]
    [SerializeField] private ElevatorPhysicalMotor SourceMotor;

    [Tooltip("If true, this visual root also copies the source rotation.")]
    [SerializeField] private bool FollowRotation = true;

    /// <summary>
    /// World displacement applied by the visual platform during the latest frame.
    /// </summary>
    public Vector3 DeltaPosition { get; private set; }

    /// <summary>
    /// Cached previous world position used to calculate frame delta.
    /// </summary>
    private Vector3 LastPosition;

    /// <summary>
    /// Initializes the cached visual position.
    /// </summary>
    private void Awake()
    {
        LastPosition = transform.position;
        DeltaPosition = Vector3.zero;
    }

    /// <summary>
    /// Mirrors the physical elevator pose after simulation and before rendering.
    /// </summary>
    private void LateUpdate()
    {
        if (SourceMotor == null)
        {
            DeltaPosition = Vector3.zero;
            return;
        }

        Vector3 PreviousPosition = transform.position;

        transform.position = SourceMotor.transform.position;

        if (FollowRotation)
        {
            transform.rotation = SourceMotor.transform.rotation;
        }

        DeltaPosition = transform.position - PreviousPosition;
        LastPosition = transform.position;
    }
}