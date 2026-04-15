using UnityEngine;

/// <summary>
/// Equipped scanner tool that scans ore veins or dropped ore pickups while the primary input is held.
/// Losing sight of the target immediately cancels the scan.
/// </summary>
public sealed class ScannerItemBehaviour : EquippedItemBehaviour
{
    private enum ScannerTargetType
    {
        None = 0,
        Vein = 1,
        DroppedOre = 2
    }

    [Header("References")]
    [Tooltip("Camera used to raycast scanner targets. If empty, one will be resolved from the owner.")]
    [SerializeField] private Camera PlayerCamera;

    [Tooltip("World-space scanner UI displayed on the equipped scanner.")]
    [SerializeField] private ScannerDisplayUI ScannerDisplayUI;

    [Tooltip("Upgrade manager used to resolve scanner stats and unlocks.")]
    [SerializeField] private UpgradeManager UpgradeManager;

    [Header("Scanner")]
    [Tooltip("Base scan distance before scanner range upgrades are applied.")]
    [SerializeField] private float BaseScanDistance = 5f;

    [Tooltip("Base scan duration before scanner duration upgrades are applied.")]
    [SerializeField] private float BaseScanDuration = 1.5f;

    [Tooltip("Layers considered valid for scanner raycasts.")]
    [SerializeField] private LayerMask ScanLayers = ~0;

    [Header("Unlock Feature Flags")]
    [Tooltip("Feature flag required to show the vein drop amount range.")]
    [SerializeField] private string VeinDropRangeUnlockId = "Scanner.Unlock.VeinDropRange";

    [Tooltip("Feature flag required to show dropped ore gold value.")]
    [SerializeField] private string OreGoldUnlockId = "Scanner.Unlock.GoldValue";

    [Tooltip("Feature flag required to show dropped ore research value.")]
    [SerializeField] private string OreResearchUnlockId = "Scanner.Unlock.ResearchValue";

    [Tooltip("Feature flag required to show dropped ore purity.")]
    [SerializeField] private string OrePurityUnlockId = "Scanner.Unlock.Purity";

    [Tooltip("Feature flag required to show dropped ore size.")]
    [SerializeField] private string OreSizeUnlockId = "Scanner.Unlock.Size";

    [Tooltip("Feature flag required to show dropped ore weight.")]
    [SerializeField] private string OreWeightUnlockId = "Scanner.Unlock.Weight";

    [Header("Debug")]
    [Tooltip("Draws the scanner ray while scanning.")]
    [SerializeField] private bool DrawDebugRay = false;

