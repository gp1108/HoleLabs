using UnityEngine;

/// <summary>
/// Defines the base body weight contributed by the player while inside an elevator.
/// Additional carried weight is evaluated by the elevator weight system.
/// </summary>
[DisallowMultipleComponent]
public sealed class ElevatorWeightActor : MonoBehaviour
{
    [Tooltip("Base body weight contributed while this actor is inside the elevator.")]
    [SerializeField] private float BaseWeight = 0f;

    /// <summary>
    /// Gets the base body weight of this actor.
    /// </summary>
    public float GetBaseWeight()
    {
        return Mathf.Max(0f, BaseWeight);
    }
}