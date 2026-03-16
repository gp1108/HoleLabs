using UnityEngine;

/// <summary>
/// Handles physical dragging behaviour for a carryable rigidbody object.
/// This component only performs active movement while the object is being held.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PhysicsCarryable : MonoBehaviour
{
    [Header("Carry Settings")]
    [Tooltip("Maximum speed used to move the object towards the hold target.")]
    [SerializeField] private float FollowSpeed = 12f;

    [Tooltip("Maximum distance allowed between the object and the hold target before stronger correction is applied.")]
    [SerializeField] private float MaxDistanceToTarget = 3f;

    [Tooltip("Drag applied while the object is being held.")]
    [SerializeField] private float HeldDrag = 10f;

    [Tooltip("Angular drag applied while the object is being held.")]
    [SerializeField] private float HeldAngularDrag = 10f;

    [Tooltip("Whether gravity remains enabled while the object is being held.")]
    [SerializeField] private bool KeepGravityWhileHeld = false;

    [Tooltip("Whether the object rotation should be frozen while being held.")]
    [SerializeField] private bool FreezeRotationWhileHeld = false;

    private Rigidbody Rigidbody;
    private bool IsHeld;
    private Transform HoldTarget;

    private float DefaultDrag;
    private float DefaultAngularDrag;
    private bool DefaultUseGravity;
    private RigidbodyConstraints DefaultConstraints;

    /// <summary>
    /// Caches the rigidbody and its default physical settings.
    /// </summary>
    private void Awake()
    {
        Rigidbody = GetComponent<Rigidbody>();

        DefaultDrag = Rigidbody.linearDamping;
        DefaultAngularDrag = Rigidbody.angularDamping;
        DefaultUseGravity = Rigidbody.useGravity;
        DefaultConstraints = Rigidbody.constraints;
    }

    /// <summary>
    /// Applies movement while the object is currently being held.
    /// </summary>
    private void FixedUpdate()
    {
        if (!IsHeld || HoldTarget == null)
        {
            return;
        }

        Vector3 DirectionToTarget = HoldTarget.position - Rigidbody.position;
        float DistanceToTarget = DirectionToTarget.magnitude;

        if (DistanceToTarget <= 0.01f)
        {
            Rigidbody.linearVelocity = Vector3.zero;
            return;
        }

        float SpeedMultiplier = DistanceToTarget > MaxDistanceToTarget ? 2f : 1f;
        Vector3 TargetVelocity = DirectionToTarget.normalized * FollowSpeed * SpeedMultiplier;

        Rigidbody.linearVelocity = TargetVelocity;
    }

    /// <summary>
    /// Starts carrying the object using the provided hold target transform.
    /// </summary>
    /// <param name="NewHoldTarget">Transform the object should follow while being held.</param>
    public void PickUp(Transform NewHoldTarget)
    {
        HoldTarget = NewHoldTarget;
        IsHeld = true;

        Rigidbody.useGravity = KeepGravityWhileHeld;
        Rigidbody.linearDamping = HeldDrag;
        Rigidbody.angularDamping = HeldAngularDrag;

        if (FreezeRotationWhileHeld)
        {
            Rigidbody.constraints = Rigidbody.constraints | RigidbodyConstraints.FreezeRotation;
        }

        Rigidbody.WakeUp();
    }

    /// <summary>
    /// Stops carrying the object and restores default physical settings.
    /// </summary>
    public void Drop()
    {
        IsHeld = false;
        HoldTarget = null;

        Rigidbody.useGravity = DefaultUseGravity;
        Rigidbody.linearDamping = DefaultDrag;
        Rigidbody.angularDamping = DefaultAngularDrag;
        Rigidbody.constraints = DefaultConstraints;
    }

    /// <summary>
    /// Returns whether the object is currently being held.
    /// </summary>
    /// <returns>True if the object is held.</returns>
    public bool GetIsHeld()
    {
        return IsHeld;
    }
}