using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Serialized graph baked over the interior cave wall.
/// Every node stores both the wall contact point and the inward-offset pipe center point.
/// Pathfinding runs on this graph instead of directly on raw cave triangles.
/// </summary>
[CreateAssetMenu(fileName = "PipeSurfaceGraph", menuName = "Game/Pipes/Pipe Surface Graph")]
public sealed class PipeSurfaceGraph : ScriptableObject
{
    /// <summary>
    /// Directed validated edge from one wall node to another.
    /// </summary>
    [Serializable]
    public sealed class PipeSurfaceEdge
    {
        [Tooltip("Target node index reached by this edge.")]
        public int ToNodeIndex;

        [Tooltip("Base geometric cost of travelling through this edge.")]
        public float Distance;

        [Tooltip("Signed vertical delta from the source node to the target node.")]
        public float VerticalDelta;
    }

    /// <summary>
    /// One sampled wall anchor used by pathfinding and final pipe placement.
    /// </summary>
    [Serializable]
    public sealed class PipeSurfaceNode
    {
        [Tooltip("Stable node index inside the graph array.")]
        public int NodeIndex;

        [Tooltip("Wall hit point sampled on the cave surface.")]
        public Vector3 SurfacePosition;

        [Tooltip("Inward-offset pipe center line point derived from the wall position.")]
        public Vector3 CenterPosition;

        [Tooltip("Original wall normal returned by the cave raycast.")]
        public Vector3 SurfaceNormal;

        [Tooltip("Inward support direction used to push the pipe away from the wall.")]
        public Vector3 SupportDirection;

        [Tooltip("Height along the central axis used during bake.")]
        public float AxisHeight;

        [Tooltip("Angular coordinate in degrees used during bake.")]
        public float PolarAngleDegrees;

        [Tooltip("Outgoing validated edges from this node.")]
        public List<PipeSurfaceEdge> Edges = new List<PipeSurfaceEdge>();
    }

    [Header("Metadata")]
    [Tooltip("Minimum sampled height along the central axis.")]
    [SerializeField] private float MinimumAxisHeight;

    [Tooltip("Maximum sampled height along the central axis.")]
    [SerializeField] private float MaximumAxisHeight;

    [Tooltip("Vertical spacing used when the graph was baked.")]
    [SerializeField] private float VerticalSpacing;

    [Tooltip("Angular sample count used when the graph was baked.")]
    [SerializeField] private int AngularSamples;

    [Header("Nodes")]
    [Tooltip("Baked interior wall nodes.")]
    [SerializeField] private List<PipeSurfaceNode> Nodes = new List<PipeSurfaceNode>();

    /// <summary>
    /// Gets the readonly baked nodes.
    /// </summary>
    public IReadOnlyList<PipeSurfaceNode> GetNodes()
    {
        return Nodes;
    }

    /// <summary>
    /// Gets one node by index or null when the index is invalid.
    /// </summary>
    public PipeSurfaceNode GetNode(int NodeIndex)
    {
        if (NodeIndex < 0 || NodeIndex >= Nodes.Count)
        {
            return null;
        }

        return Nodes[NodeIndex];
    }

    /// <summary>
    /// Replaces the graph content with freshly baked data.
    /// </summary>
    /// <param name="NewNodes">New baked nodes.</param>
    /// <param name="MinimumAxisHeightValue">Minimum baked axis height.</param>
    /// <param name="MaximumAxisHeightValue">Maximum baked axis height.</param>
    /// <param name="VerticalSpacingValue">Baked vertical spacing.</param>
    /// <param name="AngularSamplesValue">Baked angular sample count.</param>
    public void SetGraphData(
        List<PipeSurfaceNode> NewNodes,
        float MinimumAxisHeightValue,
        float MaximumAxisHeightValue,
        float VerticalSpacingValue,
        int AngularSamplesValue)
    {
        Nodes = NewNodes ?? new List<PipeSurfaceNode>();
        MinimumAxisHeight = MinimumAxisHeightValue;
        MaximumAxisHeight = MaximumAxisHeightValue;
        VerticalSpacing = VerticalSpacingValue;
        AngularSamples = AngularSamplesValue;
    }

    /// <summary>
    /// Finds the nearest node center point to a given world position within the allowed radius.
    /// This is intentionally linear because point picking is infrequent and the result must remain exact.
    /// </summary>
    /// <param name="WorldPoint">Target world point.</param>
    /// <param name="MaxDistance">Maximum accepted distance.</param>
    /// <returns>Nearest node index or -1 if no valid node was found.</returns>
    public int FindNearestNodeIndex(Vector3 WorldPoint, float MaxDistance)
    {
        float MaxDistanceSqr = Mathf.Max(0f, MaxDistance) * Mathf.Max(0f, MaxDistance);
        float BestDistanceSqr = float.MaxValue;
        int BestNodeIndex = -1;

        for (int Index = 0; Index < Nodes.Count; Index++)
        {
            PipeSurfaceNode Node = Nodes[Index];
            if (Node == null)
            {
                continue;
            }

            float DistanceSqr = (Node.CenterPosition - WorldPoint).sqrMagnitude;
            if (DistanceSqr > MaxDistanceSqr)
            {
                continue;
            }

            if (DistanceSqr < BestDistanceSqr)
            {
                BestDistanceSqr = DistanceSqr;
                BestNodeIndex = Index;
            }
        }

        return BestNodeIndex;
    }
}
