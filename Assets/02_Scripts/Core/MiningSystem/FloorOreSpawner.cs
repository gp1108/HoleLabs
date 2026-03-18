using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Controls all ore spawn points for one floor or spawn group.
/// It activates only a configurable amount of points and can rebuild the layout at runtime,
/// which fits the "reset the day and choose different active points" requirement.
/// </summary>
public sealed class FloorOreSpawner : MonoBehaviour
{
    [Serializable]
    private sealed class WeightedOreEntry
    {
        [Tooltip("Ore definition that can spawn on this floor.")]
        [SerializeField] private OreDefinition OreDefinition;

        [Tooltip("Relative weight used when this ore is selected.")]
        [SerializeField] private int Weight = 1;

        public OreDefinition GetOreDefinition()
        {
            return OreDefinition;
        }

        public int GetWeight()
        {
            return Mathf.Max(0, Weight);
        }
    }

    [Header("References")]
    [Tooltip("All spawn points controlled by this floor spawner.")]
    [SerializeField] private List<OreSpawnPoint> SpawnPoints = new();

    [Tooltip("Runtime service used to initialize spawned veins.")]
    [SerializeField] private OreRuntimeService OreRuntimeService;

    [Header("Spawning")]
    [Tooltip("Amount of spawn points that should be active at the same time.")]
    [SerializeField] private int ActiveSpawnCount = 2;

    [Tooltip("Weighted ore definitions that can be chosen by this floor.")]
    [SerializeField] private List<WeightedOreEntry> AvailableOres = new();

    [Tooltip("If true, the layout is generated automatically on Start.")]
    [SerializeField] private bool GenerateOnStart = true;

    [Header("Debug")]
    [Tooltip("Logs spawn generation operations.")]
    [SerializeField] private bool DebugLogs = false;

    private void Start()
    {
        if (GenerateOnStart)
        {
            GenerateActiveSpawns();
        }
    }

    public void SetActiveSpawnCount(int activeSpawnCount)
    {
        ActiveSpawnCount = Mathf.Clamp(activeSpawnCount, 0, SpawnPoints.Count);
        GenerateActiveSpawns();
    }

    [ContextMenu("Generate Active Spawns")]
    public void GenerateActiveSpawns()
    {
        ClearAllSpawns();

        if (SpawnPoints.Count == 0 || ActiveSpawnCount <= 0)
        {
            return;
        }

        List<OreSpawnPoint> availablePoints = new List<OreSpawnPoint>(SpawnPoints);
        int remainingSpawnCount = Mathf.Clamp(ActiveSpawnCount, 0, availablePoints.Count);

        for (int index = 0; index < remainingSpawnCount; index++)
        {
            if (availablePoints.Count == 0)
            {
                break;
            }

            int randomPointIndex = UnityEngine.Random.Range(0, availablePoints.Count);
            OreSpawnPoint selectedPoint = availablePoints[randomPointIndex];
            availablePoints.RemoveAt(randomPointIndex);

            OreDefinition selectedOreDefinition = GetRandomOreDefinition();

            if (selectedPoint == null || selectedOreDefinition == null)
            {
                continue;
            }

            selectedPoint.SpawnVein(selectedOreDefinition, OreRuntimeService);
        }

        Log("Generated floor layout with " + remainingSpawnCount + " active spawn points.");
    }

    [ContextMenu("Clear All Spawns")]
    public void ClearAllSpawns()
    {
        for (int index = 0; index < SpawnPoints.Count; index++)
        {
            if (SpawnPoints[index] != null)
            {
                SpawnPoints[index].ClearPoint();
            }
        }
    }

    private OreDefinition GetRandomOreDefinition()
    {
        int totalWeight = 0;

        for (int index = 0; index < AvailableOres.Count; index++)
        {
            if (AvailableOres[index] == null || AvailableOres[index].GetOreDefinition() == null)
            {
                continue;
            }

            totalWeight += AvailableOres[index].GetWeight();
        }

        if (totalWeight <= 0)
        {
            return null;
        }

        int randomRoll = UnityEngine.Random.Range(0, totalWeight);
        int cumulativeWeight = 0;

        for (int index = 0; index < AvailableOres.Count; index++)
        {
            WeightedOreEntry entry = AvailableOres[index];

            if (entry == null || entry.GetOreDefinition() == null)
            {
                continue;
            }

            cumulativeWeight += entry.GetWeight();

            if (randomRoll < cumulativeWeight)
            {
                return entry.GetOreDefinition();
            }
        }

        return null;
    }

    private void Log(string message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[FloorOreSpawner] " + message);
    }
}
