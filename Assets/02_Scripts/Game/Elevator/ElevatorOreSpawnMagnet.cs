using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Applies a one-shot assisted launch to newly spawned ore pickups when they are created
/// inside a configured elevator effect area.
/// The ore still spawns exactly as usual. This component only schedules a deterministic
/// planar launch towards the attraction center after one physics step, so the direction
/// is resolved from the real runtime position of the spawned object.
/// </summary>
[DisallowMultipleComponent]
public sealed class ElevatorOreSpawnMagnet : MonoBehaviour
{
    /// <summary>
    /// Runtime registry of every active elevator ore spawn magnet in the loaded scene.
    /// This allows ore veins to resolve the correct magnet automatically by world position
    /// without requiring manual references on every spawn point.
    /// </summary>
    private static readonly List<ElevatorOreSpawnMagnet> ActiveMagnets = new();

    /// <summary>
    /// Small reusable overlap buffer used to validate whether a spawn point belongs to the trigger.
    /// </summary>
    private static readonly Collider[] TriggerCheckResults = new Collider[8];

    /// <summary>
    /// One pending assisted launch waiting for the next physics step.
    /// </summary>
    private sealed class PendingLaunchRequest
    {
        /// <summary>
        /// Root transform of the spawned ore object used to calculate the planar direction robustly.
        /// </summary>
        public Transform RuntimeRoot;

        /// <summary>
        /// Rigidbody that will receive the deterministic assisted launch.
        /// </summary>
        public Rigidbody OreRigidbody;

        /// <summary>
        /// Authoritative world spawn point used only to validate the effect area.
        /// </summary>
        public Vector3 SpawnPoint;

        /// <summary>
        /// Runtime ore weight used only for debug logs.
        /// </summary>
        public float OreWeight;

        /// <summary>
        /// Runtime allowed weight used only for debug logs.
        /// </summary>
        public float AllowedWeight;
    }

    [Header("References")]
    [Tooltip("Trigger collider defining the valid effect area. Only ores spawned inside this area can be assisted.")]
    [SerializeField] private Collider EffectTrigger;

    [Tooltip("World point used as the attraction center. If empty, this transform is used.")]
    [SerializeField] private Transform AttractionCenter;

    [Tooltip("Upgrade manager used to resolve the final supported ore weight.")]
    [SerializeField] private UpgradeManager UpgradeManager;

    [Header("Weight")]
    [Tooltip("Base maximum ore weight supported by the spawn assist before upgrades are applied.")]
    [SerializeField] private float BaseMaxAttractedOreWeight = 15f;

    [Header("Launch")]
    [Tooltip("Horizontal launch speed applied towards the attraction center on the XZ plane.")]
    [SerializeField] private float HorizontalLaunchSpeed = 1.4f;

    [Tooltip("Vertical launch speed used to create a controlled short arc.")]
    [SerializeField] private float UpwardLaunchSpeed = 0.9f;

    [Tooltip("Maximum total final velocity allowed after the assisted launch is applied.")]
    [SerializeField] private float MaxTotalLaunchSpeed = 1.8f;

