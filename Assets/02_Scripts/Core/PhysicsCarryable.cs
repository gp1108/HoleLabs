using UnityEngine;

/// <summary>
/// Handles physical dragging and interaction state for a carryable rigidbody object.
/// Adds explicit runtime states, optional layer swap while interacted, automatic sleeping,
/// magnetized support, and wake hooks for moving supports such as elevators.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class PhysicsCarryable : MonoBehaviour
{
    public enum InteractionState
    {
        Free = 0,
        Held = 1,
        Magnetized = 2,
        ConveyorDriven = 3,
        Sleeping = 4
    }

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

    [Tooltip("If true, collisions against the player colliders are ignored while the object is held.")]
    [SerializeField] private bool IgnorePlayerCollisionWhileHeld = true;

    [Tooltip("If true, the object rotation is frozen while the object is held.")]
    [SerializeField] private bool FreezeRotationWhileHeld = false;

    [Header("Magnetized Physics")]
    [Tooltip("Time after the last magnet influence frame before the object leaves the magnetized state.")]
    [SerializeField] private float MagnetInfluenceGraceTime = 0.08f;

    [Header("Interaction Layers")]
    [Tooltip("If true, the object swaps to a player-ignored layer while being held or magnetized.")]
    [SerializeField] private bool UsePlayerIgnoredInteractionLayer = true;

    [Tooltip("Layer name used while the object is free or sleeping.")]
    [SerializeField] private string FreeLayerName = "PhysicsObjects";

    [Tooltip("Layer name used while the object is held or magnetized so it no longer collides with the player.")]
    [SerializeField] private string PlayerIgnoredInteractionLayerName = "PlayerIgnoredPhysicsObjects";

    [Header("Sleep")]
    [Tooltip("If true, the rigidbody is automatically put to sleep when idle.")]
    [SerializeField] private bool EnableAutoSleep = true;

    [Tooltip("Minimum time the object must stay almost still before sleeping.")]
    [SerializeField] private float SleepDelay = 0.75f;

    [Tooltip("Linear speed threshold used to consider the object idle.")]
    [SerializeField] private float SleepLinearVelocityThreshold = 0.05f;

    [Tooltip("Angular speed threshold used to consider the object idle.")]
    [SerializeField] private float SleepAngularVelocityThreshold = 0.05f;

    [Tooltip("Minimum collision impulse required from another physics object to wake this sleeping object.")]
    [SerializeField] private float WakeImpactImpulseThreshold = 1.5f;

    [Tooltip("If true, collisions wake the object back up when it was sleeping.")]
    [SerializeField] private bool WakeOnCollision = true;
    [Tooltip("If true, collisions with other physicsObjects wake the object back up when it was sleeping.")]
    [SerializeField] private bool WakeOnCollisionWithPhysicObject = true;

    [Header("Debug")]
    [Tooltip("Draws the hold target relation in the Scene view.")]
    [SerializeField] private bool DrawDebug = false;

    [Tooltip("Logs interaction state changes to the console.")]
    [SerializeField] private bool DebugLogs = false;

    [Tooltip("Optional camera override used for the minimum camera distance check.")]
    [SerializeField] private Camera OverrideCamera;

    private Rigidbody RigidbodyComponent;
    private Transform HoldAnchor;
    private Collider[] PlayerColliders;
    private Collider[] CachedColliders;
    private Transform[] CachedHierarchyTransforms;
    private InteractionState CurrentState = InteractionState.Free;

    private float SavedLinearDamping;
    private float SavedAngularDamping;
    private bool SavedUseGravity;
    private CollisionDetectionMode SavedCollisionDetectionMode;
    private RigidbodyInterpolation SavedInterpolation;
    private RigidbodyConstraints SavedConstraints;

    private Vector3 LastHoldAnchorPosition;
    private Vector3 HoldAnchorVelocity;
    private float LastMagnetInfluenceTime = float.NegativeInfinity;
    private float IdleTimer;

    private int ResolvedFreeLayer = -1;
    private int ResolvedPlayerIgnoredLayer = -1;
    private bool HasResolvedPlayerIgnoredLayer;

    private void Awake()
    {
        RigidbodyComponent = GetComponent<Rigidbody>();
        CachedColliders = GetComponentsInChildren<Collider>(true);
        CachedHierarchyTransforms = GetComponentsInChildren<Transform>(true);

        SavedLinearDamping = RigidbodyComponent.linearDamping;
        SavedAngularDamping = RigidbodyComponent.angularDamping;
        SavedUseGravity = RigidbodyComponent.useGravity;
        SavedCollisionDetectionMode = RigidbodyComponent.collisionDetectionMode;
        SavedInterpolation = RigidbodyComponent.interpolation;
        SavedConstraints = RigidbodyComponent.constraints;

        ResolveInteractionLayers();
        ApplyFreeState();
    }

    private void FixedUpdate()
    {
        switch (CurrentState)
        {
            case InteractionState.Held:
                if (HoldAnchor != null)
                {
                    UpdateAnchorVelocity();
                    UpdateHoldDrag();
                    CheckAutoDrop();
                }
                break;

            case InteractionState.Magnetized:
                if (Time.time - LastMagnetInfluenceTime > MagnetInfluenceGraceTime)
                {
                    SetInteractionState(InteractionState.Free);
                }
                break;

            case InteractionState.ConveyorDriven:
                RigidbodyComponent.WakeUp();
                break;
        }

        UpdateSleepState();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (CurrentState != InteractionState.Sleeping)
        {
            return;
        }

        if (WakeOnCollision)
        {
            ForceWakeUp();
            return;
        }

        if (!WakeOnCollisionWithPhysicObject)
        {
            return;
        }

        if (collision.gameObject.layer != LayerMask.NameToLayer("PhysicsObjects") &&
            collision.gameObject.layer != LayerMask.NameToLayer("PlayerIgnoredPhysicsObjects"))
        {
            return;
        }

        if (collision.impulse.magnitude < WakeImpactImpulseThreshold)
        {
            return;
        }

        ForceWakeUp();
    }

    public void BeginHold(Transform NewHoldAnchor, Collider[] NewPlayerColliders)
    {
        if (NewHoldAnchor == null)
        {
            return;
        }

        if (CurrentState == InteractionState.Held)
        {
            EndHold();
        }

        HoldAnchor = NewHoldAnchor;
        PlayerColliders = NewPlayerColliders;
        LastHoldAnchorPosition = HoldAnchor.position;
        HoldAnchorVelocity = Vector3.zero;

        SetInteractionState(InteractionState.Held);
        SetIgnorePlayerCollision(true);

        if (DebugLogs)
        {
            Debug.Log($"Begin hold: {name}");
        }
    }

    public void EndHold()
    {
        if (CurrentState != InteractionState.Held)
        {
            return;
        }

        SetIgnorePlayerCollision(false);
        RigidbodyComponent.linearVelocity += HoldAnchorVelocity * ReleaseVelocityInfluence;

        HoldAnchor = null;
        PlayerColliders = null;
        HoldAnchorVelocity = Vector3.zero;

        SetInteractionState(InteractionState.Free);

        if (DebugLogs)
        {
            Debug.Log($"End hold: {name}");
        }
    }

    public void NotifyMagnetInfluence()
    {
        LastMagnetInfluenceTime = Time.time;

        if (CurrentState == InteractionState.Held)
        {
            return;
        }

        if (CurrentState != InteractionState.Magnetized)
        {
            SetInteractionState(InteractionState.Magnetized);
        }
        else
        {
            RigidbodyComponent.WakeUp();
        }
    }

    public void SetConveyorDriven(bool IsConveyorDriven)
    {
        if (CurrentState == InteractionState.Held)
        {
            return;
        }

        if (IsConveyorDriven)
        {
            SetInteractionState(InteractionState.ConveyorDriven);
        }
        else if (CurrentState == InteractionState.ConveyorDriven)
        {
            SetInteractionState(InteractionState.Free);
        }
    }

    public void ForceWakeUp()
    {
        IdleTimer = 0f;

        RigidbodyComponent.isKinematic = false;
        RigidbodyComponent.WakeUp();

        if (CurrentState == InteractionState.Sleeping)
        {
            SetInteractionState(InteractionState.Free);
        }
    }

    public bool GetIsHeld()
    {
        return CurrentState == InteractionState.Held;
    }

    public bool GetIsMagnetized()
    {
        return CurrentState == InteractionState.Magnetized;
    }

    public InteractionState GetInteractionState()
    {
        return CurrentState;
    }

    private void UpdateAnchorVelocity()
    {
        float DeltaTime = Mathf.Max(Time.fixedDeltaTime, 0.0001f);
        Vector3 CurrentAnchorPosition = HoldAnchor.position;
        HoldAnchorVelocity = (CurrentAnchorPosition - LastHoldAnchorPosition) / DeltaTime;
        LastHoldAnchorPosition = CurrentAnchorPosition;
    }

    private void UpdateHoldDrag()
    {
        Vector3 DesiredPoint = GetClampedDesiredPoint();
        Vector3 CurrentPoint = RigidbodyComponent.worldCenterOfMass;
        Vector3 PositionError = DesiredPoint - CurrentPoint;

        if (PositionError.magnitude > MaxHoldErrorDistance)
        {
            PositionError = PositionError.normalized * MaxHoldErrorDistance;
        }

        Vector3 DesiredVelocity =
            (PositionError * HoldPositionStrength) +
            (HoldAnchorVelocity * HoldAnchorVelocityInfluence);

        Vector3 VelocityError = DesiredVelocity - RigidbodyComponent.linearVelocity;
        Vector3 RequiredAcceleration = VelocityError * HoldVelocityDamping;
        Vector3 ClampedAcceleration = Vector3.ClampMagnitude(RequiredAcceleration, MaxHoldAcceleration);

        RigidbodyComponent.AddForce(ClampedAcceleration, ForceMode.Acceleration);

        float MaxSpeedSquared = HeldMaxSpeed * HeldMaxSpeed;

        if (RigidbodyComponent.linearVelocity.sqrMagnitude > MaxSpeedSquared)
        {
            RigidbodyComponent.linearVelocity = RigidbodyComponent.linearVelocity.normalized * HeldMaxSpeed;
        }

        if (DrawDebug)
        {
            Debug.DrawLine(CurrentPoint, DesiredPoint, Color.yellow);
            Debug.DrawRay(DesiredPoint, HoldAnchorVelocity * 0.05f, Color.cyan);
        }
    }

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

    private void CheckAutoDrop()
    {
        Vector3 DesiredPoint = GetClampedDesiredPoint();
        float DistanceToAnchor = Vector3.Distance(RigidbodyComponent.worldCenterOfMass, DesiredPoint);

        if (DistanceToAnchor > BreakHoldDistance)
        {
            if (DebugLogs)
            {
                Debug.Log($"Auto drop because '{name}' exceeded break distance.");
            }

            EndHold();
        }
    }

    private void UpdateSleepState()
    {
        if (!EnableAutoSleep)
        {
            return;
        }

        if (CurrentState == InteractionState.Held || CurrentState == InteractionState.Magnetized || CurrentState == InteractionState.ConveyorDriven)
        {
            IdleTimer = 0f;
            return;
        }

        if (CurrentState == InteractionState.Sleeping)
        {
            return;
        }

        bool IsNearlyStill =
            RigidbodyComponent.linearVelocity.magnitude <= SleepLinearVelocityThreshold &&
            RigidbodyComponent.angularVelocity.magnitude <= SleepAngularVelocityThreshold;

        if (!IsNearlyStill)
        {
            IdleTimer = 0f;
            return;
        }

        IdleTimer += Time.fixedDeltaTime;

        if (IdleTimer >= SleepDelay)
        {
            RigidbodyComponent.Sleep();
            SetInteractionState(InteractionState.Sleeping);
        }
    }

    private void SetInteractionState(InteractionState NewState)
    {
        if (CurrentState == NewState)
        {
            return;
        }

        CurrentState = NewState;

        switch (CurrentState)
        {
            case InteractionState.Free:
                ApplyFreeState();
                break;

            case InteractionState.Held:
                ApplyHeldState();
                break;

            case InteractionState.Magnetized:
                ApplyMagnetizedState();
                break;

            case InteractionState.ConveyorDriven:
                ApplyConveyorDrivenState();
                break;

            case InteractionState.Sleeping:
                ApplySleepingState();
                break;
        }

        if (DebugLogs)
        {
            Debug.Log($"Carryable state changed to {CurrentState}: {name}");
        }
    }

    private void ApplyFreeState()
    {
        RigidbodyComponent.isKinematic = false;
        IdleTimer = 0f;
        RigidbodyComponent.linearDamping = SavedLinearDamping;
        RigidbodyComponent.angularDamping = SavedAngularDamping;
        RigidbodyComponent.useGravity = SavedUseGravity;
        RigidbodyComponent.collisionDetectionMode = SavedCollisionDetectionMode;
        RigidbodyComponent.interpolation = SavedInterpolation;
        RigidbodyComponent.constraints = SavedConstraints;

        ApplyInteractionLayer(false);
        RigidbodyComponent.WakeUp();
    }

    private void ApplyHeldState()
    {
        RigidbodyComponent.isKinematic = false;
        IdleTimer = 0f;
        RigidbodyComponent.linearDamping = HeldLinearDamping;
        RigidbodyComponent.angularDamping = HeldAngularDamping;
        RigidbodyComponent.useGravity = DisableGravityWhileHeld ? false : SavedUseGravity;
        RigidbodyComponent.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        RigidbodyComponent.interpolation = RigidbodyInterpolation.Interpolate;
        RigidbodyComponent.constraints = FreezeRotationWhileHeld
            ? SavedConstraints | RigidbodyConstraints.FreezeRotation
            : SavedConstraints;

        ApplyInteractionLayer(true);
        RigidbodyComponent.WakeUp();
    }

    private void ApplyMagnetizedState()
    {
        RigidbodyComponent.isKinematic = false;
        IdleTimer = 0f;
        RigidbodyComponent.linearDamping = 0f;
        RigidbodyComponent.angularDamping = 0f;
        RigidbodyComponent.useGravity = DisableGravityWhileHeld ? false : SavedUseGravity;
        RigidbodyComponent.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        RigidbodyComponent.interpolation = RigidbodyInterpolation.Interpolate;
        RigidbodyComponent.constraints = FreezeRotationWhileHeld
            ? SavedConstraints | RigidbodyConstraints.FreezeRotation
            : SavedConstraints;

        ApplyInteractionLayer(true);
        RigidbodyComponent.WakeUp();
    }

    private void ApplyConveyorDrivenState()
    {
        RigidbodyComponent.isKinematic = false;
        IdleTimer = 0f;
        RigidbodyComponent.linearDamping = SavedLinearDamping;
        RigidbodyComponent.angularDamping = SavedAngularDamping;
        RigidbodyComponent.useGravity = SavedUseGravity;
        RigidbodyComponent.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        RigidbodyComponent.interpolation = RigidbodyInterpolation.Interpolate;
        RigidbodyComponent.constraints = SavedConstraints;

        ApplyInteractionLayer(false);
        RigidbodyComponent.WakeUp();
    }

    private void ApplySleepingState()
    {
        ApplyInteractionLayer(false);
        RigidbodyComponent.isKinematic = true;
    }

    private void ResolveInteractionLayers()
    {
        ResolvedFreeLayer = LayerMask.NameToLayer(FreeLayerName);
        ResolvedPlayerIgnoredLayer = LayerMask.NameToLayer(PlayerIgnoredInteractionLayerName);
        HasResolvedPlayerIgnoredLayer = ResolvedPlayerIgnoredLayer >= 0;

        if (UsePlayerIgnoredInteractionLayer && !HasResolvedPlayerIgnoredLayer && DebugLogs)
        {
            Debug.LogWarning(
                $"Player ignored layer '{PlayerIgnoredInteractionLayerName}' was not found. " +
                $"The carryable will keep its normal layer while magnetized or held."
            );
        }
    }

    private void ApplyInteractionLayer(bool UsePlayerIgnoredLayer)
    {
        if (!UsePlayerIgnoredInteractionLayer || !HasResolvedPlayerIgnoredLayer || CachedHierarchyTransforms == null)
        {
            return;
        }

        int TargetLayer = UsePlayerIgnoredLayer ? ResolvedPlayerIgnoredLayer : ResolvedFreeLayer;

        if (TargetLayer < 0)
        {
            return;
        }

        for (int Index = 0; Index < CachedHierarchyTransforms.Length; Index++)
        {
            CachedHierarchyTransforms[Index].gameObject.layer = TargetLayer;
        }
    }

    private void SetIgnorePlayerCollision(bool Ignore)
    {
        if (!IgnorePlayerCollisionWhileHeld || PlayerColliders == null || CachedColliders == null)
        {
            return;
        }

        for (int PlayerIndex = 0; PlayerIndex < PlayerColliders.Length; PlayerIndex++)
        {
            Collider CurrentPlayerCollider = PlayerColliders[PlayerIndex];

            if (CurrentPlayerCollider == null)
            {
                continue;
            }

            for (int CarryableIndex = 0; CarryableIndex < CachedColliders.Length; CarryableIndex++)
            {
                Collider CurrentCarryableCollider = CachedColliders[CarryableIndex];

                if (CurrentCarryableCollider == null)
                {
                    continue;
                }

                Physics.IgnoreCollision(CurrentPlayerCollider, CurrentCarryableCollider, Ignore);
            }
        }
    }
}