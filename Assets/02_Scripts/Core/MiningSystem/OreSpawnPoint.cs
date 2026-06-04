using UnityEngine;

/// <summary>
/// Marks a fixed world position where one configured ore vein can exist.
/// Each spawn point owns its ore definition directly so level design stays deterministic.
/// </summary>
public sealed class OreSpawnPoint : MonoBehaviour
{
    [Header("Configuration")]
    [Tooltip("Ore definition assigned to this exact spawn point. Leave empty only for intentionally disabled points.")]
    [SerializeField] private OreDefinition AssignedOreDefinition;

    [Tooltip("If true, this point spawns its assigned ore when a floor spawner generates configured veins.")]
    [SerializeField] private bool SpawnOnGeneration = true;

    [Header("State")]
    [Tooltip("If true, this spawn point currently hosts an active ore vein.")]
    [SerializeField] private bool IsActive;

    [Tooltip("Ore vein currently spawned at this point.")]
    [SerializeField] private OreVein CurrentVein;

    /// <summary>
    /// Gets the ore definition assigned to this fixed spawn point.
    /// </summary>
    /// <returns>Configured ore definition, or null when this point is intentionally empty.</returns>
    public OreDefinition GetAssignedOreDefinition()
    {
        return AssignedOreDefinition;
    }

    /// <summary>
    /// Gets whether this point should spawn when its owner floor spawner generates configured veins.
    /// </summary>
    /// <returns>True when this point participates in generation.</returns>
    public bool GetSpawnOnGeneration()
    {
        return SpawnOnGeneration;
    }

    /// <summary>
    /// Gets the currently spawned vein hosted by this spawn point, if any.
    /// </summary>
    /// <returns>Current ore vein instance, or null when the point is empty.</returns>
    public OreVein GetCurrentVein()
    {
        return CurrentVein;
    }

    /// <summary>
    /// Gets whether this spawn point currently hosts an active vein.
    /// </summary>
    /// <returns>True when a vein is currently owned by this point.</returns>
    public bool GetIsActive()
    {
        return IsActive;
    }

    /// <summary>
    /// Clears the currently spawned vein from this point.
    /// </summary>
    public void ClearPoint()
    {
        if (CurrentVein != null)
        {
            Destroy(CurrentVein.gameObject);
        }

        CurrentVein = null;
        IsActive = false;
    }

    /// <summary>
    /// Spawns the assigned ore definition configured directly on this point.
    /// </summary>
    /// <param name="OreRuntimeService">Runtime service used to initialize the spawned vein.</param>
    /// <returns>True when a vein was spawned.</returns>
    public bool SpawnAssignedVein(OreRuntimeService OreRuntimeService)
    {
        if (!SpawnOnGeneration || AssignedOreDefinition == null)
        {
            ClearPoint();
            return false;
        }

        return SpawnVein(AssignedOreDefinition, OreRuntimeService);
    }

    /// <summary>
    /// Spawns the provided ore definition at this fixed point.
    /// Save/load uses this to restore the exact saved ore identity.
    /// </summary>
    /// <param name="OreDefinition">Ore definition to spawn.</param>
    /// <param name="OreRuntimeService">Runtime service used to initialize the spawned vein.</param>
    /// <returns>True when the vein was spawned correctly.</returns>
    public bool SpawnVein(OreDefinition OreDefinition, OreRuntimeService OreRuntimeService)
    {
        if (OreDefinition == null || OreDefinition.GetVeinPrefab() == null)
        {
            ClearPoint();
            return false;
        }

        ClearPoint();

        GameObject SpawnedVeinObject = Instantiate(
            OreDefinition.GetVeinPrefab(),
            transform.position,
            transform.rotation,
            transform);

        CurrentVein = SpawnedVeinObject.GetComponent<OreVein>();

        if (CurrentVein == null)
        {
            CurrentVein = SpawnedVeinObject.GetComponentInChildren<OreVein>();
        }

        if (CurrentVein == null)
        {
            Destroy(SpawnedVeinObject);
            CurrentVein = null;
            IsActive = false;
            return false;
        }

        CurrentVein.Initialize(OreDefinition, OreRuntimeService, this);
        IsActive = true;
        return true;
    }

    /// <summary>
    /// Notifies the spawn point that its current vein was released externally.
    /// </summary>
    /// <param name="OreVein">Released vein instance.</param>
    public void NotifyVeinReleased(OreVein OreVein)
    {
        if (CurrentVein == OreVein)
        {
            CurrentVein = null;
            IsActive = false;
        }
    }
}
