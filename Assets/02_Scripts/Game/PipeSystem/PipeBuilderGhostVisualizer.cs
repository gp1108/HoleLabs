
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns the runtime ghost representation shown while the player is previewing a pipe build.
/// The ghost uses the same resolved path geometry as the final pipe instance so placement feedback remains exact.
/// </summary>
public sealed class PipeBuilderGhostVisualizer : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Optional pipe path prefab used to create the preview. If empty, a runtime instance is created automatically.")]
    [SerializeField] private PipePathInstance GhostPipePrefab;

    [Tooltip("Optional root used to store the runtime ghost instance.")]
    [SerializeField] private Transform GhostRoot;

    [Header("Materials")]
    [Tooltip("Material applied while the current preview can be built successfully.")]
    [SerializeField] private Material ValidGhostMaterial;

    [Tooltip("Material applied while the current preview cannot be committed yet.")]
    [SerializeField] private Material InvalidGhostMaterial;

    [Header("State")]
    [Tooltip("If true, the preview is hidden when no valid preview geometry exists.")]
    [SerializeField] private bool HideWhenNoGeometry = true;

    [Tooltip("If true, the runtime ghost disables all colliders in its hierarchy.")]
    [SerializeField] private bool DisableGhostColliders = true;

    /// <summary>
    /// Runtime preview instance.
    /// </summary>
    private PipePathInstance RuntimeGhost;

    /// <summary>
    /// Cached preview points used to avoid unnecessary full rebuilds when the path did not change.
    /// </summary>
    private readonly List<Vector3> CachedCenterPoints = new List<Vector3>();

    /// <summary>
    /// Cached preview support directions used to avoid unnecessary full rebuilds when the path did not change.
    /// </summary>
    private readonly List<Vector3> CachedSupportDirections = new List<Vector3>();

    /// <summary>
    /// Whether the currently shown ghost is in a valid build state.
    /// </summary>
    private bool WasLastStateValid = true;

    /// <summary>
    /// Shows or updates the pipe preview geometry.
    /// </summary>
    public void ShowPreview(IReadOnlyList<Vector3> CenterPoints, IReadOnlyList<Vector3> SupportDirections, PipeBuildSettings Settings, bool IsCommitValid)
    {
        if (CenterPoints == null || SupportDirections == null || CenterPoints.Count < 2 || SupportDirections.Count != CenterPoints.Count)
        {
            if (HideWhenNoGeometry)
            {
                HidePreview();
            }

            return;
        }

        EnsureGhostExists();

        bool GeometryChanged = !MatchesCachedGeometry(CenterPoints, SupportDirections);
        if (GeometryChanged)
        {
            RuntimeGhost.Initialize(new List<Vector3>(CenterPoints), new List<Vector3>(SupportDirections), Settings);
            CacheGeometry(CenterPoints, SupportDirections);
        }

        ApplyStateMaterial(IsCommitValid, GeometryChanged);

        if (DisableGhostColliders && RuntimeGhost != null)
        {
            RuntimeGhost.SetCollidersEnabled(false);
        }

        RuntimeGhost.gameObject.SetActive(true);
    }

    /// <summary>
    /// Hides the current pipe preview.
    /// </summary>
    public void HidePreview()
    {
        if (RuntimeGhost != null)
        {
            RuntimeGhost.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Destroys the runtime ghost instance completely.
    /// </summary>
    public void DestroyPreview()
    {
        if (RuntimeGhost == null)
        {
            return;
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            DestroyImmediate(RuntimeGhost.gameObject);
        }
        else
#endif
        {
            Destroy(RuntimeGhost.gameObject);
        }

        RuntimeGhost = null;
        CachedCenterPoints.Clear();
        CachedSupportDirections.Clear();
    }

    /// <summary>
    /// Destroys the runtime ghost automatically when this visualizer is destroyed.
    /// </summary>
    private void OnDestroy()
    {
        DestroyPreview();

        if (GhostRoot != null && GhostRoot.childCount == 0 && GhostRoot.name == "PipeGhostRoot")
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                DestroyImmediate(GhostRoot.gameObject);
            }
            else
#endif
            {
                Destroy(GhostRoot.gameObject);
            }
        }
    }

    /// <summary>
    /// Ensures a runtime ghost instance exists.
    /// </summary>
    private void EnsureGhostExists()
    {
        if (RuntimeGhost != null)
        {
            return;
        }

        if (GhostRoot == null)
        {
            GameObject GhostRootObject = new GameObject("PipeGhostRoot");
            GhostRoot = GhostRootObject.transform;
            GhostRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        if (GhostPipePrefab != null)
        {
            RuntimeGhost = Instantiate(GhostPipePrefab, GhostRoot);
        }
        else
        {
            GameObject GhostObject = new GameObject("PipeGhostPreview");
            GhostObject.transform.SetParent(GhostRoot, false);
            RuntimeGhost = GhostObject.AddComponent<PipePathInstance>();
        }

        RuntimeGhost.gameObject.SetActive(false);
    }

    /// <summary>
    /// Applies the correct material according to the current preview state.
    /// </summary>
    private void ApplyStateMaterial(bool IsCommitValid, bool ForceReapply)
    {
        if (RuntimeGhost == null)
        {
            return;
        }

        if (!ForceReapply && WasLastStateValid == IsCommitValid && RuntimeGhost.gameObject.activeSelf)
        {
            return;
        }

        Material SelectedMaterial = IsCommitValid ? ValidGhostMaterial : InvalidGhostMaterial;
        RuntimeGhost.ApplyMaterialOverride(SelectedMaterial);
        WasLastStateValid = IsCommitValid;
    }

    /// <summary>
    /// Returns true when the provided geometry matches the cached runtime ghost geometry.
    /// </summary>
    private bool MatchesCachedGeometry(IReadOnlyList<Vector3> CenterPoints, IReadOnlyList<Vector3> SupportDirections)
    {
        if (CachedCenterPoints.Count != CenterPoints.Count || CachedSupportDirections.Count != SupportDirections.Count)
        {
            return false;
        }

        for (int Index = 0; Index < CenterPoints.Count; Index++)
        {
            if ((CachedCenterPoints[Index] - CenterPoints[Index]).sqrMagnitude > 0.0001f)
            {
                return false;
            }

            if ((CachedSupportDirections[Index] - SupportDirections[Index]).sqrMagnitude > 0.0001f)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Stores a copy of the currently displayed preview geometry.
    /// </summary>
    private void CacheGeometry(IReadOnlyList<Vector3> CenterPoints, IReadOnlyList<Vector3> SupportDirections)
    {
        CachedCenterPoints.Clear();
        CachedSupportDirections.Clear();

        for (int Index = 0; Index < CenterPoints.Count; Index++)
        {
            CachedCenterPoints.Add(CenterPoints[Index]);
            CachedSupportDirections.Add(SupportDirections[Index]);
        }
    }
}
