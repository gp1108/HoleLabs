using UnityEngine;

/// <summary>
/// Equipped magnet tool driven by animation events.
///
/// Behaviour:
/// - Pressing primary use starts the magnet activation animation.
/// - At the configured animation impact frame, the magnet enters an active hold state.
/// - While the player keeps holding the primary input, PhysicsCarryable objects inside the
///   attraction area are continuously pulled towards the magnet target point every FixedUpdate.
/// - Releasing the button stops the continuous pull immediately.
///
/// This version adds stronger target convergence and damping near the target point so objects
/// stick to the magnet more cleanly instead of orbiting around it due to inertia.
/// </summary>
public sealed class MagnetItemBehaviour : AnimationEventEquippedItemBehaviour
{
    [Header("References")]
    [Tooltip("Camera used to place the attraction area in front of the player.")]
    [SerializeField] private Camera PlayerCamera;

    [Tooltip("Optional explicit target point that attracted objects should move towards.")]
    [SerializeField] private Transform MagnetTargetPoint;

    [Header("Magnet Area")]
    [Tooltip("Forward distance from the camera to the center of the magnet area.")]
    [SerializeField] private float AreaForwardDistance = 3.25f;

    [Tooltip("Radius of the attraction area.")]
    [SerializeField] private float AreaRadius = 2.5f;

    [Tooltip("Layers considered valid for magnetic attraction checks.")]
    [SerializeField] private LayerMask AttractionLayers = ~0;

    [Header("Magnet Pull")]
    [Tooltip("Acceleration applied towards the magnet target point.")]
    [SerializeField] private float AttractionAcceleration = 32f;

    [Tooltip("Maximum allowed speed while a carryable object is being attracted.")]
    [SerializeField] private float AttractionMaxSpeed = 7f;

    [Tooltip("Extra upward bias added while attracting objects.")]
    [SerializeField] private float AttractionVerticalBias = 0.1f;

    [Tooltip("Distance from the target point where stronger damping begins to be applied.")]
    [SerializeField] private float TargetSlowdownRadius = 1.15f;

    [Tooltip("Distance from the target point where the object is considered magnet-locked.")]
    [SerializeField] private float MagnetLockRadius = 0.35f;

    [Tooltip("Velocity damping applied while the object is inside the slowdown radius.")]
    [SerializeField] private float NearTargetVelocityDamping = 7f;

    [Tooltip("Extra damping applied specifically to sideways velocity so objects stop orbiting around the target.")]
    [SerializeField] private float TangentialDamping = 12f;

    [Tooltip("Velocity damping applied when the object is magnet-locked near the target point.")]
    [SerializeField] private float LockedVelocityDamping = 18f;

    [Tooltip("Maximum speed allowed once the object is magnet-locked near the target point.")]
    [SerializeField] private float LockedMaxSpeed = 2.25f;

    [Tooltip("If true, already held carryable objects are ignored.")]
    [SerializeField] private bool IgnoreHeldObjects = true;

    [Header("Behaviour")]
    [Tooltip("If true, the magnet stays active continuously while the primary input remains held after activation.")]
    [SerializeField] private bool ContinuousPullWhileHeld = true;

    [Header("Debug")]
    [Tooltip("Draws the attraction area and target point in the Scene view while the pull is active.")]
    [SerializeField] private bool DrawDebug = false;

    /// <summary>
    /// Whether the magnet is currently in its continuous active state.
    /// This becomes true at the animation impact frame and remains true until the input is released
    /// or the item is forcefully interrupted.
    /// </summary>
    private bool IsMagnetActive;

    /// <summary>
    /// Initializes the magnet and resolves missing camera references.
    /// </summary>
    public override void Initialize(HotbarController OwnerHotbar, ItemInstance ItemInstance)
    {
        base.Initialize(OwnerHotbar, ItemInstance);

        if (PlayerCamera == null && this.OwnerHotbar != null)
        {
            PlayerCamera = this.OwnerHotbar.GetComponentInChildren<Camera>();
        }
    }

    /// <summary>
    /// The magnet should only start one activation action when it is currently inactive.
    /// Once active, holding the input keeps the pull alive instead of retriggering the action.
    /// </summary>
    protected override bool CanStartPrimaryAction()
    {
        return !IsMagnetActive;
    }

    /// <summary>
    /// While the magnet is active, the primary action should not automatically repeat.
    /// The continuous pull is handled every physics step instead.
    /// </summary>
    protected override void ProcessPendingPrimaryRepeat()
    {
        PendingPrimaryRepeat = false;
    }

