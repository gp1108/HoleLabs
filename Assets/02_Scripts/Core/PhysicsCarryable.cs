using UnityEngine;

/// <summary>
/// Main orchestration component for physical carryable objects.
/// This component does not own low-level joint creation, collision ignore implementation or sleep policy details.
/// It only coordinates those helpers and exposes a clean gameplay API.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CarryableAttachmentDriver))]
[RequireComponent(typeof(CarryablePlayerCollisionGate))]
[RequireComponent(typeof(CarryableSleepController))]
public sealed class PhysicsCarryable : MonoBehaviour
{
    /// <summary>
    /// Identifies the current dynamic control source affecting the carryable.
    /// </summary>
    public enum CarryableControlMode
    {
        None,
        Hold,
        Magnet,
        Conveyor
    }

    /// <summary>
    /// Identifies the current high-level physics mode of the carryable.
    /// </summary>
    public enum CarryablePhysicsMode
    {
        Dynamic,
        ExternalKinematic
    }

    [System.Serializable]
    private struct ReleaseSettings
    {
        [Tooltip("Minimum time after releasing during which the object keeps ignoring the player.")]
        public float IgnoreMinimumTime;

        [Tooltip("Maximum time after releasing before the object is forced back to its normal collision state.")]
        public float IgnoreMaximumTime;

        [Tooltip("If true, the object can restore normal player collision immediately when it falls asleep.")]
        public bool RestoreOnSleep;

        [Tooltip("Velocity inherited from the followed anchor when the player releases a held object.")]
        public float HeldReleaseVelocityInfluence;

        [Tooltip("Impulse multiplier applied when the object is launched from the magnet.")]
        public float MagnetLaunchImpulseMultiplier;
    }

    [System.Serializable]
    private struct RuntimePhysicsDefaults
    {
        [Tooltip("If true, interpolation is forced while attached to a runtime driver.")]
        public bool ForceInterpolationWhileAttached;

        [Tooltip("Collision mode used while attached to a runtime driver.")]
        public CollisionDetectionMode AttachmentCollisionDetectionMode;
    }

    [Header("Shared")]
    [Tooltip("Shared runtime physics behaviour applied while attached to an interaction driver.")]
    [SerializeField] private RuntimePhysicsDefaults SharedRuntimePhysics = new RuntimePhysicsDefaults
    {
        ForceInterpolationWhileAttached = true,
        AttachmentCollisionDetectionMode = CollisionDetectionMode.ContinuousDynamic
    };

    [Header("Hold")]
    [Tooltip("Attachment settings used while the player is holding this carryable.")]
    [SerializeField] private CarryableAttachmentDriver.AttachmentSettings HoldSettings =
        new CarryableAttachmentDriver.AttachmentSettings
        {
            Spring = 115f,
            Damper = 22f,
            MaxDistance = 0.02f,
            LinearDamping = 3f,
            AngularDamping = 1.2f,
            DisableGravity = false,
            BreakDistance = 2.6f
        };

    [Header("Magnet")]
    [Tooltip("Attachment settings used while the carryable is magnetized.")]
    [SerializeField] private CarryableAttachmentDriver.AttachmentSettings MagnetSettings =
        new CarryableAttachmentDriver.AttachmentSettings
        {
            Spring = 100f,
            Damper = 25f,
            MaxDistance = 0.01f,
            LinearDamping = 3f,
            AngularDamping = 1.5f,
            DisableGravity = false,
            BreakDistance = 1.9f
        };

    [Header("Release")]
    [Tooltip("Settings used while returning from an interaction back to normal dynamic simulation.")]
    [SerializeField] private ReleaseSettings ReleaseConfig = new ReleaseSettings
    {
        IgnoreMinimumTime = 0.15f,
        IgnoreMaximumTime = 1.25f,
        RestoreOnSleep = true,
        HeldReleaseVelocityInfluence = 0.35f,
        MagnetLaunchImpulseMultiplier = 1f
    };

