using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stores idle PhysicsCarryable objects inside an elevator by switching them to external kinematic carry
/// after they remain inside the storage trigger for a configurable amount of time.
/// This version tracks overlapping colliders per carryable so multi-collider objects are not released by a single partial exit.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class ElevatorCarryableStorageZone : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Transform used as parent for carryables while they are stored by the elevator. This must belong to the physical elevator hierarchy, not the visual follower.")]
    [SerializeField] private Transform StorageRoot;

    [Header("Timing")]
    [Tooltip("Time a carryable must remain eligible inside the trigger before it is stored.")]
    [SerializeField] private float MountDelay = 1.25f;

    [Tooltip("Grace time after the last tracked collider exits before a carryable stored by this zone is released. This prevents noisy trigger exits on moving platforms.")]
    [SerializeField] private float ExitReleaseDelay = 0.08f;

    [Header("Eligibility")]
    [Tooltip("Maximum linear speed allowed before a carryable can be stored.")]
    [SerializeField] private float MaxMountLinearSpeed = 0.15f;

    [Tooltip("Maximum angular speed allowed before a carryable can be stored.")]
    [SerializeField] private float MaxMountAngularSpeed = 2f;

    [Header("Release")]
    [Tooltip("Inherited velocity applied when a stored carryable is released automatically after leaving the zone.")]
    [SerializeField] private Vector3 ExitInheritedVelocity = Vector3.zero;

    [Header("Debug")]
    [Tooltip("Logs storage state transitions.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Runtime data tracked for each carryable currently overlapping the zone.
    /// </summary>
    private readonly Dictionary<PhysicsCarryable, CandidateState> CandidateStates = new Dictionary<PhysicsCarryable, CandidateState>();

    /// <summary>
    /// Temporary key list used to remove invalid candidates without modifying the dictionary during iteration.
    /// </summary>
    private readonly List<PhysicsCarryable> CandidatesToRemove = new List<PhysicsCarryable>();

    /// <summary>
    /// Cached trigger collider.
    /// </summary>
    private Collider TriggerCollider;

    /// <summary>
    /// Runtime state tracked for an overlapping carryable.
    /// </summary>
    private sealed class CandidateState
    {
        /// <summary>
        /// Accumulated eligible time inside the trigger.
        /// </summary>
        public float EligibleTime;

        /// <summary>
        /// Remaining time before releasing the carryable after all tracked colliders have left the zone.
        /// </summary>
        public float ExitReleaseTimer;

        /// <summary>
        /// Colliders currently considered to be overlapping this storage zone for this carryable.
        /// </summary>
        public readonly HashSet<Collider> OverlappingColliders = new HashSet<Collider>();

        /// <summary>
        /// Returns whether at least one valid active collider is still inside the trigger.
        /// </summary>
        public bool HasInsideCollider()
        {
            return OverlappingColliders.Count > 0;
        }
    }

    /// <summary>
    /// Validates setup and caches the trigger collider.
    /// </summary>
    private void Awake()
    {
        TriggerCollider = GetComponent<Collider>();

        if (TriggerCollider != null)
        {
            TriggerCollider.isTrigger = true;
        }

        if (StorageRoot == null)
        {
            StorageRoot = transform;
        }
    }

    /// <summary>
    /// Validates serialized setup in the editor.
    /// </summary>
    private void OnValidate()
    {
        Collider LocalCollider = GetComponent<Collider>();
        if (LocalCollider != null)
        {
            LocalCollider.isTrigger = true;
        }

        MountDelay = Mathf.Max(0f, MountDelay);
        ExitReleaseDelay = Mathf.Max(0f, ExitReleaseDelay);
        MaxMountLinearSpeed = Mathf.Max(0f, MaxMountLinearSpeed);
        MaxMountAngularSpeed = Mathf.Max(0f, MaxMountAngularSpeed);
    }

    /// <summary>
    /// Updates overlap candidates and stores carryables that have remained valid for long enough.
    /// </summary>
    private void FixedUpdate()
    {
        if (DebugLogs)
        {
            Debug.Log("[ElevatorCarryableStorageZone] Candidate count :: " + CandidateStates.Count, this);
        }

        if (CandidateStates.Count == 0)
        {
            return;
        }

        CandidatesToRemove.Clear();

        foreach (KeyValuePair<PhysicsCarryable, CandidateState> Pair in CandidateStates)
        {
            PhysicsCarryable Carryable = Pair.Key;
            CandidateState State = Pair.Value;

            if (Carryable == null)
            {
                CandidatesToRemove.Add(Carryable);
                continue;
            }

            PruneInvalidTrackedColliders(State);

            if (!State.HasInsideCollider())
            {
                HandleCandidateOutsideStorage(Carryable, State);
                continue;
            }

            State.ExitReleaseTimer = ExitReleaseDelay;

            if (IsStoredByThisZone(Carryable))
            {
                continue;
            }

            if (Carryable.IsExternallyCarried)
            {
                State.EligibleTime = 0f;
                continue;
            }

            if (!IsCarryableEligibleForStorage(Carryable))
            {
                LogIneligibleCarryable(Carryable);
                State.EligibleTime = 0f;
                continue;
            }

            State.EligibleTime += Time.fixedDeltaTime;

            if (State.EligibleTime < MountDelay)
            {
                continue;
            }

            Carryable.BeginExternalCarry(StorageRoot);
            State.EligibleTime = 0f;
            State.ExitReleaseTimer = ExitReleaseDelay;

            Log("Stored carryable: " + Carryable.name);
        }

        for (int Index = 0; Index < CandidatesToRemove.Count; Index++)
        {
            CandidateStates.Remove(CandidatesToRemove[Index]);
        }
    }

    /// <summary>
    /// Registers a carryable candidate when it enters the storage trigger.
    /// </summary>
    /// <param name="Other">Collider entering the trigger.</param>
    private void OnTriggerEnter(Collider Other)
    {
        RegisterOverlappingCollider(Other);
    }

    /// <summary>
    /// Keeps the candidate marked as inside while Unity continues reporting overlap stays.
    /// This helps recover from edge cases where enter/exit ordering becomes noisy on moving platforms.
    /// </summary>
    /// <param name="Other">Collider staying inside the trigger.</param>
    private void OnTriggerStay(Collider Other)
    {
        RegisterOverlappingCollider(Other);
    }

    /// <summary>
    /// Unregisters one collider from the carryable candidate when it exits the storage trigger.
    /// The carryable itself is only released when all tracked colliders have left.
    /// </summary>
    /// <param name="Other">Collider exiting the trigger.</param>
    private void OnTriggerExit(Collider Other)
    {
        PhysicsCarryable Carryable = ResolveCarryable(Other);

        if (Carryable == null)
        {
            return;
        }

        if (!CandidateStates.TryGetValue(Carryable, out CandidateState State))
        {
            return;
        }

        State.OverlappingColliders.Remove(Other);
        State.EligibleTime = 0f;

        if (!State.HasInsideCollider())
        {
            State.ExitReleaseTimer = ExitReleaseDelay;
        }
    }

    /// <summary>
    /// Registers one collider as currently overlapping this zone for its owning carryable.
    /// </summary>
    /// <param name="Other">Collider reported by Unity trigger callbacks.</param>
    private void RegisterOverlappingCollider(Collider Other)
    {
        PhysicsCarryable Carryable = ResolveCarryable(Other);

        if (Carryable == null)
        {
            return;
        }

        if (!CandidateStates.TryGetValue(Carryable, out CandidateState State))
        {
            State = new CandidateState
            {
                ExitReleaseTimer = ExitReleaseDelay
            };

            CandidateStates.Add(Carryable, State);
        }

        State.OverlappingColliders.Add(Other);
        State.ExitReleaseTimer = ExitReleaseDelay;
    }

    /// <summary>
    /// Handles a carryable candidate that no longer has any tracked collider inside the storage zone.
    /// </summary>
    /// <param name="Carryable">Carryable being evaluated.</param>
    /// <param name="State">Runtime candidate state.</param>
    private void HandleCandidateOutsideStorage(PhysicsCarryable Carryable, CandidateState State)
    {
        State.EligibleTime = 0f;
        State.ExitReleaseTimer -= Time.fixedDeltaTime;

        if (State.ExitReleaseTimer > 0f)
        {
            return;
        }

        if (IsStoredByThisZone(Carryable))
        {
            Carryable.EndExternalCarry(ExitInheritedVelocity);
            Log("Released carryable after leaving storage zone: " + Carryable.name);
        }

        CandidatesToRemove.Add(Carryable);
    }

    /// <summary>
    /// Removes disabled or destroyed colliders from a candidate state.
    /// </summary>
    /// <param name="State">Candidate state to clean.</param>
    private void PruneInvalidTrackedColliders(CandidateState State)
    {
        if (State == null || State.OverlappingColliders.Count == 0)
        {
            return;
        }

        List<Collider> CollidersToRemove = null;

        foreach (Collider TrackedCollider in State.OverlappingColliders)
        {
            if (TrackedCollider != null && TrackedCollider.enabled && TrackedCollider.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (CollidersToRemove == null)
            {
                CollidersToRemove = new List<Collider>();
            }

            CollidersToRemove.Add(TrackedCollider);
        }

        if (CollidersToRemove == null)
        {
            return;
        }

        for (int Index = 0; Index < CollidersToRemove.Count; Index++)
        {
            State.OverlappingColliders.Remove(CollidersToRemove[Index]);
        }
    }

    /// <summary>
    /// Resolves the root PhysicsCarryable from an overlapping collider.
    /// </summary>
    /// <param name="Other">Overlapping collider.</param>
    /// <returns>Resolved PhysicsCarryable or null when not found.</returns>
    private PhysicsCarryable ResolveCarryable(Collider Other)
    {
        if (Other == null)
        {
            return null;
        }

        return Other.GetComponentInParent<PhysicsCarryable>();
    }

    /// <summary>
    /// Returns whether the carryable is currently stored by this exact storage zone.
    /// </summary>
    /// <param name="Carryable">Carryable to inspect.</param>
    /// <returns>True when the carryable is externally carried and parented under this zone storage root.</returns>
    private bool IsStoredByThisZone(PhysicsCarryable Carryable)
    {
        if (Carryable == null || StorageRoot == null)
        {
            return false;
        }

        return Carryable.IsExternallyCarried && Carryable.transform.IsChildOf(StorageRoot);
    }

    /// <summary>
    /// Returns whether the carryable is currently allowed to enter storage mode.
    /// </summary>
    /// <param name="Carryable">Carryable to validate.</param>
    /// <returns>True when the carryable is idle and moving slowly enough.</returns>
    private bool IsCarryableEligibleForStorage(PhysicsCarryable Carryable)
    {
        if (Carryable == null)
        {
            return false;
        }

        if (Carryable.IsExternallyCarried)
        {
            return false;
        }

        if (Carryable.GetIsHeld() || Carryable.GetIsMagnetized())
        {
            return false;
        }

        Rigidbody CarryableRigidbody = Carryable.Rigidbody;
        if (CarryableRigidbody == null)
        {
            return false;
        }

        if (CarryableRigidbody.linearVelocity.sqrMagnitude > MaxMountLinearSpeed * MaxMountLinearSpeed)
        {
            return false;
        }

        if (CarryableRigidbody.angularVelocity.sqrMagnitude > MaxMountAngularSpeed * MaxMountAngularSpeed)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Logs detailed ineligibility data for a carryable when debug logging is enabled.
    /// </summary>
    /// <param name="Carryable">Carryable that failed eligibility.</param>
    private void LogIneligibleCarryable(PhysicsCarryable Carryable)
    {
        if (!DebugLogs || Carryable == null)
        {
            return;
        }

        Rigidbody CarryableRigidbody = Carryable.Rigidbody;
        if (CarryableRigidbody == null)
        {
            return;
        }

        Debug.LogWarning(
            "[ElevatorCarryableStorageZone] Not eligible :: " +
            Carryable.name +
            " | Held: " + Carryable.GetIsHeld() +
            " | Magnetized: " + Carryable.GetIsMagnetized() +
            " | External: " + Carryable.IsExternallyCarried +
            " | LinearSpeed: " + CarryableRigidbody.linearVelocity.magnitude.ToString("F3") +
            " | AngularSpeed: " + CarryableRigidbody.angularVelocity.magnitude.ToString("F3"),
            this);
    }

    /// <summary>
    /// Writes a storage-zone specific debug message when logging is enabled.
    /// </summary>
    /// <param name="Message">Message to log.</param>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[ElevatorCarryableStorageZone] " + name + " :: " + Message, this);
    }
}
