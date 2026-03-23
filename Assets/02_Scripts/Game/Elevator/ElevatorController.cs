using UnityEngine;

/// <summary>
/// Simplified elevator controller that moves between a minimum and maximum distance from a top anchor.
/// Exposes frame delta so CharacterController passengers can be moved explicitly without parenting.
/// </summary>
public sealed class ElevatorController : MonoBehaviour
{
    private enum MoveState
    {
        Idle,
        MovingUp,
        MovingDown
    }

    [Header("References")]
    [Tooltip("Top anchor used as the origin of the elevator travel.")]
    [SerializeField] private Transform TopAnchor;

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

    [Tooltip("If true, the elevator automatically swaps direction at the limits.")]
    [SerializeField] private bool Loop = true;

    /// <summary>
    /// Current world velocity of the elevator in meters per second.
    /// </summary>
    public Vector3 Velocity { get; private set; }

    /// <summary>
    /// World space displacement applied by the elevator during the current frame.
    /// </summary>
    public Vector3 DeltaPosition { get; private set; }

    private MoveState CurrentMoveState = MoveState.MovingDown;
    private Vector3 LastPosition;

    /// <summary>
    /// Initializes the elevator pose from the current serialized distance.
    /// </summary>
    private void Awake()
    {
        if (TopAnchor == null)
        {
            Debug.LogError("ElevatorController requires a TopAnchor reference.");
            enabled = false;
            return;
        }

        MaxDistance = Mathf.Max(MinDistance, MaxDistance);
        CurrentDistance = Mathf.Clamp(CurrentDistance, MinDistance, MaxDistance);
        transform.position = GetTargetPosition();
        LastPosition = transform.position;
    }

    /// <summary>
    /// Moves the elevator and updates its frame delta.
    /// </summary>
    private void Update()
    {
        if (CurrentMoveState == MoveState.MovingUp) CurrentDistance -= MoveSpeed * Time.deltaTime;
        if (CurrentMoveState == MoveState.MovingDown) CurrentDistance += MoveSpeed * Time.deltaTime;

        if (CurrentDistance <= MinDistance) { CurrentDistance = MinDistance; CurrentMoveState = Loop ? MoveState.MovingDown : MoveState.Idle; }
        if (CurrentDistance >= MaxDistance) { CurrentDistance = MaxDistance; CurrentMoveState = Loop ? MoveState.MovingUp : MoveState.Idle; }

        transform.position = GetTargetPosition();
        DeltaPosition = transform.position - LastPosition;
        Velocity = DeltaPosition / Mathf.Max(Time.deltaTime, 0.0001f);
        LastPosition = transform.position;
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
    /// Stops the elevator movement.
    /// </summary>
    [ContextMenu("Stop")]
    public void Stop()
    {
        CurrentMoveState = MoveState.Idle;
    }

    /// <summary>
    /// Toggles the elevator direction.
    /// </summary>
    [ContextMenu("Toggle Direction")]
    public void ToggleDirection()
    {
        CurrentMoveState = CurrentMoveState == MoveState.MovingUp ? MoveState.MovingDown : MoveState.MovingUp;
    }

    /// <summary>
    /// Sets the current travel distance in meters and updates the elevator position immediately.
    /// </summary>
    /// <param name="NewDistance">Desired travel distance from the anchor.</param>
    public void SetDistance(float NewDistance)
    {
        CurrentDistance = Mathf.Clamp(NewDistance, MinDistance, MaxDistance);
        transform.position = GetTargetPosition();
        DeltaPosition = Vector3.zero;
        LastPosition = transform.position;
    }

    /// <summary>
    /// Returns the target world position from the current travel distance.
    /// </summary>
    /// <returns>World position evaluated from anchor, direction and distance.</returns>
    private Vector3 GetTargetPosition()
    {
        return TopAnchor.position + GetWorldDirection() * CurrentDistance;
    }

    /// <summary>
    /// Returns the normalized travel direction in world space.
    /// </summary>
    /// <returns>Normalized world travel direction.</returns>
    private Vector3 GetWorldDirection()
    {
        Vector3 WorldDirection = TopAnchor.TransformDirection(LocalTravelDirection);
        return WorldDirection.sqrMagnitude > 0.0001f ? WorldDirection.normalized : Vector3.down;
    }
}