using UnityEngine;

/// <summary>
/// Authoritative kinematic elevator motor used as the single source of truth for both
/// physical support and visual representation.
/// </summary>
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(Rigidbody))]
public sealed class ElevatorPhysicalMotor : MonoBehaviour
{
    /// <summary>
    /// Current elevator runtime movement state.
    /// </summary>
    private enum MoveState
    {
        Idle,
        MovingUp,
        MovingDown
    }

    [Header("References")]
    [Tooltip("Top anchor used as the origin of the elevator travel path.")]
    [SerializeField] private Transform TopAnchor;
    [Tooltip("System that determines how much weight is carrying the elevator")]
    [SerializeField] private ElevatorWeightSystem ElevatorWeightSystem;

    [Header("Travel")]
    [Tooltip("Local travel direction evaluated from the top anchor. Usually Vector3.down.")]
    [SerializeField] private Vector3 LocalTravelDirection = Vector3.down;

    [Tooltip("Minimum travel distance in meters from the anchor.")]
    [SerializeField] private float MinDistance = 0f;

    [Tooltip("Maximum travel distance in meters from the anchor.")]
    [SerializeField] private float MaxDistance = 10f;

    [Tooltip("Current travel distance in meters from the anchor.")]
    [SerializeField] private float CurrentDistance = 0f;

    [Tooltip("Movement speed in meters per second.")]
    [SerializeField] private float MoveSpeed = 2f;

    [Tooltip("If true, the elevator automatically swaps direction at the travel limits.")]
    [SerializeField] private bool Loop = true;

    /// <summary>
    /// Current world velocity of the elevator in meters per second.
    /// </summary>
    public Vector3 Velocity { get; private set; }

    /// <summary>
    /// World space displacement applied during the latest fixed step.
    /// </summary>
    public Vector3 DeltaPosition { get; private set; }

    /// <summary>
    /// Current target world position of the elevator.
    /// </summary>
    public Vector3 CurrentTargetPosition => GetTargetPosition();

    /// <summary>
    /// Current target world rotation of the elevator.
    /// </summary>
    public Quaternion CurrentTargetRotation => transform.rotation;

    /// <summary>
    /// Cached kinematic rigidbody.
    /// </summary>
    private Rigidbody RigidbodyComponent;

    /// <summary>
    /// Current runtime move state.
    /// </summary>
    private MoveState CurrentMoveState = MoveState.MovingDown;

    /// <summary>
    /// Last simulated world position.
    /// </summary>
    private Vector3 LastSimulatedPosition;

    /// <summary>
    /// Configures the rigidbody and initializes pose.
    /// </summary>
    private void Awake()
    {
        RigidbodyComponent = GetComponent<Rigidbody>();

        if (TopAnchor == null)
        {
            Debug.LogError("ElevatorPhysicalMotor requires a TopAnchor reference.", this);
            enabled = false;
            return;
        }

        RigidbodyComponent.isKinematic = true;
        RigidbodyComponent.useGravity = false;
        RigidbodyComponent.interpolation = RigidbodyInterpolation.Interpolate;
        RigidbodyComponent.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        MaxDistance = Mathf.Max(MinDistance, MaxDistance);
        CurrentDistance = Mathf.Clamp(CurrentDistance, MinDistance, MaxDistance);

        Vector3 InitialPosition = GetTargetPosition();
        RigidbodyComponent.position = InitialPosition;
        transform.position = InitialPosition;

        LastSimulatedPosition = InitialPosition;
        DeltaPosition = Vector3.zero;
        Velocity = Vector3.zero;
    }

    /// <summary>
    /// Advances the elevator motion in the physics loop.
    /// </summary>
    private void FixedUpdate()
    {
        if(ElevatorWeightSystem.IsElevatorOverweighted())
        {
            return;
        }

        if (CurrentMoveState == MoveState.MovingUp)
        {
            CurrentDistance -= MoveSpeed * Time.fixedDeltaTime;
        }
        else if (CurrentMoveState == MoveState.MovingDown)
        {
            CurrentDistance += MoveSpeed * Time.fixedDeltaTime;
        }

        if (CurrentDistance <= MinDistance)
        {
            CurrentDistance = MinDistance;
            CurrentMoveState = Loop ? MoveState.MovingDown : MoveState.Idle;
        }

        if (CurrentDistance >= MaxDistance)
        {
            CurrentDistance = MaxDistance;
            CurrentMoveState = Loop ? MoveState.MovingUp : MoveState.Idle;
        }

        Vector3 TargetPosition = GetTargetPosition();

        RigidbodyComponent.MovePosition(TargetPosition);

        DeltaPosition = TargetPosition - LastSimulatedPosition;
        Velocity = DeltaPosition / Mathf.Max(Time.fixedDeltaTime, 0.0001f);
        LastSimulatedPosition = TargetPosition;
    }

    /// <summary>
    /// Starts moving the elevator upwards.
    /// </summary>
    [ContextMenu("Move Up")]
    public void MoveUp()
    {
        CurrentMoveState = MoveState.MovingUp;
    }

    /// <summary>
    /// Starts moving the elevator downwards.
    /// </summary>
    [ContextMenu("Move Down")]
    public void MoveDown()
    {
        CurrentMoveState = MoveState.MovingDown;
    }

    /// <summary>
    /// Stops elevator motion.
    /// </summary>
    [ContextMenu("Stop")]
    public void Stop()
    {
        CurrentMoveState = MoveState.Idle;
    }

    /// <summary>
    /// Returns the world target position evaluated from anchor, direction and distance.
    /// </summary>
    /// <returns>Target world position.</returns>
    private Vector3 GetTargetPosition()
    {
        return TopAnchor.position + GetWorldDirection() * CurrentDistance;
    }

    /// <summary>
    /// Returns the normalized world travel direction.
    /// </summary>
    /// <returns>Normalized world direction.</returns>
    private Vector3 GetWorldDirection()
    {
        Vector3 WorldDirection = TopAnchor.TransformDirection(LocalTravelDirection);
        return WorldDirection.sqrMagnitude > 0.0001f ? WorldDirection.normalized : Vector3.down;
    }
}