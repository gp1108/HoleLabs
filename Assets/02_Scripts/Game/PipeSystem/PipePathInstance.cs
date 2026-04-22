using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime built pipe instance.
/// It stores the resolved path, exposes sampling for transport systems,
/// owns the logical input and output ports, and optionally builds modular visual segments.
/// </summary>
public sealed class PipePathInstance : MonoBehaviour
{
    /// <summary>
    /// Determines how visible pipe segments are generated.
    /// </summary>
    private enum PipeVisualMode
    {
        SegmentPrefab = 0,
        PrimitiveCylinderFallback = 1
    }

    /// <summary>
    /// Declares the local forward axis used by the configured straight segment prefab.
    /// </summary>
    private enum SegmentForwardAxis
    {
        PositiveZ = 0,
        PositiveY = 1
    }

    [Header("References")]
    [Tooltip("Optional straight segment prefab used when Visual Mode is set to Segment Prefab.")]
    [SerializeField] private GameObject SegmentPrefab;

    [Tooltip("Optional parent used to store generated visual segments.")]
    [SerializeField] private Transform SegmentRoot;

    [Tooltip("Optional input port component. It will be created automatically if missing.")]
    [SerializeField] private PipePort InputPort;

    [Tooltip("Optional output port component. It will be created automatically if missing.")]
    [SerializeField] private PipePort OutputPort;

    [Header("Visual")]
    [Tooltip("If true, modular visual segments are generated from the current path.")]
    [SerializeField] private bool BuildVisuals = true;

    [Tooltip("Visual generation mode. Primitive Cylinder Fallback is the easiest way to see the pipe immediately without a custom prefab.")]
    [SerializeField] private PipeVisualMode VisualMode = PipeVisualMode.PrimitiveCylinderFallback;

    [Tooltip("Forward axis expected by the straight segment prefab. Use Positive Z for most custom game-ready meshes and Positive Y for Unity cylinders or DCC exports aligned vertically.")]
    [SerializeField] private SegmentForwardAxis PrefabForwardAxis = SegmentForwardAxis.PositiveZ;

    [Tooltip("Optional material applied to generated primitive fallback cylinders.")]
    [SerializeField] private Material PrimitiveFallbackMaterial;

    [Tooltip("If true, colliders on generated primitive fallback cylinders are removed immediately.")]
    [SerializeField] private bool RemovePrimitiveFallbackColliders = true;

    [Tooltip("Multiplier applied to the physical pipe diameter when generating primitive fallback visuals.")]
    [SerializeField] private float PrimitiveFallbackDiameterMultiplier = 1f;

    [Tooltip("If true, control points are drawn with gizmos for debugging.")]
    [SerializeField] private bool DrawDebugGizmos = true;

    [Tooltip("Gizmo radius used to draw each cached control point.")]
    [SerializeField] private float GizmoPointRadius = 0.08f;

    [Header("Runtime Path")]
    [Tooltip("Resolved pipe center control points.")]
    [SerializeField] private List<Vector3> ControlPoints = new List<Vector3>();

    [Tooltip("Resolved support directions aligned with each control point.")]
    [SerializeField] private List<Vector3> SupportDirections = new List<Vector3>();

    [Tooltip("Cumulative distances used by runtime path sampling.")]
    [SerializeField] private List<float> CumulativeDistances = new List<float>();

    [Tooltip("Total path length in meters.")]
    [SerializeField] private float TotalLength;

    [Tooltip("Settings used to build this instance.")]
    [SerializeField] private PipeBuildSettings BuildSettings;

    /// <summary>
    /// Gets the total center-line path length.
    /// </summary>
    public float GetTotalLength()
    {
        return Mathf.Max(0f, TotalLength);
    }

    /// <summary>
    /// Gets the input port.
    /// </summary>
    public PipePort GetInputPort()
    {
        return InputPort;
    }

    /// <summary>
    /// Gets the output port.
    /// </summary>
    public PipePort GetOutputPort()
    {
        return OutputPort;
    }

    /// <summary>
    /// Gets the cached control points.
    /// </summary>
    public IReadOnlyList<Vector3> GetControlPoints()
    {
        return ControlPoints;
    }

    /// <summary>
    /// Initializes this pipe with the provided resolved path data.
    /// </summary>
    public void Initialize(
        List<Vector3> CenterPoints,
        List<Vector3> SupportDirectionPoints,
        PipeBuildSettings Settings)
    {
        BuildSettings = Settings;
        ControlPoints = CenterPoints != null ? new List<Vector3>(CenterPoints) : new List<Vector3>();
        SupportDirections = SupportDirectionPoints != null ? new List<Vector3>(SupportDirectionPoints) : new List<Vector3>();

        if (SupportDirections.Count != ControlPoints.Count)
        {
            SupportDirections.Clear();
            for (int Index = 0; Index < ControlPoints.Count; Index++)
            {
                SupportDirections.Add(Vector3.up);
            }
        }

        RebuildDistanceCache();
        EnsureRuntimeChildren();
        UpdatePorts();

        if (BuildVisuals)
        {
            RebuildVisualSegments();
        }
    }

