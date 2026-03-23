using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Keeps a kinematic rigidbody collider synchronized with an elevator transform so dynamic rigidbodies
/// can rest on it correctly while the visible elevator remains driven by transform motion for the player.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class ElevatorItemCarrier : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Visible elevator transform that defines the target pose.")]
    [SerializeField] private Transform TargetTransform;

    [Tooltip("Optional elevator controller used to auto-resolve the visible elevator transform.")]
    [SerializeField] private ElevatorController Elevator;

    private Rigidbody RigidbodyComponent;

    /// <summary>
    /// Configures the rigidbody as a kinematic physics follower.
    /// </summary>
    private void Awake()
    {
        RigidbodyComponent = GetComponent<Rigidbody>();

        if (TargetTransform == null && Elevator != null)
        {
            TargetTransform = Elevator.transform;
        }

        if (TargetTransform == null)
        {
            TargetTransform = transform.parent;
        }

        RigidbodyComponent.isKinematic = true;
        RigidbodyComponent.useGravity = false;
        RigidbodyComponent.interpolation = RigidbodyInterpolation.Interpolate;
        RigidbodyComponent.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
    }

    /// <summary>
    /// Moves the kinematic rigidbody to the visible elevator pose during the physics step.
    /// </summary>
    private void FixedUpdate()
    {
        if (TargetTransform == null)
        {
            return;
        }

        RigidbodyComponent.MovePosition(TargetTransform.position);
        RigidbodyComponent.MoveRotation(TargetTransform.rotation);
    }
}