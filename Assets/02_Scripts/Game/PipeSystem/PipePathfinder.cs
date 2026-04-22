using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Descending-biased A* solver that works on the baked cave wall graph.
/// It strongly penalizes ascent, blocks edges that climb too much locally and returns
/// a center-line path ready to instantiate or to transport payloads through.
/// </summary>
public static class PipePathfinder
{
    /// <summary>
    /// Final path result returned by the pathfinder.
    /// </summary>
    public sealed class PipePathResult
    {
        /// <summary>
        /// Whether a valid path was found.
        /// </summary>
        public bool WasFound;

        /// <summary>
        /// Optional failure reason when no path was found.
        /// </summary>
        public string FailureReason;

        /// <summary>
        /// Ordered node indices that define the resolved path.
        /// </summary>
        public readonly List<int> NodeIndices = new List<int>();

        /// <summary>
        /// Ordered inward-offset center points used by the final pipe instance.
        /// </summary>
        public readonly List<Vector3> CenterPoints = new List<Vector3>();

        /// <summary>
        /// Ordered support directions used by the final pipe instance.
        /// </summary>
        public readonly List<Vector3> SupportDirections = new List<Vector3>();
    }

    /// <summary>
    /// Solves a descending-biased path between two baked graph nodes.
    /// </summary>
    /// <param name="Graph">Baked cave wall graph.</param>
    /// <param name="Settings">Shared pipe build settings.</param>
    /// <param name="StartNodeIndex">Start node index corresponding to point A.</param>
    /// <param name="GoalNodeIndex">Goal node index corresponding to point B.</param>
    /// <returns>Resolved path result.</returns>
    public static PipePathResult CalculatePath(
        PipeSurfaceGraph Graph,
        PipeBuildSettings Settings,
        int StartNodeIndex,
        int GoalNodeIndex)
    {
        PipePathResult Result = new PipePathResult();

        if (Graph == null || Settings == null)
        {
            Result.FailureReason = "Missing graph or settings.";
            return Result;
        }

        PipeSurfaceGraph.PipeSurfaceNode StartNode = Graph.GetNode(StartNodeIndex);
        PipeSurfaceGraph.PipeSurfaceNode GoalNode = Graph.GetNode(GoalNodeIndex);

        if (StartNode == null || GoalNode == null)
        {
            Result.FailureReason = "Start or goal node is invalid.";
            return Result;
        }

        if (GoalNode.CenterPosition.y >= StartNode.CenterPosition.y - Settings.GetMinimumRequiredDrop())
        {
            Result.FailureReason = "Point B must be below point A for a descending flow.";
            return Result;
        }

        IReadOnlyList<PipeSurfaceGraph.PipeSurfaceNode> Nodes = Graph.GetNodes();
        int NodeCount = Nodes.Count;

        float[] GScore = new float[NodeCount];
        float[] FScore = new float[NodeCount];
        int[] CameFrom = new int[NodeCount];
        bool[] Closed = new bool[NodeCount];
        bool[] OpenFlags = new bool[NodeCount];

        for (int Index = 0; Index < NodeCount; Index++)
        {
            GScore[Index] = float.PositiveInfinity;
            FScore[Index] = float.PositiveInfinity;
            CameFrom[Index] = -1;
        }

        MinHeap OpenHeap = new MinHeap(NodeCount);
        GScore[StartNodeIndex] = 0f;
        FScore[StartNodeIndex] = EstimateHeuristic(StartNode, GoalNode, Settings);
        OpenHeap.Push(StartNodeIndex, FScore[StartNodeIndex]);
        OpenFlags[StartNodeIndex] = true;

        while (OpenHeap.Count > 0)
        {
            int CurrentNodeIndex = OpenHeap.Pop();
            if (CurrentNodeIndex < 0)
            {
                break;
            }

            if (Closed[CurrentNodeIndex])
            {
                continue;
            }

            OpenFlags[CurrentNodeIndex] = false;
            Closed[CurrentNodeIndex] = true;

            if (CurrentNodeIndex == GoalNodeIndex)
            {
                ReconstructPath(Graph, CameFrom, GoalNodeIndex, Result);
                Result.WasFound = Result.CenterPoints.Count >= 2;
                if (!Result.WasFound)
                {
                    Result.FailureReason = "Resolved path is unexpectedly empty.";
                }

                return Result;
            }

            PipeSurfaceGraph.PipeSurfaceNode CurrentNode = Nodes[CurrentNodeIndex];
            if (CurrentNode == null)
            {
                continue;
            }

            for (int EdgeIndex = 0; EdgeIndex < CurrentNode.Edges.Count; EdgeIndex++)
            {
                PipeSurfaceGraph.PipeSurfaceEdge Edge = CurrentNode.Edges[EdgeIndex];
                int NeighborIndex = Edge.ToNodeIndex;

                if (NeighborIndex < 0 || NeighborIndex >= NodeCount || Closed[NeighborIndex])
                {
                    continue;
                }

                if (Edge.VerticalDelta > Settings.GetMaxLocalUpStep())
                {
                    continue;
                }

                float AscentPenalty = Mathf.Max(0f, Edge.VerticalDelta) * Settings.GetAscentPenaltyPerMeter();
                float TentativeGScore = GScore[CurrentNodeIndex] + Edge.Distance + AscentPenalty;

                if (TentativeGScore >= GScore[NeighborIndex])
                {
                    continue;
                }

                CameFrom[NeighborIndex] = CurrentNodeIndex;
                GScore[NeighborIndex] = TentativeGScore;

                PipeSurfaceGraph.PipeSurfaceNode NeighborNode = Nodes[NeighborIndex];
                FScore[NeighborIndex] = TentativeGScore + EstimateHeuristic(NeighborNode, GoalNode, Settings);

                OpenHeap.Push(NeighborIndex, FScore[NeighborIndex]);
                OpenFlags[NeighborIndex] = true;
            }
        }

        Result.FailureReason = "No valid wall-following descending path was found.";
        return Result;
    }

