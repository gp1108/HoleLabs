using UnityEngine;

/// <summary>
/// Runtime mineable ore vein.
/// This component handles mining hits, drop spawning, depletion state and visual regrowth.
/// </summary>
public sealed class OreVein : MonoBehaviour, IMineable
{
    private enum VeinState
    {
        Growing = 0,
        Ready = 1
    }

    [Header("References")]
    [Tooltip("Optional visual root scaled during regrowth. If empty, this transform is used.")]
    [SerializeField] private Transform VisualRoot;

    [Header("Regrowth")]
    [Tooltip("Minimum scale used while the ore is regrowing.")]
    [SerializeField] private float MinimumGrowthScale = 0.05f;

    [Tooltip("If true, the ore regrowth is animated by scaling the visual root.")]
    [SerializeField] private bool AnimateGrowth = true;

    [Header("Drops")]
    [Tooltip("Radius used to scatter dropped ore pickups.")]
    [SerializeField] private float DropScatterRadius = 0.45f;

    [Tooltip("Vertical offset applied when dropping ore pickups.")]
    [SerializeField] private float DropVerticalOffset = 0.2f;

    [Header("Debug")]
    [Tooltip("Logs mining and regeneration operations.")]
    [SerializeField] private bool DebugLogs = false;

    private OreDefinition OreDefinition;
    private OreRuntimeService OreRuntimeService;
    private OreSpawnPoint OwnerSpawnPoint;

    private VeinState CurrentState = VeinState.Ready;
    private int CurrentHitsRemaining;
    private float CurrentRespawnTimer;

    /// <summary>
    /// Initializes this ore vein with its definition, runtime service and owner point.
    /// </summary>
    public void Initialize(OreDefinition oreDefinition, OreRuntimeService oreRuntimeService, OreSpawnPoint ownerSpawnPoint)
    {
        OreDefinition = oreDefinition;
        OreRuntimeService = oreRuntimeService;
        OwnerSpawnPoint = ownerSpawnPoint;

        if (VisualRoot == null)
        {
            VisualRoot = transform;
        }

        ResetReadyState();
    }

    /// <summary>
    /// Updates regrowth if the vein is currently regenerating.
    /// </summary>
    private void Update()
    {
        if (CurrentState != VeinState.Growing)
        {
            return;
        }

        if (OreRuntimeService == null || OreDefinition == null)
        {
            return;
        }

        CurrentRespawnTimer -= Time.deltaTime;

        float respawnDuration = Mathf.Max(0.01f, OreRuntimeService.ResolveRespawnTime(OreDefinition));
        float normalizedProgress = 1f - Mathf.Clamp01(CurrentRespawnTimer / respawnDuration);

        UpdateGrowthVisual(normalizedProgress);

        if (CurrentRespawnTimer <= 0f)
        {
            ResetReadyState();
        }
    }

    /// <summary>
    /// Attempts to apply one mining hit through the generic mineable interface.
    /// </summary>
    public bool TryMine(float miningPower)
    {
        return ApplyHit();
    }

    /// <summary>
    /// Applies one mining hit to this ore vein.
    /// Returns true if the vein was successfully hit.
    /// </summary>
    public bool ApplyHit()
    {
        if (CurrentState != VeinState.Ready || OreDefinition == null || OreRuntimeService == null)
        {
            return false;
        }

        CurrentHitsRemaining--;
        Log("Ore vein hit. Remaining hits: " + CurrentHitsRemaining);

        if (CurrentHitsRemaining <= 0)
        {
            BreakVein();
        }

        return true;
    }

    /// <summary>
    /// Gets whether this vein is currently mineable.
    /// </summary>
    public bool GetIsReady()
    {
        return CurrentState == VeinState.Ready;
    }

    /// <summary>
    /// Breaks the vein, spawns ore drops and starts regrowth.
    /// </summary>
    private void BreakVein()
    {
        int dropCount = OreRuntimeService.ResolveDropCount(OreDefinition);

        for (int index = 0; index < dropCount; index++)
        {
            OreItemData oreItemData = OreRuntimeService.CreateOreItemData(OreDefinition);

            if (oreItemData == null)
            {
                continue;
            }

            Vector2 randomCircle = Random.insideUnitCircle * DropScatterRadius;
            Vector3 dropPosition = transform.position +
                                   new Vector3(randomCircle.x, DropVerticalOffset, randomCircle.y);

            OreRuntimeService.SpawnOrePickup(oreItemData, dropPosition, Quaternion.identity);
        }

        StartRegrowth();
        Log("Ore vein broken and " + dropCount + " drops were spawned.");
    }

    /// <summary>
    /// Starts the regrowth process after the vein has been mined.
    /// </summary>
    private void StartRegrowth()
    {
        CurrentState = VeinState.Growing;
        CurrentRespawnTimer = OreRuntimeService != null && OreDefinition != null
            ? OreRuntimeService.ResolveRespawnTime(OreDefinition)
            : 0f;

        UpdateGrowthVisual(0f);
    }

    /// <summary>
    /// Resets the vein to a fully grown, mineable state.
    /// </summary>
    private void ResetReadyState()
    {
        CurrentState = VeinState.Ready;
        CurrentRespawnTimer = 0f;
        CurrentHitsRemaining = OreRuntimeService != null && OreDefinition != null
            ? OreRuntimeService.ResolveHitsRequired(OreDefinition)
            : 1;

        UpdateGrowthVisual(1f);
        Log("Ore vein is ready again.");
    }

    /// <summary>
    /// Updates the visual scale of the vein according to a normalized regrowth progress.
    /// </summary>
    private void UpdateGrowthVisual(float normalizedProgress)
    {
        if (!AnimateGrowth || VisualRoot == null)
        {
            return;
        }

        float clampedProgress = Mathf.Clamp01(normalizedProgress);
        float scaleMultiplier = Mathf.Lerp(MinimumGrowthScale, 1f, clampedProgress);
        VisualRoot.localScale = Vector3.one * scaleMultiplier;
    }

    /// <summary>
    /// Logs ore vein messages if debug logging is enabled.
    /// </summary>
    private void Log(string message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[OreVein] " + message);
    }

    /// <summary>
    /// Releases ownership from its spawn point when destroyed.
    /// </summary>
    private void OnDestroy()
    {
        if (OwnerSpawnPoint != null)
        {
            OwnerSpawnPoint.NotifyVeinReleased(this);
        }
    }
}
