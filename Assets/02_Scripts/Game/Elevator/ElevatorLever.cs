using UnityEngine;

/// <summary>
/// Simple elevator lever used to control vertical movement and optional rotation.
/// This script is intentionally small so interaction logic can call a single public method.
/// </summary>
public sealed class ElevatorLever : MonoBehaviour
{
    /// <summary>
    /// Defines the action performed when the lever is activated.
    /// </summary>
    private enum LeverAction
    {
        ToggleDirection,
        MoveUp,
        MoveDown,
        StopVerticalMovement
    }

    [Header("References")]
    [Tooltip("Elevator controlled by this lever.")]
    [SerializeField] private ElevatorPhysicalMotor Elevator;

    [Header("Action")]
    [Tooltip("Action performed when the lever is activated.")]
    [SerializeField] private LeverAction Action = LeverAction.ToggleDirection;

    /// <summary>
    /// Executes the configured action on the target elevator.
    /// </summary>
    [ContextMenu("Activate")]
    public void Activate()
    {
        if (Elevator == null)
        {
            Debug.LogWarning("Elevator reference is missing.");
            return;
        }

        switch (Action)
        {
            //case LeverAction.ToggleDirection:
            //    Elevator.ToggleDirection();
            //    break;

            case LeverAction.MoveUp:
                Elevator.MoveUp();
                break;

            case LeverAction.MoveDown:
                Elevator.MoveDown();
                break;

            case LeverAction.StopVerticalMovement:
                Elevator.Stop();
                break;
        }
    }
}