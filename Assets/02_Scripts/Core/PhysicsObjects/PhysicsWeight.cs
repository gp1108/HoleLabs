using UnityEngine;

/// <summary>
/// Simple explicit weight provider for physical props that are not ores or inventory world items.
/// Add this component to any carryable object that should contribute to elevator load.
/// </summary>
[DisallowMultipleComponent]
public sealed class PhysicsWeight : MonoBehaviour, IWeightProvider
{
    [Header("Weight")]
    [Tooltip("Gameplay weight contributed by this physical object when it is inside the elevator or carried by the player.")]
    [SerializeField] private float Weight = 1f;

    /// <summary>
    /// Gets the configured gameplay weight.
    /// </summary>
    /// <returns>Non-negative weight value.</returns>
    public float GetWeight()
    {
        return Mathf.Max(0f, Weight);
    }
}
