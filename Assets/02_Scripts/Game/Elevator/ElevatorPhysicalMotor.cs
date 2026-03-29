using UnityEngine;

/// <summary>
/// Authoritative kinematic elevator motor used as the single source of truth for both
/// physical support and visual representation.
/// The motor supports independent vertical movement and self rotation at the same time.
/// </summary>
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(Rigidbody))]
public sealed class ElevatorPhysicalMotor : MonoBehaviour
{
    /// <summary>
    /// Current runtime vertical movement state.
    /// </summary>
    private enum VerticalMoveState
    {
        Idle,
        MovingUp,
        MovingDown
    }

    /// <summary>
    /// Current runtime rotation state.
    /// </summary>
    private enum RotationMoveState
    {
        Idle,
        RotatingLeft,
        RotatingRight
    }

    [Header("References")]
    [Tooltip("Top anchor used as the origin of the elevator travel path.")]
    [SerializeField] private Transform TopAnchor;

    [Tooltip("System that determines how much weight is carrying the elevator.")]
    [SerializeField] private ElevatorWeightSystem ElevatorWeightSystem;

    [Header("Vertical Travel")]
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

    [Header("Rotation")]
    [Tooltip("Local axis used to rotate the elevator around itself.")]
    [SerializeField] private Vector3 LocalRotationAxis = Vector3.up;

    [Tooltip("Rotation speed in degrees per second.")]
    [SerializeField] private float RotationSpeed = 60f;

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
    /// Current vertical move state.
    /// </summary>
    private VerticalMoveState CurrentVerticalMoveState = VerticalMoveState.Idle;

    /// <summary>
    /// Current rotation state.
    /// </summary>
    private RotationMoveState CurrentRotationMoveState = RotationMoveState.Idle;

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
        RotationSpeed = Mathf.Max(0f, RotationSpeed);

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
        if (ElevatorWeightSystem != null && ElevatorWeightSystem.IsElevatorOverweighted())
        {
            StopAllAndRefreshMotionState();
            return;
        }

        float PreviousDistance = CurrentDistance;

        if (CurrentVerticalMoveState == VerticalMoveState.MovingUp)
        {
            CurrentDistance -= MoveSpeed * Time.fixedDeltaTime;
        }
        else if (CurrentVerticalMoveState == VerticalMoveState.MovingDown)
        {
            CurrentDistance += MoveSpeed * Time.fixedDeltaTime;
        }

        CurrentDistance = Mathf.Clamp(CurrentDistance, MinDistance, MaxDistance);

        if (Mathf.Approximately(CurrentDistance, MinDistance) && CurrentVerticalMoveState == VerticalMoveState.MovingUp)
        {
            CurrentVerticalMoveState = VerticalMoveState.Idle;
        }
        else if (Mathf.Approximately(CurrentDistance, MaxDistance) && CurrentVerticalMoveState == VerticalMoveState.MovingDown)
        {
            CurrentVerticalMoveState = VerticalMoveState.Idle;
        }

        Vector3 TargetPosition = GetTargetPosition();
        Quaternion TargetRotation = GetNextRotation();

        RigidbodyComponent.MovePosition(TargetPosition);
        RigidbodyComponent.MoveRotation(TargetRotation);

        DeltaPosition = TargetPosition - LastSimulatedPosition;
        Velocity = DeltaPosition / Mathf.Max(Time.fixedDeltaTime, 0.0001f);
        LastSimulatedPosition = TargetPosition;

        if (Mathf.Approximately(PreviousDistance, CurrentDistance) &&
            CurrentVerticalMoveState == VerticalMoveState.Idle)
        {
            Velocity = Vector3.zero;
            DeltaPosition = Vector3.zero;
        }
    }

    /// <summary>
    /// Starts moving the elevator upwards.
    /// </summary>
    [ContextMenu("Move Up")]
    public void MoveUp()
    {
        CurrentVerticalMoveState = VerticalMoveState.MovingUp;
    }

    /// <summary>
    /// Starts moving the elevator downwards.
    /// </summary>
    [ContextMenu("Move Down")]
    public void MoveDown()
    {
        CurrentVerticalMoveState = VerticalMoveState.MovingDown;
    }

    /// <summary>
    /// Stops vertical elevator motion.
    /// </summary>
    [ContextMenu("Stop Vertical")]
    public void Stop()
    {
        CurrentVerticalMoveState = VerticalMoveState.Idle;
    }

    /// <summary>
    /// Starts rotating the elevator to the left.
    /// </summary>
    [ContextMenu("Rotate Left")]
    public void RotateLeft()
    {
        CurrentRotationMoveState = RotationMoveState.RotatingLeft;
    }

    /// <summary>
    /// Starts rotating the elevator to the right.
    /// </summary>
    [ContextMenu("Rotate Right")]
    public void RotateRight()
    {
        CurrentRotationMoveState = RotationMoveState.RotatingRight;
    }

    /// <summary>
    /// Stops elevator rotation.
    /// </summary>
    [ContextMenu("Stop Rotation")]
    public void StopRotation()
    {
        CurrentRotationMoveState = RotationMoveState.Idle;
    }

    /// <summary>
    /// Stops both vertical motion and rotation.
    /// </summary>
    [ContextMenu("Stop All")]
    public void StopAll()
    {
        CurrentVerticalMoveState = VerticalMoveState.Idle;
        CurrentRotationMoveState = RotationMoveState.Idle;
    }

    /// <summary>
    /// Stops the elevator and clears cached linear motion output.
    /// </summary>
    private void StopAllAndRefreshMotionState()
    {
        StopAll();
        Velocity = Vector3.zero;
        DeltaPosition = Vector3.zero;
        LastSimulatedPosition = transform.position;
    }

    /// <summary>
    /// Returns the world target position evaluated from anchor, direction and distance.
    /// </summary>
    /// <returns>Target world position.</returns>
    private Vector3 GetTargetPosition()
    {
        return TopAnchor.position + GetWorldTravelDirection() * CurrentDistance;
    }

    /// <summary>
    /// Computes the next target rotation using the current rotation state.
    /// </summary>
    /// <returns>Next world rotation.</returns>
    private Quaternion GetNextRotation()
    {
        if (CurrentRotationMoveState == RotationMoveState.Idle || RotationSpeed <= 0f)
        {
            return RigidbodyComponent.rotation;
        }

        Vector3 WorldRotationAxis = transform.TransformDirection(LocalRotationAxis);
        if (WorldRotationAxis.sqrMagnitude <= 0.0001f)
        {
            return RigidbodyComponent.rotation;
        }

        float SignedDegrees = RotationSpeed * Time.fixedDeltaTime;

        if (CurrentRotationMoveState == RotationMoveState.RotatingLeft)
        {
            SignedDegrees *= -1f;
        }

        Quaternion DeltaRotation = Quaternion.AngleAxis(SignedDegrees, WorldRotationAxis.normalized);
        return DeltaRotation * RigidbodyComponent.rotation;
    }

    /// <summary>
    /// Returns the normalized world travel direction.
    /// </summary>
    /// <returns>Normalized world direction.</returns>
    private Vector3 GetWorldTravelDirection()
    {
        Vector3 WorldDirection = TopAnchor.TransformDirection(LocalTravelDirection);
        return WorldDirection.sqrMagnitude > 0.0001f ? WorldDirection.normalized : Vector3.down;
    }
}