    /// <summary>
    /// Estimates the remaining path cost from the current node to the goal.
    /// </summary>
    /// <param name="CurrentNode">Current node.</param>
    /// <param name="GoalNode">Goal node.</param>
    /// <param name="Settings">Shared pipe build settings.</param>
    /// <returns>Heuristic cost estimate.</returns>
    private static float EstimateHeuristic(
        PipeSurfaceGraph.PipeSurfaceNode CurrentNode,
        PipeSurfaceGraph.PipeSurfaceNode GoalNode,
        PipeBuildSettings Settings)
    {
        if (CurrentNode == null || GoalNode == null || Settings == null)
        {
            return 0f;
        }

        float BaseDistance = Vector3.Distance(CurrentNode.CenterPosition, GoalNode.CenterPosition);
        float ExtraAscent = Mathf.Max(0f, GoalNode.CenterPosition.y - CurrentNode.CenterPosition.y);
        return BaseDistance + (ExtraAscent * Settings.GetAscentPenaltyPerMeter());
    }

    /// <summary>
    /// Reconstructs the ordered path once the goal has been reached.
    /// </summary>
    /// <param name="Graph">Baked graph.</param>
    /// <param name="CameFrom">Parent table built by A*.</param>
    /// <param name="GoalNodeIndex">Goal node index.</param>
    /// <param name="Result">Mutable output path result.</param>
    private static void ReconstructPath(
        PipeSurfaceGraph Graph,
        int[] CameFrom,
        int GoalNodeIndex,
        PipePathResult Result)
    {
        List<int> ReversedNodeIndices = new List<int>();
        int CurrentNodeIndex = GoalNodeIndex;

        while (CurrentNodeIndex >= 0)
        {
            ReversedNodeIndices.Add(CurrentNodeIndex);
            CurrentNodeIndex = CameFrom[CurrentNodeIndex];
        }

        for (int Index = ReversedNodeIndices.Count - 1; Index >= 0; Index--)
        {
            int NodeIndex = ReversedNodeIndices[Index];
            PipeSurfaceGraph.PipeSurfaceNode Node = Graph.GetNode(NodeIndex);
            if (Node == null)
            {
                continue;
            }

            Result.NodeIndices.Add(NodeIndex);
            Result.CenterPoints.Add(Node.CenterPosition);
            Result.SupportDirections.Add(Node.SupportDirection);
        }
    }

