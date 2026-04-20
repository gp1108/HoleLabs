using UnityEngine;

/// <summary>
/// Explicit sleep controller for money pickups.
/// It puts the rigidbody to sleep only after the pickup has remained supported and almost motionless
/// for a short amount of time, which reduces physics cost when many pickups accumulate on the floor.
/// </summary>
[DisallowMultipleComponent]
public sealed class MoneyPickupSleepController : MonoBehaviour
{
    [System.Serializable]
    public struct SleepSettings
    {
        [Tooltip("Seconds the rigidbody must remain idle before sleeping.")]
        public float RequiredStillTime;

        [Tooltip("Maximum linear speed allowed to consider the rigidbody idle.")]
        public float MaxLinearSpeed;

        [Tooltip("Maximum angular speed allowed to consider the rigidbody idle.")]
        public float MaxAngularSpeed;

        [Tooltip("Minimum upward normal required to consider a collision as supporting ground.")]
        public float MinimumSupportNormalY;
    }

    [Header("References")]
    [Tooltip("Rigidbody controlled by this sleep policy. If empty, one will be searched on this object or its children.")]
    [SerializeField] private Rigidbody TargetRigidbody;

    [Header("Sleep")]
    [Tooltip("Configuration used to decide when the money pickup may sleep.")]
    [SerializeField]
    private SleepSettings Settings = new SleepSettings
    {
        RequiredStillTime = 0.2f,
        MaxLinearSpeed = 0.05f,
        MaxAngularSpeed = 0.08f,
        MinimumSupportNormalY = 0.45f
    };

    [Header("Debug")]
    [Tooltip("Logs when the rigidbody is put to sleep.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Accumulated idle time while all sleep conditions are satisfied.
    /// </summary>
    private float IdleTimer;

    /// <summary>
    /// Whether a valid support contact was detected during the current simulation step.
    /// </summary>
    private bool HasSupportThisStep;

    /// <summary>
    /// Resolves the target rigidbody reference.
    /// </summary>
    private void Awake()
    {
        ResolveRigidbody();
    }

    /// <summary>
    /// Evaluates whether the money pickup should be put to sleep.
    /// </summary>
    private void FixedUpdate()
    {
        if (TargetRigidbody == null || TargetRigidbody.isKinematic)
        {
            IdleTimer = 0f;
            HasSupportThisStep = false;
            return;
        }

        bool IsIdle =
            TargetRigidbody.linearVelocity.sqrMagnitude <= Settings.MaxLinearSpeed * Settings.MaxLinearSpeed &&
            TargetRigidbody.angularVelocity.sqrMagnitude <= Settings.MaxAngularSpeed * Settings.MaxAngularSpeed;

        bool CanSleepNow = HasSupportThisStep && IsIdle;

        if (!CanSleepNow)
        {
            IdleTimer = 0f;
            HasSupportThisStep = false;
            return;
        }

        IdleTimer += Time.fixedDeltaTime;

        if (IdleTimer >= Settings.RequiredStillTime && !TargetRigidbody.IsSleeping())
        {
            TargetRigidbody.Sleep();

            if (DebugLogs)
            {
                Debug.Log("[MoneyPickupSleepController] Slept money rigidbody: " + name, this);
            }
        }

        HasSupportThisStep = false;
    }

    /// <summary>
    /// Forces the controlled rigidbody awake and clears internal sleep timers.
    /// Useful when the pickup is emitted again from the machine or reused from pool.
    /// </summary>
    public void WakeUp()
    {
        IdleTimer = 0f;
        HasSupportThisStep = false;

        if (TargetRigidbody != null)
        {
            TargetRigidbody.WakeUp();
        }
    }

    /// <summary>
    /// Collects support contacts from the rigidbody collision callbacks.
    /// </summary>
    private void OnCollisionEnter(Collision Collision)
    {
        EvaluateSupportContacts(Collision);
    }

    /// <summary>
    /// Collects support contacts from the rigidbody collision callbacks.
    /// </summary>
    private void OnCollisionStay(Collision Collision)
    {
        EvaluateSupportContacts(Collision);
    }

    /// <summary>
    /// Evaluates whether any collision contact can support the rigidbody from below.
    /// </summary>
    private void EvaluateSupportContacts(Collision Collision)
    {
        if (Collision == null)
        {
            return;
        }

        for (int ContactIndex = 0; ContactIndex < Collision.contactCount; ContactIndex++)
        {
            ContactPoint Contact = Collision.GetContact(ContactIndex);

            if (Contact.normal.y >= Settings.MinimumSupportNormalY)
            {
                HasSupportThisStep = true;
                return;
            }
        }
    }

    /// <summary>
    /// Resolves the target rigidbody on this object or its children.
    /// </summary>
    private void ResolveRigidbody()
    {
        if (TargetRigidbody != null)
        {
            return;
        }

        TargetRigidbody = GetComponent<Rigidbody>();

        if (TargetRigidbody == null)
        {
            TargetRigidbody = GetComponentInChildren<Rigidbody>(true);
        }
    }
}