using UnityEngine;

/// <summary>
/// Converts generic lever snap indices into elevator motor commands.
/// The same binder can control either vertical travel or self rotation.
/// It also forces neutral when the elevator is overweighted or when no weight actor
/// remains inside the elevator trigger.
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
    [Tooltip("Target snap lever controlled by this binder.")]
    [SerializeField] private SnapLever SnapLever;

    [Tooltip("Target elevator motor controlled by this binder.")]
    [SerializeField] private ElevatorPhysicalMotor ElevatorPhysicalMotor;

    [Tooltip("Weight system used to validate whether the elevator can currently operate.")]
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

    [Header("Debug")]
    [Tooltip("Logs received snap indices and forced neutral states.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Whether the lever was forced to neutral during the previous frame.
    /// </summary>
    private bool WasForcedNeutralLastFrame;

    /// <summary>
    /// Enforces neutral lever state whenever the elevator cannot currently operate.
    /// </summary>
    private void LateUpdate()
    {
        if (SnapLever == null || ElevatorPhysicalMotor == null || ElevatorWeightSystem == null)
        {
            return;
        }

        bool MustForceNeutral =
            ElevatorWeightSystem.IsElevatorOverweighted() ||
            !ElevatorWeightSystem.HasAnyWeightActorInside();

        if (MustForceNeutral)
        {
            SnapLever.SetExternalLock(true, NeutralIndex);
            ApplyNeutralStateToMotor();

            if (!WasForcedNeutralLastFrame)
            {
                SnapLever.SetSnapIndexWithoutNotify(NeutralIndex);
                WasForcedNeutralLastFrame = true;

                if (DebugLogs)
                {
                    Debug.Log(
                        "[ElevatorLeverStateBinder] Lever forced to neutral. " +
                        "Mode=" + ControlMode +
                        " | Overweighted=" + ElevatorWeightSystem.IsElevatorOverweighted() +
                        " | HasActorInside=" + ElevatorWeightSystem.HasAnyWeightActorInside(),
                        this);
                }
            }

            return;
        }

        SnapLever.SetExternalLock(false, NeutralIndex);
        WasForcedNeutralLastFrame = false;
    }

    /// <summary>
    /// Applies the lever command associated with the given snap index.
    /// </summary>
    /// <param name="SnapIndex">Received snap index from the lever.</param>
    public void ApplyLeverState(int SnapIndex)
    {
        if (SnapLever == null || ElevatorPhysicalMotor == null || ElevatorWeightSystem == null)
        {
            return;
        }

        bool MustForceNeutral =
            ElevatorWeightSystem.IsElevatorOverweighted() ||
            !ElevatorWeightSystem.HasAnyWeightActorInside();

        if (MustForceNeutral)
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
}