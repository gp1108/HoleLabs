using UnityEngine;

/// <summary>
/// Keeps a kinematic rigidbody anchor aligned with a target transform during FixedUpdate.
/// This is used by spring-joint driven interactions so the anchor follows the player hold point
/// or the magnet target in physics time instead of relying on transform parenting alone.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class JointAnchorFollower : MonoBehaviour
{
    [Tooltip("Transform that the kinematic anchor should follow every physics step.")]
    public Transform TargetTransform;

    [Tooltip("If true, the anchor also copies the target rotation.")]
    public bool FollowRotation = true;

    /// <summary>
    /// Cached rigidbody used as the kinematic spring-joint anchor.
    /// </summary>
    private Rigidbody AnchorRigidbody;

    /// <summary>
    /// Initializes the kinematic rigidbody reference.
    /// </summary>
    private void Awake()
    {
        AnchorRigidbody = GetComponent<Rigidbody>();
        AnchorRigidbody.isKinematic = true;
        AnchorRigidbody.useGravity = false;
        AnchorRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
    }

    /// <summary>
    /// Moves the anchor rigidbody to the target transform in physics time.
    /// </summary>
    private void FixedUpdate()
    {
        if (TargetTransform == null)
        {
            return;
        }

        AnchorRigidbody.MovePosition(TargetTransform.position);

        if (FollowRotation)
        {
            AnchorRigidbody.MoveRotation(TargetTransform.rotation);
        }
    }
}
