using UnityEngine;

/// <summary>
/// Handles physical dragging behaviour for a carryable rigidbody object.
/// While held, the object is pulled towards a target anchor using spring-like acceleration.
/// This version is adapted for a Rigidbody based player controller and ignores collisions
/// against the player collider while the object is being held.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class PhysicsCarryable : MonoBehaviour
{
    [Header("Hold Follow")]
    [Tooltip("Spring strength used to pull the object towards the hold anchor.")]
    [SerializeField] private float HoldSpring = 55f;

    [Tooltip("Velocity damping applied while correcting the object towards the hold anchor.")]
    [SerializeField] private float HoldDamping = 14f;

    [Tooltip("Maximum linear speed allowed while the object is being dragged.")]
    [SerializeField] private float HeldMaxSpeed = 8f;

    [Tooltip("Maximum allowed positional error used for force correction.")]
    [SerializeField] private float MaxHoldErrorDistance = 2f;

    [Tooltip("Minimum distance allowed between the camera and the object while it is held.")]
    [SerializeField] private float MinDistanceFromCamera = 1.2f;

    [Header("Held Physics")]
    [Tooltip("Linear damping applied to the rigidbody while it is being held.")]
    [SerializeField] private float HeldLinearDamping = 4.5f;

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

        UpdateHoldDrag();
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

        Rigidbody.linearVelocity = Vector3.zero;
        Rigidbody.angularVelocity = Vector3.zero;
        Rigidbody.WakeUp();

        SetIgnorePlayerCollision(true);

        if (DebugLogs)
        {
            Debug.Log($"Begin hold: {name}");
        }
    }

    /// <summary>
    /// Stops holding the object and restores its original rigidbody state.
    /// </summary>
    public void EndHold()
    {
        if (!IsHeld)
        {
            return;
        }

        SetIgnorePlayerCollision(false);

        Rigidbody.linearVelocity = Vector3.zero;
        Rigidbody.angularVelocity = Vector3.zero;

        RestoreRuntimeState();

        HoldAnchor = null;
        PlayerCollider = null;
        IsHeld = false;

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
    /// Applies spring-based dragging towards the hold anchor.
    /// </summary>
    private void UpdateHoldDrag()
    {
        Vector3 DesiredPoint = HoldAnchor.position;
        Vector3 CurrentPoint = Rigidbody.worldCenterOfMass;

        Camera ActiveCamera = OverrideCamera != null ? OverrideCamera : Camera.main;

        if (ActiveCamera != null)
        {
            Vector3 CameraToBody = CurrentPoint - ActiveCamera.transform.position;

            if (CameraToBody.sqrMagnitude < MinDistanceFromCamera * MinDistanceFromCamera)
            {
                Vector3 SafeDirection = ActiveCamera.transform.forward;

                if (SafeDirection.sqrMagnitude < 0.0001f)
                {
                    SafeDirection = Vector3.forward;
                }

                Vector3 PushedPoint = ActiveCamera.transform.position + SafeDirection.normalized * MinDistanceFromCamera;
                Vector3 PushDelta = PushedPoint - CurrentPoint;

                Rigidbody.AddForce(PushDelta * HoldSpring, ForceMode.Acceleration);
                CurrentPoint = Rigidbody.worldCenterOfMass;
            }
        }

        Vector3 PositionError = DesiredPoint - CurrentPoint;

        if (PositionError.magnitude > MaxHoldErrorDistance)
        {
            PositionError = PositionError.normalized * MaxHoldErrorDistance;
        }

        Vector3 Acceleration = (PositionError * HoldSpring) - (Rigidbody.linearVelocity * HoldDamping);
        Rigidbody.AddForce(Acceleration, ForceMode.Acceleration);

        float MaxSpeedSquared = HeldMaxSpeed * HeldMaxSpeed;

        if (Rigidbody.linearVelocity.sqrMagnitude > MaxSpeedSquared)
        {
            Rigidbody.linearVelocity = Rigidbody.linearVelocity.normalized * HeldMaxSpeed;
        }

        if (DrawDebug)
        {
            Debug.DrawLine(CurrentPoint, DesiredPoint, Color.yellow);
        }
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