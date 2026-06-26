using System;
using UnityEngine;

/// <summary>
/// Stores runtime ore data on a dropped physical ore object.
/// This component is separate from the player's generic item system so ore-specific
/// properties can remain flexible without polluting every item type.
/// It also applies the runtime ore size scale safely every time the pickup is reused.
/// </summary>
public sealed class OrePickup : MonoBehaviour, IWeightProvider
{
    [Header("Runtime Data")]
    [Tooltip("Runtime ore data carried by this dropped pickup.")]
    [SerializeField] private OreItemData OreItemData;

    [Header("Structure")]
    [Tooltip("Root transform moved, activated and deactivated by the pool. If empty, this transform is used.")]
    [SerializeField] private Transform RuntimeRoot;

    [Header("Runtime Size Scale")]
    [Tooltip("If true, the pickup applies OreItemData size scale to the configured scale root whenever runtime ore data is assigned.")]
    [SerializeField] private bool ApplyOreSizeScale = true;

    [Tooltip("Transform scaled by the runtime ore size. If empty, Runtime Root is used. This should usually contain both the visual mesh and physical colliders.")]
    [SerializeField] private Transform ScaleRoot;

    [Tooltip("Minimum runtime scale multiplier allowed when applying ore size. This prevents invalid or near-zero physics shapes.")]
    [SerializeField] private float MinimumAppliedSizeScale = 0.05f;

    [Tooltip("Maximum runtime scale multiplier allowed when applying ore size. This prevents accidental extreme physics shapes during balancing.")]
    [SerializeField] private float MaximumAppliedSizeScale = 10f;

    [Tooltip("If true, scale operations are logged for debugging pool reuse and save/load restoration.")]
    [SerializeField] private bool DebugScaleLogs = false;

    [Header("Cached Components")]
    [Tooltip("Optional rigidbody reset when the pickup is reused by the pool.")]
    [SerializeField] private Rigidbody CachedRigidbody;

    [Tooltip("Optional collider array enabled again when the pickup is reused by the pool.")]
    [SerializeField] private Collider[] CachedColliders;

    [Header("Scanner Runtime")]
    [Tooltip("Stable runtime id used by ScannerRuntimeService to remember this exact physical ore pickup while it exists.")]
    [SerializeField] private string ScannerInstanceId;

    /// <summary>
    /// Pool that owns this pickup instance, if it was spawned by a pool.
    /// </summary>
    private OrePickupPool OwnerPool;

    /// <summary>
    /// Prefab originally used to create this pickup instance.
    /// </summary>
    private GameObject SourcePrefab;

    /// <summary>
    /// Base local scale captured from the configured scale root before any runtime ore size multiplier is applied.
    /// </summary>
    private Vector3 BaseScaleRootLocalScale = Vector3.one;

    /// <summary>
    /// Whether the base scale root local scale has already been captured.
    /// </summary>
    private bool HasCachedBaseScale;

    /// <summary>
    /// Gets the prefab originally used to create this pickup.
    /// This is used by the save system to recreate the same visual object.
    /// </summary>
    public GameObject GetSourcePrefab()
    {
        return SourcePrefab;
    }

    /// <summary>
    /// Gets the source prefab name used to recreate the same ore pickup visual during load.
    /// </summary>
    public string GetSourcePrefabName()
    {
        return SourcePrefab != null ? SourcePrefab.name : string.Empty;
    }

    /// <summary>
    /// Initializes this pickup with runtime ore data and applies the runtime size scale.
    /// </summary>
    /// <param name="OreItemDataValue">Runtime ore data assigned to this pickup.</param>
    public void Initialize(OreItemData OreItemDataValue)
    {
        NotifyScannerInstanceRemoved();
        ScannerInstanceId = Guid.NewGuid().ToString("N");
        OreItemData = OreItemDataValue;

        if (OreItemData != null && OreItemData.GetOreDefinition() != null)
        {
            GetRuntimeRoot().name = "OrePickup_" + OreItemData.GetOreDefinition().GetDisplayName();
        }

        ApplyCurrentOreSizeScale();
        RuntimeWorldObjectRegistry.RegisterOrePickup(this);
    }

