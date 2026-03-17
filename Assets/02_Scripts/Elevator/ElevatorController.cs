using UnityEngine;

/// <summary>
/// Kinematic rope-based elevator controller.
/// The platform moves along a configurable axis using cable length as the main state,
/// supports independent up and down speeds, optional yaw rotation,
/// and exposes point velocity for passengers such as custom character controllers.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class ElevatorController : MonoBehaviour
{
    /// <summary>
    /// Defines the current vertical movement state.
    /// </summary>
    private enum MoveState
    {
        Idle,
        MovingUp,
        MovingDown
    }

    [Header("References")]
    [Tooltip("Top anchor used as the origin for cable length.")]
    [SerializeField] private Transform TopAnchor;

    [Header("Travel")]
    [Tooltip("Local travel direction evaluated from the top anchor. Usually Vector3.down.")]
    [SerializeField] private Vector3 LocalTravelDirection = Vector3.down;

    [Tooltip("Minimum cable length in meters. Usually zero means fully raised.")]
    [SerializeField] private float MinCableLength = 0f;

    [Tooltip("Maximum cable length in meters. Defines the lowest reachable point.")]
    [SerializeField] private float MaxCableLength = 10f;

    [Tooltip("Current cable length in meters.")]
    [SerializeField] private float CurrentCableLength = 0f;

    [Tooltip("Meters per second while moving up.")]
    [SerializeField] private float MoveUpSpeed = 2f;

    [Tooltip("Meters per second while moving down.")]
    [SerializeField] private float MoveDownSpeed = 1.5f;

    [Header("Rotation")]
    [Tooltip("If enabled, the platform can rotate around the anchor up axis.")]
    [SerializeField] private bool AllowYawRotation = true;

    [Tooltip("Current local yaw angle around the anchor up axis.")]
    [SerializeField] private float CurrentYawDegrees = 0f;

    [Tooltip("Maximum yaw speed in degrees per second.")]
    [SerializeField] private float MaxYawSpeed = 45f;

    [Tooltip("Current yaw input in range [-1, 1].")]
    [SerializeField] private float CurrentYawInput = 0f;

    [Header("Debug State")]
    [Tooltip("Current linear velocity of the elevator in world space.")]
    [SerializeField] private Vector3 CurrentLinearVelocity;

    [Tooltip("Current angular velocity of the elevator in world space, expressed in radians per second.")]
    [SerializeField] private Vector3 CurrentAngularVelocity;

    private Rigidbody Rigidbody;
    private Quaternion InitialRotation;
    private MoveState CurrentMoveState = MoveState.Idle;

    private Vector3 LastPosition;
    private float LastYawDegrees;

    /// <summary>
    /// Initializes rigidbody settings and aligns the elevator with the configured cable length.
    /// </summary>
    private void Awake()
    {
        Rigidbody = GetComponent<Rigidbody>();
        InitialRotation = transform.rotation;

        if (TopAnchor == null)
        {
            Debug.LogError("TopAnchor reference is missing.");
            enabled = false;
            return;
        }

        Rigidbody.isKinematic = true;
        Rigidbody.useGravity = false;
        Rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        Rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        ValidateTravelValues();
        SyncCableLengthFromCurrentPosition();
        ApplyPoseImmediate();

        LastPosition = Rigidbody.position;
        LastYawDegrees = CurrentYawDegrees;
    }

    /// <summary>
    /// Moves the platform during the physics step and updates motion data used by passengers.
    /// </summary>
    private void FixedUpdate()
    {
        float DeltaTime = Time.fixedDeltaTime;

        UpdateCableLength(DeltaTime);
        UpdateYaw(DeltaTime);

        Vector3 TargetPosition = CalculateTargetPosition();
        Quaternion TargetRotation = CalculateTargetRotation();

        Rigidbody.MovePosition(TargetPosition);
        Rigidbody.MoveRotation(TargetRotation);

        UpdateMotionState(TargetPosition, DeltaTime);
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
    /// Stops vertical movement.
    /// </summary>
    [ContextMenu("Stop")]
    public void Stop()
    {
        CurrentMoveState = MoveState.Idle;
    }

    /// <summary>
    /// Toggles movement direction based on current state and travel limits.
    /// </summary>
    [ContextMenu("Toggle Direction")]
    public void ToggleDirection()
    {
        if (Mathf.Approximately(CurrentCableLength, MinCableLength))
        {
            MoveDown();
            return;
        }

        if (Mathf.Approximately(CurrentCableLength, MaxCableLength))
        {
            MoveUp();
            return;
        }

        CurrentMoveState = CurrentMoveState == MoveState.MovingUp ? MoveState.MovingDown : MoveState.MovingUp;
    }

    /// <summary>
    /// Sets the current cable length in meters and immediately updates the pose.
    /// </summary>
    /// <param name="NewCableLength">Desired cable length.</param>
    public void SetCableLength(float NewCableLength)
    {
        CurrentCableLength = Mathf.Clamp(NewCableLength, MinCableLength, MaxCableLength);
        ApplyPoseImmediate();
        LastPosition = Rigidbody.position;
        LastYawDegrees = CurrentYawDegrees;
        CurrentLinearVelocity = Vector3.zero;
        CurrentAngularVelocity = Vector3.zero;
    }

    /// <summary>
    /// Sets yaw input in range [-1, 1].
    /// </summary>
    /// <param name="NewYawInput">Desired yaw input.</param>
    public void SetYawInput(float NewYawInput)
    {
        CurrentYawInput = Mathf.Clamp(NewYawInput, -1f, 1f);
    }

    /// <summary>
    /// Clears yaw input.
    /// </summary>
    public void StopYaw()
    {
        CurrentYawInput = 0f;
    }

    /// <summary>
    /// Returns the current world velocity of any point attached to the elevator.
    /// </summary>
    /// <param name="WorldPoint">Point in world space.</param>
    /// <returns>Linear plus angular point velocity in world space.</returns>
    public Vector3 GetVelocityAtPoint(Vector3 WorldPoint)
    {
        Vector3 FromCenter = WorldPoint - Rigidbody.worldCenterOfMass;
        return CurrentLinearVelocity + Vector3.Cross(CurrentAngularVelocity, FromCenter);
    }

    /// <summary>
    /// Validates serialized travel values.
    /// </summary>
    private void ValidateTravelValues()
    {
        if (MaxCableLength < MinCableLength)
        {
            MaxCableLength = MinCableLength;
        }

        CurrentCableLength = Mathf.Clamp(CurrentCableLength, MinCableLength, MaxCableLength);
    }

    /// <summary>
    /// Synchronizes cable length using the current world position.
    /// </summary>
    private void SyncCableLengthFromCurrentPosition()
    {
        Vector3 TravelDirection = GetWorldTravelDirection();
        Vector3 AnchorToPlatform = transform.position - TopAnchor.position;
        float ProjectedDistance = Vector3.Dot(AnchorToPlatform, TravelDirection);

        CurrentCableLength = Mathf.Clamp(ProjectedDistance, MinCableLength, MaxCableLength);
    }

    /// <summary>
    /// Updates cable length according to the active movement state.
    /// </summary>
    /// <param name="DeltaTime">Current fixed delta time.</param>
    private void UpdateCableLength(float DeltaTime)
    {
        switch (CurrentMoveState)
        {
            case MoveState.MovingUp:
                CurrentCableLength -= MoveUpSpeed * DeltaTime;

                if (CurrentCableLength <= MinCableLength)
                {
                    CurrentCableLength = MinCableLength;
                    CurrentMoveState = MoveState.Idle;
                }
                break;

            case MoveState.MovingDown:
                CurrentCableLength += MoveDownSpeed * DeltaTime;

                if (CurrentCableLength >= MaxCableLength)
                {
                    CurrentCableLength = MaxCableLength;
                    CurrentMoveState = MoveState.Idle;
                }
                break;
        }
    }

    /// <summary>
    /// Updates yaw angle according to current yaw input.
    /// </summary>
    /// <param name="DeltaTime">Current fixed delta time.</param>
    private void UpdateYaw(float DeltaTime)
    {
        if (!AllowYawRotation)
        {
            CurrentYawInput = 0f;
            return;
        }

        CurrentYawDegrees += CurrentYawInput * MaxYawSpeed * DeltaTime;
    }

    /// <summary>
    /// Calculates the world target position from current cable length.
    /// </summary>
    /// <returns>World target position.</returns>
    private Vector3 CalculateTargetPosition()
    {
        return TopAnchor.position + (GetWorldTravelDirection() * CurrentCableLength);
    }

    /// <summary>
    /// Calculates the world target rotation using current yaw.
    /// </summary>
    /// <returns>World target rotation.</returns>
    private Quaternion CalculateTargetRotation()
    {
        if (!AllowYawRotation)
        {
            return InitialRotation;
        }

        Quaternion YawOffset = Quaternion.AngleAxis(CurrentYawDegrees, TopAnchor.up);
        return YawOffset * InitialRotation;
    }

    /// <summary>
    /// Returns the normalized travel direction in world space.
    /// </summary>
    /// <returns>World travel direction.</returns>
    private Vector3 GetWorldTravelDirection()
    {
        Vector3 TravelDirection = TopAnchor.TransformDirection(LocalTravelDirection);

        if (TravelDirection.sqrMagnitude <= 0.0001f)
        {
            return Vector3.down;
        }

        return TravelDirection.normalized;
    }

    /// <summary>
    /// Applies the current target pose immediately.
    /// </summary>
    private void ApplyPoseImmediate()
    {
        Vector3 TargetPosition = CalculateTargetPosition();
        Quaternion TargetRotation = CalculateTargetRotation();

        transform.SetPositionAndRotation(TargetPosition, TargetRotation);
        Rigidbody.position = TargetPosition;
        Rigidbody.rotation = TargetRotation;
    }

    /// <summary>
    /// Updates linear and angular motion state used by custom passengers.
    /// </summary>
    /// <param name="TargetPosition">Current target world position.</param>
    /// <param name="DeltaTime">Current fixed delta time.</param>
    private void UpdateMotionState(Vector3 TargetPosition, float DeltaTime)
    {
        CurrentLinearVelocity = (TargetPosition - LastPosition) / Mathf.Max(DeltaTime, 0.0001f);

        float DeltaYawDegrees = Mathf.DeltaAngle(LastYawDegrees, CurrentYawDegrees);
        CurrentAngularVelocity = AllowYawRotation
            ? TopAnchor.up * (DeltaYawDegrees * Mathf.Deg2Rad / Mathf.Max(DeltaTime, 0.0001f))
            : Vector3.zero;

        LastPosition = TargetPosition;
        LastYawDegrees = CurrentYawDegrees;
    }
}