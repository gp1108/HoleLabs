using UnityEngine;

/// <summary>
/// Companion component for a generic placeable installation spot that initializes and saves an installed elevator drill module.
/// The generic PlaceableInstallationSpot owns placement, ghost preview, obstruction checks, hotbar consumption and installed item persistence.
/// </summary>
[DisallowMultipleComponent]
public sealed class ElevatorDrillInstallationSpot : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Generic placement spot that owns the installed elevator drill prefab.")]
    [SerializeField] private PlaceableInstallationSpot InstallationSpot;

    [Tooltip("Upgrade manager passed to the installed elevator drill module.")]
    [SerializeField] private UpgradeManager UpgradeManager;

    [Header("Default State")]
    [Tooltip("If true, a newly installed elevator drill starts enabled before any save state is applied.")]
    [SerializeField] private bool DefaultEnabledAfterInstall = false;

    [Header("Debug")]
    [Tooltip("Logs initialization and save restoration operations.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Currently installed elevator drill module controlled by this spot.
    /// </summary>
    private ElevatorDrillModule CurrentModule;

    /// <summary>
    /// Saved enabled state waiting for the installed visual to exist.
    /// </summary>
    private bool PendingEnabledState;

    /// <summary>
    /// Whether a saved enabled state is waiting to be applied.
    /// </summary>
    private bool HasPendingEnabledState;

    /// <summary>
    /// Resolves references.
    /// </summary>
    private void Awake()
    {
        EnsureReferences();
    }

    /// <summary>
    /// Subscribes to placement events.
    /// </summary>
    private void OnEnable()
    {
        EnsureReferences();

        if (InstallationSpot != null)
        {
            InstallationSpot.InstallationChanged += HandleInstallationChanged;
            InstallationSpot.InstallationCleared += HandleInstallationCleared;
        }
    }

    /// <summary>
    /// Unsubscribes from placement events.
    /// </summary>
    private void OnDisable()
    {
        if (InstallationSpot != null)
        {
            InstallationSpot.InstallationChanged -= HandleInstallationChanged;
            InstallationSpot.InstallationCleared -= HandleInstallationCleared;
        }
    }

    /// <summary>
    /// Initializes an already installed module on scene start.
    /// </summary>
    private void Start()
    {
        RefreshInstalledModuleFromCurrentSpotState();
    }

    /// <summary>
    /// Gets whether the current installed module is enabled.
    /// </summary>
    public bool GetCurrentEnabledState()
    {
        if (CurrentModule != null)
        {
            return CurrentModule.GetIsEnabled();
        }

        return HasPendingEnabledState ? PendingEnabledState : DefaultEnabledAfterInstall;
    }

    /// <summary>
    /// Applies saved runtime state for this installed elevator drill module.
    /// The generic placeable installation state must be restored before this method for immediate initialization.
    /// </summary>
    /// <param name="IsEnabled">Saved enabled state.</param>
    public void ApplySavedState(bool IsEnabled)
    {
        PendingEnabledState = IsEnabled;
        HasPendingEnabledState = true;
        RefreshInstalledModuleFromCurrentSpotState();
    }

    /// <summary>
    /// Resolves optional scene references.
    /// </summary>
    private void EnsureReferences()
    {
        if (InstallationSpot == null)
        {
            InstallationSpot = GetComponent<PlaceableInstallationSpot>();
        }

        if (UpgradeManager == null)
        {
            UpgradeManager = FindFirstObjectByType<UpgradeManager>();
        }
    }

    /// <summary>
    /// Handles a newly installed or restored visual.
    /// </summary>
    private void HandleInstallationChanged(PlaceableInstallationSpot Spot, ItemInstance InstalledItem, GameObject InstalledVisual)
    {
        InitializeInstalledModule(InstalledVisual);
    }

    /// <summary>
    /// Handles the installation spot being cleared.
    /// </summary>
    private void HandleInstallationCleared(PlaceableInstallationSpot Spot)
    {
        CurrentModule = null;
    }

    /// <summary>
    /// Resolves the installed visual currently owned by the generic installation spot.
    /// </summary>
    private void RefreshInstalledModuleFromCurrentSpotState()
    {
        if (InstallationSpot == null)
        {
            return;
        }

        InitializeInstalledModule(InstallationSpot.GetCurrentInstalledVisual());
    }

    /// <summary>
    /// Initializes the elevator drill module found on an installed visual.
    /// </summary>
    /// <param name="InstalledVisual">Installed visual spawned by the generic placement spot.</param>
    private void InitializeInstalledModule(GameObject InstalledVisual)
    {
        CurrentModule = null;

        if (InstalledVisual == null)
        {
            return;
        }

        CurrentModule = InstalledVisual.GetComponent<ElevatorDrillModule>();

        if (CurrentModule == null)
        {
            CurrentModule = InstalledVisual.GetComponentInChildren<ElevatorDrillModule>(true);
        }

        if (CurrentModule == null)
        {
            Log("Installed visual has no ElevatorDrillModule: " + InstalledVisual.name);
            return;
        }

        bool InitialEnabledState = HasPendingEnabledState ? PendingEnabledState : DefaultEnabledAfterInstall;
        CurrentModule.Initialize(this, UpgradeManager, InitialEnabledState);
        HasPendingEnabledState = false;
        Log("Initialized installed elevator drill module. Enabled: " + InitialEnabledState);
    }

    /// <summary>
    /// Logs a debug message when enabled.
    /// </summary>
    /// <param name="Message">Message to log.</param>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[ElevatorDrillInstallationSpot] " + Message, this);
    }
}
