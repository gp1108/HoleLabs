using UnityEngine;

/// <summary>
/// Converts generic lever snap indices into elevator motor commands.
/// The same binder can control either vertical travel or self rotation.
/// It also forces neutral when the elevator is overweighted, when no weight actor
/// remains inside the elevator trigger, or when the selected subsystem is locked by progression.
/// Installed lever prefabs can resolve their elevator references automatically at runtime.
/// </summary>
[DisallowMultipleComponent]
public sealed class ElevatorLeverStateBinder : MonoBehaviour
{
    /// <summary>
    /// Defines which elevator subsystem is controlled by this lever.
    /// </summary>
    private enum LeverControlMode
    {
        Vertical,
        Rotation
    }

    [Header("References")]
    [Tooltip("Target snap lever controlled by this binder. If empty, one is resolved in this hierarchy.")]
    [SerializeField] private SnapLever SnapLever;

    [Tooltip("Target elevator motor controlled by this binder. If empty, the first active motor is resolved at runtime.")]
    [SerializeField] private ElevatorPhysicalMotor ElevatorPhysicalMotor;

    [Tooltip("Weight system used to validate whether the elevator can currently operate. If empty, it is resolved from the motor or scene.")]
    [SerializeField] private ElevatorWeightSystem ElevatorWeightSystem;

    [Header("Mode")]
    [Tooltip("Determines whether this lever controls vertical travel or self rotation.")]
    [SerializeField] private LeverControlMode ControlMode = LeverControlMode.Vertical;

    [Header("Indices")]
    [Tooltip("Snap index mapped to the negative direction. Vertical: down. Rotation: left.")]
    [SerializeField] private int NegativeIndex = 0;

    [Tooltip("Snap index mapped to neutral stop.")]
    [SerializeField] private int NeutralIndex = 1;

    [Tooltip("Snap index mapped to the positive direction. Vertical: up. Rotation: right.")]
    [SerializeField] private int PositiveIndex = 2;

    [Header("Progression Locks")]
    [Tooltip("If true, rotation levers are locked to neutral until the elevator motor reports rotation as unlocked.")]
    [SerializeField] private bool LockRotationLeverUntilUnlocked = true;

    [Header("Auto Resolve")]
    [Tooltip("If true, missing motor and weight references are resolved automatically. Useful for installed prefabs spawned at runtime.")]
    [SerializeField] private bool AutoResolveReferences = true;