    /// <summary>
    /// Samples a world position along the cached center line using traveled distance.
    /// </summary>
    public Vector3 SamplePosition(float Distance)
    {
        if (ControlPoints.Count == 0)
        {
            return transform.position;
        }

        if (ControlPoints.Count == 1)
        {
            return ControlPoints[0];
        }

        float ClampedDistance = Mathf.Clamp(Distance, 0f, TotalLength);
        int SegmentIndex = FindSegmentIndex(ClampedDistance);

        if (SegmentIndex >= ControlPoints.Count - 1)
        {
            return ControlPoints[ControlPoints.Count - 1];
        }

        float SegmentStartDistance = CumulativeDistances[SegmentIndex];
        float SegmentEndDistance = CumulativeDistances[SegmentIndex + 1];
        float SegmentLength = Mathf.Max(0.0001f, SegmentEndDistance - SegmentStartDistance);
        float T = Mathf.Clamp01((ClampedDistance - SegmentStartDistance) / SegmentLength);

        return Vector3.Lerp(ControlPoints[SegmentIndex], ControlPoints[SegmentIndex + 1], T);
    }

    /// <summary>
    /// Samples the travel direction along the center line using traveled distance.
    /// </summary>
    public Vector3 SampleTangent(float Distance)
    {
        if (ControlPoints.Count <= 1)
        {
            return transform.forward;
        }

        float ClampedDistance = Mathf.Clamp(Distance, 0f, TotalLength);
        int SegmentIndex = FindSegmentIndex(ClampedDistance);
        int NextIndex = Mathf.Min(ControlPoints.Count - 1, SegmentIndex + 1);
        Vector3 Tangent = ControlPoints[NextIndex] - ControlPoints[SegmentIndex];
        return Tangent.sqrMagnitude > 0.000001f ? Tangent.normalized : transform.forward;
    }

    /// <summary>
    /// Samples the support direction along the center line using traveled distance.
    /// </summary>
    public Vector3 SampleSupportDirection(float Distance)
    {
        if (SupportDirections.Count == 0)
        {
            return Vector3.up;
        }

        if (SupportDirections.Count == 1)
        {
            return SupportDirections[0];
        }

        float ClampedDistance = Mathf.Clamp(Distance, 0f, TotalLength);
        int SegmentIndex = FindSegmentIndex(ClampedDistance);
        int NextIndex = Mathf.Min(SupportDirections.Count - 1, SegmentIndex + 1);

        float SegmentStartDistance = CumulativeDistances[SegmentIndex];
        float SegmentEndDistance = CumulativeDistances[NextIndex];
        float SegmentLength = Mathf.Max(0.0001f, SegmentEndDistance - SegmentStartDistance);
        float T = Mathf.Clamp01((ClampedDistance - SegmentStartDistance) / SegmentLength);

        Vector3 Support = Vector3.Slerp(SupportDirections[SegmentIndex], SupportDirections[NextIndex], T);
        return Support.sqrMagnitude > 0.000001f ? Support.normalized : Vector3.up;
    }

    /// <summary>
    /// Rebuilds cumulative distances used for runtime sampling.
    /// </summary>
    private void RebuildDistanceCache()
    {
        CumulativeDistances.Clear();
        TotalLength = 0f;

        if (ControlPoints.Count == 0)
        {
            return;
        }

        CumulativeDistances.Add(0f);

        for (int Index = 1; Index < ControlPoints.Count; Index++)
        {
            TotalLength += Vector3.Distance(ControlPoints[Index - 1], ControlPoints[Index]);
            CumulativeDistances.Add(TotalLength);
        }
    }

    /// <summary>
    /// Ensures segment root and endpoint ports exist.
    /// </summary>
    private void EnsureRuntimeChildren()
    {
        if (SegmentRoot == null)
        {
            GameObject SegmentRootObject = new GameObject("Segments");
            SegmentRoot = SegmentRootObject.transform;
            SegmentRoot.SetParent(transform, false);
        }

        if (InputPort == null)
        {
            GameObject InputPortObject = new GameObject("InputPort");
            InputPortObject.transform.SetParent(transform, false);
            InputPort = InputPortObject.AddComponent<PipePort>();
        }

        if (OutputPort == null)
        {
            GameObject OutputPortObject = new GameObject("OutputPort");
            OutputPortObject.transform.SetParent(transform, false);
            OutputPort = OutputPortObject.AddComponent<PipePort>();
        }
    }

