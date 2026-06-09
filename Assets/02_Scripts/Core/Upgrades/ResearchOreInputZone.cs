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
    [Tooltip("Layer mask accepted by this ore input zone. Use a dedicated ore layer when available.")]
    [SerializeField] private LayerMask OreInputLayers = ~0;

    [Header("Debug")]
    [Tooltip("Logs ore input zone registration events.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Colliders registered through this zone so they can be safely removed on disable.
    /// </summary>
    private readonly HashSet<Collider> RegisteredColliders = new();

    /// <summary>
    /// Ensures the zone collider behaves as a trigger.
    /// </summary>
    private void Reset()
    {
        Collider ZoneCollider = GetComponent<Collider>();

        if (ZoneCollider != null)
        {
            ZoneCollider.isTrigger = true;
        }

        OwnerStation = GetComponentInParent<ResearchStation>();
    }

    /// <summary>
    /// Resolves the owner station.
    /// </summary>
    private void Awake()
    {
        if (OwnerStation == null)
        {
            OwnerStation = GetComponentInParent<ResearchStation>();
        }
    }

    /// <summary>
    /// Unregisters colliders still inside this zone when it is disabled or destroyed.
    /// </summary>
    private void OnDisable()
    {
        if (OwnerStation != null)
        {
            foreach (Collider RegisteredCollider in RegisteredColliders)
            {
                OwnerStation.UnregisterOreInputCollider(RegisteredCollider);
            }
        }

        RegisteredColliders.Clear();
    }

    /// <summary>
    /// Registers ore colliders entering this input zone.
    /// </summary>
    private void OnTriggerEnter(Collider Other)
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
        if (OwnerStation == null || Other == null)
        {
            return;
        }

        if ((OreInputLayers.value & (1 << Other.gameObject.layer)) == 0)
        {
            return;
        }

        if (Other.GetComponent<OrePickup>() == null && Other.GetComponentInParent<OrePickup>() == null)
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
