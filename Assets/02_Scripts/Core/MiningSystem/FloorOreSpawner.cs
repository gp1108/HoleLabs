using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Controls a deterministic group of ore spawn points.
/// Every spawn point owns its ore definition, so level design is fixed and save/load can restore damaged veins reliably.
/// </summary>
public sealed class FloorOreSpawner : MonoBehaviour
{
    [Header("References")]
    [Tooltip("All fixed spawn points controlled by this floor spawner.")]
    [SerializeField] private List<OreSpawnPoint> SpawnPoints = new();

    [Tooltip("Runtime service used to initialize spawned veins.")]
    [SerializeField] private OreRuntimeService OreRuntimeService;

    [Header("Generation")]
    [Tooltip("If true, fixed configured veins are generated automatically on Start.")]
    [SerializeField] private bool GenerateOnStart = true;

    [Tooltip("If true, all existing veins are cleared before the configured layout is generated.")]
    [SerializeField] private bool ClearBeforeGeneration = true;

    [Header("Debug")]
    [Tooltip("Logs spawn generation operations.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Generates the configured deterministic ore layout when requested.
    /// Save/load restores after scene Start, so saved damaged states still override this default layout.
    /// </summary>
    private void Start()
    {
        if (GenerateOnStart)
        {
            GenerateConfiguredSpawns();
        }
    }

    /// <summary>
    /// Regenerates every configured ore spawn point using its own assigned ore definition.
    /// </summary>
    [ContextMenu("Generate Configured Spawns")]
    public void GenerateConfiguredSpawns()
    {
        if (ClearBeforeGeneration)
        {
            ClearAllSpawns();
        }

        int SpawnedCount = 0;

        for (int Index = 0; Index < SpawnPoints.Count; Index++)
        {
            OreSpawnPoint SpawnPoint = SpawnPoints[Index];

            if (SpawnPoint == null)
            {
                continue;
            }

            if (SpawnPoint.SpawnAssignedVein(OreRuntimeService))
            {
                SpawnedCount++;
            }
        }

        Log("Generated deterministic ore layout with " + SpawnedCount + " configured spawn points.");
    }

    /// <summary>
    /// Legacy entry point kept so old buttons or context menu calls still work.
    /// It now generates the fixed configured layout instead of a random weighted layout.
    /// </summary>
    [ContextMenu("Generate Active Spawns")]
    public void GenerateActiveSpawns()
    {
        GenerateConfiguredSpawns();
    }

    /// <summary>
    /// Clears every vein currently owned by this spawner.
    /// </summary>
    [ContextMenu("Clear All Spawns")]
    public void ClearAllSpawns()
    {
        for (int Index = 0; Index < SpawnPoints.Count; Index++)
        {
            if (SpawnPoints[Index] != null)
            {
                SpawnPoints[Index].ClearPoint();
            }
        }
    }

    /// <summary>
    /// Logs messages if debug logging is enabled.
    /// </summary>
    /// <param name="Message">Message to log.</param>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[FloorOreSpawner] " + Message, this);
    }
}