    /// <summary>
    /// Updates logical input and output ports from the cached path.
    /// </summary>
    private void UpdatePorts()
    {
        if (ControlPoints.Count < 2 || InputPort == null || OutputPort == null)
        {
            return;
        }

        Vector3 InputDirection = (ControlPoints[1] - ControlPoints[0]).normalized;
        Vector3 OutputDirection = (ControlPoints[ControlPoints.Count - 1] - ControlPoints[ControlPoints.Count - 2]).normalized;

        InputPort.Configure(PipePort.PipePortRole.Input, this, ControlPoints[0], InputDirection);
        OutputPort.Configure(PipePort.PipePortRole.Output, this, ControlPoints[ControlPoints.Count - 1], OutputDirection);
    }

    /// <summary>
    /// Rebuilds modular visual segments from the resolved path.
    /// </summary>
    [ContextMenu("Rebuild Visual Segments")]
    private void RebuildVisualSegments()
    {
        if (SegmentRoot == null)
        {
            return;
        }

        ClearExistingSegments();

        if (ControlPoints.Count < 2)
        {
            return;
        }

        List<float> SampleDistances = BuildVisualSampleDistances();
        for (int SampleIndex = 1; SampleIndex < SampleDistances.Count; SampleIndex++)
        {
            float StartDistance = SampleDistances[SampleIndex - 1];
            float EndDistance = SampleDistances[SampleIndex];

            Vector3 StartPoint = SamplePosition(StartDistance);
            Vector3 EndPoint = SamplePosition(EndDistance);
            Vector3 Direction = EndPoint - StartPoint;
            float Length = Direction.magnitude;

            if (Length <= 0.0001f)
            {
                continue;
            }

            Vector3 MidPoint = (StartPoint + EndPoint) * 0.5f;
            Vector3 Support = SampleSupportDirection((StartDistance + EndDistance) * 0.5f);
            Vector3 Up = PipeAxisUtility.BuildFrameUp(Direction, Support);
            float FinalLength = Length + (BuildSettings != null ? BuildSettings.GetVisualSegmentOverlap() : 0f);

            BuildOneVisualSegment(MidPoint, Direction.normalized, Up, FinalLength);
        }
    }

    /// <summary>
    /// Creates one visible pipe segment using the configured visual mode.
    /// </summary>
    private void BuildOneVisualSegment(Vector3 MidPoint, Vector3 Direction, Vector3 Up, float FinalLength)
    {
        bool UsePrimitiveFallback = VisualMode == PipeVisualMode.PrimitiveCylinderFallback || SegmentPrefab == null;

        if (UsePrimitiveFallback)
        {
            CreatePrimitiveCylinderSegment(MidPoint, Direction, FinalLength);
            return;
        }

        Quaternion Rotation = BuildPrefabRotation(Direction, Up);
        GameObject SegmentObject = Instantiate(SegmentPrefab, MidPoint, Rotation, SegmentRoot);
        Vector3 LocalScale = SegmentObject.transform.localScale;

        if (PrefabForwardAxis == SegmentForwardAxis.PositiveY)
        {
            SegmentObject.transform.localScale = new Vector3(LocalScale.x, FinalLength, LocalScale.z);
        }
        else
        {
            SegmentObject.transform.localScale = new Vector3(LocalScale.x, LocalScale.y, FinalLength);
        }
    }

    /// <summary>
    /// Creates one primitive cylinder fallback segment.
    /// </summary>
    private void CreatePrimitiveCylinderSegment(Vector3 MidPoint, Vector3 Direction, float FinalLength)
    {
        GameObject SegmentObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        SegmentObject.name = "PipeSegment_Fallback";
        SegmentObject.transform.SetParent(SegmentRoot, false);
        SegmentObject.transform.SetPositionAndRotation(MidPoint, Quaternion.FromToRotation(Vector3.up, Direction));

        float PipeDiameter = BuildSettings != null
            ? BuildSettings.GetPipeRadius() * 2f * Mathf.Max(0.01f, PrimitiveFallbackDiameterMultiplier)
            : 0.5f;

        SegmentObject.transform.localScale = new Vector3(PipeDiameter, FinalLength * 0.5f, PipeDiameter);

        if (RemovePrimitiveFallbackColliders)
        {
            Collider SegmentCollider = SegmentObject.GetComponent<Collider>();
            if (SegmentCollider != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    DestroyImmediate(SegmentCollider);
                }
                else
#endif
                {
                    Destroy(SegmentCollider);
                }
            }
        }

