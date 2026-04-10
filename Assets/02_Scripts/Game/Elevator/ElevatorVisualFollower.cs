using UnityEngine;

/// <summary>
/// Visual follower that mirrors the authoritative physical elevator pose.
/// It exposes both frame displacement and full point-motion queries so CharacterController
/// passengers can inherit correct movement from translating and rotating platforms.
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
    /// World displacement applied by the visual platform root during the latest frame.
    /// </summary>
    public Vector3 DeltaPosition { get; private set; }

    /// <summary>
    /// World rotation delta applied by the visual platform during the latest frame.
    /// </summary>
    public Quaternion DeltaRotation { get; private set; }

    /// <summary>
    /// Cached previous world position.
    /// </summary>
    private Vector3 PreviousWorldPosition;

    /// <summary>
    /// Cached previous world rotation.
    /// </summary>
    private Quaternion PreviousWorldRotation;

    /// <summary>
    /// Initializes cached transform state.
    /// </summary>
    private void Awake()
    {
        PreviousWorldPosition = transform.position;
        PreviousWorldRotation = transform.rotation;
        DeltaPosition = Vector3.zero;
        DeltaRotation = Quaternion.identity;
    }

    /// <summary>
    /// Mirrors the physical elevator pose after simulation and before rendering.
    /// </summary>
    private void LateUpdate()
    {
        if (SourceMotor == null)
        {
            DeltaPosition = Vector3.zero;
            DeltaRotation = Quaternion.identity;
            return;
        }

        Vector3 LastPosition = transform.position;
        Quaternion LastRotation = transform.rotation;

        transform.position = SourceMotor.transform.position;

        if (FollowRotation)
        {
            transform.rotation = SourceMotor.transform.rotation;
        }

        DeltaPosition = transform.position - LastPosition;
        DeltaRotation = transform.rotation * Quaternion.Inverse(LastRotation);

        PreviousWorldPosition = LastPosition;
        PreviousWorldRotation = LastRotation;
    }

    /// <summary>
    /// Returns the displacement applied to an arbitrary world point by the carrier transform
    /// between the previous and current frame poses.
    /// </summary>
    /// <param name="WorldPoint">World point to evaluate.</param>
    /// <returns>World-space displacement of the provided point.</returns>
    public Vector3 GetWorldPointDelta(Vector3 WorldPoint)
    {
        Vector3 LocalPointInPreviousPose = Quaternion.Inverse(PreviousWorldRotation) * (WorldPoint - PreviousWorldPosition);
        Vector3 ReprojectedCurrentWorldPoint = transform.position + transform.rotation * LocalPointInPreviousPose;
        return ReprojectedCurrentWorldPoint - WorldPoint;
    }
}