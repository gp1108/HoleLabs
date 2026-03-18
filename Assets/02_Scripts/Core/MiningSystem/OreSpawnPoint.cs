using UnityEngine;

/// <summary>
/// Marks a world position where one ore vein can exist.
/// The spawner controls whether this point is active and what ore definition it currently hosts.
/// </summary>
public sealed class OreSpawnPoint : MonoBehaviour
{
    [Header("State")]
    [Tooltip("If true, this spawn point currently hosts an active ore vein.")]
    [SerializeField] private bool IsActive;

    [Tooltip("Ore vein currently spawned at this point.")]
    [SerializeField] private OreVein CurrentVein;

    public bool GetIsActive()
    {
        return IsActive;
    }

    public void ClearPoint()
    {
        if (CurrentVein != null)
        {
            Destroy(CurrentVein.gameObject);
        }

        CurrentVein = null;
        IsActive = false;
    }

    public bool SpawnVein(OreDefinition oreDefinition, OreRuntimeService oreRuntimeService)
    {
        if (oreDefinition == null || oreDefinition.GetVeinPrefab() == null)
        {
            return false;
        }

        ClearPoint();

        GameObject spawnedVeinObject = Instantiate(
            oreDefinition.GetVeinPrefab(),
            transform.position,
            transform.rotation,
            transform
        );

        CurrentVein = spawnedVeinObject.GetComponent<OreVein>();

        if (CurrentVein == null)
        {
            CurrentVein = spawnedVeinObject.GetComponentInChildren<OreVein>();
        }

        if (CurrentVein == null)
        {
            Destroy(spawnedVeinObject);
            return false;
        }

        CurrentVein.Initialize(oreDefinition, oreRuntimeService, this);
        IsActive = true;
        return true;
    }

    public void NotifyVeinReleased(OreVein oreVein)
    {
        if (CurrentVein == oreVein)
        {
            CurrentVein = null;
        }
    }
}
