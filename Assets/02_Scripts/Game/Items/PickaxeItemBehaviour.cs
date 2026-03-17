using UnityEngine;

/// <summary>
/// Example equipped pickaxe behaviour.
/// </summary>
public sealed class PickaxeItemBehaviour : EquippedItemBehaviour
{
    [Header("Mining")]
    [Tooltip("Mining strength applied by this pickaxe.")]
    [SerializeField] private float MiningPower = 1f;

    /// <summary>
    /// Called when the primary use input starts.
    /// </summary>
    public override void OnPrimaryUseStarted()
    {
        Debug.Log("Pickaxe primary use started.");
    }

    /// <summary>
    /// Called every frame while the primary use input is held.
    /// </summary>
    public override void OnPrimaryUseHeld()
    {
        Debug.Log("Pickaxe mining...");
    }

    /// <summary>
    /// Called when the primary use input ends.
    /// </summary>
    public override void OnPrimaryUseEnded()
    {
        Debug.Log("Pickaxe primary use ended.");
    }

    /// <summary>
    /// Called when the primary use input starts.
    /// </summary>
    public override void OnSecondaryUseStarted()
    {
        Debug.Log("Pickaxe secondary use started.");
    }

    /// <summary>
    /// Called every frame while the primary use input is held.
    /// </summary>
    public override void OnSecondaryUseHeld()
    {
        Debug.Log("Pickaxe mining secondary...");
    }

    /// <summary>
    /// Called when the primary use input ends.
    /// </summary>
    public override void OnSecondaryUseEnded()
    {
        Debug.Log("Pickaxe secondary use ended.");
    }
}