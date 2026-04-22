using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Logical transport system that moves payloads through a built pipe path without relying on unstable full physics inside the tube.
/// Payloads are carried by a hidden runtime carrier transform and released back into dynamic simulation at the output.
/// </summary>
[RequireComponent(typeof(PipePathInstance))]
public sealed class PipeTransportLine : MonoBehaviour
{
    /// <summary>
    /// Runtime transported payload.
    /// </summary>
    private sealed class TransportPayload
    {
        /// <summary>
        /// Carryable currently transported by the line.
        /// </summary>
        public PhysicsCarryable Carryable;

        /// <summary>
        /// Runtime carrier transform that parents the carryable while moving through the line.
        /// </summary>
        public Transform CarrierTransform;

        /// <summary>
        /// Current traveled distance along the pipe center line.
        /// </summary>
        public float Distance;

        /// <summary>
        /// Logical speed used by this payload.
        /// </summary>
        public float Speed;
    }

    [Header("References")]
    [Tooltip("Built pipe path sampled by this transport line. If empty, one is resolved from the same GameObject.")]
    [SerializeField] private PipePathInstance PipePathInstance;

    [Tooltip("Optional parent used to store runtime carrier transforms.")]
    [SerializeField] private Transform RuntimeCarrierRoot;

    [Header("Transport")]
    [Tooltip("Default payload speed used when an explicit speed is not provided.")]
    [SerializeField] private float DefaultSpeed = 2.5f;

    [Tooltip("Extra release push applied at the output so the payload leaves the pipe cleanly.")]
    [SerializeField] private float OutputImpulseSpeed = 0.5f;