    /// <summary>
    /// Runs the continuous magnet pull while the magnet is active and the input remains held.
    /// </summary>
    private void FixedUpdate()
    {
        if (!IsMagnetActive)
        {
            return;
        }

        if (!IsPrimaryUseActive)
        {
            StopMagnetPull();
            return;
        }

        ApplyMagnetPull();
    }

    /// <summary>
    /// Activates the continuous magnetic pull exactly at the animation impact frame.
    /// </summary>
    protected override void OnPrimaryActionImpact()
    {
        IsMagnetActive = true;
        ApplyMagnetPull();
        Log("Magnet pull activated at animation impact time.");
    }

    /// <summary>
    /// Keeps the magnet active after the activation animation finished if the player is still holding.
    /// If the player already released the button before the animation ended, the magnet is stopped here.
    /// </summary>
    protected override void OnPrimaryActionFinished()
    {
        base.OnPrimaryActionFinished();

        if (!ContinuousPullWhileHeld)
        {
            StopMagnetPull();
            return;
        }

        if (!IsPrimaryUseActive)
        {
            StopMagnetPull();
        }
    }

    /// <summary>
    /// Releasing the primary input immediately disables the continuous magnetic pull.
    /// </summary>
    public override void OnPrimaryUseEnded()
    {
        base.OnPrimaryUseEnded();
        StopMagnetPull();
    }

    /// <summary>
    /// Stops any remaining magnet pull when the item is forcefully interrupted.
    /// </summary>
    protected override void OnForcedUsageStopped()
    {
        base.OnForcedUsageStopped();
        StopMagnetPull();
    }

    /// <summary>
    /// Applies magnetic acceleration to all valid carryable objects inside the attraction area.
    /// This version damps tangential velocity and slows objects near the target to reduce orbiting and overshoot.
    /// </summary>
    private void ApplyMagnetPull()
    {
        if (PlayerCamera == null)
        {
            return;
        }

        Vector3 AreaCenter = GetAreaCenter();
        Vector3 TargetPoint = GetTargetPoint();

        if (DrawDebug)
        {
            DebugExtension.DrawWireSphere(AreaCenter, AreaRadius, Color.cyan, Time.fixedDeltaTime);
            Debug.DrawLine(AreaCenter, TargetPoint, Color.magenta, Time.fixedDeltaTime);
            DebugExtension.DrawWireSphere(TargetPoint, TargetSlowdownRadius, Color.yellow, Time.fixedDeltaTime);
            DebugExtension.DrawWireSphere(TargetPoint, MagnetLockRadius, Color.green, Time.fixedDeltaTime);
        }

        Collider[] Hits = Physics.OverlapSphere(
            AreaCenter,
            AreaRadius,
            AttractionLayers,
            QueryTriggerInteraction.Ignore
        );

        for (int Index = 0; Index < Hits.Length; Index++)
        {
            Collider CurrentCollider = Hits[Index];

            if (CurrentCollider == null)
            {
                continue;
            }

            PhysicsCarryable Carryable = CurrentCollider.GetComponent<PhysicsCarryable>();

            if (Carryable == null)
            {
                Carryable = CurrentCollider.GetComponentInParent<PhysicsCarryable>();
            }

            if (Carryable == null)
            {
                continue;
            }

            if (IgnoreHeldObjects && Carryable.GetIsHeld())
            {
                continue;
            }

            Rigidbody CarryableRigidbody = Carryable.GetComponent<Rigidbody>();

            if (CarryableRigidbody == null)
            {
                continue;
            }

            //EN CUANTO PONGO ESTA LIENA LSO OBJETOS YA NO PUEDEN SER ARRASTRADOS FUNCIONAN FATAL. REVISALO CHATGPT
            Carryable.NotifyMagnetInfluence();
            //---
            Vector3 CurrentPosition = CarryableRigidbody.worldCenterOfMass;
            Vector3 ToTarget = TargetPoint - CurrentPosition;
            ToTarget.y += AttractionVerticalBias;

            float DistanceToTarget = ToTarget.magnitude;

            if (DistanceToTarget <= 0.0001f)
            {
                continue;
            }

            Vector3 TargetDirection = ToTarget / DistanceToTarget;
            Vector3 CurrentVelocity = CarryableRigidbody.linearVelocity;

            Vector3 RadialVelocity = Vector3.Project(CurrentVelocity, TargetDirection);
            Vector3 TangentialVelocity = CurrentVelocity - RadialVelocity;

            // Base attraction
            float PullStrengthMultiplier = 1f;
            float MaxSpeed = AttractionMaxSpeed;
            float VelocityDamping = 0f;

            if (DistanceToTarget <= TargetSlowdownRadius)
            {
                float NormalizedDistance = Mathf.Clamp01(DistanceToTarget / Mathf.Max(0.001f, TargetSlowdownRadius));
                PullStrengthMultiplier = Mathf.Lerp(0.35f, 1f, NormalizedDistance);
                VelocityDamping = NearTargetVelocityDamping;
                MaxSpeed = Mathf.Lerp(LockedMaxSpeed, AttractionMaxSpeed, NormalizedDistance);
            }

            if (DistanceToTarget <= MagnetLockRadius)
            {
                PullStrengthMultiplier = 0.2f;
                VelocityDamping = LockedVelocityDamping;
                MaxSpeed = LockedMaxSpeed;
            }

            Vector3 DesiredAcceleration = TargetDirection * (AttractionAcceleration * PullStrengthMultiplier);
            CarryableRigidbody.AddForce(DesiredAcceleration, ForceMode.Acceleration);

            // Strong sideways damping reduces orbiting around the target.
            if (TangentialVelocity.sqrMagnitude > 0.0001f)
            {
                Vector3 TangentialDampingForce = -TangentialVelocity * TangentialDamping;
                CarryableRigidbody.AddForce(TangentialDampingForce, ForceMode.Acceleration);
            }

            // Additional global damping near the target keeps the object from overshooting.
            if (VelocityDamping > 0f && CurrentVelocity.sqrMagnitude > 0.0001f)
            {
                Vector3 VelocityDampingForce = -CurrentVelocity * VelocityDamping;
                CarryableRigidbody.AddForce(VelocityDampingForce, ForceMode.Acceleration);
            }

            // Clamp final speed.
            Vector3 UpdatedVelocity = CarryableRigidbody.linearVelocity;
            float MaxSpeedSquared = MaxSpeed * MaxSpeed;

            if (UpdatedVelocity.sqrMagnitude > MaxSpeedSquared)
            {
                CarryableRigidbody.linearVelocity = UpdatedVelocity.normalized * MaxSpeed;
            }
        }
    }

