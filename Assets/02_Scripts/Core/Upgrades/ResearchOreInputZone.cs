using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Physical trigger zone used by a ResearchStation to assimilate ore pickups.
/// A researcher machine may use multiple input zones to match its model shape without enlarging the player interaction area.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class ResearchOreInputZone : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Station that receives ore colliders from this input zone. If empty, the nearest parent ResearchStation is used.")]
    [SerializeField] private ResearchStation OwnerStation;

    [Header("Filtering")]
    [Tooltip("Layer mask accepted by this ore input zone. The pickup root layer is also checked so child colliders on different layers are still handled correctly.")]
    [SerializeField] private LayerMask OreInputLayers = ~0;

    [Tooltip("If true, the zone accepts a collider when either the hit collider layer, the OrePickup layer, or the OrePickup runtime root layer matches Ore Input Layers.")]
    [SerializeField] private bool AcceptOreRootLayer = true;

    [Header("Robust Detection")]
    [Tooltip("If true, this zone periodically reconciles its registered colliders with a physics overlap scan. This fixes missed trigger enter or exit events caused by activation order, sleeping bodies, nested colliders or objects already inside the trigger.")]
    [SerializeField] private bool UsePeriodicOverlapScan = true;

    [Tooltip("Seconds between overlap reconciliation scans. Lower values react faster but cost slightly more physics queries.")]
    [SerializeField] private float OverlapScanInterval = 0.15f;

    [Tooltip("Maximum number of colliders returned by one overlap scan. Increase this if many ores can be inside the same input zone.")]
    [SerializeField] private int MaxOverlapResults = 64;

    [Header("Debug")]
    [Tooltip("Logs ore input zone registration and scan events.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Cached trigger collider used by this input zone.
    /// </summary>
    private Collider ZoneCollider;

    /// <summary>
    /// Colliders registered through this zone so they can be safely removed on disable or when overlap scans detect that they left.
    /// </summary>
    private readonly HashSet<Collider> RegisteredColliders = new();

    /// <summary>
    /// Reusable collider set populated by periodic overlap scans.
    /// </summary>
    private readonly HashSet<Collider> ScannedColliders = new();

    /// <summary>
    /// Reusable list used to unregister stale colliders without mutating a collection during enumeration.
    /// </summary>
    private readonly List<Collider> StaleRegisteredColliders = new();

    /// <summary>
    /// Non-allocating physics query buffer used by overlap scans.
    /// </summary>
    private Collider[] OverlapResults = Array.Empty<Collider>();

    /// <summary>
    /// Countdown until the next overlap reconciliation scan.
    /// </summary>
    private float OverlapScanTimer;

    /// <summary>
    /// Ensures the zone collider behaves as a trigger.
    /// </summary>
    private void Reset()
    {
        ZoneCollider = GetComponent<Collider>();

        if (ZoneCollider != null)
        {
            ZoneCollider.isTrigger = true;
        }

        OwnerStation = GetComponentInParent<ResearchStation>();
    }

    /// <summary>
    /// Resolves the owner station and cached trigger collider.
    /// </summary>
    private void Awake()
    {
        ResolveReferences();
        EnsureOverlapBuffer();
    }

    /// <summary>
    /// Performs an initial scan so ores already inside the trigger are registered after scene load or prefab installation.
    /// </summary>
    private void OnEnable()
    {
        ResolveReferences();
        EnsureOverlapBuffer();
        OverlapScanTimer = 0f;

        if (UsePeriodicOverlapScan)
        {
            ReconcileOverlappingColliders();
        }
    }

    /// <summary>
    /// Periodically reconciles trigger state with explicit overlap queries for deterministic ore detection.
    /// </summary>
    private void FixedUpdate()
    {
        if (!UsePeriodicOverlapScan)
        {
            return;
        }

        OverlapScanTimer -= Time.fixedDeltaTime;

        if (OverlapScanTimer > 0f)
        {
            return;
        }

        OverlapScanTimer = Mathf.Max(0.02f, OverlapScanInterval);
        ReconcileOverlappingColliders();
    }

    /// <summary>
    /// Unregisters colliders still inside this zone when it is disabled or destroyed.
    /// </summary>
    private void OnDisable()
    {
        UnregisterAllColliders();
    }

    /// <summary>
    /// Registers ore colliders entering this input zone.
    /// </summary>
    private void OnTriggerEnter(Collider Other)
    {
        TryRegisterCollider(Other);
    }

    /// <summary>
    /// Registers ore colliders that are already staying inside this input zone.
    /// This covers activation order cases where OnTriggerEnter is not produced.
    /// </summary>
    private void OnTriggerStay(Collider Other)
    {
        TryRegisterCollider(Other);
    }

    /// <summary>
    /// Unregisters ore colliders leaving this input zone.
    /// </summary>
    private void OnTriggerExit(Collider Other)
    {
        TryUnregisterCollider(Other);
    }

    /// <summary>
    /// Attempts to register a collider as an ore candidate for the owner station.
    /// </summary>
    private void TryRegisterCollider(Collider Other)
    {
        if (!TryResolveAcceptedOreCollider(Other, out _))
        {
            return;
        }

        if (RegisteredColliders.Add(Other))
        {
            OwnerStation.RegisterOreInputCollider(Other);
            Log("Ore collider registered: " + Other.name);
        }
    }

    /// <summary>
    /// Attempts to unregister a collider from the owner station.
    /// </summary>
    private void TryUnregisterCollider(Collider Other)
    {
        if (OwnerStation == null || Other == null)
        {
            return;
        }

        if (RegisteredColliders.Remove(Other))
        {
            OwnerStation.UnregisterOreInputCollider(Other);
            Log("Ore collider unregistered: " + Other.name);
        }
    }

    /// <summary>
    /// Reconciles registered colliders against the current overlap volume.
    /// </summary>
    private void ReconcileOverlappingColliders()
    {
        if (OwnerStation == null || ZoneCollider == null || !ZoneCollider.enabled || !ZoneCollider.gameObject.activeInHierarchy)
        {
            return;
        }

        ScannedColliders.Clear();
        int HitCount = QueryOverlappingColliders();

        for (int Index = 0; Index < HitCount; Index++)
        {
            Collider HitCollider = OverlapResults[Index];

            if (!TryResolveAcceptedOreCollider(HitCollider, out _))
            {
                continue;
            }

            ScannedColliders.Add(HitCollider);
            TryRegisterCollider(HitCollider);
        }

        RemoveStaleRegisteredColliders();

        if (DebugLogs && HitCount >= OverlapResults.Length)
        {
            Log("Overlap scan reached its result limit. Increase Max Overlap Results if valid ores are missed.");
        }
    }

    /// <summary>
    /// Performs the most accurate non-allocating overlap query available for the configured trigger collider shape.
    /// </summary>
    private int QueryOverlappingColliders()
    {
        EnsureOverlapBuffer();

        if (ZoneCollider is BoxCollider BoxCollider)
        {
            return QueryBoxCollider(BoxCollider);
        }

        if (ZoneCollider is SphereCollider SphereCollider)
        {
            return QuerySphereCollider(SphereCollider);
        }

        if (ZoneCollider is CapsuleCollider CapsuleCollider)
        {
            return QueryCapsuleCollider(CapsuleCollider);
        }

        Bounds ColliderBounds = ZoneCollider.bounds;
        return Physics.OverlapBoxNonAlloc(
            ColliderBounds.center,
            ColliderBounds.extents,
            OverlapResults,
            Quaternion.identity,
            OreInputLayers,
            QueryTriggerInteraction.Collide);
    }

    /// <summary>
    /// Performs a non-allocating overlap query for a BoxCollider zone.
    /// </summary>
    private int QueryBoxCollider(BoxCollider BoxCollider)
    {
        Transform BoxTransform = BoxCollider.transform;
        Vector3 WorldCenter = BoxTransform.TransformPoint(BoxCollider.center);
        Vector3 AbsoluteScale = GetAbsoluteLossyScale(BoxTransform);
        Vector3 HalfExtents = Vector3.Scale(BoxCollider.size, AbsoluteScale) * 0.5f;

        return Physics.OverlapBoxNonAlloc(
            WorldCenter,
            HalfExtents,
            OverlapResults,
            BoxTransform.rotation,
            OreInputLayers,
            QueryTriggerInteraction.Collide);
    }

    /// <summary>
    /// Performs a non-allocating overlap query for a SphereCollider zone.
    /// </summary>
    private int QuerySphereCollider(SphereCollider SphereCollider)
    {
        Transform SphereTransform = SphereCollider.transform;
        Vector3 WorldCenter = SphereTransform.TransformPoint(SphereCollider.center);
        Vector3 AbsoluteScale = GetAbsoluteLossyScale(SphereTransform);
        float WorldRadius = SphereCollider.radius * Mathf.Max(AbsoluteScale.x, Mathf.Max(AbsoluteScale.y, AbsoluteScale.z));

        return Physics.OverlapSphereNonAlloc(
            WorldCenter,
            WorldRadius,
            OverlapResults,
            OreInputLayers,
            QueryTriggerInteraction.Collide);
    }

    /// <summary>
    /// Performs a non-allocating overlap query for a CapsuleCollider zone.
    /// </summary>
    private int QueryCapsuleCollider(CapsuleCollider CapsuleCollider)
    {
        Transform CapsuleTransform = CapsuleCollider.transform;
        Vector3 WorldCenter = CapsuleTransform.TransformPoint(CapsuleCollider.center);
        Vector3 AbsoluteScale = GetAbsoluteLossyScale(CapsuleTransform);
        Vector3 Axis = GetCapsuleAxis(CapsuleTransform, CapsuleCollider.direction);
        float AxisScale = GetCapsuleAxisScale(AbsoluteScale, CapsuleCollider.direction);
        float RadiusScale = GetCapsuleRadiusScale(AbsoluteScale, CapsuleCollider.direction);
        float WorldRadius = CapsuleCollider.radius * RadiusScale;
        float WorldHeight = Mathf.Max(CapsuleCollider.height * AxisScale, WorldRadius * 2f);
        float SegmentHalfLength = Mathf.Max(0f, (WorldHeight * 0.5f) - WorldRadius);
        Vector3 PointA = WorldCenter + Axis * SegmentHalfLength;
        Vector3 PointB = WorldCenter - Axis * SegmentHalfLength;

        return Physics.OverlapCapsuleNonAlloc(
            PointA,
            PointB,
            WorldRadius,
            OverlapResults,
            OreInputLayers,
            QueryTriggerInteraction.Collide);
    }

    /// <summary>
    /// Removes registered colliders that are no longer inside the current overlap scan or are no longer valid ore pickups.
    /// </summary>
    private void RemoveStaleRegisteredColliders()
    {
        StaleRegisteredColliders.Clear();

        foreach (Collider RegisteredCollider in RegisteredColliders)
        {
            if (RegisteredCollider == null ||
                !RegisteredCollider.enabled ||
                !RegisteredCollider.gameObject.activeInHierarchy ||
                !ScannedColliders.Contains(RegisteredCollider) ||
                !TryResolveAcceptedOreCollider(RegisteredCollider, out _))
            {
                StaleRegisteredColliders.Add(RegisteredCollider);
            }
        }

        for (int Index = 0; Index < StaleRegisteredColliders.Count; Index++)
        {
            TryUnregisterCollider(StaleRegisteredColliders[Index]);
        }
    }

    /// <summary>
    /// Unregisters every collider currently owned by this input zone.
    /// </summary>
    private void UnregisterAllColliders()
    {
        if (OwnerStation != null)
        {
            foreach (Collider RegisteredCollider in RegisteredColliders)
            {
                OwnerStation.UnregisterOreInputCollider(RegisteredCollider);
            }
        }

        RegisteredColliders.Clear();
        ScannedColliders.Clear();
        StaleRegisteredColliders.Clear();
    }

    /// <summary>
    /// Resolves whether a collider belongs to a valid ore pickup accepted by this zone.
    /// </summary>
    private bool TryResolveAcceptedOreCollider(Collider Other, out OrePickup Pickup)
    {
        Pickup = null;

        if (OwnerStation == null || Other == null || !Other.enabled || !Other.gameObject.activeInHierarchy)
        {
            return false;
        }

        Pickup = Other.GetComponent<OrePickup>() ?? Other.GetComponentInParent<OrePickup>();

        if (Pickup == null || !Pickup.gameObject.activeInHierarchy)
        {
            return false;
        }

        if (Pickup.GetOreItemData() == null || Pickup.GetOreItemData().GetOreDefinition() == null)
        {
            return false;
        }

        return IsAcceptedLayer(Other, Pickup);
    }

    /// <summary>
    /// Returns whether the collider or its ore pickup root belongs to an accepted layer.
    /// </summary>
    private bool IsAcceptedLayer(Collider Other, OrePickup Pickup)
    {
        if (IsLayerInMask(Other.gameObject.layer))
        {
            return true;
        }

        if (!AcceptOreRootLayer || Pickup == null)
        {
            return false;
        }

        if (IsLayerInMask(Pickup.gameObject.layer))
        {
            return true;
        }

        Transform RuntimeRoot = Pickup.GetRuntimeRoot();
        return RuntimeRoot != null && IsLayerInMask(RuntimeRoot.gameObject.layer);
    }

    /// <summary>
    /// Returns whether a Unity layer index is contained in the configured ore input mask.
    /// </summary>
    private bool IsLayerInMask(int Layer)
    {
        return (OreInputLayers.value & (1 << Layer)) != 0;
    }

    /// <summary>
    /// Resolves missing component references.
    /// </summary>
    private void ResolveReferences()
    {
        if (OwnerStation == null)
        {
            OwnerStation = GetComponentInParent<ResearchStation>();
        }

        if (ZoneCollider == null)
        {
            ZoneCollider = GetComponent<Collider>();
        }

        if (ZoneCollider != null)
        {
            ZoneCollider.isTrigger = true;
        }
    }

    /// <summary>
    /// Ensures the non-allocating overlap buffer matches the configured capacity.
    /// </summary>
    private void EnsureOverlapBuffer()
    {
        int RequiredSize = Mathf.Max(1, MaxOverlapResults);

        if (OverlapResults == null || OverlapResults.Length != RequiredSize)
        {
            OverlapResults = new Collider[RequiredSize];
        }
    }

    /// <summary>
    /// Returns the absolute lossy scale of a transform.
    /// </summary>
    private static Vector3 GetAbsoluteLossyScale(Transform SourceTransform)
    {
        Vector3 Scale = SourceTransform.lossyScale;
        return new Vector3(Mathf.Abs(Scale.x), Mathf.Abs(Scale.y), Mathf.Abs(Scale.z));
    }

    /// <summary>
    /// Returns the world-space primary axis used by a capsule collider.
    /// </summary>
    private static Vector3 GetCapsuleAxis(Transform CapsuleTransform, int Direction)
    {
        switch (Direction)
        {
            case 0:
                return CapsuleTransform.right;

            case 1:
                return CapsuleTransform.up;

            default:
                return CapsuleTransform.forward;
        }
    }

    /// <summary>
    /// Returns the scale applied along the capsule primary axis.
    /// </summary>
    private static float GetCapsuleAxisScale(Vector3 AbsoluteScale, int Direction)
    {
        switch (Direction)
        {
            case 0:
                return AbsoluteScale.x;

            case 1:
                return AbsoluteScale.y;

            default:
                return AbsoluteScale.z;
        }
    }

    /// <summary>
    /// Returns the largest perpendicular scale used by the capsule radius.
    /// </summary>
    private static float GetCapsuleRadiusScale(Vector3 AbsoluteScale, int Direction)
    {
        switch (Direction)
        {
            case 0:
                return Mathf.Max(AbsoluteScale.y, AbsoluteScale.z);

            case 1:
                return Mathf.Max(AbsoluteScale.x, AbsoluteScale.z);

            default:
                return Mathf.Max(AbsoluteScale.x, AbsoluteScale.y);
        }
    }

    /// <summary>
    /// Writes an ore-input-zone-specific debug message.
    /// </summary>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[ResearchOreInputZone] " + Message, this);
    }
}
