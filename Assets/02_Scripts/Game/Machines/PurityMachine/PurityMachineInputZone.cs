using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Trigger zone used by the purity machine to track either the single target ore or the sacrifice ore set.
/// This component only owns robust trigger tracking; validation and processing rules live in PurityMachineController.
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class PurityMachineInputZone : MonoBehaviour
{
    /// <summary>
    /// Defines which input role this trigger volume represents.
    /// </summary>
    public enum InputZoneType
    {
        Target = 0,
        Sacrifice = 1
    }

    [Header("References")]
    [Tooltip("Purity machine controller that owns this input zone.")]
    [SerializeField] private PurityMachineController PurityMachineController;

    [Header("Zone")]
    [Tooltip("Role represented by this trigger volume.")]
    [SerializeField] private InputZoneType ZoneType = InputZoneType.Sacrifice;

    [Tooltip("If true, this component forces its collider to be a trigger in edit mode.")]
    [SerializeField] private bool ForceTriggerCollider = true;

    [Header("Debug")]
    [Tooltip("Logs ore enter and exit events for this zone.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Ores currently tracked by this trigger volume.
    /// </summary>
    private readonly HashSet<OrePickup> TrackedOres = new();

    /// <summary>
    /// Per-ore collider overlap counts used to handle ore prefabs with multiple colliders.
    /// </summary>
    private readonly Dictionary<OrePickup, int> OreOverlapCounts = new();

    /// <summary>
    /// Gets the role represented by this input zone.
    /// </summary>
    public InputZoneType GetZoneType()
    {
        return ZoneType;
    }

    /// <summary>
    /// Registers an ore collider entering this input zone.
    /// </summary>
    /// <param name="Other">Collider that entered the zone.</param>
    private void OnTriggerEnter(Collider Other)
    {
        OrePickup OrePickup = ResolveOrePickup(Other);

        if (OrePickup == null)
        {
            return;
        }

        RegisterOreEnter(OrePickup);
    }

    /// <summary>
    /// Registers an ore collider exiting this input zone.
    /// </summary>
    /// <param name="Other">Collider that left the zone.</param>
    private void OnTriggerExit(Collider Other)
    {
        OrePickup OrePickup = ResolveOrePickup(Other);

        if (OrePickup == null)
        {
            return;
        }

        RegisterOreExit(OrePickup);
    }

    /// <summary>
    /// Adds every currently valid ore pickup tracked by this zone into the provided output list.
    /// Invalid inactive or data-less entries are pruned before the result is returned.
    /// </summary>
    /// <param name="Results">List that receives valid ore pickups.</param>
    public void AppendValidOrePickups(List<OrePickup> Results)
    {
        if (Results == null)
        {
            return;
        }

        PruneInvalidTrackedOres();

        foreach (OrePickup OrePickup in TrackedOres)
        {
            if (IsOreValid(OrePickup))
            {
                Results.Add(OrePickup);
            }
        }
    }

    /// <summary>
    /// Returns whether this zone currently contains the provided ore pickup.
    /// </summary>
    /// <param name="OrePickup">Ore pickup to query.</param>
    /// <returns>True when the pickup is still tracked and valid inside this zone.</returns>
    public bool ContainsOre(OrePickup OrePickup)
    {
        PruneInvalidTrackedOres();
        return OrePickup != null && TrackedOres.Contains(OrePickup) && IsOreValid(OrePickup);
    }

    /// <summary>
    /// Forgets one ore immediately. This is used when a machine consumes and despawns a sacrifice pickup before Unity sends trigger exits.
    /// </summary>
    /// <param name="OrePickup">Ore pickup to remove from this zone.</param>
    public void ForgetOre(OrePickup OrePickup)
    {
        if (OrePickup == null)
        {
            return;
        }

        TrackedOres.Remove(OrePickup);
        OreOverlapCounts.Remove(OrePickup);
        NotifyControllerChanged();
    }

    /// <summary>
    /// Clears every tracked ore from this zone.
    /// </summary>
    public void ClearTrackedOres()
    {
        TrackedOres.Clear();
        OreOverlapCounts.Clear();
        NotifyControllerChanged();
    }

    /// <summary>
    /// Resolves an OrePickup from an entered or exited collider.
    /// </summary>
    /// <param name="Other">Collider to inspect.</param>
    /// <returns>Resolved ore pickup, or null.</returns>
    private OrePickup ResolveOrePickup(Collider Other)
    {
        if (Other == null)
        {
            return null;
        }

        OrePickup OrePickup = Other.GetComponent<OrePickup>();

        if (OrePickup != null)
        {
            return OrePickup;
        }

        OrePickup = Other.GetComponentInParent<OrePickup>();

        if (OrePickup != null)
        {
            return OrePickup;
        }

        if (Other.attachedRigidbody != null)
        {
            OrePickup = Other.attachedRigidbody.GetComponent<OrePickup>();

            if (OrePickup != null)
            {
                return OrePickup;
            }

            OrePickup = Other.attachedRigidbody.GetComponentInChildren<OrePickup>(true);
        }

        return OrePickup;
    }

    /// <summary>
    /// Registers one collider from an ore pickup entering the trigger volume.
    /// </summary>
    /// <param name="OrePickup">Ore pickup that entered.</param>
    private void RegisterOreEnter(OrePickup OrePickup)
    {
        if (OrePickup == null)
        {
            return;
        }

        if (!OreOverlapCounts.TryGetValue(OrePickup, out int OverlapCount))
        {
            OverlapCount = 0;
        }

        OreOverlapCounts[OrePickup] = OverlapCount + 1;
        TrackedOres.Add(OrePickup);
        Log("Ore entered " + ZoneType + " zone: " + OrePickup.name);
        NotifyControllerChanged();
    }

    /// <summary>
    /// Registers one collider from an ore pickup leaving the trigger volume.
    /// The ore is removed only when all of its colliders have exited.
    /// </summary>
    /// <param name="OrePickup">Ore pickup that exited.</param>
    private void RegisterOreExit(OrePickup OrePickup)
    {
        if (OrePickup == null)
        {
            return;
        }

        if (!OreOverlapCounts.TryGetValue(OrePickup, out int OverlapCount))
        {
            TrackedOres.Remove(OrePickup);
            NotifyControllerChanged();
            return;
        }

        OverlapCount--;

        if (OverlapCount > 0)
        {
            OreOverlapCounts[OrePickup] = OverlapCount;
            return;
        }

        OreOverlapCounts.Remove(OrePickup);
        TrackedOres.Remove(OrePickup);
        Log("Ore exited " + ZoneType + " zone: " + OrePickup.name);
        NotifyControllerChanged();
    }

    /// <summary>
    /// Removes stale ore references that can no longer participate in machine validation.
    /// </summary>
    private void PruneInvalidTrackedOres()
    {
        if (TrackedOres.Count == 0)
        {
            return;
        }

        List<OrePickup> InvalidOres = null;

        foreach (OrePickup OrePickup in TrackedOres)
        {
            if (IsOreValid(OrePickup))
            {
                continue;
            }

            if (InvalidOres == null)
            {
                InvalidOres = new List<OrePickup>();
            }

            InvalidOres.Add(OrePickup);
        }

        if (InvalidOres == null)
        {
            return;
        }

        for (int Index = 0; Index < InvalidOres.Count; Index++)
        {
            OrePickup OrePickup = InvalidOres[Index];
            TrackedOres.Remove(OrePickup);

            if (OrePickup != null)
            {
                OreOverlapCounts.Remove(OrePickup);
            }
        }
    }

    /// <summary>
    /// Returns whether the ore can still be considered present in this zone.
    /// </summary>
    /// <param name="OrePickup">Ore pickup to validate.</param>
    /// <returns>True when the pickup exists, is active and carries runtime ore data.</returns>
    private bool IsOreValid(OrePickup OrePickup)
    {
        if (OrePickup == null)
        {
            return false;
        }

        Transform RuntimeRoot = OrePickup.GetRuntimeRoot();

        if (RuntimeRoot == null || !RuntimeRoot.gameObject.activeInHierarchy)
        {
            return false;
        }

        return OrePickup.GetOreItemData() != null;
    }

    /// <summary>
    /// Notifies the owning controller that zone contents have changed.
    /// </summary>
    private void NotifyControllerChanged()
    {
        if (PurityMachineController != null)
        {
            PurityMachineController.NotifyInputZoneContentsChanged(this);
        }
    }

    /// <summary>
    /// Keeps the collider configured as a trigger while editing.
    /// </summary>
    private void OnValidate()
    {
        if (!ForceTriggerCollider)
        {
            return;
        }

        Collider Collider = GetComponent<Collider>();

        if (Collider != null)
        {
            Collider.isTrigger = true;
        }
    }

    /// <summary>
    /// Logs zone messages when debug logging is enabled.
    /// </summary>
    /// <param name="Message">Message to write.</param>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[PurityMachineInputZone] " + Message, this);
    }
}