    [Header("Debug")]
    [Tooltip("Logs received snap indices, forced neutral states and auto-resolved references.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Whether the lever was forced to neutral during the previous frame.
    /// </summary>
    private bool WasForcedNeutralLastFrame;

    /// <summary>
    /// Last force-neutral reason logged by this binder.
    /// </summary>
    private string LastForceNeutralReason;

    /// <summary>
    /// Whether this binder has subscribed to the snap lever runtime event.
    /// </summary>
    private bool HasSubscribedToSnapLever;

    /// <summary>
    /// Resolves references before runtime interaction starts.
    /// </summary>
    private void Awake()
    {
        ResolveReferences();
    }

    /// <summary>
    /// Subscribes to the snap lever so installed prefabs do not require manual UnityEvent wiring.
    /// </summary>
    private void OnEnable()
    {
        ResolveReferences();
        SubscribeToSnapLever();
    }

    /// <summary>
    /// Unsubscribes from runtime snap events.
    /// </summary>
    private void OnDisable()
    {
        UnsubscribeFromSnapLever();
    }

    /// <summary>
    /// Enforces neutral lever state whenever the elevator cannot currently operate.
    /// </summary>
    private void LateUpdate()
    {
        ResolveReferences();

        if (SnapLever == null || ElevatorPhysicalMotor == null || ElevatorWeightSystem == null)
        {
            return;
        }

        bool MustForceNeutral = ShouldForceNeutral(out string ForceReason);

        if (MustForceNeutral)
        {
            SnapLever.SetExternalLock(true, NeutralIndex);
            ApplyNeutralStateToMotor();

            if (!WasForcedNeutralLastFrame || LastForceNeutralReason != ForceReason)
            {
                SnapLever.SetSnapIndexWithoutNotify(NeutralIndex);
                WasForcedNeutralLastFrame = true;
                LastForceNeutralReason = ForceReason;

                Log("Lever forced to neutral. Mode=" + ControlMode + " | Reason=" + ForceReason);
            }

            return;
        }

        SnapLever.SetExternalLock(false, NeutralIndex);
        WasForcedNeutralLastFrame = false;
        LastForceNeutralReason = string.Empty;
    }

    /// <summary>
    /// Applies the lever command associated with the given snap index.
    /// </summary>
    /// <param name="SnapIndex">Received snap index from the lever.</param>
    public void ApplyLeverState(int SnapIndex)
    {
        ResolveReferences();

        if (SnapLever == null || ElevatorPhysicalMotor == null || ElevatorWeightSystem == null)
        {
            return;
        }

        if (ShouldForceNeutral(out _))
        {
            SnapLever.SetSnapIndexWithoutNotify(NeutralIndex);
            ApplyNeutralStateToMotor();
            return;
        }

        if (SnapIndex == PositiveIndex)
        {
            ApplyPositiveStateToMotor();
            return;
        }

        if (SnapIndex == NegativeIndex)
        {
            ApplyNegativeStateToMotor();
            return;
        }

        ApplyNeutralStateToMotor();
    }

    /// <summary>
    /// Resolves missing references for installed prefab usage.
    /// </summary>
    private void ResolveReferences()
    {
        if (!AutoResolveReferences)
        {
            return;
        }

        if (SnapLever == null)
        {
            SnapLever = GetComponent<SnapLever>();

            if (SnapLever == null)
            {
                SnapLever = GetComponentInParent<SnapLever>();
            }

            if (SnapLever == null)
            {
                SnapLever = GetComponentInChildren<SnapLever>(true);
            }
        }

        if (ElevatorPhysicalMotor == null)
        {
            ElevatorPhysicalMotor = GetComponentInParent<ElevatorPhysicalMotor>();

            if (ElevatorPhysicalMotor == null)
            {
                ElevatorPhysicalMotor = FindFirstObjectByType<ElevatorPhysicalMotor>();
            }
        }

        if (ElevatorWeightSystem == null)
        {
            if (ElevatorPhysicalMotor != null)
            {
                ElevatorWeightSystem = ElevatorPhysicalMotor.GetComponentInChildren<ElevatorWeightSystem>(true);

                if (ElevatorWeightSystem == null)
                {
                    ElevatorWeightSystem = ElevatorPhysicalMotor.GetComponentInParent<ElevatorWeightSystem>();
                }
            }

            if (ElevatorWeightSystem == null)
            {
                ElevatorWeightSystem = GetComponentInParent<ElevatorWeightSystem>();
            }

            if (ElevatorWeightSystem == null)
            {
                ElevatorWeightSystem = FindFirstObjectByType<ElevatorWeightSystem>();
            }
        }
    }

    /// <summary>
    /// Subscribes this binder to the snap lever runtime event.
    /// </summary>
    private void SubscribeToSnapLever()
    {
        if (SnapLever == null || HasSubscribedToSnapLever)
        {
            return;
        }

        SnapLever.AddSnapChangedListener(ApplyLeverState);
        HasSubscribedToSnapLever = true;
    }

    /// <summary>
    /// Removes this binder from the snap lever runtime event.
    /// </summary>
    private void UnsubscribeFromSnapLever()
    {
        if (SnapLever == null || !HasSubscribedToSnapLever)
        {
            return;
        }

        SnapLever.RemoveSnapChangedListener(ApplyLeverState);
        HasSubscribedToSnapLever = false;
    }

    /// <summary>
    /// Returns whether the lever must be forced to neutral because the controlled subsystem cannot operate.
    /// </summary>
    /// <param name="Reason">Human-readable reason used for debug logs.</param>
    /// <returns>True when the lever must be locked to neutral.</returns>
    private bool ShouldForceNeutral(out string Reason)
    {
        if (ElevatorWeightSystem.IsElevatorOverweighted())
        {
            Reason = "Overweighted";
            return true;
        }

        if (!ElevatorWeightSystem.HasAnyWeightActorInside())
        {
            Reason = "NoWeightActorInside";
            return true;
        }

        if (ControlMode == LeverControlMode.Rotation && LockRotationLeverUntilUnlocked && !ElevatorPhysicalMotor.GetIsRotationUnlocked())
        {
            Reason = "RotationLocked";
            return true;
        }

        Reason = string.Empty;
        return false;
    }

    /// <summary>
    /// Applies the positive-direction command to the selected motor subsystem.
    /// </summary>
    private void ApplyPositiveStateToMotor()
    {
        if (ControlMode == LeverControlMode.Vertical)
        {
            ElevatorPhysicalMotor.MoveUp();
        }
        else
        {
            ElevatorPhysicalMotor.RotateRight();
        }
    }

    /// <summary>
    /// Applies the negative-direction command to the selected motor subsystem.
    /// </summary>
    private void ApplyNegativeStateToMotor()
    {
        if (ControlMode == LeverControlMode.Vertical)
        {
            ElevatorPhysicalMotor.MoveDown();
        }
        else
        {
            ElevatorPhysicalMotor.RotateLeft();
        }
    }

    /// <summary>
    /// Applies the neutral command to the selected motor subsystem.
    /// </summary>
    private void ApplyNeutralStateToMotor()
    {
        if (ControlMode == LeverControlMode.Vertical)
        {
            ElevatorPhysicalMotor.Stop();
        }
        else
        {
            ElevatorPhysicalMotor.StopRotation();
        }
    }

    /// <summary>
    /// Writes a debug message when enabled.
    /// </summary>
    /// <param name="Message">Message to write.</param>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[ElevatorLeverStateBinder] " + Message, this);
    }
}
