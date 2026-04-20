using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Equipped scanner tool that scans ore veins or dropped ore pickups while the primary input is held.
/// Secondary input controls a smooth visual zoom pose of the scanner independently from scanning.
/// Losing sight of the current scan target immediately cancels the active scan attempt.
/// Previously scanned targets are cached and can be displayed instantly while their runtime identity remains valid.
/// </summary>
public sealed class ScannerItemBehaviour : EquippedItemBehaviour
{
    private enum ScannerTargetType
    {
        None = 0,
        Vein = 1,
        DroppedOre = 2
    }

    /// <summary>
    /// Immutable cache key representing one specific scanable runtime identity.
    /// It is intentionally based on object instance ids and runtime payload identity so pooled ores
    /// stop matching automatically when reused with a different OreItemData instance.
    /// </summary>
    private readonly struct ScanCacheKey
    {
        /// <summary>
        /// Target category used by this cache key.
        /// </summary>
        public readonly ScannerTargetType TargetType;

        /// <summary>
        /// Instance id of the scanned target component.
        /// </summary>
        public readonly int TargetInstanceId;

        /// <summary>
        /// Secondary identity used to invalidate cached entries when runtime data changes.
        /// </summary>
        public readonly int DataIdentity;

        public ScanCacheKey(ScannerTargetType TargetTypeValue, int TargetInstanceIdValue, int DataIdentityValue)
        {
            TargetType = TargetTypeValue;
            TargetInstanceId = TargetInstanceIdValue;
            DataIdentity = DataIdentityValue;
        }
    }

    [Header("References")]
    [Tooltip("Camera used to raycast scanner targets. When empty or invalid, the scanner resolves the MainCamera automatically.")]
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

    [Header("Animator")]
    [Tooltip("Animator used to drive scanner visual states.")]
    [SerializeField] private Animator ItemAnimator;

    [Tooltip("Animator bool parameter driven while primary input is held.")]
    [SerializeField] private string PrimaryUseParameterName = "PrimaryUse";

    [Tooltip("Animator bool parameter driven while secondary input is held.")]
    [SerializeField] private string SecondaryUseParameterName = "SecondaryUse";

    [Tooltip("Animator bool parameter driven while either primary or secondary input is active.")]
    [SerializeField] private string IsUsingParameterName = "IsUsing";

    [Tooltip("Animator float parameter used to blend between idle and zoom scanner poses.")]
    [SerializeField] private string ZoomAlphaParameterName = "ZoomAlpha";

    [Header("Zoom")]
    [Tooltip("Speed used to blend the scanner zoom pose in and out.")]
    [SerializeField] private float ZoomBlendSpeed = 6f;

    [Header("Debug")]
    [Tooltip("Draws the scanner ray while scanning.")]
    [SerializeField] private bool DrawDebugRay = false;

