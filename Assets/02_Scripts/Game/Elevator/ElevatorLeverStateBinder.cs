using UnityEngine;

/// <summary>
/// Converts generic lever snap indices into elevator motor commands and forces neutral
/// when the elevator is overweighted or when no weight actor remains inside the elevator trigger.
/// </summary>
[DisallowMultipleComponent]
public sealed class ElevatorLeverStateBinder : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Target snap lever controlled by this binder.")]
    [SerializeField] private SnapLever SnapLever;

    [Tooltip("Target elevator motor controlled by this binder.")]
    [SerializeField] private ElevatorPhysicalMotor ElevatorPhysicalMotor;

    [Tooltip("Weight system used to validate whether the elevator can currently operate.")]
    [SerializeField] private ElevatorWeightSystem ElevatorWeightSystem;

    [Header("Indices")]
    [Tooltip("Snap index mapped to downward movement.")]
    [SerializeField] private int DownIndex = 0;

    [Tooltip("Snap index mapped to neutral stop.")]
    [SerializeField] private int NeutralIndex = 1;

    [Tooltip("Snap index mapped to upward movement.")]
    [SerializeField] private int UpIndex = 2;

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
            ElevatorPhysicalMotor.Stop();

            if (!WasForcedNeutralLastFrame)
            {
                SnapLever.SetSnapIndexWithoutNotify(NeutralIndex);
                WasForcedNeutralLastFrame = true;

                if (DebugLogs)
                {
                    Debug.Log(
                        "[ElevatorLeverStateBinder] Lever forced to neutral. " +
                        "Overweighted=" + ElevatorWeightSystem.IsElevatorOverweighted() +
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
    /// Applies the vertical elevator command associated with the given snap index.
    /// </summary>
    /// <param name="SnapIndex">Received snap index from the lever.</param>
    public void ApplyVerticalState(int SnapIndex)
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
            ElevatorPhysicalMotor.Stop();
            return;
        }

        if (SnapIndex == UpIndex)
        {
            ElevatorPhysicalMotor.MoveUp();
            return;
        }

        if (SnapIndex == DownIndex)
        {
            ElevatorPhysicalMotor.MoveDown();
            return;
        }

        ElevatorPhysicalMotor.Stop();
    }
}