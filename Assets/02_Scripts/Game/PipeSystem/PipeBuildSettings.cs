using UnityEngine;

/// <summary>
/// Global configuration used by the cave pipe build, validation and transport systems.
/// Keep all clearance and pathfinding parameters centralized here so the graph bake,
/// runtime path search and final placement always use the same rules.
/// </summary>
[CreateAssetMenu(fileName = "PipeBuildSettings", menuName = "Game/Pipes/Pipe Build Settings")]
public sealed class PipeBuildSettings : ScriptableObject
{
    [Header("Pipe")]
    [Tooltip("Radius of the transported pipe or conveyor tube measured from its center line.")]
    [SerializeField] private float PipeRadius = 0.25f;

    [Tooltip("Extra inward offset from the cave wall to the pipe center line to avoid z-fighting and shallow clipping.")]
    [SerializeField] private float WallGap = 0.03f;

    [Tooltip("Maximum distance allowed between the player clicked wall point and the nearest graph node.")]
    [SerializeField] private float MaxNodeSelectionDistance = 1.5f;

    [Header("Elevator Exclusion")]
    [Tooltip("Extra clearance kept between the pipe outer surface and the configured elevator exclusion trigger volume.")]
    [SerializeField] private float ExclusionSurfacePadding = 0.35f;

    [Tooltip("Fallback physical radius of the central elevator volume used only when no exclusion trigger volume is assigned.")]
    [SerializeField] private float ElevatorRadius = 2f;

    [Tooltip("Additional fallback configurable margin added around the elevator radius when no exclusion trigger volume is assigned.")]
    [SerializeField] private float ElevatorSafetyMargin = 0.5f;

    [Header("Direction")]
    [Tooltip("Minimum vertical difference required so point A is considered meaningfully above point B.")]
    [SerializeField] private float MinimumRequiredDrop = 0.1f;

    [Tooltip("Small local upward allowance per edge to tolerate wall irregularities without allowing globally wrong paths.")]
    [SerializeField] private float MaxLocalUpStep = 0.18f;

    [Tooltip("Additional path cost applied per meter of local ascent.")]
    [SerializeField] private float AscentPenaltyPerMeter = 20f;

    [Header("Bake")]
    [Tooltip("Distance between vertical graph sampling rings along the cave axis.")]
    [SerializeField] private float VerticalSampleSpacing = 0.75f;

    [Tooltip("Number of angular samples generated around each ring.")]
    [SerializeField] private int AngularSamplesPerRing = 48;

    [Tooltip("Maximum outward distance used when probing the cave wall from the central axis.")]
    [SerializeField] private float MaxCaveRadius = 25f;

    [Tooltip("Step used while validating whether a graph edge remains attached to the cave wall.")]
    [SerializeField] private float EdgeValidationStep = 0.35f;

    [Tooltip("Maximum error allowed between the expected pipe offset from the wall and the validated sample along an edge.")]
    [SerializeField] private float MaxAllowedWallOffsetError = 0.25f;

    [Header("Visual")]
    [Tooltip("Target straight segment length used to build the visible modular representation.")]
    [SerializeField] private float VisualSegmentLength = 0.65f;

    [Tooltip("Small extra overlap applied to each visible segment so neighboring modules do not leave tiny gaps.")]
    [SerializeField] private float VisualSegmentOverlap = 0.025f;

    [Header("Transport")]
    [Tooltip("Default logical movement speed used by transported objects inside the pipe.")]
    [SerializeField] private float DefaultTransportSpeed = 2.5f;

    /// <summary>
    /// Gets the configured physical pipe radius.
    /// </summary>
    public float GetPipeRadius()
    {
        return Mathf.Max(0.01f, PipeRadius);
    }

    /// <summary>
    /// Gets the configured wall gap.
    /// </summary>
    public float GetWallGap()
    {
        return Mathf.Max(0f, WallGap);
    }

