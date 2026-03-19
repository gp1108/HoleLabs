using UnityEngine;

/// <summary>
/// Wake zone for carryable rigidbodies.
/// Use this on elevators, moving supports or trigger volumes that should prevent nearby carryables
/// from remaining asleep while the support beneath them is moving.
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class PhysicsCarryableWakeZone : MonoBehaviour
{
    [Header("Behaviour")]
    [Tooltip("If true, carryables are force-woken while they stay inside the trigger.")]
    [SerializeField] private bool WakeWhileInside = true;

    [Tooltip("If true, carryables are marked as conveyor-driven while they stay inside the trigger.")]
    [SerializeField] private bool MarkAsConveyorDriven = false;

    [Header("Debug")]
    [Tooltip("Logs wake zone events to the console.")]
    [SerializeField] private bool DebugLogs = false;

    private void Reset()
    {
        Collider triggerCollider = GetComponent<Collider>();
        if (triggerCollider != null)
        {
            triggerCollider.isTrigger = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        HandleCarryable(other, true);
    }

    private void OnTriggerStay(Collider other)
    {
        if (WakeWhileInside)
        {
            HandleCarryable(other, true);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        HandleCarryable(other, false);
    }

    private void HandleCarryable(Collider other, bool isInside)
    {
        if (other == null)
        {
            return;
        }

        PhysicsCarryable carryable = other.GetComponent<PhysicsCarryable>() ?? other.GetComponentInParent<PhysicsCarryable>();
        if (carryable == null)
        {
            return;
        }

        if (isInside)
        {
            carryable.ForceWakeUp();

            if (MarkAsConveyorDriven)
            {
                carryable.SetConveyorDriven(true);
            }

            if (DebugLogs)
            {
                Debug.Log("[PhysicsCarryableWakeZone] Wake carryable: " + carryable.name);
            }
        }
        else
        {
            if (MarkAsConveyorDriven)
            {
                carryable.SetConveyorDriven(false);
            }

            if (DebugLogs)
            {
                Debug.Log("[PhysicsCarryableWakeZone] Exit carryable: " + carryable.name);
            }
        }
    }
}