    /// <summary>
    /// Binds pool ownership data used when the pickup is returned.
    /// </summary>
    /// <param name="OwnerPoolValue">Pool that owns this pickup instance.</param>
    /// <param name="SourcePrefabValue">Prefab used to create this pickup instance.</param>
    public void BindPool(OrePickupPool OwnerPoolValue, GameObject SourcePrefabValue)
    {
        OwnerPool = OwnerPoolValue;
        SourcePrefab = SourcePrefabValue;
        CacheBaseScaleIfNeeded();
    }

    /// <summary>
    /// Prepares the pickup to be reused at the provided world transform.
    /// Runtime scale is reset before ore data is assigned so pooled objects cannot inherit the previous ore size.
    /// </summary>
    /// <param name="Position">World position used for this reuse.</param>
    /// <param name="Rotation">World rotation used for this reuse.</param>
    public void PrepareForReuse(Vector3 Position, Quaternion Rotation)
    {
        Transform RuntimeRootValue = GetRuntimeRoot();
        RuntimeRootValue.SetParent(null, true);
        RuntimeRootValue.SetPositionAndRotation(Position, Rotation);

        EnsureCachedReferences();
        RestoreBaseScale();
        ResetPhysicsState();
        SetCollidersEnabled(true);
        RuntimeRootValue.gameObject.SetActive(true);
    }

    /// <summary>
    /// Prepares the pickup to be stored back inside the pool.
    /// Runtime ore size scale is cleared so the next reuse always starts from prefab scale.
    /// </summary>
    /// <param name="PoolRoot">Root transform used to store inactive pooled instances.</param>
    public void PrepareForPoolStorage(Transform PoolRoot)
    {
        Transform RuntimeRootValue = GetRuntimeRoot();

        EnsureCachedReferences();
        ResetPhysicsState();
        SetCollidersEnabled(false);
        RuntimeWorldObjectRegistry.UnregisterOrePickup(this);
        NotifyScannerInstanceRemoved();
        ScannerInstanceId = string.Empty;
        OreItemData = null;
        RuntimeRootValue.name = SourcePrefab != null ? SourcePrefab.name + "_Pooled" : "OrePickup_Pooled";
        RestoreBaseScale();

        SetContainedCarryablesDisableResetSuppressed(true);
        RuntimeRootValue.SetParent(PoolRoot, false);
        RuntimeRootValue.gameObject.SetActive(false);
        SetContainedCarryablesDisableResetSuppressed(false);
    }

    /// <summary>
    /// Attempts to return this pickup back to its owner pool.
    /// </summary>
    /// <returns>True when the pickup was returned to its pool.</returns>
    public bool ReturnToPool()
    {
        if (OwnerPool == null || SourcePrefab == null)
        {
            return false;
        }

        OwnerPool.ReturnPickup(this, SourcePrefab);
        return true;
    }

    /// <summary>
    /// Gets the runtime ore payload currently stored by this pickup.
    /// </summary>
    public OreItemData GetOreItemData()
    {
        return OreItemData;
    }

    /// <summary>
    /// Gets the stable scanner instance id assigned to this physical pickup while it exists.
    /// </summary>
    /// <returns>Stable scanner instance id for this runtime pickup.</returns>
    public string GetScannerInstanceId()
    {
        if (string.IsNullOrWhiteSpace(ScannerInstanceId))
        {
            ScannerInstanceId = Guid.NewGuid().ToString("N");
        }

        return ScannerInstanceId;
    }