    /// <summary>
    /// Gets the full center line offset from the wall contact point.
    /// </summary>
    public float GetPipeCenterOffset()
    {
        return GetPipeRadius() + GetWallGap();
    }

    /// <summary>
    /// Gets the extra clearance kept from the elevator exclusion trigger surface.
    /// </summary>
    public float GetExclusionSurfacePadding()
    {
        return Mathf.Max(0f, ExclusionSurfacePadding);
    }

    /// <summary>
    /// Gets the minimum required center-line clearance from the elevator exclusion trigger surface.
    /// </summary>
    public float GetRequiredExclusionClearance()
    {
        return GetPipeRadius() + GetExclusionSurfacePadding();
    }

    /// <summary>
    /// Gets the fallback radius of the forbidden elevator exclusion zone including safety margin and pipe thickness.
    /// This is used only when no explicit exclusion trigger volume is assigned.
    /// </summary>
    public float GetFallbackMinimumAllowedAxisRadius()
    {
        return Mathf.Max(0f, ElevatorRadius) + Mathf.Max(0f, ElevatorSafetyMargin) + GetPipeRadius();
    }

    /// <summary>
    /// Compatibility accessor kept for older code paths.
    /// </summary>
    public float GetMinimumAllowedAxisRadius()
    {
        return GetFallbackMinimumAllowedAxisRadius();
    }

    /// <summary>
    /// Gets the maximum accepted distance between a clicked wall point and a graph node.
    /// </summary>
    public float GetMaxNodeSelectionDistance()
    {
        return Mathf.Max(0.05f, MaxNodeSelectionDistance);
    }

    /// <summary>
    /// Gets the minimum required global drop from A to B.
    /// </summary>
    public float GetMinimumRequiredDrop()
    {
        return Mathf.Max(0f, MinimumRequiredDrop);
    }

    /// <summary>
    /// Gets the small local upward tolerance used during path search.
    /// </summary>
    public float GetMaxLocalUpStep()
    {
        return Mathf.Max(0f, MaxLocalUpStep);
    }

    /// <summary>
    /// Gets the ascent penalty used by pathfinding.
    /// </summary>
    public float GetAscentPenaltyPerMeter()
    {
        return Mathf.Max(0f, AscentPenaltyPerMeter);
    }

    /// <summary>
    /// Gets the vertical ring spacing used while baking the graph.
    /// </summary>
    public float GetVerticalSampleSpacing()
    {
        return Mathf.Max(0.1f, VerticalSampleSpacing);
    }

    /// <summary>
    /// Gets the angular sample count used while baking the graph.
    /// </summary>
    public int GetAngularSamplesPerRing()
    {
        return Mathf.Max(8, AngularSamplesPerRing);
    }

    /// <summary>
    /// Gets the maximum cave probe radius.
    /// </summary>
    public float GetMaxCaveRadius()
    {
        return Mathf.Max(1f, MaxCaveRadius);
    }

    /// <summary>
    /// Gets the edge validation step size.
    /// </summary>
    public float GetEdgeValidationStep()
    {
        return Mathf.Max(0.05f, EdgeValidationStep);
    }

    /// <summary>
    /// Gets the maximum allowed edge offset error.
    /// </summary>
    public float GetMaxAllowedWallOffsetError()
    {
        return Mathf.Max(0.01f, MaxAllowedWallOffsetError);
    }

    /// <summary>
    /// Gets the desired visual straight segment length.
    /// </summary>
    public float GetVisualSegmentLength()
    {
        return Mathf.Max(0.1f, VisualSegmentLength);
    }

    /// <summary>
    /// Gets the overlap used between visible modules.
    /// </summary>
    public float GetVisualSegmentOverlap()
    {
        return Mathf.Max(0f, VisualSegmentOverlap);
    }

    /// <summary>
    /// Gets the default logical transport speed.
    /// </summary>
    public float GetDefaultTransportSpeed()
    {
        return Mathf.Max(0.01f, DefaultTransportSpeed);
    }
}
