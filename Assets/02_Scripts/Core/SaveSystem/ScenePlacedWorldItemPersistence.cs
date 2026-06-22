using UnityEngine;

/// <summary>
/// Marks a world item placed directly in the scene so save/load can preserve its existence
/// without destroying the original scene object.
/// Runtime-spawned world items must never be preserved by this component, even if their prefab accidentally contains it.
/// </summary>
[DisallowMultipleComponent]
public sealed class ScenePlacedWorldItemPersistence : MonoBehaviour
{
    [Header("References")]
    [Tooltip("World item owned by this persistent scene object. If empty, one will be searched on this object or its children.")]
    [SerializeField] private WorldItem WorldItem;

    [Tooltip("Optional rigidbody reset when the item is restored from save.")]
    [SerializeField] private Rigidbody CachedRigidbody;

    [Header("Runtime Safety")]
    [Tooltip("If true, this scene object is hidden instead of destroyed when collected. Disable this on any prefab that should behave like a runtime item.")]
    [SerializeField] private bool PreserveWhenCollected = true;

    /// <summary>
    /// True when this component belongs to a runtime-spawned clone and must not preserve the object when collected.
    /// </summary>
    private bool IsRuntimeSpawnedInstance;

    /// <summary>
    /// Resolves missing cached references.
    /// </summary>
    private void Awake()
    {
        ResolveReferences();
    }

    /// <summary>
    /// Marks every scene persistence component found under the provided root as runtime-spawned.
    /// This prevents runtime drops or shop-delivered items from accumulating as disabled hierarchy objects when collected.
    /// </summary>
    /// <param name="Root">Runtime-spawned object root.</param>
    public static void MarkRuntimeSpawnedObject(GameObject Root)
    {
        if (Root == null)
        {
            return;
        }

        ScenePlacedWorldItemPersistence[] PersistenceComponents = Root.GetComponentsInChildren<ScenePlacedWorldItemPersistence>(true);

        if (PersistenceComponents == null)
        {
            return;
        }

        for (int Index = 0; Index < PersistenceComponents.Length; Index++)
        {
            if (PersistenceComponents[Index] == null)
            {
                continue;
            }

            PersistenceComponents[Index].MarkAsRuntimeSpawnedInstance();
        }
    }

    /// <summary>
    /// Marks this persistence component as belonging to a runtime-spawned object.
    /// </summary>
    public void MarkAsRuntimeSpawnedInstance()
    {
        IsRuntimeSpawnedInstance = true;
    }

    /// <summary>
    /// Gets whether this component should be treated as an original scene-placed object by pickup and save systems.
    /// </summary>
    public bool ShouldPreserveAsScenePlacedItem()
    {
        return PreserveWhenCollected &&
               !IsRuntimeSpawnedInstance &&
               HasValidSceneSaveId();
    }

    /// <summary>
    /// Gets the world item represented by this scene persistence wrapper.
    /// </summary>
    public WorldItem GetWorldItem()
    {
        ResolveReferences();
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
    /// Runtime-spawned clones are destroyed when asked to disappear, because they are not scene anchors.
    /// </summary>
    /// <param name="IsPresent">True to show the item, false to hide it.</param>
    public void SetPresent(bool IsPresent)
    {
        if (!IsPresent && !ShouldPreserveAsScenePlacedItem())
        {
            Destroy(gameObject);
            return;
        }

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
        ResolveReferences();

        if (WorldItem == null || ItemInstance == null)
        {
            return;
        }

        IsRuntimeSpawnedInstance = false;
        transform.SetPositionAndRotation(Position, Rotation);
        WorldItem.ApplyItemInstance(ItemInstance.Clone());
        ResetPhysicsState();
        gameObject.SetActive(true);
    }

    /// <summary>
    /// Resolves cached references that can be missing after prefab changes or load operations.
    /// </summary>
    private void ResolveReferences()
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
    /// Gets whether this object has a valid scene save identifier on the same root.
    /// </summary>
    private bool HasValidSceneSaveId()
    {
        SceneSaveId SaveId = GetComponent<SceneSaveId>();
        return SaveId != null && !string.IsNullOrWhiteSpace(SaveId.GetId());
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
