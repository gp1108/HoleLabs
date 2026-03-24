using UnityEngine;

/// <summary>
/// Runtime spring-anchor driver used by hold, magnet and future conveyor style interactions.
/// This component only owns anchor creation, joint setup, target following and break checks.
/// </summary>
[DisallowMultipleComponent]
public sealed class CarryableAttachmentDriver : MonoBehaviour
{
    /// <summary>
    /// Configuration used to create and run a spring-based attachment.
    /// </summary>
    [System.Serializable]
    public struct AttachmentSettings
    {
        [Tooltip("Spring force applied by the runtime anchor joint.")]
        public float Spring;

        [Tooltip("Damping applied by the runtime anchor joint.")]
        public float Damper;

        [Tooltip("Maximum allowed distance inside the spring joint.")]
        public float MaxDistance;

        [Tooltip("Linear damping applied to the rigidbody while attached.")]
        public float LinearDamping;

        [Tooltip("Angular damping applied to the rigidbody while attached.")]
        public float AngularDamping;

        [Tooltip("If true, gravity is disabled while attached.")]
        public bool DisableGravity;

        [Tooltip("If greater than zero, the attachment breaks when the carryable gets farther than this distance from the target.")]
        public float BreakDistance;
    }

    /// <summary>
    /// Current anchor target.
    /// </summary>
    public Transform TargetTransform { get; private set; }

    /// <summary>
    /// Current anchor velocity estimated from the target movement.
    /// </summary>
    public Vector3 AnchorVelocity { get; private set; }

    /// <summary>
    /// Returns true while the runtime attachment exists.
    /// </summary>
    public bool IsActive => AnchorObject != null && ActiveJoint != null && TargetTransform != null;

    /// <summary>
    /// Connected rigidbody driven by the runtime anchor.
    /// </summary>
    private Rigidbody ConnectedBody;

    /// <summary>
    /// Runtime anchor object.
    /// </summary>
    private GameObject AnchorObject;

    /// <summary>
    /// Runtime anchor rigidbody.
    /// </summary>
    private Rigidbody AnchorRigidbody;

    /// <summary>
    /// Runtime target follower.
    /// </summary>
    private JointAnchorFollower AnchorFollower;

    /// <summary>
    /// Runtime spring joint.
    /// </summary>
    private SpringJoint ActiveJoint;

    /// <summary>
    /// Previous target position used to estimate target velocity.
    /// </summary>
    private Vector3 LastTargetPosition;

    /// <summary>
    /// Active settings currently used by the runtime driver.
    /// </summary>
    private AttachmentSettings CurrentSettings;

    /// <summary>
    /// Starts a runtime spring-anchor attachment.
    /// </summary>
    /// <param name="TargetBody">Rigidbody to drive.</param>
    /// <param name="NewTarget">Anchor target transform.</param>
    /// <param name="Settings">Attachment configuration.</param>
    /// <param name="AnchorName">Runtime anchor object name.</param>
    public void Begin(Rigidbody TargetBody, Transform NewTarget, AttachmentSettings Settings, string AnchorName)
    {
        if (TargetBody == null || NewTarget == null)
        {
            return;
        }

        End();

        ConnectedBody = TargetBody;
        TargetTransform = NewTarget;
        CurrentSettings = Settings;
        LastTargetPosition = NewTarget.position;
        AnchorVelocity = Vector3.zero;

        AnchorObject = new GameObject(AnchorName);
        AnchorObject.transform.SetPositionAndRotation(NewTarget.position, NewTarget.rotation);

        AnchorRigidbody = AnchorObject.AddComponent<Rigidbody>();
        AnchorRigidbody.isKinematic = true;
        AnchorRigidbody.useGravity = false;
        AnchorRigidbody.interpolation = RigidbodyInterpolation.Interpolate;

        AnchorFollower = AnchorObject.AddComponent<JointAnchorFollower>();
        AnchorFollower.TargetTransform = NewTarget;
        AnchorFollower.FollowRotation = true;

        ActiveJoint = AnchorObject.AddComponent<SpringJoint>();
        ActiveJoint.connectedBody = ConnectedBody;
        ActiveJoint.autoConfigureConnectedAnchor = false;
        ActiveJoint.connectedAnchor = ConnectedBody.transform.InverseTransformPoint(ConnectedBody.worldCenterOfMass);
        ActiveJoint.anchor = Vector3.zero;
        ActiveJoint.spring = Settings.Spring;
        ActiveJoint.damper = Settings.Damper;
        ActiveJoint.maxDistance = Settings.MaxDistance;
        ActiveJoint.minDistance = 0f;
        ActiveJoint.tolerance = 0f;
        ActiveJoint.enableCollision = false;
        ActiveJoint.breakForce = Mathf.Infinity;
        ActiveJoint.breakTorque = Mathf.Infinity;
    }

    /// <summary>
    /// Stops the current runtime attachment immediately.
    /// </summary>
    public void End()
    {
        if (AnchorObject != null)
        {
            Destroy(AnchorObject);
        }

        AnchorObject = null;
        AnchorRigidbody = null;
        AnchorFollower = null;
        ActiveJoint = null;
        ConnectedBody = null;
        TargetTransform = null;
        AnchorVelocity = Vector3.zero;
    }

    /// <summary>
    /// Updates anchor velocity estimation and returns true if the current attachment should break.
    /// </summary>
    /// <returns>True when the attachment exceeded its configured break distance.</returns>
    public bool Tick()
    {
        if (!IsActive)
        {
            AnchorVelocity = Vector3.zero;
            return false;
        }

        Vector3 CurrentTargetPosition = TargetTransform.position;
        AnchorVelocity = (CurrentTargetPosition - LastTargetPosition) / Mathf.Max(Time.fixedDeltaTime, 0.0001f);
        LastTargetPosition = CurrentTargetPosition;

        if (CurrentSettings.BreakDistance > 0f)
        {
            float DistanceToTarget = Vector3.Distance(ConnectedBody.worldCenterOfMass, CurrentTargetPosition);
            if (DistanceToTarget > CurrentSettings.BreakDistance)
            {
                return true;
            }
        }

        return false;
    }
}