    [Header("Debug")]
    [Tooltip("Logs state transitions to the console.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Cached rigidbody exposed to external systems.
    /// </summary>
    public Rigidbody Rigidbody => RigidbodyComponent;

    /// <summary>
    /// Current dynamic control mode.
    /// </summary>
    public CarryableControlMode ControlMode { get; private set; } = CarryableControlMode.None;

    /// <summary>
    /// Current high-level physics mode.
    /// </summary>
    public CarryablePhysicsMode PhysicsMode { get; private set; } = CarryablePhysicsMode.Dynamic;

    /// <summary>
    /// Returns true if the rigidbody is currently sleeping.
    /// </summary>
    public bool IsSleeping => RigidbodyComponent != null && RigidbodyComponent.IsSleeping();

    /// <summary>
    /// Returns true if the carryable is externally carried by a kinematic carrier system.
    /// </summary>
    public bool IsExternallyCarried => PhysicsMode == CarryablePhysicsMode.ExternalKinematic;

    /// <summary>
    /// Cached rigidbody.
    /// </summary>
    private Rigidbody RigidbodyComponent;

    /// <summary>
    /// Runtime attachment driver.
    /// </summary>
    private CarryableAttachmentDriver AttachmentDriver;

    /// <summary>
    /// Runtime player collision gate.
    /// </summary>
    private CarryablePlayerCollisionGate CollisionGate;

    /// <summary>
    /// Explicit sleep controller.
    /// </summary>
    private CarryableSleepController SleepController;

    /// <summary>
    /// Default interpolation restored outside driven interactions.
    /// </summary>
    private RigidbodyInterpolation DefaultInterpolation;

    /// <summary>
    /// Default collision mode restored outside driven interactions.
    /// </summary>
    private CollisionDetectionMode DefaultCollisionDetectionMode;

    /// <summary>
    /// Default linear damping restored outside driven interactions.
    /// </summary>
    private float DefaultLinearDamping;

    /// <summary>
    /// Default angular damping restored outside driven interactions.
    /// </summary>
    private float DefaultAngularDamping;

    /// <summary>
    /// Default gravity flag restored outside driven interactions.
    /// </summary>
    private bool DefaultUseGravity;

    /// <summary>
    /// Cached player colliders used by the collision gate.
    /// </summary>
    private Collider[] ActivePlayerColliders;

    /// <summary>
    /// Parent that owned the transform before external kinematic carry started.
    /// </summary>
    private Transform PreviousParentBeforeExternalCarry;

    /// <summary>
    /// Remaining cooldown that blocks magnet reattachment after a forced break.
    /// </summary>
    private float MagnetReattachCooldownTimer;

    /// <summary>
    /// Caches references and default rigidbody values.
    /// </summary>
    private void Awake()
    {
        RigidbodyComponent = GetComponent<Rigidbody>();
        AttachmentDriver = GetComponent<CarryableAttachmentDriver>();
        CollisionGate = GetComponent<CarryablePlayerCollisionGate>();
        SleepController = GetComponent<CarryableSleepController>();

        DefaultInterpolation = RigidbodyComponent.interpolation;
        DefaultCollisionDetectionMode = RigidbodyComponent.collisionDetectionMode;
        DefaultLinearDamping = RigidbodyComponent.linearDamping;
        DefaultAngularDamping = RigidbodyComponent.angularDamping;
        DefaultUseGravity = RigidbodyComponent.useGravity;
    }

    /// <summary>
    /// Updates runtime attachment, release collision gate and explicit sleep policy.
    /// </summary>
    private void FixedUpdate()
    {
        if (MagnetReattachCooldownTimer > 0f)
        {
            MagnetReattachCooldownTimer -= Time.fixedDeltaTime;
        }

        if (PhysicsMode == CarryablePhysicsMode.ExternalKinematic)
        {
            return;
        }

        if (AttachmentDriver.IsActive)
        {
            bool ShouldBreak = AttachmentDriver.Tick();
            if (ShouldBreak)
            {
                if (ControlMode == CarryableControlMode.Hold)
                {
                    EndHold();
                }
                else if (ControlMode == CarryableControlMode.Magnet)
                {
                    ForceBreakMagnet();
                }
            }
        }

        CollisionGate.Tick(RigidbodyComponent.IsSleeping());

        bool CanSleep =
            PhysicsMode == CarryablePhysicsMode.Dynamic &&
            ControlMode == CarryableControlMode.None &&
            !CollisionGate.IsReleaseGraceActive;

        SleepController.Tick(CanSleep);
    }

    /// <summary>
    /// Fully restores the carryable when disabled.
    /// </summary>
    private void OnDisable()
    {
        ForceResetImmediate();
    }

    /// <summary>
    /// Returns true when the carryable can be picked up by the player.
    /// External kinematic carry is allowed because the hold flow will release it first.
    /// </summary>
    public bool CanBeginHold()
    {
        return ControlMode == CarryableControlMode.None;
    }

    /// <summary>
    /// Returns true when the carryable can attach to a magnet.
    /// External kinematic carry is allowed because the magnet flow will release it first.
    /// </summary>
    public bool CanAttachToMagnet()
    {
        return ControlMode != CarryableControlMode.Hold &&
               MagnetReattachCooldownTimer <= 0f;
    }

    /// <summary>
    /// Starts player hold mode.
    /// </summary>
    /// <param name="HoldAnchor">Hold target transform.</param>
    /// <param name="PlayerColliders">Player colliders to ignore.</param>
    public void BeginHold(Transform HoldAnchor, Collider[] PlayerColliders)
    {
        if (!CanBeginHold() || HoldAnchor == null)
        {
            return;
        }

        BeginDynamicControlMode(CarryableControlMode.Hold, HoldAnchor, PlayerColliders, HoldSettings, "HoldAnchor");
    }

    /// <summary>
    /// Ends player hold mode and starts the short post-release grace window.
    /// </summary>
    public void EndHold()
    {
        if (ControlMode != CarryableControlMode.Hold)
        {
            return;
        }

        RigidbodyComponent.linearVelocity += AttachmentDriver.AnchorVelocity * ReleaseConfig.HeldReleaseVelocityInfluence;
        EndDynamicControlMode(true);
    }

    /// <summary>
    /// Starts magnet mode.
    /// </summary>
    /// <param name="MagnetTarget">Magnet target transform.</param>
    /// <param name="PlayerColliders">Player colliders to ignore.</param>
    public void BeginMagnet(Transform MagnetTarget, Collider[] PlayerColliders)
    {
        if (!CanAttachToMagnet() || MagnetTarget == null)
        {
            return;
        }

        BeginDynamicControlMode(CarryableControlMode.Magnet, MagnetTarget, PlayerColliders, MagnetSettings, "MagnetAnchor");
    }

    /// <summary>
    /// Ends magnet mode and starts the short post-release grace window.
    /// </summary>
    public void EndMagnet()
    {
        if (ControlMode != CarryableControlMode.Magnet)
        {
            return;
        }

        EndDynamicControlMode(true);
    }

    /// <summary>
    /// Launches the carryable away from the magnet with an impulse.
    /// </summary>
    /// <param name="WorldImpulse">World impulse applied after leaving magnet mode.</param>
    public void LaunchFromMagnet(Vector3 WorldImpulse)
    {
        if (ControlMode == CarryableControlMode.Magnet)
        {
            EndMagnet();
        }

        RigidbodyComponent.AddForce(WorldImpulse * ReleaseConfig.MagnetLaunchImpulseMultiplier, ForceMode.Impulse);
        SleepController.WakeUp();
    }

    /// <summary>
    /// Starts external kinematic carry mode used by systems such as elevators or parking carriers.
    /// </summary>
    /// <param name="CarrierTransform">Carrier transform that will parent the object.</param>
    public void BeginExternalCarry(Transform CarrierTransform)
    {
        if (CarrierTransform == null || PhysicsMode == CarryablePhysicsMode.ExternalKinematic)
        {
            return;
        }

        if (ControlMode != CarryableControlMode.None)
        {
            EndDynamicControlMode(false);
        }

        CollisionGate.EndIgnore();

        PreviousParentBeforeExternalCarry = transform.parent;
        PhysicsMode = CarryablePhysicsMode.ExternalKinematic;

        RigidbodyComponent.linearVelocity = Vector3.zero;
        RigidbodyComponent.angularVelocity = Vector3.zero;

        RestoreDefaultDynamicPhysics();

        RigidbodyComponent.useGravity = false;
        RigidbodyComponent.isKinematic = true;
        RigidbodyComponent.interpolation = RigidbodyInterpolation.None;
        RigidbodyComponent.collisionDetectionMode = CollisionDetectionMode.Discrete;
        RigidbodyComponent.detectCollisions = true;

        transform.SetParent(CarrierTransform, true);

        SleepController.PushSleepBlock();
        Log("Entered external kinematic carry.");
    }

    /// <summary>
    /// Leaves external kinematic carry mode and restores dynamic simulation.
    /// </summary>
    /// <param name="InheritedVelocity">Velocity inherited from the carrier on release.</param>
    public void EndExternalCarry(Vector3 InheritedVelocity)
    {
        if (PhysicsMode != CarryablePhysicsMode.ExternalKinematic)
        {
            return;
        }

        transform.SetParent(PreviousParentBeforeExternalCarry, true);

        RigidbodyComponent.isKinematic = false;
        RestoreDefaultDynamicPhysics();
        RigidbodyComponent.linearVelocity = InheritedVelocity;
        RigidbodyComponent.angularVelocity = Vector3.zero;

        PhysicsMode = CarryablePhysicsMode.Dynamic;
        SleepController.PopSleepBlock();
        SleepController.WakeUp();

        Log("Exited external kinematic carry.");
    }

    /// <summary>
    /// Blocks explicit sleep while an external system requires the object to remain awake.
    /// </summary>
    public void PushSleepBlock()
    {
        SleepController.PushSleepBlock();
    }

    /// <summary>
    /// Removes one explicit sleep block.
    /// </summary>
    public void PopSleepBlock()
    {
        SleepController.PopSleepBlock();
    }

    /// <summary>
    /// Compatibility method kept for older systems.
    /// </summary>
    public void NotifyMagnetInfluence()
    {
        SleepController.WakeUp();
    }

    /// <summary>
    /// Compatibility method for future conveyor integrations.
    /// </summary>
    /// <param name="IsConveyorDriven">Unused compatibility flag.</param>
    public void SetConveyorDriven(bool IsConveyorDriven)
    {
    }

    /// <summary>
    /// Returns true when the carryable is currently held.
    /// </summary>
    public bool GetIsHeld()
    {
        return ControlMode == CarryableControlMode.Hold;
    }

    /// <summary>
    /// Returns true when the carryable is currently magnetized.
    /// </summary>
    public bool GetIsMagnetized()
    {
        return ControlMode == CarryableControlMode.Magnet;
    }

    /// <summary>
    /// Starts one of the dynamic control modes that use a runtime spring driver.
    /// </summary>
    /// <param name="NewMode">New control mode.</param>
    /// <param name="TargetTransform">Target transform used by the runtime driver.</param>
    /// <param name="PlayerColliders">Player colliders to ignore.</param>
    /// <param name="Settings">Attachment settings.</param>
    /// <param name="AnchorName">Runtime anchor name.</param>
    private void BeginDynamicControlMode(
        CarryableControlMode NewMode,
        Transform TargetTransform,
        Collider[] PlayerColliders,
        CarryableAttachmentDriver.AttachmentSettings Settings,
        string AnchorName)
    {
        if (PhysicsMode == CarryablePhysicsMode.ExternalKinematic)
        {
            EndExternalCarry(Vector3.zero);
        }

        if (ControlMode != CarryableControlMode.None)
        {
            EndDynamicControlMode(false);
        }

        ActivePlayerColliders = PlayerColliders;
        ControlMode = NewMode;

        AttachmentDriver.Begin(RigidbodyComponent, TargetTransform, Settings, AnchorName);

        RigidbodyComponent.linearDamping = Settings.LinearDamping;
        RigidbodyComponent.angularDamping = Settings.AngularDamping;
        RigidbodyComponent.useGravity = !Settings.DisableGravity;
        RigidbodyComponent.collisionDetectionMode = SharedRuntimePhysics.AttachmentCollisionDetectionMode;

        if (SharedRuntimePhysics.ForceInterpolationWhileAttached)
        {
            RigidbodyComponent.interpolation = RigidbodyInterpolation.Interpolate;
        }

        CollisionGate.BeginIgnore(PlayerColliders);
        SleepController.PushSleepBlock();
        SleepController.WakeUp();

        Log("Entered control mode: " + ControlMode);
    }

    /// <summary>
    /// Ends the current dynamic control mode and optionally starts release grace.
    /// </summary>
    /// <param name="BeginReleaseGrace">True to keep ignoring the player briefly after detach.</param>
    private void EndDynamicControlMode(bool BeginReleaseGrace)
    {
        AttachmentDriver.End();

        RestoreDefaultDynamicPhysics();

        if (BeginReleaseGrace)
        {
            CollisionGate.BeginReleaseGrace(
                ActivePlayerColliders,
                ReleaseConfig.IgnoreMinimumTime,
                ReleaseConfig.IgnoreMaximumTime,
                ReleaseConfig.RestoreOnSleep);
        }
        else
        {
            CollisionGate.EndIgnore();
        }

        ActivePlayerColliders = null;
        ControlMode = CarryableControlMode.None;
        SleepController.PopSleepBlock();
        SleepController.WakeUp();

        Log("Returned to dynamic free mode.");
    }

    /// <summary>
    /// Forces the current magnet mode to break and starts the magnet cooldown.
    /// </summary>
    private void ForceBreakMagnet()
    {
        if (ControlMode != CarryableControlMode.Magnet)
        {
            return;
        }

        MagnetReattachCooldownTimer = 0.35f;
        EndMagnet();
        Log("Magnet attachment broke.");
    }

    /// <summary>
    /// Restores the dynamic rigidbody defaults captured at startup.
    /// </summary>
    private void RestoreDefaultDynamicPhysics()
    {
        RigidbodyComponent.linearDamping = DefaultLinearDamping;
        RigidbodyComponent.angularDamping = DefaultAngularDamping;
        RigidbodyComponent.useGravity = DefaultUseGravity;
        RigidbodyComponent.collisionDetectionMode = DefaultCollisionDetectionMode;
        RigidbodyComponent.interpolation = DefaultInterpolation;
    }

    /// <summary>
    /// Fully restores the object to a safe default state immediately.
    /// </summary>
    private void ForceResetImmediate()
    {
        AttachmentDriver.End();
        CollisionGate.EndIgnore();

        if (PhysicsMode == CarryablePhysicsMode.ExternalKinematic)
        {
            transform.SetParent(PreviousParentBeforeExternalCarry, true);
            RigidbodyComponent.isKinematic = false;
            PhysicsMode = CarryablePhysicsMode.Dynamic;
        }

        ControlMode = CarryableControlMode.None;
        RestoreDefaultDynamicPhysics();
    }

    /// <summary>
    /// Writes a carryable-specific debug message when logging is enabled.
    /// </summary>
    /// <param name="Message">Message to log.</param>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[PhysicsCarryable] " + name + " :: " + Message, this);
    }
}