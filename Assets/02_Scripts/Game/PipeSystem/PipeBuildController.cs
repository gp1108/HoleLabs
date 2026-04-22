
using UnityEngine;

/// <summary>
/// High-level controller that resolves clicked cave wall points into baked graph nodes,
/// stores point A, evaluates point B previews and finally instantiates the built pipe.
/// This component is intentionally input-agnostic so an equipped build item can drive it cleanly.
/// </summary>
public sealed class PipeBuildController : MonoBehaviour
{
    /// <summary>
    /// Preview payload returned while the player is aiming point B after point A was already placed.
    /// The final build should reuse this exact resolved data so the committed pipe matches the ghost.
    /// </summary>
    public sealed class PipePreviewResult
    {
        /// <summary>
        /// Whether point A currently exists.
        /// </summary>
        public bool HasPendingStartPoint;

        /// <summary>
        /// Whether point B was resolved to a valid graph path.
        /// </summary>
        public bool IsGeometryValid;

        /// <summary>
        /// User-facing failure reason when the preview is invalid.
        /// </summary>
        public string FailureReason;

        /// <summary>
        /// Start surface point originally selected by the player.
        /// </summary>
        public Vector3 StartSurfacePoint;

        /// <summary>
        /// Current preview end surface point.
        /// </summary>
        public Vector3 EndSurfacePoint;

        /// <summary>
        /// Start node index used by the graph path.
        /// </summary>
        public int StartNodeIndex = -1;

        /// <summary>
        /// End node index used by the graph path.
        /// </summary>
        public int EndNodeIndex = -1;

        /// <summary>
        /// Cached preview center-line points.
        /// </summary>
        public readonly System.Collections.Generic.List<Vector3> CenterPoints = new System.Collections.Generic.List<Vector3>();

        /// <summary>
        /// Cached preview support directions.
        /// </summary>
        public readonly System.Collections.Generic.List<Vector3> SupportDirections = new System.Collections.Generic.List<Vector3>();

        /// <summary>
        /// Gets the total center-line path length represented by this preview.
        /// </summary>
        public float GetTotalLength()
        {
            float Result = 0f;

            for (int Index = 1; Index < CenterPoints.Count; Index++)
            {
                Result += Vector3.Distance(CenterPoints[Index - 1], CenterPoints[Index]);
            }

            return Mathf.Max(0f, Result);
        }
    }

    [Header("References")]
    [Tooltip("Camera used to create debug wall selection rays. If empty, Camera.main is used.")]
    [SerializeField] private Camera PlayerCamera;

    [Tooltip("Central axis used for cave radial sampling and inward wall offset resolution.")]
    [SerializeField] private Transform AxisTransform;

    [Tooltip("Optional trigger volume that defines the forbidden elevator space more accurately than a simple radius.")]
    [SerializeField] private PipeExclusionVolume ElevatorExclusionVolume;

    [Tooltip("Baked wall graph used by runtime pathfinding.")]
    [SerializeField] private PipeSurfaceGraph SurfaceGraph;

    [Tooltip("Shared settings asset used by validation, pathfinding and visuals.")]
    [SerializeField] private PipeBuildSettings BuildSettings;

    [Tooltip("Optional prefab used to spawn the final built pipe instance.")]
    [SerializeField] private PipePathInstance PipePathPrefab;

    [Tooltip("Optional parent used to store built pipes.")]
    [SerializeField] private Transform PipeRoot;

    [Header("Selection")]
    [Tooltip("Layers considered valid cave wall geometry for player clicks.")]
    [SerializeField] private LayerMask CaveLayers = ~0;

    [Tooltip("Default maximum raycast distance used by debug input or by tools that do not provide an explicit override.")]
    [SerializeField] private float WallSelectionDistance = 12f;

    [Header("Debug")]
    [Tooltip("If true, left mouse click is used to test the system directly in play mode.")]
    [SerializeField] private bool UseDebugMouseInput = false;

    [Tooltip("Draws the pending start point and last failure reason with gizmos.")]
    [SerializeField] private bool DrawDebugGizmos = true;

