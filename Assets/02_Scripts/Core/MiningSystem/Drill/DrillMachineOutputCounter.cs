using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Counts valid ore pickups currently inside the configured drill output trigger volume.
/// This is the authority used by the drill to know whether it can keep producing.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class DrillMachineOutputCounter : MonoBehaviour
{
    [Header("Filtering")]
    [Tooltip("If true, only ore pickups are counted. Any other collider is ignored.")]
    [SerializeField] private bool CountOnlyOrePickups = true;

    [Header("Debug")]
    [Tooltip("Logs output counter changes.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Trigger collider used as the output zone.
    /// </summary>
    [Tooltip("Cached trigger collider used to detect output occupancy.")]
    [SerializeField] private Collider TriggerCollider;

    /// <summary>
    /// Runtime set of ore pickups currently inside the output zone.
    /// </summary>
    private readonly HashSet<OrePickup> TrackedOrePickups = new HashSet<OrePickup>();

    /// <summary>
    /// Gets the number of valid ore pickups currently inside the zone.
    /// </summary>
    public int GetCurrentCount()
    {
        CleanupNullEntries();
        return TrackedOrePickups.Count;
    }

    /// <summary>
    /// Clears and rebuilds the runtime occupancy from world overlaps.
    /// This is important after load because trigger enter events are not guaranteed
    /// to replay for already overlapping colliders.
    /// </summary>
    public void RefreshOccupancy()
    {
        TrackedOrePickups.Clear();

        if (TriggerCollider == null)
        {
            return;
        }

        Bounds TriggerBounds = TriggerCollider.bounds;
        Collider[] Hits = Physics.OverlapBox(
            TriggerBounds.center,
            TriggerBounds.extents,
            transform.rotation,
            ~0,
            QueryTriggerInteraction.Ignore);

        for (int Index = 0; Index < Hits.Length; Index++)
        {
            TryTrackFromCollider(Hits[Index]);
        }

        Log("Output occupancy refreshed. Count=" + TrackedOrePickups.Count);
    }

    /// <summary>
    /// Resolves missing references and validates the trigger setup.
    /// </summary>
    private void Awake()
    {
        if (TriggerCollider == null)
        {
            TriggerCollider = GetComponent<Collider>();
        }

        if (TriggerCollider != null)
        {
            TriggerCollider.isTrigger = true;
        }
    }

    /// <summary>
    /// Rebuilds the occupancy when enabled.
    /// </summary>
    private void OnEnable()
    {
        RefreshOccupancy();
    }

    /// <summary>
    /// Tracks ore pickups that enter the output zone.
    /// </summary>
    private void OnTriggerEnter(Collider Other)
    {
        TryTrackFromCollider(Other);
    }

    /// <summary>
    /// Untracks ore pickups that leave the output zone.
    /// </summary>
    private void OnTriggerExit(Collider Other)
    {
        OrePickup OrePickup = ResolveOrePickup(Other);

        if (OrePickup == null)
        {
            return;
        }

        if (TrackedOrePickups.Remove(OrePickup))
        {
            Log("Ore pickup left output zone. Count=" + TrackedOrePickups.Count);
        }
    }

    /// <summary>
    /// Attempts to track a valid ore pickup from the provided collider.
    /// </summary>
    private void TryTrackFromCollider(Collider Other)
    {
        OrePickup OrePickup = ResolveOrePickup(Other);

        if (OrePickup == null)
        {
            return;
        }

        if (TrackedOrePickups.Add(OrePickup))
        {
            Log("Ore pickup entered output zone. Count=" + TrackedOrePickups.Count);
        }
    }

    /// <summary>
    /// Resolves an ore pickup from a collider or its hierarchy.
    /// </summary>
    private OrePickup ResolveOrePickup(Collider Other)
    {
        if (Other == null)
        {
            return null;
        }

        OrePickup OrePickup = Other.GetComponent<OrePickup>() ?? Other.GetComponentInParent<OrePickup>();

        if (CountOnlyOrePickups)
        {
            return OrePickup;
        }

        return OrePickup;
    }

    /// <summary>
    /// Removes null references caused by pooled or destroyed pickups.
    /// </summary>
    private void CleanupNullEntries()
    {
        TrackedOrePickups.RemoveWhere(Item => Item == null || !Item.gameObject.activeInHierarchy);
    }

    /// <summary>
    /// Writes a debug log when enabled.
    /// </summary>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[DrillMachineOutputCounter] " + Message, this);
    }
}