    /// <summary>
    /// Lightweight binary min heap used by the A* open set.
    /// </summary>
    private sealed class MinHeap
    {
        /// <summary>
        /// Stored heap item.
        /// </summary>
        private struct HeapItem
        {
            /// <summary>
            /// Graph node index stored in the heap.
            /// </summary>
            public int NodeIndex;

            /// <summary>
            /// Priority used to sort the heap.
            /// </summary>
            public float Priority;
        }

        /// <summary>
        /// Backing storage for heap items.
        /// </summary>
        private readonly List<HeapItem> Items;

        /// <summary>
        /// Gets the current amount of heap items.
        /// </summary>
        public int Count => Items.Count;

        /// <summary>
        /// Creates a new heap with the requested starting capacity.
        /// </summary>
        /// <param name="Capacity">Initial backing capacity.</param>
        public MinHeap(int Capacity)
        {
            Items = new List<HeapItem>(Mathf.Max(16, Capacity));
        }

        /// <summary>
        /// Pushes one node index with its priority into the heap.
        /// </summary>
        /// <param name="NodeIndex">Graph node index.</param>
        /// <param name="Priority">Priority value.</param>
        public void Push(int NodeIndex, float Priority)
        {
            HeapItem Item = new HeapItem
            {
                NodeIndex = NodeIndex,
                Priority = Priority
            };

            Items.Add(Item);
            SiftUp(Items.Count - 1);
        }

        /// <summary>
        /// Pops the lowest-priority node index.
        /// </summary>
        /// <returns>Lowest-priority node index or -1 when empty.</returns>
        public int Pop()
        {
            if (Items.Count == 0)
            {
                return -1;
            }

            int Result = Items[0].NodeIndex;
            int LastIndex = Items.Count - 1;
            Items[0] = Items[LastIndex];
            Items.RemoveAt(LastIndex);

            if (Items.Count > 0)
            {
                SiftDown(0);
            }

            return Result;
        }

        /// <summary>
        /// Restores heap ordering upwards from a child index.
        /// </summary>
        /// <param name="Index">Child index.</param>
        private void SiftUp(int Index)
        {
            while (Index > 0)
            {
                int ParentIndex = (Index - 1) / 2;
                if (Items[ParentIndex].Priority <= Items[Index].Priority)
                {
                    break;
                }

                Swap(Index, ParentIndex);
                Index = ParentIndex;
            }
        }

        /// <summary>
        /// Restores heap ordering downwards from a parent index.
        /// </summary>
        /// <param name="Index">Parent index.</param>
        private void SiftDown(int Index)
        {
            while (true)
            {
                int LeftChild = (Index * 2) + 1;
                int RightChild = LeftChild + 1;
                int SmallestIndex = Index;

                if (LeftChild < Items.Count && Items[LeftChild].Priority < Items[SmallestIndex].Priority)
                {
                    SmallestIndex = LeftChild;
                }

                if (RightChild < Items.Count && Items[RightChild].Priority < Items[SmallestIndex].Priority)
                {
                    SmallestIndex = RightChild;
                }

                if (SmallestIndex == Index)
                {
                    break;
                }

                Swap(Index, SmallestIndex);
                Index = SmallestIndex;
            }
        }

        /// <summary>
        /// Swaps two heap items.
        /// </summary>
        /// <param name="A">First index.</param>
        /// <param name="B">Second index.</param>
        private void Swap(int A, int B)
        {
            HeapItem Temp = Items[A];
            Items[A] = Items[B];
            Items[B] = Temp;
        }
    }
}