    [Tooltip("Logs scanner state changes.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Whether the player is currently requesting scanner zoom with secondary input.
    /// </summary>
    private bool WantsZoom;

    /// <summary>
    /// Current normalized zoom blend value applied to the animator.
    /// </summary>
    private float CurrentZoomAlpha;

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
    /// Cache of already scanned runtime identities.
    /// Value is unused because only membership matters.
    /// </summary>
    private readonly HashSet<ScanCacheKey> CachedScans = new();

    /// <summary>
    /// Initializes references when the scanner is equipped.
    /// </summary>
    public override void Initialize(HotbarController OwnerHotbar, ItemInstance ItemInstance)
    {
        base.Initialize(OwnerHotbar, ItemInstance);

        ResolvePlayerCamera(true);

        if (UpgradeManager == null)
        {
            UpgradeManager = FindFirstObjectByType<UpgradeManager>();
        }

        if (ItemAnimator == null)
        {
            ItemAnimator = GetComponentInChildren<Animator>(true);
        }

        if (ScannerDisplayUI != null)
        {
            ScannerDisplayUI.SetVisible(true);
            ScannerDisplayUI.ShowIdle();
        }

        WantsZoom = false;
        CurrentZoomAlpha = 0f;
        ApplyAnimatorState();
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

        WantsZoom = false;
        CurrentZoomAlpha = 0f;
        ApplyAnimatorState();
    }

    /// <summary>
    /// Ensures the scanner is reset when usage is forcefully interrupted.
    /// </summary>
    public override void ForceStopItemUsage()
    {
        base.ForceStopItemUsage();

        ResetCurrentScan();

        WantsZoom = false;
        CurrentZoomAlpha = 0f;
        ApplyAnimatorState();
    }

    /// <summary>
    /// Cancels scanning when primary use ends.
    /// </summary>
    public override void OnPrimaryUseEnded()
    {
        base.OnPrimaryUseEnded();
        ResetCurrentScan();
        ApplyAnimatorState();
    }

    /// <summary>
    /// Starts requesting scanner zoom while secondary input is held.
    /// </summary>
    public override void OnSecondaryUseStarted()
    {
        base.OnSecondaryUseStarted();
        WantsZoom = true;
        ApplyAnimatorState();
    }

    /// <summary>
    /// Stops requesting scanner zoom when secondary input is released.
    /// </summary>
    public override void OnSecondaryUseEnded()
    {
        base.OnSecondaryUseEnded();
        WantsZoom = false;
        ApplyAnimatorState();
    }

    /// <summary>
    /// Updates scanner zoom continuously and scan logic only while primary input is held.
    /// Zoom is completely independent from scan target validity.
    /// </summary>
    private void Update()
    {
        ResolvePlayerCamera(false);
        UpdateZoom();

        if (IsPrimaryUseActive)
        {
            UpdateScanning();
        }

        ApplyAnimatorState();
    }

    /// <summary>
    /// Smoothly blends the scanner zoom pose in or out based on secondary input.
    /// </summary>
    private void UpdateZoom()
    {
        float TargetZoomAlpha = WantsZoom ? 1f : 0f;

        CurrentZoomAlpha = Mathf.MoveTowards(
            CurrentZoomAlpha,
            TargetZoomAlpha,
            ZoomBlendSpeed * Time.deltaTime);
    }

    /// <summary>
    /// Pushes current scanner runtime state into the animator.
    /// </summary>
    private void ApplyAnimatorState()
    {
        if (ItemAnimator == null)
        {
            return;
        }

        SetAnimatorBool(PrimaryUseParameterName, IsPrimaryUseActive);
        SetAnimatorBool(SecondaryUseParameterName, WantsZoom);
        SetAnimatorBool(IsUsingParameterName, IsPrimaryUseActive || WantsZoom);
        SetAnimatorFloat(ZoomAlphaParameterName, CurrentZoomAlpha);
    }

    /// <summary>
    /// Resolves the currently looked target and advances scan progress.
    /// If the looked target was scanned previously and its runtime identity still matches,
    /// the scanner shows its data instantly instead of scanning again.
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

        ScanCacheKey CurrentCacheKey = BuildCacheKey(ResolvedTarget);

        if (IsTargetCached(CurrentCacheKey))
        {
            if (!HasCompletedCurrentScan)
            {
                HasCompletedCurrentScan = true;
                ShowResolvedTargetResult(ResolvedTarget);
                Log("Displayed cached scan result instantly for target: " + ResolvedTarget.GetDisplayTargetLabel());
            }

            return;
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
            CompleteCurrentScan(ResolvedTarget, CurrentCacheKey);
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
    /// Completes the current scan, caches the target identity and shows the final data.
    /// </summary>
    private void CompleteCurrentScan(ResolvedScannerTarget ResolvedTarget, ScanCacheKey CurrentCacheKey)
    {
        HasCompletedCurrentScan = true;
        CachedScans.Add(CurrentCacheKey);
        ShowResolvedTargetResult(ResolvedTarget);
    }

    /// <summary>
    /// Routes a resolved target to the correct result presenter.
    /// </summary>
    private void ShowResolvedTargetResult(ResolvedScannerTarget ResolvedTarget)
    {
        switch (ResolvedTarget.TargetType)
        {
            case ScannerTargetType.Vein:
                ShowVeinScanResult(ResolvedTarget.OreVein);
                break;

            case ScannerTargetType.DroppedOre:
                ShowDroppedOreScanResult(ResolvedTarget.OrePickup);
                break;

            default:
                ResetCurrentScan();
                break;
        }
    }

    /// <summary>
    /// Builds and displays the scan result for an ore vein.
    /// </summary>
    private void ShowVeinScanResult(OreVein OreVein)
    {
        if (OreVein == null)
        {
            ResetCurrentScan();
            return;
        }

        OreDefinition OreDefinition = OreVein.GetOreDefinition();

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
    private void ShowDroppedOreScanResult(OrePickup OrePickup)
    {
        if (OrePickup == null)
        {
            ResetCurrentScan();
            return;
        }

        OreItemData OreItemData = OrePickup.GetOreItemData();

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
    /// This does not clear the persistent scan cache.
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
    /// Builds a cache key for the currently resolved target.
    /// Veins use their current ore definition and range values.
    /// Dropped ores use both the pickup instance and the exact OreItemData reference.
    /// </summary>
    private ScanCacheKey BuildCacheKey(ResolvedScannerTarget ResolvedTarget)
    {
        switch (ResolvedTarget.TargetType)
        {
            case ScannerTargetType.Vein:
                {
                    if (ResolvedTarget.OreVein == null)
                    {
                        return default;
                    }

                    OreDefinition OreDefinition = ResolvedTarget.OreVein.GetOreDefinition();
                    int DefinitionIdentity = OreDefinition != null ? OreDefinition.GetInstanceID() : 0;
                    int DropMin = OreDefinition != null ? OreDefinition.GetBaseDropCountMin() : 0;
                    int DropMax = OreDefinition != null ? OreDefinition.GetBaseDropCountMax() : 0;
                    int DataIdentity = (DefinitionIdentity * 397) ^ (DropMin * 17) ^ DropMax;

                    return new ScanCacheKey(
                        ScannerTargetType.Vein,
                        ResolvedTarget.OreVein.GetInstanceID(),
                        DataIdentity);
                }

            case ScannerTargetType.DroppedOre:
                {
                    if (ResolvedTarget.OrePickup == null)
                    {
                        return default;
                    }

                    OreItemData OreItemData = ResolvedTarget.OrePickup.GetOreItemData();
                    int DataIdentity = OreItemData != null ? OreItemData.GetHashCode() : 0;

                    return new ScanCacheKey(
                        ScannerTargetType.DroppedOre,
                        ResolvedTarget.OrePickup.GetInstanceID(),
                        DataIdentity);
                }

            default:
                return default;
        }
    }

    /// <summary>
    /// Returns whether the provided target identity is already cached as scanned.
    /// </summary>
    private bool IsTargetCached(ScanCacheKey CacheKey)
    {
        if (CacheKey.TargetType == ScannerTargetType.None)
        {
            return false;
        }

        return CachedScans.Contains(CacheKey);
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
    /// Resolves the camera that should be used by the scanner.
    /// MainCamera always has priority over any tools camera.
    /// </summary>
    private void ResolvePlayerCamera(bool ForceRefresh)
    {
        if (!ForceRefresh && PlayerCamera != null && PlayerCamera.CompareTag("MainCamera"))
        {
            return;
        }

        Camera MainCamera = Camera.main;

        if (MainCamera != null)
        {
            PlayerCamera = MainCamera;
            return;
        }

        Camera[] AllCameras = FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        for (int Index = 0; Index < AllCameras.Length; Index++)
        {
            if (AllCameras[Index] != null && AllCameras[Index].CompareTag("MainCamera"))
            {
                PlayerCamera = AllCameras[Index];
                return;
            }
        }

        if (OwnerHotbar != null)
        {
            Camera FallbackCamera = OwnerHotbar.GetComponentInChildren<Camera>(true);

            if (FallbackCamera != null)
            {
                PlayerCamera = FallbackCamera;
            }
        }
    }

    /// <summary>
    /// Writes an animator bool parameter when the parameter name is valid.
    /// </summary>
    private void SetAnimatorBool(string ParameterName, bool Value)
    {
        if (ItemAnimator == null || string.IsNullOrWhiteSpace(ParameterName))
        {
            return;
        }

        ItemAnimator.SetBool(ParameterName, Value);
    }

    /// <summary>
    /// Writes an animator float parameter when the parameter name is valid.
    /// </summary>
    private void SetAnimatorFloat(string ParameterName, float Value)
    {
        if (ItemAnimator == null || string.IsNullOrWhiteSpace(ParameterName))
        {
            return;
        }

        ItemAnimator.SetFloat(ParameterName, Value);
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