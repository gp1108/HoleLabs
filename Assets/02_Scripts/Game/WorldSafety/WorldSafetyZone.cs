using UnityEngine;

/// <summary>
/// Defines a world-space safety volume used by death recovery and object cleanup policies.
/// The recovery service uses laboratory zones to decide whether loose runtime objects are safe or should be lost.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class WorldSafetyZone : MonoBehaviour
{
    /// <summary>
    /// Logical zone category used by recovery policy systems.
    /// </summary>
    public enum SafetyZoneKind
    {
        Laboratory = 0,
        Recovery = 1,
        Hazard = 2
    }

    [Header("Zone")]
    [Tooltip("Logical purpose of this zone. Laboratory zones preserve loose objects during death cleanup.")]
    [SerializeField] private SafetyZoneKind ZoneKind = SafetyZoneKind.Laboratory;

    [Tooltip("Collider volume used to classify world positions. If empty, the collider on this GameObject is used.")]
    [SerializeField] private Collider ZoneCollider;

    [Tooltip("If true, the zone collider is forced to trigger mode so it cannot physically block gameplay objects.")]
    [SerializeField] private bool ForceTrigger = true;

    [Tooltip("If false, this zone is ignored by runtime recovery policies without disabling the GameObject.")]
    [SerializeField] private bool IsEnabledForPolicy = true;

    [Header("Debug")]
    [Tooltip("Draws this safety zone in the Scene view when selected.")]
    [SerializeField] private bool DrawDebugGizmos = true;

    /// <summary>
    /// Gets the logical kind configured for this zone.
    /// </summary>
    public SafetyZoneKind GetZoneKind()
    {
        return ZoneKind;
    }

    /// <summary>
    /// Gets whether this zone can be used by runtime recovery policies.
    /// </summary>
    public bool GetIsEnabledForPolicy()
    {
        return IsEnabledForPolicy && isActiveAndEnabled && GetZoneCollider() != null;
    }

    /// <summary>
    /// Gets whether the provided world position is inside this zone volume.
    /// </summary>
    /// <param name="WorldPosition">World position to test.</param>
    /// <returns>True when the position is inside the zone collider.</returns>
    public bool ContainsWorldPosition(Vector3 WorldPosition)
    {
        Collider ColliderValue = GetZoneCollider();

        if (!GetIsEnabledForPolicy() || ColliderValue == null)
        {
            return false;
        }

        if (!ColliderValue.bounds.Contains(WorldPosition))
        {
            return false;
        }

        Vector3 ClosestPoint = ColliderValue.ClosestPoint(WorldPosition);
        return (ClosestPoint - WorldPosition).sqrMagnitude <= 0.0001f;
    }

    /// <summary>
    /// Gets the collider used by this zone, resolving it lazily if needed.
    /// </summary>
    /// <returns>Resolved zone collider, or null.</returns>
    public Collider GetZoneCollider()
    {
        if (ZoneCollider == null)
        {
            ZoneCollider = GetComponent<Collider>();
        }

        return ZoneCollider;
    }

    /// <summary>
    /// Resolves editor-time collider settings when values change.
    /// </summary>
    private void OnValidate()
    {
        RefreshColliderMode();
    }

    /// <summary>
    /// Resolves runtime collider settings when the zone wakes up.
    /// </summary>
    private void Awake()
    {
        RefreshColliderMode();
    }

    /// <summary>
    /// Applies safe collider configuration for this classification volume.
    /// </summary>
    private void RefreshColliderMode()
    {
        Collider ColliderValue = GetZoneCollider();

        if (ColliderValue != null && ForceTrigger)
        {
            ColliderValue.isTrigger = true;
        }
    }

    /// <summary>
    /// Draws an approximate editor visualization of this zone.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!DrawDebugGizmos)
        {
            return;
        }

        Collider ColliderValue = GetZoneCollider();

        if (ColliderValue == null)
        {
            return;
        }

        Color GizmoColor = ZoneKind == SafetyZoneKind.Laboratory
            ? new Color(0.1f, 0.8f, 0.25f, 0.2f)
            : new Color(0.7f, 0.7f, 1f, 0.2f);

        Gizmos.color = GizmoColor;
        Gizmos.DrawCube(ColliderValue.bounds.center, ColliderValue.bounds.size);

        Gizmos.color = new Color(GizmoColor.r, GizmoColor.g, GizmoColor.b, 0.85f);
        Gizmos.DrawWireCube(ColliderValue.bounds.center, ColliderValue.bounds.size);
    }
}