    [Tooltip("Logs scanner state changes.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Current resolved scanner target type.
    /// </summary>
    private ScannerTargetType CurrentTargetType = ScannerTargetType.None;

    /// <summary>
    /// Current vein target being scanned.
    /// </summary>
    private OreVein CurrentOreVein;

    /// <summary>
    /// Current dropped ore target being scanned.
    /// </summary>
    private OrePickup CurrentOrePickup;

    /// <summary>
    /// Current accumulated scan time on the active target.
    /// </summary>
    private float CurrentScanTimer;

    /// <summary>
    /// Whether the current target has already finished scanning.
    /// </summary>
    private bool HasCompletedCurrentScan;

    /// <summary>
    /// Initializes references when the scanner is equipped.
    /// </summary>
    public override void Initialize(HotbarController OwnerHotbar, ItemInstance ItemInstance)
    {
        base.Initialize(OwnerHotbar, ItemInstance);

        if (PlayerCamera == null && this.OwnerHotbar != null)
        {
            PlayerCamera = this.OwnerHotbar.GetComponentInChildren<Camera>();
        }

        if (UpgradeManager == null)
        {
            UpgradeManager = FindFirstObjectByType<UpgradeManager>();
        }

        if (ScannerDisplayUI != null)
        {
            ScannerDisplayUI.SetVisible(true);
            ScannerDisplayUI.ShowIdle();
        }
    }

    /// <summary>
    /// Ensures the scanner is reset when unequipped.
    /// </summary>
    public override void OnUnequipped()
    {
        base.OnUnequipped();
        ResetCurrentScan();

        if (ScannerDisplayUI != null)
        {
            ScannerDisplayUI.SetVisible(false);
        }
    }

    /// <summary>
    /// Ensures the scanner is reset when usage is forcefully interrupted.
    /// </summary>
    public override void ForceStopItemUsage()
    {
        base.ForceStopItemUsage();
        ResetCurrentScan();
    }

    /// <summary>
    /// Cancels the scan when primary use ends.
    /// </summary>
    public override void OnPrimaryUseEnded()
    {
        base.OnPrimaryUseEnded();
        ResetCurrentScan();
    }

    /// <summary>
    /// Updates scanning while the primary input is held.
    /// </summary>
    private void Update()
    {
        if (!IsPrimaryUseActive)
        {
            return;
        }

        UpdateScanning();
    }

    /// <summary>
    /// Resolves the currently looked target and advances scan progress.
    /// If no valid target is found, the scan is cancelled immediately.
    /// </summary>
    private void UpdateScanning()
    {
        ResolvedScannerTarget ResolvedTarget = ResolveScannerTarget();

        if (!ResolvedTarget.IsValid())
        {
            ResetCurrentScan();
            return;
        }

        if (!IsSameTarget(ResolvedTarget))
        {
            BeginNewTargetScan(ResolvedTarget);
        }

        if (HasCompletedCurrentScan)
        {
            return;
        }

        CurrentScanTimer += Time.deltaTime;

        float ScanDuration = GetResolvedScanDuration();
        float NormalizedProgress = Mathf.Clamp01(CurrentScanTimer / ScanDuration);

        if (ScannerDisplayUI != null)
        {
            ScannerDisplayUI.ShowScanning(ResolvedTarget.GetDisplayTargetLabel(), NormalizedProgress);
        }

        if (CurrentScanTimer >= ScanDuration)
        {
            CompleteCurrentScan();
        }
    }

    /// <summary>
    /// Starts a fresh scan on a newly acquired target.
    /// </summary>
    private void BeginNewTargetScan(ResolvedScannerTarget ResolvedTarget)
    {
        CurrentTargetType = ResolvedTarget.TargetType;
        CurrentOreVein = ResolvedTarget.OreVein;
        CurrentOrePickup = ResolvedTarget.OrePickup;
        CurrentScanTimer = 0f;
        HasCompletedCurrentScan = false;

        Log("Started scanning target: " + ResolvedTarget.GetDisplayTargetLabel());
    }

    /// <summary>
    /// Completes the current scan and pushes the data to the correct UI panel.
    /// </summary>
    private void CompleteCurrentScan()
    {
        HasCompletedCurrentScan = true;

        switch (CurrentTargetType)
        {
            case ScannerTargetType.Vein:
                ShowVeinScanResult();
                break;

            case ScannerTargetType.DroppedOre:
                ShowDroppedOreScanResult();
                break;

            default:
                ResetCurrentScan();
                break;
        }
    }

    /// <summary>
    /// Builds and displays the scan result for an ore vein.
    /// </summary>
    private void ShowVeinScanResult()
    {
        if (CurrentOreVein == null)
        {
            ResetCurrentScan();
            return;
        }

        OreDefinition OreDefinition = CurrentOreVein.GetOreDefinition();

        if (OreDefinition == null)
        {
            ResetCurrentScan();
            return;
        }

        if (ScannerDisplayUI != null)
        {
            ScannerDisplayUI.ShowVeinResult(
                OreDefinition.GetDisplayName(),
                IsFeatureUnlocked(VeinDropRangeUnlockId),
                OreDefinition.GetBaseDropCountMin(),
                OreDefinition.GetBaseDropCountMax());
        }

        Log("Completed vein scan.");
    }

    /// <summary>
    /// Builds and displays the scan result for a dropped ore pickup.
    /// </summary>
    private void ShowDroppedOreScanResult()
    {
        if (CurrentOrePickup == null)
        {
            ResetCurrentScan();
            return;
        }

        OreItemData OreItemData = CurrentOrePickup.GetOreItemData();

        if (OreItemData == null || OreItemData.GetOreDefinition() == null)
        {
            ResetCurrentScan();
            return;
        }

        string MineralType = OreItemData.GetOreDefinition().GetDisplayName();
        float GoldValue = OreItemData.GetGoldValue();
        float ResearchValue = OreItemData.GetResearchValue();
        float Purity = OreItemData.GetPropertyValue(OrePropertyType.Purity, 0f);
        float Size = OreItemData.GetPropertyValue(OrePropertyType.Size, 0f);
        float Weight = OreItemData.GetWeightValue();

        if (ScannerDisplayUI != null)
        {
            ScannerDisplayUI.ShowOreResult(
                MineralType,
                IsFeatureUnlocked(OreGoldUnlockId),
                GoldValue,
                IsFeatureUnlocked(OreResearchUnlockId),
                ResearchValue,
                IsFeatureUnlocked(OrePurityUnlockId),
                Purity,
                IsFeatureUnlocked(OreSizeUnlockId),
                Size,
                IsFeatureUnlocked(OreWeightUnlockId),
                Weight);
        }

        Log("Completed dropped ore scan.");
    }

    /// <summary>
    /// Clears all current scan state and returns the UI to idle.
    /// </summary>
    private void ResetCurrentScan()
    {
        CurrentTargetType = ScannerTargetType.None;
        CurrentOreVein = null;
        CurrentOrePickup = null;
        CurrentScanTimer = 0f;
        HasCompletedCurrentScan = false;

        if (ScannerDisplayUI != null)
        {
            ScannerDisplayUI.ShowIdle();
        }
    }

    /// <summary>
    /// Resolves the current scanner target from the center-screen ray.
    /// Dropped ores have priority over veins because they provide richer runtime data.
    /// </summary>
    private ResolvedScannerTarget ResolveScannerTarget()
    {
        ResolvedScannerTarget Result = default;

        if (PlayerCamera == null)
        {
            return Result;
        }

        float ScanDistance = GetResolvedScanDistance();
        Ray ScanRay = PlayerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (DrawDebugRay)
        {
            Debug.DrawRay(ScanRay.origin, ScanRay.direction * ScanDistance, Color.green);
        }

        if (!Physics.Raycast(ScanRay, out RaycastHit HitInfo, ScanDistance, ScanLayers, QueryTriggerInteraction.Ignore))
        {
            return Result;
        }

        OrePickup OrePickup = ResolveOrePickup(HitInfo);

        if (OrePickup != null && OrePickup.GetOreItemData() != null)
        {
            Result.TargetType = ScannerTargetType.DroppedOre;
            Result.OrePickup = OrePickup;
            return Result;
        }

        OreVein OreVein = ResolveOreVein(HitInfo);

        if (OreVein != null && OreVein.GetOreDefinition() != null)
        {
            Result.TargetType = ScannerTargetType.Vein;
            Result.OreVein = OreVein;
            return Result;
        }

        return Result;
    }

    /// <summary>
    /// Resolves an ore pickup from a raycast hit.
    /// </summary>
    private OrePickup ResolveOrePickup(RaycastHit HitInfo)
    {
        if (HitInfo.collider == null)
        {
            return null;
        }

        OrePickup OrePickup = HitInfo.collider.GetComponent<OrePickup>() ?? HitInfo.collider.GetComponentInParent<OrePickup>();

        if (OrePickup != null)
        {
            return OrePickup;
        }

        if (HitInfo.rigidbody != null)
        {
            OrePickup = HitInfo.rigidbody.GetComponent<OrePickup>() ?? HitInfo.rigidbody.GetComponentInParent<OrePickup>();
        }

        return OrePickup;
    }

    /// <summary>
    /// Resolves an ore vein from a raycast hit.
    /// </summary>
    private OreVein ResolveOreVein(RaycastHit HitInfo)
    {
        if (HitInfo.collider == null)
        {
            return null;
        }

        OreVein OreVein = HitInfo.collider.GetComponent<OreVein>() ?? HitInfo.collider.GetComponentInParent<OreVein>();

        if (OreVein != null)
        {
            return OreVein;
        }

        if (HitInfo.rigidbody != null)
        {
            OreVein = HitInfo.rigidbody.GetComponent<OreVein>() ?? HitInfo.rigidbody.GetComponentInParent<OreVein>();
        }

        return OreVein;
    }

    /// <summary>
    /// Returns true when the resolved target matches the current active target.
    /// </summary>
    private bool IsSameTarget(ResolvedScannerTarget ResolvedTarget)
    {
        if (ResolvedTarget.TargetType != CurrentTargetType)
        {
            return false;
        }

        switch (ResolvedTarget.TargetType)
        {
            case ScannerTargetType.Vein:
                return ResolvedTarget.OreVein == CurrentOreVein;

            case ScannerTargetType.DroppedOre:
                return ResolvedTarget.OrePickup == CurrentOrePickup;

            default:
                return false;
        }
    }

    /// <summary>
    /// Returns the final scan distance after applying upgrades.
    /// </summary>
    private float GetResolvedScanDistance()
    {
        if (UpgradeManager == null)
        {
            return Mathf.Max(0.1f, BaseScanDistance);
        }

        return Mathf.Max(0.1f, UpgradeManager.GetModifiedFloatStat(UpgradeStatType.ScannerRange, BaseScanDistance));
    }

    /// <summary>
    /// Returns the final scan duration after applying upgrades.
    /// Lower values make the scan faster.
    /// </summary>
    private float GetResolvedScanDuration()
    {
        if (UpgradeManager == null)
        {
            return Mathf.Max(0.01f, BaseScanDuration);
        }

        return Mathf.Max(0.01f, UpgradeManager.GetModifiedFloatStat(UpgradeStatType.ScannerDuration, BaseScanDuration));
    }

    /// <summary>
    /// Returns whether the given feature flag is unlocked.
    /// Empty ids are treated as unlocked to keep setup flexible.
    /// </summary>
    private bool IsFeatureUnlocked(string FeatureId)
    {
        if (string.IsNullOrWhiteSpace(FeatureId))
        {
            return true;
        }

        return UpgradeManager != null && UpgradeManager.IsFeatureUnlocked(FeatureId);
    }

    /// <summary>
    /// Writes scanner-specific logs when enabled.
    /// </summary>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[ScannerItemBehaviour] " + Message, this);
    }

    /// <summary>
    /// Lightweight container describing the target currently resolved by the scanner ray.
    /// </summary>
    private struct ResolvedScannerTarget
    {
        [Tooltip("Resolved target type under the scanner ray.")]
        public ScannerTargetType TargetType;

        [Tooltip("Resolved ore vein target.")]
        public OreVein OreVein;

        [Tooltip("Resolved dropped ore pickup target.")]
        public OrePickup OrePickup;

        /// <summary>
        /// Returns whether this resolved target is valid.
        /// </summary>
        public bool IsValid()
        {
            switch (TargetType)
            {
                case ScannerTargetType.Vein:
                    return OreVein != null;

                case ScannerTargetType.DroppedOre:
                    return OrePickup != null;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Returns a user-facing label for the resolved target category.
        /// </summary>
        public string GetDisplayTargetLabel()
        {
            switch (TargetType)
            {
                case ScannerTargetType.Vein:
                    return "Vein";

                case ScannerTargetType.DroppedOre:
                    return "Dropped Ore";

                default:
                    return "Unknown";
            }
        }
    }
}