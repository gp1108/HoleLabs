using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bakes a wall-following graph for a roughly cylindrical cave around a central elevator axis.
/// The bake probes the real cave geometry with raycasts, builds inward-offset pipe nodes,
/// then validates neighbor connectivity so runtime pathfinding never works on imaginary free space.
/// </summary>
[ExecuteAlways]
public sealed class PipeSurfaceGraphBaker : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Central axis used as the elevator line and cylindrical sampling reference.")]
    [SerializeField] private Transform AxisTransform;

    [Tooltip("Optional trigger volume that defines the forbidden elevator space more accurately than a simple radius.")]
    [SerializeField] private PipeExclusionVolume ElevatorExclusionVolume;

    [Tooltip("Settings asset shared by baking, pathfinding and final placement.")]
    [SerializeField] private PipeBuildSettings BuildSettings;

    [Tooltip("Graph asset that will receive the baked wall nodes and edges.")]
    [SerializeField] private PipeSurfaceGraph GraphAsset;

    [Header("Height Range")]
    [Tooltip("Minimum sampled local height relative to the axis transform origin.")]
    [SerializeField] private float MinimumAxisHeight = -30f;

    [Tooltip("Maximum sampled local height relative to the axis transform origin.")]
    [SerializeField] private float MaximumAxisHeight = 30f;

    [Header("Collision")]
    [Tooltip("Layers considered part of the cave wall geometry.")]
    [SerializeField] private LayerMask CaveLayers = ~0;

    [Header("Debug")]
    [Tooltip("Draws baked nodes and validated edges in the Scene view.")]
    [SerializeField] private bool DrawGizmos = true;

    [Tooltip("Radius used to draw each baked node gizmo.")]
    [SerializeField] private float NodeGizmoRadius = 0.08f;

    [Tooltip("Logs bake statistics in the console.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Rebuilds the graph asset using the current scene geometry and bake settings.
    /// </summary>
    [ContextMenu("Bake Graph")]
    public void BakeGraph()
    {
        if (AxisTransform == null || BuildSettings == null || GraphAsset == null)
        {
            Debug.LogError("PipeSurfaceGraphBaker is missing a required reference.", this);
            return;
        }

        float VerticalSpacing = BuildSettings.GetVerticalSampleSpacing();
        int AngularSamples = BuildSettings.GetAngularSamplesPerRing();
        int RingCount = Mathf.Max(1, Mathf.RoundToInt((MaximumAxisHeight - MinimumAxisHeight) / VerticalSpacing) + 1);

        List<PipeSurfaceGraph.PipeSurfaceNode> BakedNodes = new List<PipeSurfaceGraph.PipeSurfaceNode>(RingCount * AngularSamples);
        int[,] NodeIndexByRingAndAngle = new int[RingCount, AngularSamples];

        for (int RingIndex = 0; RingIndex < RingCount; RingIndex++)
        {
            for (int AngleIndex = 0; AngleIndex < AngularSamples; AngleIndex++)
            {
                NodeIndexByRingAndAngle[RingIndex, AngleIndex] = -1;
            }
        }

        for (int RingIndex = 0; RingIndex < RingCount; RingIndex++)
        {
            float AxisHeight = MinimumAxisHeight + (RingIndex * VerticalSpacing);

            for (int AngleIndex = 0; AngleIndex < AngularSamples; AngleIndex++)
            {
                float PolarAngleDegrees = (360f / AngularSamples) * AngleIndex;

                if (!TrySampleNode(AxisHeight, PolarAngleDegrees, out PipeSurfaceGraph.PipeSurfaceNode Node))
                {
                    continue;
                }

                Node.NodeIndex = BakedNodes.Count;
                BakedNodes.Add(Node);
                NodeIndexByRingAndAngle[RingIndex, AngleIndex] = Node.NodeIndex;
            }
        }

        for (int RingIndex = 0; RingIndex < RingCount; RingIndex++)
        {
            for (int AngleIndex = 0; AngleIndex < AngularSamples; AngleIndex++)
            {
                int CurrentNodeIndex = NodeIndexByRingAndAngle[RingIndex, AngleIndex];
                if (CurrentNodeIndex < 0)
                {
                    continue;
                }

                TryConnectNodes(BakedNodes, CurrentNodeIndex, NodeIndexByRingAndAngle[RingIndex, (AngleIndex + 1) % AngularSamples]);

                if (RingIndex + 1 < RingCount)
                {
                    TryConnectNodes(BakedNodes, CurrentNodeIndex, NodeIndexByRingAndAngle[RingIndex + 1, AngleIndex]);
                    TryConnectNodes(BakedNodes, CurrentNodeIndex, NodeIndexByRingAndAngle[RingIndex + 1, (AngleIndex + 1) % AngularSamples]);
                    TryConnectNodes(BakedNodes, CurrentNodeIndex, NodeIndexByRingAndAngle[RingIndex + 1, (AngleIndex - 1 + AngularSamples) % AngularSamples]);
                }
            }
        }

        GraphAsset.SetGraphData(BakedNodes, MinimumAxisHeight, MaximumAxisHeight, VerticalSpacing, AngularSamples);

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(GraphAsset);
#endif

        Log("Bake completed. Nodes=" + BakedNodes.Count);
    }

    /// <summary>
    /// Tries to sample one cave wall node at the provided axis height and polar angle.
    /// </summary>
    /// <param name="AxisHeight">Height along the central axis.</param>
    /// <param name="PolarAngleDegrees">Angular coordinate around the axis.</param>
    /// <param name="Node">Resulting baked node.</param>
    /// <returns>True when a valid node was sampled.</returns>
    private bool TrySampleNode(float AxisHeight, float PolarAngleDegrees, out PipeSurfaceGraph.PipeSurfaceNode Node)
    {
        Node = null;

        Vector3 AxisOriginAtHeight = AxisTransform.position + (AxisTransform.up * AxisHeight);
        Quaternion AngularRotation = Quaternion.AngleAxis(PolarAngleDegrees, AxisTransform.up);
        Vector3 ProbeDirection = (AngularRotation * AxisTransform.right).normalized;

        if (!Physics.Raycast(
                AxisOriginAtHeight,
                ProbeDirection,
                out RaycastHit HitInfo,
                BuildSettings.GetMaxCaveRadius(),
                CaveLayers,
                QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        Vector3 SupportDirection = PipeAxisUtility.GetInwardDirectionToAxis(AxisTransform, HitInfo.point);
        Vector3 CenterPosition = HitInfo.point + (SupportDirection * BuildSettings.GetPipeCenterOffset());

        if (IsElevatorSpaceBlocked(CenterPosition))
        {
            return false;
        }

        if (Physics.CheckSphere(CenterPosition, BuildSettings.GetPipeRadius(), CaveLayers, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        Node = new PipeSurfaceGraph.PipeSurfaceNode
        {
            SurfacePosition = HitInfo.point,
            CenterPosition = CenterPosition,
            SurfaceNormal = HitInfo.normal,
            SupportDirection = SupportDirection,
            AxisHeight = AxisHeight,
            PolarAngleDegrees = PolarAngleDegrees
        };

        return true;
    }

    /// <summary>
    /// Tries to add a symmetric validated edge between two nodes.
    /// </summary>
    private void TryConnectNodes(List<PipeSurfaceGraph.PipeSurfaceNode> Nodes, int FromNodeIndex, int ToNodeIndex)
    {
        if (FromNodeIndex < 0 || ToNodeIndex < 0 || FromNodeIndex == ToNodeIndex)
        {
            return;
        }

        PipeSurfaceGraph.PipeSurfaceNode FromNode = Nodes[FromNodeIndex];
        PipeSurfaceGraph.PipeSurfaceNode ToNode = Nodes[ToNodeIndex];

        if (FromNode == null || ToNode == null)
        {
            return;
        }

        if (!ValidateCenterLineEdge(FromNode.CenterPosition, ToNode.CenterPosition))
        {
            return;
        }

        AddDirectedEdgeIfMissing(FromNode, ToNode);
        AddDirectedEdgeIfMissing(ToNode, FromNode);
    }

    /// <summary>
    /// Validates that the straight center line between two neighboring nodes remains attached to the wall,
    /// remains outside the elevator exclusion volume and does not clip into cave geometry.
    /// </summary>
    private bool ValidateCenterLineEdge(Vector3 FromCenter, Vector3 ToCenter)
    {
        float Distance = Vector3.Distance(FromCenter, ToCenter);
        int SampleCount = Mathf.Max(2, Mathf.CeilToInt(Distance / BuildSettings.GetEdgeValidationStep()));

        for (int SampleIndex = 0; SampleIndex <= SampleCount; SampleIndex++)
        {
            float T = SampleCount <= 0 ? 0f : (float)SampleIndex / SampleCount;
            Vector3 SampleCenter = Vector3.Lerp(FromCenter, ToCenter, T);

            if (IsElevatorSpaceBlocked(SampleCenter))
            {
                return false;
            }

            Vector3 AxisPoint = PipeAxisUtility.GetClosestPointOnAxis(AxisTransform, SampleCenter);
            Vector3 RadialDirection = PipeAxisUtility.GetRadialDirectionFromAxis(AxisTransform, SampleCenter);

            if (!Physics.Raycast(
                    AxisPoint,
                    RadialDirection,
                    out RaycastHit HitInfo,
                    BuildSettings.GetMaxCaveRadius(),
                    CaveLayers,
                    QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            float ExpectedOffset = BuildSettings.GetPipeCenterOffset();
            float ActualOffset = HitInfo.distance - Vector3.Distance(AxisPoint, SampleCenter);
            float OffsetError = Mathf.Abs(ExpectedOffset - ActualOffset);

            if (OffsetError > BuildSettings.GetMaxAllowedWallOffsetError())
            {
                return false;
            }

            if (Physics.CheckSphere(SampleCenter, BuildSettings.GetPipeRadius(), CaveLayers, QueryTriggerInteraction.Ignore))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns true when the provided pipe center point is blocked by the configured elevator exclusion shape.
    /// Falls back to the original axis-radius check when no trigger volume is assigned.
    /// </summary>
    private bool IsElevatorSpaceBlocked(Vector3 PipeCenterPoint)
    {
        if (ElevatorExclusionVolume != null && ElevatorExclusionVolume.IsConfigured())
        {
            return ElevatorExclusionVolume.IsPointBlocked(PipeCenterPoint, BuildSettings.GetRequiredExclusionClearance());
        }

        return PipeAxisUtility.GetDistanceToAxis(AxisTransform, PipeCenterPoint) < BuildSettings.GetFallbackMinimumAllowedAxisRadius();
    }

    /// <summary>
    /// Adds one directed edge if it does not already exist.
    /// </summary>
    private void AddDirectedEdgeIfMissing(PipeSurfaceGraph.PipeSurfaceNode FromNode, PipeSurfaceGraph.PipeSurfaceNode ToNode)
    {
        for (int EdgeIndex = 0; EdgeIndex < FromNode.Edges.Count; EdgeIndex++)
        {
            if (FromNode.Edges[EdgeIndex].ToNodeIndex == ToNode.NodeIndex)
            {
                return;
            }
        }

        Vector3 Delta = ToNode.CenterPosition - FromNode.CenterPosition;

        FromNode.Edges.Add(new PipeSurfaceGraph.PipeSurfaceEdge
        {
            ToNodeIndex = ToNode.NodeIndex,
            Distance = Delta.magnitude,
            VerticalDelta = Delta.y
        });
    }

    /// <summary>
    /// Draws baked node and edge gizmos.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!DrawGizmos || GraphAsset == null)
        {
            return;
        }

        IReadOnlyList<PipeSurfaceGraph.PipeSurfaceNode> Nodes = GraphAsset.GetNodes();
        if (Nodes == null)
        {
            return;
        }

        Gizmos.color = Color.cyan;

        for (int NodeIndex = 0; NodeIndex < Nodes.Count; NodeIndex++)
        {
            PipeSurfaceGraph.PipeSurfaceNode Node = Nodes[NodeIndex];
            if (Node == null)
            {
                continue;
            }

            Gizmos.DrawSphere(Node.CenterPosition, NodeGizmoRadius);

            for (int EdgeIndex = 0; EdgeIndex < Node.Edges.Count; EdgeIndex++)
            {
                PipeSurfaceGraph.PipeSurfaceEdge Edge = Node.Edges[EdgeIndex];
                PipeSurfaceGraph.PipeSurfaceNode NeighborNode = GraphAsset.GetNode(Edge.ToNodeIndex);

                if (NeighborNode == null || NeighborNode.NodeIndex <= Node.NodeIndex)
                {
                    continue;
                }

                Gizmos.DrawLine(Node.CenterPosition, NeighborNode.CenterPosition);
            }
        }
    }

    /// <summary>
    /// Writes baker-specific logs when enabled.
    /// </summary>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[PipeSurfaceGraphBaker] " + Message, this);
    }
}
