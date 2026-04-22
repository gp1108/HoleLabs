using UnityEngine;

/// <summary>
/// Runtime pipe endpoint used by future machine connectors and transport logic.
/// Each built pipe owns one input port and one output port with explicit flow direction.
/// </summary>
[DisallowMultipleComponent]
public sealed class PipePort : MonoBehaviour
{
    /// <summary>
    /// Identifies whether the port receives or emits flow.
    /// </summary>
    public enum PipePortRole
    {
        Input = 0,
        Output = 1
    }

    [Header("State")]
    [Tooltip("Whether this port is the logical input or output of the pipe.")]
    [SerializeField] private PipePortRole Role = PipePortRole.Input;

    [Tooltip("Direction of flow at this port in world space.")]
    [SerializeField] private Vector3 FlowDirection = Vector3.forward;

    [Tooltip("Pipe instance that owns this port.")]
    [SerializeField] private PipePathInstance OwnerPipe;

    /// <summary>
    /// Gets the port role.
    /// </summary>
    public PipePortRole GetRole()
    {
        return Role;
    }

    /// <summary>
    /// Gets the owning pipe instance.
    /// </summary>
    public PipePathInstance GetOwnerPipe()
    {
        return OwnerPipe;
    }

    /// <summary>
    /// Gets the world flow direction.
    /// </summary>
    public Vector3 GetFlowDirection()
    {
        return FlowDirection.sqrMagnitude > 0.000001f ? FlowDirection.normalized : transform.forward;
    }

    /// <summary>
    /// Updates the role, owner and transform of this port.
    /// </summary>
    /// <param name="RoleValue">Logical port role.</param>
    /// <param name="OwnerPipeValue">Owning pipe instance.</param>
    /// <param name="WorldPosition">World position of the port.</param>
    /// <param name="WorldFlowDirection">World flow direction.</param>
    public void Configure(
        PipePortRole RoleValue,
        PipePathInstance OwnerPipeValue,
        Vector3 WorldPosition,
        Vector3 WorldFlowDirection)
    {
        Role = RoleValue;
        OwnerPipe = OwnerPipeValue;
        FlowDirection = WorldFlowDirection.sqrMagnitude > 0.000001f ? WorldFlowDirection.normalized : Vector3.forward;

        transform.position = WorldPosition;
        transform.rotation = Quaternion.LookRotation(GetFlowDirection(), Vector3.up);
    }
}