    /// <summary>
    /// Applies a scanner instance id restored from save.
    /// This should only be called by save/load code after Initialize has restored the ore payload.
    /// </summary>
    /// <param name="ScannerInstanceIdValue">Saved scanner instance id.</param>
    public void SetScannerInstanceId(string ScannerInstanceIdValue)
    {
        if (string.IsNullOrWhiteSpace(ScannerInstanceIdValue))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(ScannerInstanceId) && !string.Equals(ScannerInstanceId, ScannerInstanceIdValue, StringComparison.Ordinal))
        {
            NotifyScannerInstanceRemoved();
        }

        ScannerInstanceId = ScannerInstanceIdValue;
    }

    /// <summary>
    /// Gets the gameplay weight currently contributed by this ore pickup.
    /// </summary>
    /// <returns>Runtime ore weight, or zero when no ore data is assigned.</returns>
    public float GetWeight()
    {
        return OreItemData != null ? Mathf.Max(0f, OreItemData.GetWeightValue()) : 0f;
    }

    /// <summary>
    /// Gets the root transform controlled by the pool.
    /// </summary>
    public Transform GetRuntimeRoot()
    {
        if (RuntimeRoot == null)
        {
            RuntimeRoot = transform;
        }

        return RuntimeRoot;
    }

    /// <summary>
    /// Gets the transform that receives the runtime ore size scale.
    /// </summary>
    /// <returns>Configured scale root or runtime root fallback.</returns>
    private Transform GetScaleRoot()
    {
        if (ScaleRoot == null)
        {
            ScaleRoot = GetRuntimeRoot();
        }

        return ScaleRoot;
    }

    /// <summary>
    /// Captures the prefab-authored local scale before runtime size multipliers modify it.
    /// </summary>
    private void CacheBaseScaleIfNeeded()
    {
        if (HasCachedBaseScale)
        {
            return;
        }

        Transform ScaleRootValue = GetScaleRoot();
        BaseScaleRootLocalScale = ScaleRootValue != null ? ScaleRootValue.localScale : Vector3.one;
        HasCachedBaseScale = true;
    }

    /// <summary>
    /// Restores the configured scale root to its prefab-authored local scale.
    /// </summary>
    private void RestoreBaseScale()
    {
        CacheBaseScaleIfNeeded();

        Transform ScaleRootValue = GetScaleRoot();

        if (ScaleRootValue == null)
        {
            return;
        }

        ScaleRootValue.localScale = BaseScaleRootLocalScale;

        if (DebugScaleLogs)
        {
            Debug.Log("[OrePickup] Restored base scale " + BaseScaleRootLocalScale + " on " + ScaleRootValue.name + ".", this);
        }
    }

    /// <summary>
    /// Applies the current ore runtime size scale to the configured scale root.
    /// </summary>
    private void ApplyCurrentOreSizeScale()
    {
        RestoreBaseScale();

        if (!ApplyOreSizeScale || OreItemData == null)
        {
            return;
        }

        Transform ScaleRootValue = GetScaleRoot();

        if (ScaleRootValue == null)
        {
            return;
        }

        float RuntimeSizeScale = Mathf.Clamp(
            OreItemData.GetSizeScale(),
            Mathf.Max(0.01f, MinimumAppliedSizeScale),
            Mathf.Max(Mathf.Max(0.01f, MinimumAppliedSizeScale), MaximumAppliedSizeScale));

        ScaleRootValue.localScale = new Vector3(
            BaseScaleRootLocalScale.x * RuntimeSizeScale,
            BaseScaleRootLocalScale.y * RuntimeSizeScale,
            BaseScaleRootLocalScale.z * RuntimeSizeScale);

        if (DebugScaleLogs)
        {
            Debug.Log("[OrePickup] Applied runtime ore size scale x" + RuntimeSizeScale.ToString("0.###") + " to " + ScaleRootValue.name + ".", this);
        }
    }

    /// <summary>
    /// Resets rigidbody motion before reusing or storing the pickup.
    /// Kinematic rigidbodies cannot accept velocity writes, so only dynamic bodies are zeroed explicitly.
    /// </summary>
    private void ResetPhysicsState()
    {
        if (CachedRigidbody == null)
        {
            return;
        }

        if (!CachedRigidbody.isKinematic)
        {
            CachedRigidbody.linearVelocity = Vector3.zero;
            CachedRigidbody.angularVelocity = Vector3.zero;
        }

        CachedRigidbody.Sleep();
    }

    /// <summary>
    /// Notifies the scanner runtime service that this physical pickup instance no longer exists as a valid scanned target.
    /// </summary>
    private void NotifyScannerInstanceRemoved()
    {
        if (string.IsNullOrWhiteSpace(ScannerInstanceId))
        {
            return;
        }

        ScannerRuntimeService RuntimeService = ScannerRuntimeService.Instance;

        if (RuntimeService == null)
        {
            RuntimeService = FindFirstObjectByType<ScannerRuntimeService>();
        }

        if (RuntimeService != null)
        {
            RuntimeService.ForgetOrePickupInstanceId(ScannerInstanceId);
        }
    }

    /// <summary>
    /// Ensures scanner instance cache is cleaned when this pickup is destroyed outside its pool flow.
    /// </summary>
    private void OnDestroy()
    {
        RuntimeWorldObjectRegistry.UnregisterOrePickup(this);
        NotifyScannerInstanceRemoved();
    }

    /// <summary>
    /// Suppresses or restores disable reset on every physics carryable in this pickup hierarchy.
    /// </summary>
    /// <param name="IsSuppressed">True to suppress disable reset, false to restore it.</param>
    private void SetContainedCarryablesDisableResetSuppressed(bool IsSuppressed)
    {
        PhysicsCarryable[] Carryables = GetComponentsInChildren<PhysicsCarryable>(true);

        for (int Index = 0; Index < Carryables.Length; Index++)
        {
            if (Carryables[Index] == null)
            {
                continue;
            }

            Carryables[Index].SetDisableResetSuppressed(IsSuppressed);
        }
    }

    /// <summary>
    /// Enables or disables every cached collider during pool transitions.
    /// </summary>
    /// <param name="IsEnabled">True to enable colliders, false to disable them.</param>
    private void SetCollidersEnabled(bool IsEnabled)
    {
        if (CachedColliders == null)
        {
            return;
        }

        for (int Index = 0; Index < CachedColliders.Length; Index++)
        {
            if (CachedColliders[Index] == null)
            {
                continue;
            }

            CachedColliders[Index].enabled = IsEnabled;
        }
    }

    /// <summary>
    /// Caches missing rigidbody, collider and scale references the first time they are needed.
    /// </summary>
    private void EnsureCachedReferences()
    {
        if (CachedRigidbody == null)
        {
            CachedRigidbody = GetComponent<Rigidbody>();

            if (CachedRigidbody == null)
            {
                CachedRigidbody = GetComponentInChildren<Rigidbody>(true);
            }
        }

        if (CachedColliders == null || CachedColliders.Length == 0)
        {
            CachedColliders = GetComponentsInChildren<Collider>(true);
        }

        CacheBaseScaleIfNeeded();
    }

    /// <summary>
    /// Keeps scale clamp values valid in the inspector.
    /// </summary>
    private void OnValidate()
    {
        MinimumAppliedSizeScale = Mathf.Max(0.01f, MinimumAppliedSizeScale);
        MaximumAppliedSizeScale = Mathf.Max(MinimumAppliedSizeScale, MaximumAppliedSizeScale);
    }

    /// <summary>
    /// Debug helper that reapplies the current runtime size scale while in play mode.
    /// </summary>
    [ContextMenu("Apply Current Ore Size Scale")]
    private void DebugApplyCurrentOreSizeScale()
    {
        ApplyCurrentOreSizeScale();
    }

    /// <summary>
    /// Debug helper that restores the pickup scale root to its cached prefab-authored base scale.
    /// </summary>
    [ContextMenu("Restore Base Ore Pickup Scale")]
    private void DebugRestoreBaseScale()
    {
        RestoreBaseScale();
    }
}