    [Header("Debug")]
    [Tooltip("Logs assist decisions and applied launch values.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Pending launch requests processed during the next physics step.
    /// </summary>
    private readonly List<PendingLaunchRequest> PendingLaunchRequests = new();

    /// <summary>
    /// Registers this magnet instance in the global runtime list.
    /// </summary>
    private void OnEnable()
    {
        if (!ActiveMagnets.Contains(this))
        {
            ActiveMagnets.Add(this);
        }
    }

    /// <summary>
    /// Unregisters this magnet instance from the global runtime list.
    /// </summary>
    private void OnDisable()
    {
        ActiveMagnets.Remove(this);
        PendingLaunchRequests.Clear();
    }

    /// <summary>
    /// Processes deferred assisted launches in physics time after the spawned ores had one step
    /// to settle their initial contacts.
    /// </summary>
    private void FixedUpdate()
    {
        if (PendingLaunchRequests.Count == 0)
        {
            return;
        }

        for (int Index = PendingLaunchRequests.Count - 1; Index >= 0; Index--)
        {
            PendingLaunchRequest Request = PendingLaunchRequests[Index];
            PendingLaunchRequests.RemoveAt(Index);

            if (Request == null || Request.OreRigidbody == null || Request.RuntimeRoot == null)
            {
                continue;
            }

            ApplyDeferredLaunch(Request);
        }
    }

    /// <summary>
    /// Resolves the best active magnet for the provided world point.
    /// The selected magnet must contain the point inside its trigger volume.
    /// If multiple magnets contain the point, the closest attraction center is preferred.
    /// </summary>
    /// <param name="WorldPoint">World position used to resolve the appropriate magnet.</param>
    /// <returns>Resolved magnet or null when no active magnet contains the point.</returns>
    public static ElevatorOreSpawnMagnet FindBestForPoint(Vector3 WorldPoint)
    {
        ElevatorOreSpawnMagnet BestMagnet = null;
        float BestDistanceSqr = float.MaxValue;

        for (int Index = 0; Index < ActiveMagnets.Count; Index++)
        {
            ElevatorOreSpawnMagnet Candidate = ActiveMagnets[Index];

            if (Candidate == null || !Candidate.ContainsWorldPoint(WorldPoint))
            {
                continue;
            }

            Vector3 CenterPoint = Candidate.GetAttractionCenterPosition();
            float DistanceSqr = (CenterPoint - WorldPoint).sqrMagnitude;

            if (DistanceSqr < BestDistanceSqr)
            {
                BestDistanceSqr = DistanceSqr;
                BestMagnet = Candidate;
            }
        }

        return BestMagnet;
    }

    /// <summary>
    /// Gets the current resolved maximum supported ore weight after upgrades.
    /// </summary>
    /// <returns>Final supported ore weight threshold.</returns>
    public float GetResolvedMaxAttractedOreWeight()
    {
        float BaseValue = Mathf.Max(0f, BaseMaxAttractedOreWeight);

        if (UpgradeManager == null)
        {
            return BaseValue;
        }

        return Mathf.Max(
            0f,
            UpgradeManager.GetModifiedFloatStat(
                UpgradeStatType.ElevatorOreMagnetMaxWeight,
                BaseValue));
    }

    /// <summary>
    /// Returns whether the provided world point is inside this magnet effect area.
    /// Uses a tiny overlap check against the trigger itself so the result stays stable
    /// regardless of the trigger center being above or below the attraction center.
    /// </summary>
    /// <param name="WorldPoint">World point to test.</param>
    /// <returns>True when the point lies inside the configured trigger collider.</returns>
    public bool ContainsWorldPoint(Vector3 WorldPoint)
    {
        if (EffectTrigger == null)
        {
            return false;
        }

        int HitCount = Physics.OverlapSphereNonAlloc(
            WorldPoint,
            0.01f,
            TriggerCheckResults,
            ~0,
            QueryTriggerInteraction.Collide);

        for (int Index = 0; Index < HitCount; Index++)
        {
            if (TriggerCheckResults[Index] == EffectTrigger)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Attempts to schedule the assisted spawn launch for the provided ore object.
    /// The actual launch is deferred until the next physics step and resolved from
    /// the runtime root position of the object, not from the original spawn point.
    /// </summary>
    /// <param name="SpawnedOreObject">Root object returned by the ore runtime service.</param>
    /// <param name="SpawnPoint">Authoritative world spawn point used to validate the effect area.</param>
    public void TryAssistSpawnedOre(GameObject SpawnedOreObject, Vector3 SpawnPoint)
    {
        if (SpawnedOreObject == null)
        {
            return;
        }

        OrePickup OrePickup = SpawnedOreObject.GetComponent<OrePickup>();

        if (OrePickup == null)
        {
            OrePickup = SpawnedOreObject.GetComponentInChildren<OrePickup>(true);
        }

        if (OrePickup == null)
        {
            Log("Skipped spawn assist because the spawned object has no OrePickup.");
            return;
        }

        OreItemData OreItemData = OrePickup.GetOreItemData();

        if (OreItemData == null)
        {
            Log("Skipped spawn assist because the spawned ore has no OreItemData.");
            return;
        }

        float OreWeight = Mathf.Max(0f, OreItemData.GetWeightValue());
        float AllowedWeight = GetResolvedMaxAttractedOreWeight();
        if(DebugLogs)
        {
            Debug.Log("Runtime allowedweight : " + AllowedWeight);
        }

        if (OreWeight > AllowedWeight)
        {
            Log(
                "Skipped spawn assist because ore weight exceeds threshold. " +
                "Weight=" + OreWeight.ToString("F2") +
                " | Allowed=" + AllowedWeight.ToString("F2"));
            return;
        }

        Rigidbody OreRigidbody = ResolveRigidbody(SpawnedOreObject);

        if (OreRigidbody == null)
        {
            Log("Skipped spawn assist because the spawned ore has no Rigidbody.");
            return;
        }

        if (!ContainsWorldPoint(SpawnPoint))
        {
            Log(
                "Skipped spawn assist because the spawn point is outside the effect trigger. " +
                "SpawnPoint=" + SpawnPoint.ToString("F3"));
            return;
        }

        PendingLaunchRequests.Add(new PendingLaunchRequest
        {
            RuntimeRoot = SpawnedOreObject.transform,
            OreRigidbody = OreRigidbody,
            SpawnPoint = SpawnPoint,
            OreWeight = OreWeight,
            AllowedWeight = AllowedWeight
        });
    }

    /// <summary>
    /// Applies one deferred assisted launch using the current runtime root position instead of the
    /// original spawn point so the planar direction always points to the real attraction center.
    /// </summary>
    /// <param name="Request">Deferred launch request to process.</param>
    private void ApplyDeferredLaunch(PendingLaunchRequest Request)
    {
        if (Request == null || Request.OreRigidbody == null || Request.RuntimeRoot == null)
        {
            return;
        }

        Vector3 ObjectPoint = Request.RuntimeRoot.position;
        Vector3 AttractionCenterPosition = GetAttractionCenterPosition();

        Vector3 PlanarToCenter = new Vector3(
            AttractionCenterPosition.x - ObjectPoint.x,
            0f,
            AttractionCenterPosition.z - ObjectPoint.z);

        if (PlanarToCenter.sqrMagnitude <= 0.0001f)
        {
            Log("Skipped deferred spawn assist because the ore is already at the horizontal attraction center.");
            return;
        }

        Vector3 HorizontalDirection = PlanarToCenter.normalized;

        Vector3 FinalVelocity = (HorizontalDirection * Mathf.Max(0f, HorizontalLaunchSpeed)) +
                                (Vector3.up * Mathf.Max(0f, UpwardLaunchSpeed));

        if (MaxTotalLaunchSpeed > 0f &&
            FinalVelocity.sqrMagnitude > MaxTotalLaunchSpeed * MaxTotalLaunchSpeed)
        {
            FinalVelocity = FinalVelocity.normalized * MaxTotalLaunchSpeed;
        }

        Request.OreRigidbody.angularVelocity = Vector3.zero;
        Request.OreRigidbody.linearVelocity = FinalVelocity;
        Request.OreRigidbody.WakeUp();

        Log(
            "Applied deferred spawn assist. " +
            "OreWeight=" + Request.OreWeight.ToString("F2") +
            " | AllowedWeight=" + Request.AllowedWeight.ToString("F2") +
            " | ObjectPoint=" + ObjectPoint.ToString("F3") +
            " | Center=" + AttractionCenterPosition.ToString("F3") +
            " | FinalVelocity=" + FinalVelocity.ToString("F3"));
    }

    /// <summary>
    /// Returns the configured attraction center world position.
    /// </summary>
    /// <returns>World position of the attraction center.</returns>
    private Vector3 GetAttractionCenterPosition()
    {
        return AttractionCenter != null ? AttractionCenter.position : transform.position;
    }

    /// <summary>
    /// Resolves the runtime rigidbody from the spawned ore object hierarchy.
    /// </summary>
    /// <param name="SpawnedOreObject">Spawned ore root object.</param>
    /// <returns>Resolved rigidbody or null when not found.</returns>
    private Rigidbody ResolveRigidbody(GameObject SpawnedOreObject)
    {
        Rigidbody OreRigidbody = SpawnedOreObject.GetComponent<Rigidbody>();

        if (OreRigidbody != null)
        {
            return OreRigidbody;
        }

        return SpawnedOreObject.GetComponentInChildren<Rigidbody>(true);
    }

    /// <summary>
    /// Writes a debug message when logging is enabled.
    /// </summary>
    /// <param name="Message">Message to log.</param>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[ElevatorOreSpawnMagnet] " + Message, this);
    }
}