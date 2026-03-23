using UnityEngine;

/// <summary>
/// Physical carryable object driven by runtime spring anchors.
/// This component owns the full interaction lifecycle for:
/// - Player hold.
/// - Magnet attachment.
/// - Temporary post-release recovery.
/// - Player collision ignore and temporary layer switching.
/// - Automatic magnet break when the object lags too far behind.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class PhysicsCarryable : MonoBehaviour
{
    /// <summary>
    /// Runtime interaction state currently affecting this carryable.
    /// </summary>
    public enum InteractionState
    {
        Free,
        Held,
        Magnetized,
        Released
    }

    /// <summary>
    /// Group of spring and rigidbody values used while attached to an anchor.
    /// </summary>
    [System.Serializable]
    private struct AttachmentSettings
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
    }

    /// <summary>
    /// Group of settings used while the object is transitioning from attached back to free state.
    /// </summary>
    [System.Serializable]
    private struct ReleaseSettings
    {
        [Tooltip("Minimum time after releasing during which the object keeps ignoring the player.")]
        public float IgnoreMinimumTime;

        [Tooltip("Maximum time after releasing before the object is forced back to its normal collision state.")]
        public float IgnoreMaximumTime;

        [Tooltip("If true, the object can finish its release state immediately once it falls asleep.")]
        public bool RestoreOnSleep;

        [Tooltip("Velocity inherited from the followed anchor when the player releases a held object.")]
        public float HeldReleaseVelocityInfluence;

        [Tooltip("Impulse multiplier applied when the object is launched from the magnet.")]
        public float MagnetLaunchImpulseMultiplier;

        [Tooltip("Seconds that interpolation remains enabled after releasing the object.")]
        public float InterpolationGraceTime;
    }

    /// <summary>
    /// Group of settings used to break a magnet attachment when the object cannot keep up.
    /// </summary>
    [System.Serializable]
    private struct MagnetBreakSettings
    {
        [Tooltip("Soft distance from the magnet target after which break time starts accumulating.")]
        public float BreakDistance;

        [Tooltip("Hard distance from the magnet target that forces an immediate detach.")]
        public float HardBreakDistance;

        [Tooltip("Time allowed beyond the soft break distance before the object detaches.")]
        public float BreakGraceTime;

        [Tooltip("Time during which the object cannot be magnetized again after a forced break.")]
        public float ReattachCooldown;
    }

    [Header("Shared")]
    [Tooltip("Maximum distance allowed between the hold anchor and the rigidbody before manual carry breaks automatically.")]
    [SerializeField] private float BreakHoldDistance = 2.6f;

    [Tooltip("If true, interpolation is forced while attached to a runtime anchor.")]
    [SerializeField] private bool ForceInterpolationWhileAttached = true;

    [Header("Hold Settings")]
    [SerializeField]
    private AttachmentSettings HeldSettings = new AttachmentSettings
    {
        Spring = 115f,
        Damper = 22f,
        MaxDistance = 0.02f,
        LinearDamping = 3f,
        AngularDamping = 1.2f,
        DisableGravity = false
    };

    [Header("Magnet Settings")]
    [SerializeField]
    private AttachmentSettings MagnetSettings = new AttachmentSettings
    {
        Spring = 100f,
        Damper = 25f,
        MaxDistance = 0.01f,
        LinearDamping = 3f,
        AngularDamping = 1.5f,
        DisableGravity = false
    };

    [SerializeField]
    private MagnetBreakSettings MagnetBreakConfig = new MagnetBreakSettings
    {
        BreakDistance = 1.25f,
        HardBreakDistance = 1.9f,
        BreakGraceTime = 0.12f,
        ReattachCooldown = 0.35f
    };

    [Header("Release")]
    [SerializeField]
    private ReleaseSettings ReleaseConfig = new ReleaseSettings
    {
        IgnoreMinimumTime = 0.15f,
        IgnoreMaximumTime = 1.25f,
        RestoreOnSleep = true,
        HeldReleaseVelocityInfluence = 0.35f,
        MagnetLaunchImpulseMultiplier = 1f,
        InterpolationGraceTime = 0.2f
    };

    [Header("Collision")]
    [Tooltip("Temporary layer applied while this carryable must ignore collisions against the player.")]
    [SerializeField] private string IgnoredByPlayerLayerName = "PlayerIgnoredPhysicsObjects";


    [Header("Debug")]
    [Tooltip("Logs state changes and attachment events to the console.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Cached rigidbody driven by the runtime anchor.
    /// </summary>
    private Rigidbody RigidbodyComponent;

    /// <summary>
    /// Cached colliders that belong to this carryable hierarchy.
    /// </summary>
    private Collider[] CachedColliders;

    /// <summary>
    /// Cached transforms used to restore original layers after temporary interaction changes.
    /// </summary>
    private Transform[] CachedTransforms;

    /// <summary>
    /// Original layer of every transform in the hierarchy.
    /// </summary>
    private int[] CachedOriginalLayers;

    /// <summary>
    /// Resolved layer used while the carryable must ignore the player.
    /// </summary>
    private int IgnoredByPlayerLayer = -1;

    /// <summary>
    /// Current runtime interaction state.
    /// </summary>
    private InteractionState CurrentState = InteractionState.Free;

    /// <summary>
    /// Default interpolation restored after temporary interaction smoothing.
    /// </summary>
    private RigidbodyInterpolation DefaultInterpolation;

    /// <summary>
    /// Default collision mode restored after temporary interaction changes.
    /// </summary>
    private CollisionDetectionMode DefaultCollisionDetectionMode;

    /// <summary>
    /// Default linear damping restored after temporary interaction changes.
    /// </summary>
    private float DefaultLinearDamping;

    /// <summary>
    /// Default angular damping restored after temporary interaction changes.
    /// </summary>
    private float DefaultAngularDamping;

    /// <summary>
    /// Default gravity flag restored after temporary interaction changes.
    /// </summary>
    private bool DefaultUseGravity;

    /// <summary>
    /// Current runtime anchor target followed by the anchor follower.
    /// </summary>
    private Transform ActiveAnchorTarget;

    /// <summary>
    /// Runtime anchor object created while attached.
    /// </summary>
    private GameObject ActiveAnchorObject;

    /// <summary>
    /// Component that keeps the runtime anchor aligned to its target transform.
    /// </summary>
    private JointAnchorFollower ActiveAnchorFollower;

    /// <summary>
    /// Spring joint created on the runtime anchor and connected to this rigidbody.
    /// </summary>
    private SpringJoint ActiveJoint;

    /// <summary>
    /// Player colliders currently ignored by this carryable.
    /// </summary>
    private Collider[] IgnoredPlayerColliders;

    /// <summary>
    /// Previous anchor position used to estimate anchor velocity.
    /// </summary>
    private Vector3 LastAnchorPosition;

    /// <summary>
    /// Current anchor velocity used for manual release inheritance.
    /// </summary>
    private Vector3 AnchorVelocity;

    /// <summary>
    /// Countdown used to restore default interpolation after releasing.
    /// </summary>
    private float InterpolationGraceTimer;

    /// <summary>
    /// Time spent in the temporary released state.
    /// </summary>
    private float ReleasedTimer;

    /// <summary>
    /// Time accumulated while the magnetized object stays too far away from its target.
    /// </summary>
    private float MagnetBreakTimer;

    /// <summary>
    /// Remaining cooldown that blocks magnet reattachment after a forced break.
    /// </summary>
    private float MagnetReattachCooldownTimer;

    /// <summary>
    /// Exposes the cached rigidbody to external systems that need direct physics access.
    /// </summary>
    public Rigidbody Rigidbody => RigidbodyComponent;

    /// <summary>
    /// Whether the carryable is currently being transported by an external carrier system.
    /// </summary>
    private bool IsExternallyCarried;

    /// <summary>
    /// Parent that owned the carryable transform before external carrying started.
    /// </summary>
    private Transform PreviousParentBeforeExternalCarry;

    /// <summary>
    /// Returns true when the object can be transported by an external carrier system.
    /// </summary>
    public bool CanBeExternallyCarried()
    {
        return CurrentState == InteractionState.Free || CurrentState == InteractionState.Released;
    }

    /// <summary>
    /// Initializes cached references and stores default rigidbody values.
    /// </summary>
    private void Awake()
    {
        RigidbodyComponent = GetComponent<Rigidbody>();
        CachedColliders = GetComponentsInChildren<Collider>(true);
        CachedTransforms = GetComponentsInChildren<Transform>(true);

        DefaultInterpolation = RigidbodyComponent.interpolation;
        DefaultCollisionDetectionMode = RigidbodyComponent.collisionDetectionMode;
        DefaultLinearDamping = RigidbodyComponent.linearDamping;
        DefaultAngularDamping = RigidbodyComponent.angularDamping;
        DefaultUseGravity = RigidbodyComponent.useGravity;

        CacheOriginalLayers();
        ResolveIgnoredByPlayerLayer();
    }

    /// <summary>
    /// Updates anchor tracking, magnet break checks, released recovery and interpolation restoration.
    /// </summary>
    private void FixedUpdate()
    {
        UpdateMagnetReattachCooldown();
        UpdateAnchorTracking();
        UpdateReleasedState();
        UpdateInterpolationGrace();
    }

    /// <summary>
    /// Ensures the object is fully restored if it gets disabled while attached.
    /// </summary>
    private void OnDisable()
    {
        ClearAttachmentImmediate(true, false);
    }

    /// <summary>
    /// Starts external carry mode used by transport systems such as elevators.
    /// The rigidbody becomes kinematic and the object is parented to the carrier transform.
    /// </summary>
    /// <param name="CarrierTransform">Transform that will transport the carryable.</param>
    public void BeginExternalCarry(Transform CarrierTransform)
    {
        if (CarrierTransform == null || !CanBeExternallyCarried() || IsExternallyCarried)
        {
            return;
        }

        PreviousParentBeforeExternalCarry = transform.parent;
        IsExternallyCarried = true;

        RigidbodyComponent.linearVelocity = Vector3.zero;
        RigidbodyComponent.angularVelocity = Vector3.zero;
        RigidbodyComponent.isKinematic = true;

        transform.SetParent(CarrierTransform, true);
    }

    /// <summary>
    /// Stops external carry mode and restores dynamic simulation.
    /// </summary>
    /// <param name="InheritedVelocity">Velocity inherited from the transport system when leaving it.</param>
    public void EndExternalCarry(Vector3 InheritedVelocity)
    {
        if (!IsExternallyCarried)
        {
            return;
        }

        IsExternallyCarried = false;
        transform.SetParent(PreviousParentBeforeExternalCarry, true);

        RigidbodyComponent.isKinematic = false;
        RigidbodyComponent.linearVelocity = InheritedVelocity;
    }

    /// <summary>
    /// Returns true when the object is currently transported by an external carrier system.
    /// </summary>
    public bool GetIsExternallyCarried()
    {
        return IsExternallyCarried;
    }

    /// <summary>
    /// Starts manual carry using the provided hold anchor.
    /// </summary>
    public void BeginHold(Transform NewHoldAnchor, Collider[] NewPlayerColliders)
    {
        if (NewHoldAnchor == null)
        {
            return;
        }

        BeginAttachment(InteractionState.Held, NewHoldAnchor, NewPlayerColliders, HeldSettings, "HeldAnchor");
    }

    /// <summary>
    /// Ends manual carry and enters the released state.
    /// </summary>
    public void EndHold()
    {
        if (CurrentState != InteractionState.Held)
        {
            return;
        }

        RigidbodyComponent.linearVelocity += AnchorVelocity * ReleaseConfig.HeldReleaseVelocityInfluence;
        BeginReleasedState();
    }

    /// <summary>
    /// Returns true when the object is currently allowed to attach to a magnet.
    /// </summary>
    public bool CanAttachToMagnet()
    {
        return CurrentState != InteractionState.Held && MagnetReattachCooldownTimer <= 0f;
    }

    /// <summary>
    /// Starts magnet attachment if the object is allowed to attach.
    /// </summary>
    public void BeginMagnet(Transform MagnetTarget, Collider[] NewPlayerColliders)
    {
        if (MagnetTarget == null || !CanAttachToMagnet())
        {
            return;
        }

        BeginAttachment(InteractionState.Magnetized, MagnetTarget, NewPlayerColliders, MagnetSettings, "MagnetAnchor");
    }

    /// <summary>
    /// Ends magnet attachment and enters the released state.
    /// </summary>
    public void EndMagnet(Collider[] CurrentPlayerColliders)
    {
        if (CurrentState != InteractionState.Magnetized)
        {
            return;
        }

        BeginReleasedState();
    }

    /// <summary>
    /// Compatibility method kept for older callers. It only wakes the rigidbody.
    /// </summary>
    public void NotifyMagnetInfluence()
    {
        RigidbodyComponent.WakeUp();
    }

    /// <summary>
    /// Compatibility method kept for older conveyor integrations. It intentionally does nothing.
    /// </summary>
    public void SetConveyorDriven(bool IsConveyorDriven)
    {
    }

    /// <summary>
    /// Launches the object away from the magnet with an impulse.
    /// </summary>
    public void LaunchFromMagnet(Vector3 WorldImpulse)
    {
        if (CurrentState == InteractionState.Magnetized)
        {
            BeginReleasedState();
        }

        RigidbodyComponent.AddForce(WorldImpulse * ReleaseConfig.MagnetLaunchImpulseMultiplier, ForceMode.Impulse);
    }

    /// <summary>
    /// Returns true when the object is currently held by the player.
    /// </summary>
    public bool GetIsHeld()
    {
        return CurrentState == InteractionState.Held;
    }

    /// <summary>
    /// Returns true when the object is currently attached to a magnet.
    /// </summary>
    public bool GetIsMagnetized()
    {
        return CurrentState == InteractionState.Magnetized;
    }

    /// <summary>
    /// Returns the current runtime interaction state.
    /// </summary>
    public InteractionState GetInteractionState()
    {
        return CurrentState;
    }

    /// <summary>
    /// Begins a new attachment mode using the provided settings and anchor target.
    /// </summary>
    private void BeginAttachment(
        InteractionState NewState,
        Transform NewAnchorTarget,
        Collider[] NewPlayerColliders,
        AttachmentSettings Settings,
        string AnchorName)
    {
        if (NewState == CurrentState && ActiveAnchorTarget == NewAnchorTarget)
        {
            return;
        }

        if (IsExternallyCarried)
        {
            EndExternalCarry(Vector3.zero);
        }

        ClearAttachmentImmediate(false, false);

        ActiveAnchorTarget = NewAnchorTarget;
        IgnoredPlayerColliders = NewPlayerColliders;
        LastAnchorPosition = ActiveAnchorTarget.position;
        AnchorVelocity = Vector3.zero;
        MagnetBreakTimer = 0f;

        CreateRuntimeAnchor(AnchorName, Settings);
        ApplyAttachmentPhysics(Settings);

        SetIgnorePlayerCollision(true);
        SetIgnoredByPlayerLayer(true);

        if (NewState == InteractionState.Magnetized)
        {
            MagnetReattachCooldownTimer = 0f;
        }

        CurrentState = NewState;
        Log("Attached as " + CurrentState + ".");
    }

    /// <summary>
    /// Creates the runtime anchor, follower and spring joint used by the current attachment.
    /// </summary>
    private void CreateRuntimeAnchor(string AnchorName, AttachmentSettings Settings)
    {
        ActiveAnchorObject = new GameObject(AnchorName);
        ActiveAnchorObject.transform.SetPositionAndRotation(ActiveAnchorTarget.position, ActiveAnchorTarget.rotation);

        Rigidbody AnchorRigidbody = ActiveAnchorObject.AddComponent<Rigidbody>();
        AnchorRigidbody.isKinematic = true;
        AnchorRigidbody.useGravity = false;
        AnchorRigidbody.interpolation = RigidbodyInterpolation.Interpolate;

        ActiveAnchorFollower = ActiveAnchorObject.AddComponent<JointAnchorFollower>();
        ActiveAnchorFollower.TargetTransform = ActiveAnchorTarget;
        ActiveAnchorFollower.FollowRotation = true;

        ActiveJoint = ActiveAnchorObject.AddComponent<SpringJoint>();
        ActiveJoint.connectedBody = RigidbodyComponent;
        ActiveJoint.autoConfigureConnectedAnchor = false;
        ActiveJoint.connectedAnchor = RigidbodyComponent.transform.InverseTransformPoint(RigidbodyComponent.worldCenterOfMass);
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
    /// Applies the rigidbody values required while attached to a runtime anchor.
    /// </summary>
    private void ApplyAttachmentPhysics(AttachmentSettings Settings)
    {
        RigidbodyComponent.linearDamping = Settings.LinearDamping;
        RigidbodyComponent.angularDamping = Settings.AngularDamping;
        RigidbodyComponent.useGravity = !Settings.DisableGravity;
        RigidbodyComponent.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        RigidbodyComponent.WakeUp();

        if (ForceInterpolationWhileAttached)
        {
            RigidbodyComponent.interpolation = RigidbodyInterpolation.Interpolate;
        }
    }

    /// <summary>
    /// Enters the temporary released state after hold or magnet detach.
    /// </summary>
    private void BeginReleasedState()
    {
        DestroyRuntimeAnchor();

        RestoreDefaultPhysics(true);
        RigidbodyComponent.WakeUp();

        ReleasedTimer = 0f;
        CurrentState = InteractionState.Released;

        Log("Entered released state.");
    }

    /// <summary>
    /// Finalizes the released state and restores normal collision with the player.
    /// </summary>
    private void FinalizeReleasedState()
    {
        SetIgnorePlayerCollision(false);
        SetIgnoredByPlayerLayer(false);

        IgnoredPlayerColliders = null;
        ReleasedTimer = 0f;
        CurrentState = InteractionState.Free;

        Log("Released state finished.");
    }

    /// <summary>
    /// Clears the current attachment immediately and fully restores the object state.
    /// This is intended for disable or hard reset paths.
    /// </summary>
    private void ClearAttachmentImmediate(bool RestorePhysicsDefaults, bool UseInterpolationGrace)
    {
        if (IsExternallyCarried)
        {
            EndExternalCarry(Vector3.zero);
        }

        SetIgnorePlayerCollision(false);
        SetIgnoredByPlayerLayer(false);

        DestroyRuntimeAnchor();

        ActiveAnchorTarget = null;
        IgnoredPlayerColliders = null;
        AnchorVelocity = Vector3.zero;
        ReleasedTimer = 0f;
        MagnetBreakTimer = 0f;

        if (RestorePhysicsDefaults && RigidbodyComponent != null)
        {
            RestoreDefaultPhysics(UseInterpolationGrace);
        }

        CurrentState = InteractionState.Free;
    }

    /// <summary>
    /// Destroys the runtime anchor hierarchy if it exists.
    /// </summary>
    private void DestroyRuntimeAnchor()
    {
        if (ActiveAnchorObject != null)
        {
            Destroy(ActiveAnchorObject);
        }

        ActiveAnchorObject = null;
        ActiveAnchorFollower = null;
        ActiveJoint = null;
        ActiveAnchorTarget = null;
    }

    /// <summary>
    /// Restores the rigidbody defaults that were active before attachment.
    /// </summary>
    private void RestoreDefaultPhysics(bool UseInterpolationGrace)
    {
        RigidbodyComponent.linearDamping = DefaultLinearDamping;
        RigidbodyComponent.angularDamping = DefaultAngularDamping;
        RigidbodyComponent.useGravity = DefaultUseGravity;
        RigidbodyComponent.collisionDetectionMode = DefaultCollisionDetectionMode;

        if (UseInterpolationGrace && ForceInterpolationWhileAttached)
        {
            InterpolationGraceTimer = ReleaseConfig.InterpolationGraceTime;
        }
        else
        {
            InterpolationGraceTimer = 0f;
            RigidbodyComponent.interpolation = DefaultInterpolation;
        }
    }

    /// <summary>
    /// Updates runtime anchor tracking and evaluates hold and magnet break conditions.
    /// </summary>
    private void UpdateAnchorTracking()
    {
        if (ActiveAnchorFollower == null || ActiveAnchorTarget == null)
        {
            AnchorVelocity = Vector3.zero;
            return;
        }

        Vector3 CurrentAnchorPosition = ActiveAnchorTarget.position;
        AnchorVelocity = (CurrentAnchorPosition - LastAnchorPosition) / Mathf.Max(Time.fixedDeltaTime, 0.0001f);
        LastAnchorPosition = CurrentAnchorPosition;

        switch (CurrentState)
        {
            case InteractionState.Held:
                UpdateHoldBreakCheck(CurrentAnchorPosition);
                break;

            case InteractionState.Magnetized:
                UpdateMagnetBreakCheck(CurrentAnchorPosition);
                break;
        }
    }

    /// <summary>
    /// Breaks manual carry if the object drifts too far away from the hold anchor.
    /// </summary>
    private void UpdateHoldBreakCheck(Vector3 CurrentAnchorPosition)
    {
        float DistanceToAnchor = Vector3.Distance(RigidbodyComponent.worldCenterOfMass, CurrentAnchorPosition);
        if (DistanceToAnchor > BreakHoldDistance)
        {
            EndHold();
        }
    }

    /// <summary>
    /// Breaks magnet attachment when the object lags too far behind the magnet target.
    /// </summary>
    private void UpdateMagnetBreakCheck(Vector3 CurrentAnchorPosition)
    {
        float DistanceToAnchor = Vector3.Distance(RigidbodyComponent.worldCenterOfMass, CurrentAnchorPosition);

        if (DistanceToAnchor >= MagnetBreakConfig.HardBreakDistance)
        {
            ForceBreakMagnet();
            return;
        }

        if (DistanceToAnchor >= MagnetBreakConfig.BreakDistance)
        {
            MagnetBreakTimer += Time.fixedDeltaTime;

            if (MagnetBreakTimer >= MagnetBreakConfig.BreakGraceTime)
            {
                ForceBreakMagnet();
            }

            return;
        }

        MagnetBreakTimer = 0f;
    }

    /// <summary>
    /// Forces the current magnet attachment to break and starts a reattach cooldown.
    /// </summary>
    private void ForceBreakMagnet()
    {
        if (CurrentState != InteractionState.Magnetized)
        {
            return;
        }

        MagnetBreakTimer = 0f;
        MagnetReattachCooldownTimer = MagnetBreakConfig.ReattachCooldown;

        BeginReleasedState();
        Log("Magnet attachment broke.");
    }

    /// <summary>
    /// Updates the temporary released state and restores normal collision when safe.
    /// </summary>
    private void UpdateReleasedState()
    {
        if (CurrentState != InteractionState.Released)
        {
            return;
        }

        ReleasedTimer += Time.fixedDeltaTime;

        bool HasMetMinimumTime = ReleasedTimer >= ReleaseConfig.IgnoreMinimumTime;
        bool HasReachedMaximumTime = ReleasedTimer >= ReleaseConfig.IgnoreMaximumTime;
        bool IsSleeping = ReleaseConfig.RestoreOnSleep && RigidbodyComponent.IsSleeping();
        bool IsStillOverlappingPlayer = IsOverlappingIgnoredPlayerColliders();

        if ((HasMetMinimumTime && !IsStillOverlappingPlayer) || IsSleeping || HasReachedMaximumTime)
        {
            FinalizeReleasedState();
        }
    }

    /// <summary>
    /// Updates the interpolation grace countdown after release.
    /// </summary>
    private void UpdateInterpolationGrace()
    {
        if (InterpolationGraceTimer <= 0f)
        {
            return;
        }

        InterpolationGraceTimer -= Time.fixedDeltaTime;

        if (InterpolationGraceTimer <= 0f && CurrentState == InteractionState.Free)
        {
            RigidbodyComponent.interpolation = DefaultInterpolation;
        }
    }

    /// <summary>
    /// Updates the timer that blocks magnet reattachment after a forced break.
    /// </summary>
    private void UpdateMagnetReattachCooldown()
    {
        if (MagnetReattachCooldownTimer <= 0f)
        {
            return;
        }

        MagnetReattachCooldownTimer -= Time.fixedDeltaTime;
    }

    /// <summary>
    /// Returns true if any carryable collider is still intersecting any ignored player collider.
    /// </summary>
    private bool IsOverlappingIgnoredPlayerColliders()
    {
        if (IgnoredPlayerColliders == null || CachedColliders == null)
        {
            return false;
        }

        for (int CarryableColliderIndex = 0; CarryableColliderIndex < CachedColliders.Length; CarryableColliderIndex++)
        {
            Collider CarryableCollider = CachedColliders[CarryableColliderIndex];
            if (CarryableCollider == null || !CarryableCollider.enabled)
            {
                continue;
            }

            for (int PlayerColliderIndex = 0; PlayerColliderIndex < IgnoredPlayerColliders.Length; PlayerColliderIndex++)
            {
                Collider PlayerCollider = IgnoredPlayerColliders[PlayerColliderIndex];
                if (PlayerCollider == null || !PlayerCollider.enabled)
                {
                    continue;
                }

                bool IsOverlapping = Physics.ComputePenetration(
                    CarryableCollider,
                    CarryableCollider.transform.position,
                    CarryableCollider.transform.rotation,
                    PlayerCollider,
                    PlayerCollider.transform.position,
                    PlayerCollider.transform.rotation,
                    out _,
                    out _);

                if (IsOverlapping)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Applies or removes collision ignores against the current player colliders.
    /// </summary>
    private void SetIgnorePlayerCollision(bool Ignore)
    {
        if (IgnoredPlayerColliders == null || CachedColliders == null)
        {
            return;
        }

        for (int CarryableColliderIndex = 0; CarryableColliderIndex < CachedColliders.Length; CarryableColliderIndex++)
        {
            Collider CarryableCollider = CachedColliders[CarryableColliderIndex];
            if (CarryableCollider == null)
            {
                continue;
            }

            for (int PlayerColliderIndex = 0; PlayerColliderIndex < IgnoredPlayerColliders.Length; PlayerColliderIndex++)
            {
                Collider PlayerCollider = IgnoredPlayerColliders[PlayerColliderIndex];
                if (PlayerCollider == null)
                {
                    continue;
                }

                Physics.IgnoreCollision(CarryableCollider, PlayerCollider, Ignore);
            }
        }
    }

    /// <summary>
    /// Applies or restores the temporary layer used to ignore the player.
    /// </summary>
    private void SetIgnoredByPlayerLayer(bool UseIgnoredLayer)
    {
        if (CachedTransforms == null || CachedOriginalLayers == null)
        {
            return;
        }

        if (UseIgnoredLayer)
        {
            if (IgnoredByPlayerLayer < 0)
            {
                return;
            }

            for (int TransformIndex = 0; TransformIndex < CachedTransforms.Length; TransformIndex++)
            {
                Transform CurrentTransform = CachedTransforms[TransformIndex];
                if (CurrentTransform == null)
                {
                    continue;
                }

                CurrentTransform.gameObject.layer = IgnoredByPlayerLayer;
            }

            return;
        }

        for (int TransformIndex = 0; TransformIndex < CachedTransforms.Length; TransformIndex++)
        {
            Transform CurrentTransform = CachedTransforms[TransformIndex];
            if (CurrentTransform == null)
            {
                continue;
            }

            CurrentTransform.gameObject.layer = CachedOriginalLayers[TransformIndex];
        }
    }

    /// <summary>
    /// Caches the original layer of every transform in the hierarchy.
    /// </summary>
    private void CacheOriginalLayers()
    {
        CachedOriginalLayers = new int[CachedTransforms.Length];

        for (int TransformIndex = 0; TransformIndex < CachedTransforms.Length; TransformIndex++)
        {
            CachedOriginalLayers[TransformIndex] = CachedTransforms[TransformIndex].gameObject.layer;
        }
    }

    /// <summary>
    /// Resolves the layer used while temporarily ignoring the player.
    /// </summary>
    private void ResolveIgnoredByPlayerLayer()
    {
        IgnoredByPlayerLayer = LayerMask.NameToLayer(IgnoredByPlayerLayerName);

        if (IgnoredByPlayerLayer < 0)
        {
            Debug.LogError(
                $"Layer '{IgnoredByPlayerLayerName}' does not exist. Create it in Project Settings > Tags and Layers.",
                this);
        }
    }

    /// <summary>
    /// Writes a carryable-specific debug message when logging is enabled.
    /// </summary>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[PhysicsCarryable] " + name + " :: " + Message, this);
    }
}