        if (PrimitiveFallbackMaterial != null)
        {
            MeshRenderer Renderer = SegmentObject.GetComponent<MeshRenderer>();
            if (Renderer != null)
            {
                Renderer.sharedMaterial = PrimitiveFallbackMaterial;
            }
        }
    }

    /// <summary>
    /// Builds the world rotation for a straight segment prefab according to its expected local forward axis.
    /// </summary>
    private Quaternion BuildPrefabRotation(Vector3 Direction, Vector3 Up)
    {
        if (PrefabForwardAxis == SegmentForwardAxis.PositiveY)
        {
            return Quaternion.FromToRotation(Vector3.up, Direction);
        }

        return Quaternion.LookRotation(Direction, Up);
    }

    /// <summary>
    /// Returns evenly distributed distances used to place visual modules.
    /// </summary>
    private List<float> BuildVisualSampleDistances()
    {
        List<float> Distances = new List<float>();
        Distances.Add(0f);

        if (TotalLength <= 0.0001f)
        {
            return Distances;
        }

        float SegmentLength = BuildSettings != null ? BuildSettings.GetVisualSegmentLength() : 0.65f;
        float CurrentDistance = SegmentLength;

        while (CurrentDistance < TotalLength)
        {
            Distances.Add(CurrentDistance);
            CurrentDistance += SegmentLength;
        }

        Distances.Add(TotalLength);
        return Distances;
    }

    /// <summary>
    /// Removes every previously generated visual segment.
    /// </summary>
    private void ClearExistingSegments()
    {
        if (SegmentRoot == null)
        {
            return;
        }

        for (int ChildIndex = SegmentRoot.childCount - 1; ChildIndex >= 0; ChildIndex--)
        {
            Transform Child = SegmentRoot.GetChild(ChildIndex);

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                DestroyImmediate(Child.gameObject);
                continue;
            }
#endif

            Destroy(Child.gameObject);
        }
    }

    /// <summary>
    /// Finds the polyline segment containing the provided traveled distance.
    /// </summary>
    private int FindSegmentIndex(float Distance)
    {
        for (int Index = 0; Index < CumulativeDistances.Count - 1; Index++)
        {
            if (Distance <= CumulativeDistances[Index + 1])
            {
                return Index;
            }
        }

        return Mathf.Max(0, CumulativeDistances.Count - 2);
    }



/// <summary>
/// Applies the provided shared material to every renderer generated under this pipe instance.
/// This is mainly used by ghost previews so the final built geometry and the preview share the same shape.
/// </summary>
/// <param name="MaterialOverride">Shared material applied to every renderer. Passing null keeps the current materials unchanged.</param>
public void ApplyMaterialOverride(Material MaterialOverride)
{
    if (MaterialOverride == null)
    {
        return;
    }

    Renderer[] Renderers = GetComponentsInChildren<Renderer>(true);

    for (int RendererIndex = 0; RendererIndex < Renderers.Length; RendererIndex++)
    {
        if (Renderers[RendererIndex] == null)
        {
            continue;
        }

        Material[] SharedMaterials = Renderers[RendererIndex].sharedMaterials;
        if (SharedMaterials == null || SharedMaterials.Length == 0)
        {
            SharedMaterials = new Material[1];
        }

        for (int MaterialIndex = 0; MaterialIndex < SharedMaterials.Length; MaterialIndex++)
        {
            SharedMaterials[MaterialIndex] = MaterialOverride;
        }

        Renderers[RendererIndex].sharedMaterials = SharedMaterials;
    }
}

/// <summary>
/// Enables or disables every collider found in the pipe hierarchy.
/// This is useful for ghost previews that should never affect physics or clicks.
/// </summary>
/// <param name="IsEnabled">True to enable colliders, false to disable them.</param>
public void SetCollidersEnabled(bool IsEnabled)
{
    Collider[] Colliders = GetComponentsInChildren<Collider>(true);

    for (int Index = 0; Index < Colliders.Length; Index++)
    {
        if (Colliders[Index] == null)
        {
            continue;
        }

        Colliders[Index].enabled = IsEnabled;
    }
}

    /// <summary>
    /// Draws the cached polyline for debugging.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!DrawDebugGizmos || ControlPoints == null || ControlPoints.Count == 0)
        {
            return;
        }

        Gizmos.color = Color.green;

        for (int Index = 0; Index < ControlPoints.Count; Index++)
        {
            Gizmos.DrawSphere(ControlPoints[Index], GizmoPointRadius);

            if (Index < ControlPoints.Count - 1)
            {
                Gizmos.DrawLine(ControlPoints[Index], ControlPoints[Index + 1]);
            }
        }
    }
}