    /// <summary>
    /// Disables the continuous pull state.
    /// </summary>
    private void StopMagnetPull()
    {
        IsMagnetActive = false;
    }

    /// <summary>
    /// Gets the center point of the magnetic attraction area.
    /// </summary>
    private Vector3 GetAreaCenter()
    {
        if (PlayerCamera == null)
        {
            return transform.position;
        }

        return PlayerCamera.transform.position + (PlayerCamera.transform.forward * AreaForwardDistance);
    }

    /// <summary>
    /// Gets the target point towards which objects are attracted.
    /// </summary>
    private Vector3 GetTargetPoint()
    {
        if (MagnetTargetPoint != null)
        {
            MagnetTargetPoint.position = GetAreaCenter();
            return MagnetTargetPoint.position;
        }

        if (PlayerCamera == null)
        {
            return transform.position;
        }

        return PlayerCamera.transform.position + (PlayerCamera.transform.forward * AreaForwardDistance);
    }

    /// <summary>
    /// Small internal helper for debug wire spheres without requiring gizmos-only rendering.
    /// </summary>
    private static class DebugExtension
    {
        public static void DrawWireSphere(Vector3 Center, float Radius, Color Color, float Duration)
        {
            const int SegmentCount = 20;
            float AngleStep = 360f / SegmentCount;

            Vector3 PreviousXY = Center + new Vector3(Radius, 0f, 0f);
            Vector3 PreviousXZ = Center + new Vector3(Radius, 0f, 0f);
            Vector3 PreviousYZ = Center + new Vector3(0f, Radius, 0f);

            for (int Index = 1; Index <= SegmentCount; Index++)
            {
                float Angle = AngleStep * Index * Mathf.Deg2Rad;

                Vector3 NextXY = Center + new Vector3(Mathf.Cos(Angle) * Radius, Mathf.Sin(Angle) * Radius, 0f);
                Vector3 NextXZ = Center + new Vector3(Mathf.Cos(Angle) * Radius, 0f, Mathf.Sin(Angle) * Radius);
                Vector3 NextYZ = Center + new Vector3(0f, Mathf.Cos(Angle) * Radius, Mathf.Sin(Angle) * Radius);

                Debug.DrawLine(PreviousXY, NextXY, Color, Duration);
                Debug.DrawLine(PreviousXZ, NextXZ, Color, Duration);
                Debug.DrawLine(PreviousYZ, NextYZ, Color, Duration);

                PreviousXY = NextXY;
                PreviousXZ = NextXZ;
                PreviousYZ = NextYZ;
            }
        }
    }
}
