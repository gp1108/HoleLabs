using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stores idle PhysicsCarryable objects inside an elevator by switching them to external kinematic carry
/// after they remain inside the storage trigger for a configurable amount of time.
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
        /// Whether the carryable is currently inside the trigger according to enter/exit callbacks.
        /// </summary>
        public bool IsInside;
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

        List<PhysicsCarryable> KeysToRemove = null;

        foreach (KeyValuePair<PhysicsCarryable, CandidateState> Pair in CandidateStates)
        {
            PhysicsCarryable Carryable = Pair.Key;
            CandidateState State = Pair.Value;

            if (Carryable == null)
            {
                if (KeysToRemove == null)
                {
                    KeysToRemove = new List<PhysicsCarryable>();
                }

                KeysToRemove.Add(Carryable);
                continue;
            }

            if (!State.IsInside)
            {
                if (KeysToRemove == null)
                {
                    KeysToRemove = new List<PhysicsCarryable>();
                }

                KeysToRemove.Add(Carryable);
                continue;
            }

            if (Carryable.IsExternallyCarried)
            {
                continue;
            }

            if (!IsCarryableEligibleForStorage(Carryable))
            {
                if (DebugLogs)
                {
                    Rigidbody CarryableRigidbody = Carryable.Rigidbody;
                    if (CarryableRigidbody != null)
                    {
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
                }

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

            Log("Stored carryable: " + Carryable.name);
        }

        if (KeysToRemove == null)
        {
            return;
        }

        for (int Index = 0; Index < KeysToRemove.Count; Index++)
        {
            CandidateStates.Remove(KeysToRemove[Index]);
        }
    }

    /// <summary>
    /// Registers a carryable candidate when it enters the storage trigger.
    /// </summary>
    /// <param name="Other">Collider entering the trigger.</param>
    private void OnTriggerEnter(Collider Other)
    {
        PhysicsCarryable Carryable = ResolveCarryable(Other);

        if (Carryable == null)
        {
            return;
        }

        if (!CandidateStates.TryGetValue(Carryable, out CandidateState State))
        {
            State = new CandidateState();
            CandidateStates.Add(Carryable, State);
        }

        State.IsInside = true;
        State.EligibleTime = 0f;
    }

    /// <summary>
    /// Keeps the candidate marked as inside while Unity continues reporting overlap stays.
    /// This helps recover from edge cases where enter/exit ordering becomes noisy on moving platforms.
    /// </summary>
    /// <param name="Other">Collider staying inside the trigger.</param>
    private void OnTriggerStay(Collider Other)
    {
        PhysicsCarryable Carryable = ResolveCarryable(Other);

        //Debug.Log(
        //    "[ElevatorCarryableStorageZone] Stay :: Collider=" + Other.name +
        //    " | Carryable=" + (Carryable != null ? Carryable.name : "NULL"),
        //    this);

        if (Carryable == null)
        {
            return;
        }

        if (!CandidateStates.TryGetValue(Carryable, out CandidateState State))
        {
            State = new CandidateState();
            CandidateStates.Add(Carryable, State);
        }

        State.IsInside = true;
    }

    /// <summary>
    /// Releases a carryable automatically if it leaves the storage zone while still externally carried by this zone.
    /// </summary>
    /// <param name="Other">Collider exiting the trigger.</param>
    private void OnTriggerExit(Collider Other)
    {
        PhysicsCarryable Carryable = ResolveCarryable(Other);

        //Debug.Log(
        //    "[ElevatorCarryableStorageZone] Exit :: Collider=" + Other.name +
        //    " | Carryable=" + (Carryable != null ? Carryable.name : "NULL"),
        //    this);

        if (Carryable == null)
        {
            return;
        }

        if (CandidateStates.TryGetValue(Carryable, out CandidateState State))
        {
            State.IsInside = false;
            State.EligibleTime = 0f;
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