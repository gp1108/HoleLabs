using UnityEngine;

/// <summary>
/// Handles physical dragging behaviour for a carryable rigidbody object.
/// While held, the object follows a target anchor using a velocity-driven spring model,
/// staying relatively close to the player while still feeling heavy.
/// 
/// This version:
/// - follows the hold anchor more tightly while preserving weight,
/// - keeps release inertia instead of killing velocity,
/// - auto drops if the object gets stuck too far from the anchor,
/// - ignores collisions against the player collider while held.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class PhysicsCarryable : MonoBehaviour
{
    [Header("Hold Follow")]
    [Tooltip("Position strength used to pull the object towards the hold anchor.")]
    [SerializeField] private float HoldPositionStrength = 22f;

    [Tooltip("Velocity damping applied while matching the hold target velocity.")]
    [SerializeField] private float HoldVelocityDamping = 16f;

    [Tooltip("How much of the hold anchor velocity is injected into the object follow behaviour.")]
    [SerializeField] private float HoldAnchorVelocityInfluence = 1f;

    [Tooltip("Maximum linear speed allowed while the object is being dragged.")]
    [SerializeField] private float HeldMaxSpeed = 12f;

    [Tooltip("Maximum acceleration applied while trying to follow the hold anchor.")]
    [SerializeField] private float MaxHoldAcceleration = 70f;

    [Tooltip("Maximum positional error considered for follow correction.")]
    [SerializeField] private float MaxHoldErrorDistance = 1.25f;

    [Tooltip("Minimum forward distance allowed between the camera and the hold target point.")]
    [SerializeField] private float MinDistanceFromCamera = 1.15f;

    [Header("Release")]
    [Tooltip("Additional velocity inherited from the hold anchor when the object is released.")]
    [SerializeField] private float ReleaseVelocityInfluence = 0.35f;

    [Tooltip("Distance from the hold anchor after which the object is dropped automatically.")]
    [SerializeField] private float BreakHoldDistance = 2.25f;

    [Header("Held Physics")]
    [Tooltip("Linear damping applied to the rigidbody while it is being held.")]
    [SerializeField] private float HeldLinearDamping = 4f;

    [Tooltip("Angular damping applied to the rigidbody while it is being held.")]
    [SerializeField] private float HeldAngularDamping = 8f;

    [Tooltip("If true, gravity is disabled while the object is being held.")]
    [SerializeField] private bool DisableGravityWhileHeld = true;

    [Tooltip("If true, collisions against the player collider are ignored while the object is held.")]
    [SerializeField] private bool IgnorePlayerCollisionWhileHeld = true;

    [Tooltip("If true, the object rotation is frozen while the object is held.")]
    [SerializeField] private bool FreezeRotationWhileHeld = false;

    [Header("Debug")]
    [Tooltip("Draws the hold target relation in the Scene view.")]
    [SerializeField] private bool DrawDebug = false;

    [Tooltip("Logs hold state changes to the console.")]
    [SerializeField] private bool DebugLogs = false;

    [Tooltip("Optional camera override used for the minimum camera distance check.")]
    [SerializeField] private Camera OverrideCamera;

    /// <summary>
    /// Rigidbody that drives the carryable object movement.
    /// </summary>
    private Rigidbody Rigidbody;

    /// <summary>
    /// Anchor transform that defines the target held position.
    /// </summary>
    private Transform HoldAnchor;

    /// <summary>
    /// Player collider ignored while the object is being held.
    /// </summary>
    private Collider PlayerCollider;

    /// <summary>
    /// Cached colliders that belong to this carryable object hierarchy.
    /// </summary>
    private Collider[] CachedColliders;

    /// <summary>
    /// Whether the object is currently held.
    /// </summary>
    private bool IsHeld;

    /// <summary>
    /// Original linear damping restored when the object is released.
    /// </summary>
    private float SavedLinearDamping;

    /// <summary>
    /// Original angular damping restored when the object is released.
    /// </summary>
    private float SavedAngularDamping;

    /// <summary>
    /// Original gravity usage restored when the object is released.
    /// </summary>
    private bool SavedUseGravity;

    /// <summary>
    /// Original collision detection mode restored when the object is released.
    /// </summary>
    private CollisionDetectionMode SavedCollisionDetectionMode;

    /// <summary>
    /// Original interpolation mode restored when the object is released.
    /// </summary>
    private RigidbodyInterpolation SavedInterpolation;

    /// <summary>
    /// Original rigidbody constraints restored when the object is released.
    /// </summary>
    private RigidbodyConstraints SavedConstraints;

    /// <summary>
    /// Previous hold anchor position used to estimate anchor velocity.
    /// </summary>
    private Vector3 LastHoldAnchorPosition;

    /// <summary>
    /// Estimated hold anchor velocity in world space.
    /// </summary>
    private Vector3 HoldAnchorVelocity;

    /// <summary>
    /// Caches component references and the default rigidbody state.
    /// </summary>
    private void Awake()
    {
        Rigidbody = GetComponent<Rigidbody>();
        CachedColliders = GetComponentsInChildren<Collider>(true);

        SavedLinearDamping = Rigidbody.linearDamping;
        SavedAngularDamping = Rigidbody.angularDamping;
        SavedUseGravity = Rigidbody.useGravity;
        SavedCollisionDetectionMode = Rigidbody.collisionDetectionMode;
        SavedInterpolation = Rigidbody.interpolation;
        SavedConstraints = Rigidbody.constraints;
    }

    /// <summary>
    /// Updates the dragged rigidbody movement while held.
    /// </summary>
    private void FixedUpdate()
    {
        if (!IsHeld || HoldAnchor == null)
        {
            return;
        }

        UpdateAnchorVelocity();
        UpdateHoldDrag();
        CheckAutoDrop();
    }

    /// <summary>
    /// Starts holding the object using the provided hold anchor and player collider.
    /// </summary>
    /// <param name="NewHoldAnchor">Target anchor the object should follow.</param>
    /// <param name="NewPlayerCollider">Player collider used to ignore collisions while held.</param>
    public void BeginHold(Transform NewHoldAnchor, Collider NewPlayerCollider)
    {
        if (NewHoldAnchor == null)
        {
            return;
        }

        if (IsHeld)
        {
            EndHold();
        }

        HoldAnchor = NewHoldAnchor;
        PlayerCollider = NewPlayerCollider;
        IsHeld = true;

        SaveRuntimeState();
        ApplyHeldState();

        Rigidbody.WakeUp();

        LastHoldAnchorPosition = HoldAnchor.position;
        HoldAnchorVelocity = Vector3.zero;

        SetIgnorePlayerCollision(true);

        if (DebugLogs)
        {
            Debug.Log($"Begin hold: {name}");
        }
    }

    /// <summary>
    /// Stops holding the object and restores its original rigidbody state.
    /// The object keeps its physical inertia and also inherits a portion of the hold anchor velocity.
    /// </summary>
    public void EndHold()
    {
        if (!IsHeld)
        {
            return;
        }

        SetIgnorePlayerCollision(false);

        Rigidbody.linearVelocity += HoldAnchorVelocity * ReleaseVelocityInfluence;

        RestoreRuntimeState();

        HoldAnchor = null;
        PlayerCollider = null;
        IsHeld = false;
        HoldAnchorVelocity = Vector3.zero;

        if (DebugLogs)
        {
            Debug.Log($"End hold: {name}");
        }
    }

    /// <summary>
    /// Returns whether the object is currently being held.
    /// </summary>
    /// <returns>True if the object is being held.</returns>
    public bool GetIsHeld()
    {
        return IsHeld;
    }

    /// <summary>
    /// Estimates the current hold anchor velocity from its world position delta.
    /// </summary>
    private void UpdateAnchorVelocity()
    {
        float DeltaTime = Mathf.Max(Time.fixedDeltaTime, 0.0001f);
        Vector3 CurrentAnchorPosition = HoldAnchor.position;
        HoldAnchorVelocity = (CurrentAnchorPosition - LastHoldAnchorPosition) / DeltaTime;
        LastHoldAnchorPosition = CurrentAnchorPosition;
    }

    /// <summary>
    /// Applies velocity-driven dragging towards the hold anchor.
    /// The object follows the anchor more tightly by matching both anchor position error
    /// and anchor velocity, which reduces visible lag while still feeling weighted.
    /// </summary>
    private void UpdateHoldDrag()
    {
        Vector3 DesiredPoint = GetClampedDesiredPoint();
        Vector3 CurrentPoint = Rigidbody.worldCenterOfMass;

        Vector3 PositionError = DesiredPoint - CurrentPoint;

        if (PositionError.magnitude > MaxHoldErrorDistance)
        {
            PositionError = PositionError.normalized * MaxHoldErrorDistance;
        }

        Vector3 DesiredVelocity =
            (PositionError * HoldPositionStrength) +
            (HoldAnchorVelocity * HoldAnchorVelocityInfluence);

        Vector3 VelocityError = DesiredVelocity - Rigidbody.linearVelocity;
        Vector3 RequiredAcceleration = VelocityError * HoldVelocityDamping;
        Vector3 ClampedAcceleration = Vector3.ClampMagnitude(RequiredAcceleration, MaxHoldAcceleration);

        Rigidbody.AddForce(ClampedAcceleration, ForceMode.Acceleration);

        float MaxSpeedSquared = HeldMaxSpeed * HeldMaxSpeed;

        if (Rigidbody.linearVelocity.sqrMagnitude > MaxSpeedSquared)
        {
            Rigidbody.linearVelocity = Rigidbody.linearVelocity.normalized * HeldMaxSpeed;
        }

        if (DrawDebug)
        {
            Debug.DrawLine(CurrentPoint, DesiredPoint, Color.yellow);
            Debug.DrawRay(DesiredPoint, HoldAnchorVelocity * 0.05f, Color.cyan);
        }
    }

    /// <summary>
    /// Returns the desired hold point clamped so it never targets a position too close to the camera.
    /// </summary>
    /// <returns>Clamped desired hold point in world space.</returns>
    private Vector3 GetClampedDesiredPoint()
    {
        Vector3 DesiredPoint = HoldAnchor.position;
        Camera ActiveCamera = OverrideCamera != null ? OverrideCamera : Camera.main;

        if (ActiveCamera == null)
        {
            return DesiredPoint;
        }

        Vector3 CameraPosition = ActiveCamera.transform.position;
        Vector3 CameraForward = ActiveCamera.transform.forward;

        if (CameraForward.sqrMagnitude < 0.0001f)
        {
            CameraForward = Vector3.forward;
        }

        CameraForward.Normalize();

        Vector3 DesiredFromCamera = DesiredPoint - CameraPosition;
        float ForwardDistance = Vector3.Dot(DesiredFromCamera, CameraForward);

        if (ForwardDistance < MinDistanceFromCamera)
        {
            DesiredPoint = CameraPosition + CameraForward * MinDistanceFromCamera;
        }

        return DesiredPoint;
    }

    /// <summary>
    /// Drops the held object automatically if it gets too far away from the hold anchor,
    /// which usually means it became obstructed or stuck in the environment.
    /// </summary>
    private void CheckAutoDrop()
    {
        Vector3 DesiredPoint = GetClampedDesiredPoint();
        float DistanceToAnchor = Vector3.Distance(Rigidbody.worldCenterOfMass, DesiredPoint);

        if (DistanceToAnchor <= BreakHoldDistance)
        {
            return;
        }

        if (DebugLogs)
        {
            Debug.Log($"Auto drop because '{name}' exceeded break distance.");
        }

        EndHold();
    }

    /// <summary>
    /// Saves the current runtime rigidbody state before applying hold settings.
    /// </summary>
    private void SaveRuntimeState()
    {
        SavedLinearDamping = Rigidbody.linearDamping;
        SavedAngularDamping = Rigidbody.angularDamping;
        SavedUseGravity = Rigidbody.useGravity;
        SavedCollisionDetectionMode = Rigidbody.collisionDetectionMode;
        SavedInterpolation = Rigidbody.interpolation;
        SavedConstraints = Rigidbody.constraints;
    }

    /// <summary>
    /// Applies the physical state used while the object is being held.
    /// </summary>
    private void ApplyHeldState()
    {
        Rigidbody.linearDamping = HeldLinearDamping;
        Rigidbody.angularDamping = HeldAngularDamping;
        Rigidbody.useGravity = DisableGravityWhileHeld ? false : SavedUseGravity;
        Rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        Rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

        if (FreezeRotationWhileHeld)
        {
            Rigidbody.constraints = SavedConstraints | RigidbodyConstraints.FreezeRotation;
        }
    }

    /// <summary>
    /// Restores the original runtime rigidbody state after the object is released.
    /// </summary>
    private void RestoreRuntimeState()
    {
        Rigidbody.linearDamping = SavedLinearDamping;
        Rigidbody.angularDamping = SavedAngularDamping;
        Rigidbody.useGravity = SavedUseGravity;
        Rigidbody.collisionDetectionMode = SavedCollisionDetectionMode;
        Rigidbody.interpolation = SavedInterpolation;
        Rigidbody.constraints = SavedConstraints;
    }

    /// <summary>
    /// Enables or disables collisions between the held object and the player collider.
    /// </summary>
    /// <param name="Ignore">True to ignore collisions, false to restore them.</param>
    private void SetIgnorePlayerCollision(bool Ignore)
    {
        if (!IgnorePlayerCollisionWhileHeld || PlayerCollider == null || CachedColliders == null)
        {
            return;
        }

        for (int Index = 0; Index < CachedColliders.Length; Index++)
        {
            Collider CurrentCollider = CachedColliders[Index];

            if (CurrentCollider == null)
            {
                continue;
            }

            Physics.IgnoreCollision(PlayerCollider, CurrentCollider, Ignore);
        }
    }
}