    [Header("Debug")]
    [Tooltip("Logs payload enter and exit operations.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Active payloads currently moving through this transport line.
    /// </summary>
    private readonly List<TransportPayload> ActivePayloads = new List<TransportPayload>();

    /// <summary>
    /// Initializes missing references.
    /// </summary>
    private void Awake()
    {
        if (PipePathInstance == null)
        {
            PipePathInstance = GetComponent<PipePathInstance>();
        }

        if (RuntimeCarrierRoot == null)
        {
            GameObject CarrierRootObject = new GameObject("RuntimeCarriers");
            RuntimeCarrierRoot = CarrierRootObject.transform;
            RuntimeCarrierRoot.SetParent(transform, false);
        }
    }

    /// <summary>
    /// Updates every active payload along the built path.
    /// </summary>
    private void Update()
    {
        if (PipePathInstance == null || PipePathInstance.GetTotalLength() <= 0f)
        {
            return;
        }

        for (int PayloadIndex = ActivePayloads.Count - 1; PayloadIndex >= 0; PayloadIndex--)
        {
            TransportPayload Payload = ActivePayloads[PayloadIndex];
            if (Payload == null || Payload.Carryable == null || Payload.CarrierTransform == null)
            {
                RemovePayloadAt(PayloadIndex, false);
                continue;
            }

            Payload.Distance += Payload.Speed * Time.deltaTime;
            float ClampedDistance = Mathf.Clamp(Payload.Distance, 0f, PipePathInstance.GetTotalLength());

            Vector3 Position = PipePathInstance.SamplePosition(ClampedDistance);
            Vector3 Tangent = PipePathInstance.SampleTangent(ClampedDistance);
            Vector3 Support = PipePathInstance.SampleSupportDirection(ClampedDistance);
            Vector3 Up = PipeAxisUtility.BuildFrameUp(Tangent, Support);

            Payload.CarrierTransform.SetPositionAndRotation(Position, Quaternion.LookRotation(Tangent, Up));

            if (Payload.Distance >= PipePathInstance.GetTotalLength())
            {
                ReleasePayload(PayloadIndex, Tangent * (Payload.Speed + OutputImpulseSpeed));
            }
        }
    }

    /// <summary>
    /// Tries to accept a physics carryable into the pipe transport line.
    /// </summary>
    /// <param name="Carryable">Carryable to transport.</param>
    /// <param name="SpeedOverride">Optional logical speed override.</param>
    /// <returns>True when the payload was accepted.</returns>
    public bool TryAcceptCarryable(PhysicsCarryable Carryable, float SpeedOverride = -1f)
    {
        if (PipePathInstance == null || Carryable == null || PipePathInstance.GetControlPoints().Count < 2)
        {
            return false;
        }

        if (Carryable.GetIsHeld())
        {
            Carryable.EndHold();
        }

        if (Carryable.GetIsMagnetized())
        {
            Carryable.EndMagnet();
        }

        float Speed = SpeedOverride > 0f ? SpeedOverride : Mathf.Max(0.01f, DefaultSpeed);

        GameObject CarrierObject = new GameObject("PipePayloadCarrier_" + Carryable.name);
        Transform CarrierTransform = CarrierObject.transform;
        CarrierTransform.SetParent(RuntimeCarrierRoot, false);

        Vector3 StartPosition = PipePathInstance.SamplePosition(0f);
        Vector3 StartTangent = PipePathInstance.SampleTangent(0f);
        Vector3 StartSupport = PipePathInstance.SampleSupportDirection(0f);
        Vector3 StartUp = PipeAxisUtility.BuildFrameUp(StartTangent, StartSupport);
        CarrierTransform.SetPositionAndRotation(StartPosition, Quaternion.LookRotation(StartTangent, StartUp));

        Carryable.BeginExternalCarry(CarrierTransform);
        Carryable.SetConveyorDriven(true);

        ActivePayloads.Add(new TransportPayload
        {
            Carryable = Carryable,
            CarrierTransform = CarrierTransform,
            Distance = 0f,
            Speed = Speed
        });

        Log("Accepted carryable into pipe: " + Carryable.name);
        return true;
    }

    /// <summary>
    /// Tries to accept one ore pickup by resolving its carryable component.
    /// </summary>
    /// <param name="OrePickup">Ore pickup to transport.</param>
    /// <param name="SpeedOverride">Optional logical speed override.</param>
    /// <returns>True when the payload was accepted.</returns>
    public bool TryAcceptOrePickup(OrePickup OrePickup, float SpeedOverride = -1f)
    {
        if (OrePickup == null)
        {
            return false;
        }

        PhysicsCarryable Carryable = OrePickup.GetComponent<PhysicsCarryable>();
        if (Carryable == null)
        {
            Carryable = OrePickup.GetComponentInChildren<PhysicsCarryable>(true);
        }

        return TryAcceptCarryable(Carryable, SpeedOverride);
    }

    /// <summary>
    /// Releases one payload back to dynamic simulation at the pipe output.
    /// </summary>
    /// <param name="PayloadIndex">Active payload index.</param>
    /// <param name="ExitVelocity">Velocity inherited on release.</param>
    private void ReleasePayload(int PayloadIndex, Vector3 ExitVelocity)
    {
        if (PayloadIndex < 0 || PayloadIndex >= ActivePayloads.Count)
        {
            return;
        }

        TransportPayload Payload = ActivePayloads[PayloadIndex];
        if (Payload?.Carryable != null)
        {
            Payload.Carryable.SetConveyorDriven(false);
            Payload.Carryable.EndExternalCarry(ExitVelocity);
            Log("Released carryable from pipe: " + Payload.Carryable.name);
        }

        RemovePayloadAt(PayloadIndex, true);
    }

    /// <summary>
    /// Removes one runtime payload entry and optionally destroys its carrier object.
    /// </summary>
    /// <param name="PayloadIndex">Active payload index.</param>
    /// <param name="DestroyCarrier">True to destroy the carrier GameObject.</param>
    private void RemovePayloadAt(int PayloadIndex, bool DestroyCarrier)
    {
        if (PayloadIndex < 0 || PayloadIndex >= ActivePayloads.Count)
        {
            return;
        }

        Transform CarrierTransform = ActivePayloads[PayloadIndex]?.CarrierTransform;
        ActivePayloads.RemoveAt(PayloadIndex);

        if (DestroyCarrier && CarrierTransform != null)
        {
            Destroy(CarrierTransform.gameObject);
        }
    }

    /// <summary>
    /// Writes transport-specific debug logs.
    /// </summary>
    /// <param name="Message">Message to log.</param>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[PipeTransportLine] " + Message, this);
    }
}
