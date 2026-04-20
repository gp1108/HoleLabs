using UnityEngine;

/// <summary>
/// Marks a world item placed directly in the scene so save/load can preserve its existence
/// without destroying the original scene object.
/// Attach this only to scene instances, never to the prefab asset.
/// </summary>
[DisallowMultipleComponent]
public sealed class ScenePlacedWorldItemPersistence : MonoBehaviour
{
    [Header("References")]
    [Tooltip("World item owned by this persistent scene object. If empty, one will be searched on this object or its children.")]
    [SerializeField] private WorldItem WorldItem;

    [Tooltip("Optional rigidbody reset when the item is restored from save.")]
    [SerializeField] private Rigidbody CachedRigidbody;

    /// <summary>
    /// Resolves missing cached references.
    /// </summary>
    private void Awake()
    {
        if (WorldItem == null)
        {
            WorldItem = GetComponent<WorldItem>();

            if (WorldItem == null)
            {
                WorldItem = GetComponentInChildren<WorldItem>(true);
            }
        }

        if (CachedRigidbody == null && WorldItem != null)
        {
            CachedRigidbody = WorldItem.GetRigidbody();
        }
    }

    /// <summary>
    /// Gets the world item represented by this scene persistence wrapper.
    /// </summary>
    public WorldItem GetWorldItem()
    {
        return WorldItem;
    }

    /// <summary>
    /// Gets whether the scene item is currently present in the world.
    /// </summary>
    public bool GetIsPresent()
    {
        return gameObject.activeSelf;
    }

    /// <summary>
    /// Hides or shows the scene item without destroying the original object.
    /// </summary>
    /// <param name="IsPresent">True to show the item, false to hide it.</param>
    public void SetPresent(bool IsPresent)
    {
        if (IsPresent)
        {
            ResetPhysicsState();
        }

        gameObject.SetActive(IsPresent);
    }

    /// <summary>
    /// Restores the scene item runtime state from save data.
    /// </summary>
    /// <param name="ItemInstance">Runtime item payload to apply.</param>
    /// <param name="Position">World position to restore.</param>
    /// <param name="Rotation">World rotation to restore.</param>
    public void ApplySavedState(ItemInstance ItemInstance, Vector3 Position, Quaternion Rotation)
    {
        if (WorldItem == null || ItemInstance == null)
        {
            return;
        }

        transform.SetPositionAndRotation(Position, Rotation);
        WorldItem.ApplyItemInstance(ItemInstance.Clone());
        ResetPhysicsState();
        gameObject.SetActive(true);
    }

    /// <summary>
    /// Resets rigidbody motion so the object comes back in a stable state.
    /// </summary>
    private void ResetPhysicsState()
    {
        if (CachedRigidbody == null)
        {
            return;
        }

        CachedRigidbody.linearVelocity = Vector3.zero;
        CachedRigidbody.angularVelocity = Vector3.zero;
        CachedRigidbody.Sleep();
    }
}