    [Tooltip("Logs selection, validation and path results.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Whether a valid point A is currently pending.
    /// </summary>
    private bool HasPendingStartPoint;

    /// <summary>
    /// Pending clicked surface position for point A.
    /// </summary>
    private Vector3 PendingStartSurfacePoint;

    /// <summary>
    /// Pending nearest graph node for point A.
    /// </summary>
    private int PendingStartNodeIndex = -1;

    /// <summary>
    /// Most recent user-facing validation reason.
    /// </summary>
    private string LastFailureReason = string.Empty;

    /// <summary>
    /// Gets whether point A is currently stored.
    /// </summary>
    public bool GetHasPendingStartPoint()
    {
        return HasPendingStartPoint;
    }

    /// <summary>
    /// Gets the pending point A world surface position.
    /// </summary>
    public Vector3 GetPendingStartSurfacePoint()
    {
        return PendingStartSurfacePoint;
    }

    /// <summary>
    /// Gets the last validation or build failure reason.
    /// </summary>
    public string GetLastFailureReason()
    {
        return LastFailureReason;
    }

    /// <summary>
    /// Gets the shared build settings reference.
    /// </summary>
    public PipeBuildSettings GetBuildSettings()
    {
        return BuildSettings;
    }

    /// <summary>
    /// Gets the default wall selection distance.
    /// </summary>
    public float GetDefaultWallSelectionDistance()
    {
        return Mathf.Max(0.1f, WallSelectionDistance);
    }

    /// <summary>
    /// Resolves missing references.
    /// </summary>
    private void Awake()
    {
        if (PlayerCamera == null)
        {
            PlayerCamera = Camera.main;
        }

        if (PipeRoot == null)
        {
            PipeRoot = transform;
        }
    }

    /// <summary>
    /// Temporary debug-only mouse entry point.
    /// Production integration should call the explicit begin, preview and commit methods from the equipped build tool.
    /// </summary>
    private void Update()
    {
        if (!UseDebugMouseInput || PlayerCamera == null)
        {
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            Ray BuildRay = PlayerCamera.ScreenPointToRay(Input.mousePosition);
            HandleDebugBuildClick(BuildRay);
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            ClearPendingStartPoint();
        }
    }

    /// <summary>
    /// Tries to raycast the cave wall using the configured cave layers.
    /// </summary>
    public bool TryRaycastCaveWall(Ray BuildRay, float MaxDistance, out RaycastHit HitInfo)
    {
        return Physics.Raycast(
            BuildRay,
            out HitInfo,
            Mathf.Max(0.1f, MaxDistance),
            CaveLayers,
            QueryTriggerInteraction.Ignore);
    }

    /// <summary>
    /// Stores point A from an already resolved cave wall point.
    /// </summary>
    public bool TryBeginBuildFromWallPoint(Vector3 WallPoint)
    {
        LastFailureReason = string.Empty;

        if (!TryResolveWallPointToNode(WallPoint, out int StartNodeIndex))
        {
            return false;
        }

        PendingStartSurfacePoint = WallPoint;
        PendingStartNodeIndex = StartNodeIndex;
        HasPendingStartPoint = true;
        Log("Stored pipe start point A.");
        return true;
    }

    /// <summary>
    /// Evaluates a preview from the stored point A to the provided wall point B.
    /// </summary>
    public PipePreviewResult EvaluatePreviewFromWallPoint(Vector3 WallPoint)
    {
        PipePreviewResult Result = new PipePreviewResult();
        Result.HasPendingStartPoint = HasPendingStartPoint;
        Result.StartSurfacePoint = PendingStartSurfacePoint;
        Result.EndSurfacePoint = WallPoint;
        Result.StartNodeIndex = PendingStartNodeIndex;

        if (!HasPendingStartPoint)
        {
            Result.FailureReason = "No pending point A is currently stored.";
            return Result;
        }

        if (!TryResolveWallPointToNode(WallPoint, out int EndNodeIndex))
        {
            Result.FailureReason = LastFailureReason;
            return Result;
        }

        Result.EndNodeIndex = EndNodeIndex;

        if (WallPoint.y >= PendingStartSurfacePoint.y - BuildSettings.GetMinimumRequiredDrop())
        {
            Result.FailureReason = "Point B must be below point A.";
            return Result;
        }

        PipePathfinder.PipePathResult PathResult = PipePathfinder.CalculatePath(
            SurfaceGraph,
            BuildSettings,
            PendingStartNodeIndex,
            EndNodeIndex);

        if (!PathResult.WasFound)
        {
            Result.FailureReason = PathResult.FailureReason;
            return Result;
        }

        Result.CenterPoints.AddRange(PathResult.CenterPoints);
        Result.SupportDirections.AddRange(PathResult.SupportDirections);
        Result.IsGeometryValid = Result.CenterPoints.Count >= 2;
        return Result;
    }

    /// <summary>
    /// Tries to evaluate a preview from a build ray by raycasting the wall first.
    /// </summary>
    public bool TryEvaluatePreviewFromRay(Ray BuildRay, float MaxDistance, out PipePreviewResult PreviewResult, out RaycastHit HitInfo)
    {
        PreviewResult = null;
        HitInfo = default;

        if (!TryRaycastCaveWall(BuildRay, MaxDistance, out HitInfo))
        {
            SetFailure("No valid cave wall was hit.");
            return false;
        }

        PreviewResult = EvaluatePreviewFromWallPoint(HitInfo.point);
        if (PreviewResult == null || !PreviewResult.IsGeometryValid)
        {
            SetFailure(PreviewResult != null ? PreviewResult.FailureReason : "Failed to evaluate a pipe preview.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Builds the final pipe from a previously validated preview result.
    /// </summary>
    public bool TryBuildFromPreview(PipePreviewResult PreviewResult, out PipePathInstance BuiltPipe)
    {
        BuiltPipe = null;
        LastFailureReason = string.Empty;

        if (!HasPendingStartPoint)
        {
            SetFailure("No pending point A is currently stored.");
            return false;
        }

        if (PreviewResult == null || !PreviewResult.IsGeometryValid || PreviewResult.CenterPoints.Count < 2)
        {
            SetFailure("The provided pipe preview is not valid for building.");
            return false;
        }

        if (PreviewResult.StartNodeIndex != PendingStartNodeIndex)
        {
            SetFailure("The provided pipe preview no longer matches the current point A.");
            return false;
        }

        BuiltPipe = CreatePipeInstance(PreviewResult.CenterPoints, PreviewResult.SupportDirections);
        if (BuiltPipe == null)
        {
            SetFailure("Failed to create the final pipe instance.");
            return false;
        }

        ClearPendingStartPoint();
        Log("Built pipe successfully with " + PreviewResult.CenterPoints.Count + " path points.");
        return true;
    }

    /// <summary>
    /// Clears the pending point A selection.
    /// </summary>
    public void ClearPendingStartPoint()
    {
        HasPendingStartPoint = false;
        PendingStartNodeIndex = -1;
        PendingStartSurfacePoint = Vector3.zero;
    }

    /// <summary>
    /// Debug helper that reproduces the full two-click flow using a ray.
    /// </summary>
    private void HandleDebugBuildClick(Ray BuildRay)
    {
        if (!TryRaycastCaveWall(BuildRay, WallSelectionDistance, out RaycastHit HitInfo))
        {
            SetFailure("No valid cave wall was hit.");
            return;
        }

        if (!HasPendingStartPoint)
        {
            TryBeginBuildFromWallPoint(HitInfo.point);
            return;
        }

        PipePreviewResult PreviewResult = EvaluatePreviewFromWallPoint(HitInfo.point);
        if (PreviewResult == null || !PreviewResult.IsGeometryValid)
        {
            SetFailure(PreviewResult != null ? PreviewResult.FailureReason : "Failed to evaluate a pipe preview.");
            return;
        }

        TryBuildFromPreview(PreviewResult, out _);
    }

    /// <summary>
    /// Resolves one clicked cave wall point and maps it to the nearest baked graph node.
    /// </summary>
    private bool TryResolveWallPointToNode(Vector3 WallPoint, out int NodeIndex)
    {
        NodeIndex = -1;

        if (AxisTransform == null || SurfaceGraph == null || BuildSettings == null)
        {
            SetFailure("PipeBuildController is missing required references.");
            return false;
        }

        Vector3 InwardDirection = PipeAxisUtility.GetInwardDirectionToAxis(AxisTransform, WallPoint);
        Vector3 CandidateCenter = WallPoint + (InwardDirection * BuildSettings.GetPipeCenterOffset());

        if (IsElevatorSpaceBlocked(CandidateCenter))
        {
            SetFailure("The selected point is too close to the elevator exclusion zone.");
            return false;
        }

        NodeIndex = SurfaceGraph.FindNearestNodeIndex(CandidateCenter, BuildSettings.GetMaxNodeSelectionDistance());
        if (NodeIndex < 0)
        {
            SetFailure("No nearby baked wall node matches the selected point. Re-bake the graph or increase sampling density.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Instantiates and initializes the final built pipe instance.
    /// </summary>
    private PipePathInstance CreatePipeInstance(
        System.Collections.Generic.List<Vector3> CenterPoints,
        System.Collections.Generic.List<Vector3> SupportDirections)
    {
        if (CenterPoints == null || SupportDirections == null || CenterPoints.Count < 2 || SupportDirections.Count < 2)
        {
            return null;
        }

        PipePathInstance Instance;

        if (PipePathPrefab != null)
        {
            Instance = Instantiate(PipePathPrefab, PipeRoot);
        }
        else
        {
            GameObject PipeObject = new GameObject("BuiltPipe");
            PipeObject.transform.SetParent(PipeRoot, false);
            Instance = PipeObject.AddComponent<PipePathInstance>();
            PipeObject.AddComponent<PipeTransportLine>();
        }

        Instance.Initialize(CenterPoints, SupportDirections, BuildSettings);
        return Instance;
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
    /// Stores the last failure reason and writes it to the log when enabled.
    /// </summary>
    private void SetFailure(string Reason)
    {
        LastFailureReason = string.IsNullOrWhiteSpace(Reason) ? "Unknown pipe build validation error." : Reason;
        Log(LastFailureReason);
    }

    /// <summary>
    /// Draws pending selection and feedback gizmos.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!DrawDebugGizmos || !HasPendingStartPoint)
        {
            return;
        }

        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(PendingStartSurfacePoint, 0.12f);
    }

    /// <summary>
    /// Writes controller-specific debug messages when enabled.
    /// </summary>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[PipeBuildController] " + Message, this);
    }
}
