using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
#endif

/// <summary>
/// Stable scene identifier used by the save system to resolve scene objects
/// without storing Unity object references in save data.
/// </summary>
[DisallowMultipleComponent]
public sealed class SceneSaveId : MonoBehaviour
{
    [Tooltip("Stable unique identifier used by the save system.")]
    [SerializeField] private string Id;

    /// <summary>
    /// Gets the stable identifier assigned to this scene object.
    /// </summary>
    public string GetId()
    {
        return Id;
    }

#if UNITY_EDITOR
    /// <summary>
    /// Regenerates this scene save id immediately.
    /// Use this only before relying on existing save files, because saved objects are resolved by this id.
    /// </summary>
    [ContextMenu("Regenerate Scene Save Id")]
    public void RegenerateSceneSaveId()
    {
        Undo.RecordObject(this, "Regenerate Scene Save Id");
        AssignNewId(new HashSet<string>());
        RecordPrefabInstanceOverride(this);
        EditorUtility.SetDirty(this);
        MarkOwningSceneDirty();
    }

    /// <summary>
    /// Ensures a persistent id exists while editing the scene.
    /// </summary>
    private void OnValidate()
    {
        if (Application.isPlaying)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Id))
        {
            AssignNewId(null);
            RecordPrefabInstanceOverride(this);
            EditorUtility.SetDirty(this);
            MarkOwningSceneDirty();
        }
    }

    /// <summary>
    /// Regenerates every SceneSaveId in all currently opened scenes.
    /// This is useful after duplicating authored ore veins or scene objects from prefabs.
    /// </summary>
    [MenuItem("Tools/HoleLabs/Save/Regenerate All Scene Save Ids In Open Scenes")]
    private static void RegenerateAllSceneSaveIdsInOpenScenes()
    {
        SceneSaveId[] SceneSaveIds = FindObjectsByType<SceneSaveId>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        HashSet<string> UsedIds = new HashSet<string>(StringComparer.Ordinal);
        List<UnityEngine.Object> DirtyObjects = new List<UnityEngine.Object>();
        HashSet<Scene> DirtyScenes = new HashSet<Scene>();

        for (int Index = 0; Index < SceneSaveIds.Length; Index++)
        {
            SceneSaveId SceneSaveId = SceneSaveIds[Index];

            if (!IsEditableOpenSceneObject(SceneSaveId))
            {
                continue;
            }

            Undo.RecordObject(SceneSaveId, "Regenerate Scene Save Ids");
            SceneSaveId.AssignNewId(UsedIds);
            RecordPrefabInstanceOverride(SceneSaveId);
            DirtyObjects.Add(SceneSaveId);
            DirtyScenes.Add(SceneSaveId.gameObject.scene);
        }

        MarkDirty(DirtyObjects, DirtyScenes);
        Debug.Log("Regenerated " + DirtyObjects.Count + " scene save ids in open scenes.");
    }

    /// <summary>
    /// Regenerates only missing or duplicated SceneSaveId values in the currently opened scenes.
    /// This is the safer routine for normal level-authoring work.
    /// </summary>
    [MenuItem("Tools/HoleLabs/Save/Repair Missing Or Duplicate Scene Save Ids In Open Scenes")]
    private static void RepairMissingOrDuplicateSceneSaveIdsInOpenScenes()
    {
        SceneSaveId[] SceneSaveIds = FindObjectsByType<SceneSaveId>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        HashSet<string> UsedIds = new HashSet<string>(StringComparer.Ordinal);
        List<UnityEngine.Object> DirtyObjects = new List<UnityEngine.Object>();
        HashSet<Scene> DirtyScenes = new HashSet<Scene>();

        for (int Index = 0; Index < SceneSaveIds.Length; Index++)
        {
            SceneSaveId SceneSaveId = SceneSaveIds[Index];

            if (!IsEditableOpenSceneObject(SceneSaveId))
            {
                continue;
            }

            bool RequiresNewId = string.IsNullOrWhiteSpace(SceneSaveId.Id) ||
                                 UsedIds.Contains(SceneSaveId.Id) ||
                                 IsSceneInstanceUsingPrefabSourceId(SceneSaveId);

            if (RequiresNewId)
            {
                Undo.RecordObject(SceneSaveId, "Repair Scene Save Ids");
                SceneSaveId.AssignNewId(UsedIds);
                RecordPrefabInstanceOverride(SceneSaveId);
                DirtyObjects.Add(SceneSaveId);
                DirtyScenes.Add(SceneSaveId.gameObject.scene);
                continue;
            }

            UsedIds.Add(SceneSaveId.Id);
        }

        MarkDirty(DirtyObjects, DirtyScenes);
        Debug.Log("Repaired " + DirtyObjects.Count + " missing or duplicated scene save ids in open scenes.");
    }

    /// <summary>
    /// Validates every SceneSaveId in all currently opened scenes and reports duplicate, missing or prefab-source ids.
    /// </summary>
    [MenuItem("Tools/HoleLabs/Save/Validate Scene Save Ids In Open Scenes")]
    private static void ValidateSceneSaveIdsInOpenScenes()
    {
        SceneSaveId[] SceneSaveIds = FindObjectsByType<SceneSaveId>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Dictionary<string, SceneSaveId> FirstSceneSaveIdById = new Dictionary<string, SceneSaveId>(StringComparer.Ordinal);
        int MissingCount = 0;
        int DuplicateCount = 0;
        int PrefabSourceIdCount = 0;

        for (int Index = 0; Index < SceneSaveIds.Length; Index++)
        {
            SceneSaveId SceneSaveId = SceneSaveIds[Index];

            if (!IsEditableOpenSceneObject(SceneSaveId))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(SceneSaveId.Id))
            {
                MissingCount++;
                Debug.LogError("[SceneSaveId] Missing scene save id on: " + GetHierarchyPath(SceneSaveId.transform), SceneSaveId);
                continue;
            }

            if (IsSceneInstanceUsingPrefabSourceId(SceneSaveId))
            {
                PrefabSourceIdCount++;
                Debug.LogWarning("[SceneSaveId] Scene instance is still using its prefab source id and should be repaired: " + GetHierarchyPath(SceneSaveId.transform), SceneSaveId);
            }

            if (FirstSceneSaveIdById.TryGetValue(SceneSaveId.Id, out SceneSaveId ExistingSceneSaveId) && ExistingSceneSaveId != null)
            {
                DuplicateCount++;
                Debug.LogError(
                    "[SceneSaveId] Duplicate id '" + SceneSaveId.Id + "' found between '" +
                    GetHierarchyPath(ExistingSceneSaveId.transform) + "' and '" +
                    GetHierarchyPath(SceneSaveId.transform) + "'.",
                    SceneSaveId);
                continue;
            }

            FirstSceneSaveIdById[SceneSaveId.Id] = SceneSaveId;
        }

        Debug.Log("[SceneSaveId] Validation completed. Missing: " + MissingCount +
                  " | Duplicates: " + DuplicateCount +
                  " | Prefab source ids: " + PrefabSourceIdCount +
                  " | Valid unique ids: " + FirstSceneSaveIdById.Count + ".");
    }

    /// <summary>
    /// Assigns a new unique id to this component.
    /// </summary>
    /// <param name="UsedIds">Optional set of already used ids that must be avoided.</param>
    private void AssignNewId(HashSet<string> UsedIds)
    {
        string NewId;

        do
        {
            NewId = Guid.NewGuid().ToString("N");
        }
        while (UsedIds != null && UsedIds.Contains(NewId));

        Id = NewId;
        UsedIds?.Add(Id);
    }

    /// <summary>
    /// Records a prefab instance property override after changing an id from editor tooling.
    /// Without this call, duplicated prefab instances can appear fixed in memory but reload with their prefab source id.
    /// </summary>
    /// <param name="SceneSaveId">Scene save id modified by the tool.</param>
    private static void RecordPrefabInstanceOverride(SceneSaveId SceneSaveId)
    {
        if (SceneSaveId == null || SceneSaveId.gameObject == null)
        {
            return;
        }

        if (PrefabUtility.IsPartOfPrefabInstance(SceneSaveId.gameObject))
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(SceneSaveId);
        }
    }

    /// <summary>
    /// Returns whether a scene prefab instance is still using the id serialized on its prefab asset.
    /// Scene objects must own instance-specific ids because prefab source ids are shared by every duplicate.
    /// </summary>
    /// <param name="SceneSaveId">Scene save id to evaluate.</param>
    /// <returns>True when the scene instance still matches the source prefab id.</returns>
    private static bool IsSceneInstanceUsingPrefabSourceId(SceneSaveId SceneSaveId)
    {
        if (SceneSaveId == null || SceneSaveId.gameObject == null)
        {
            return false;
        }

        if (!PrefabUtility.IsPartOfPrefabInstance(SceneSaveId.gameObject))
        {
            return false;
        }

        SceneSaveId PrefabSourceSceneSaveId = PrefabUtility.GetCorrespondingObjectFromSource(SceneSaveId);

        if (PrefabSourceSceneSaveId == null || string.IsNullOrWhiteSpace(PrefabSourceSceneSaveId.Id))
        {
            return false;
        }

        return string.Equals(SceneSaveId.Id, PrefabSourceSceneSaveId.Id, StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds a readable hierarchy path for editor validation messages.
    /// </summary>
    /// <param name="TransformValue">Transform used as the path leaf.</param>
    /// <returns>Readable scene hierarchy path.</returns>
    private static string GetHierarchyPath(Transform TransformValue)
    {
        if (TransformValue == null)
        {
            return "<null>";
        }

        string Path = TransformValue.name;
        Transform Parent = TransformValue.parent;

        while (Parent != null)
        {
            Path = Parent.name + "/" + Path;
            Parent = Parent.parent;
        }

        return Path;
    }

    /// <summary>
    /// Returns whether this SceneSaveId belongs to an editable object in an opened scene rather than to a prefab asset.
    /// </summary>
    /// <param name="SceneSaveId">Scene save id to evaluate.</param>
    /// <returns>True when the object can be modified by scene id maintenance tools.</returns>
    private static bool IsEditableOpenSceneObject(SceneSaveId SceneSaveId)
    {
        if (SceneSaveId == null || SceneSaveId.gameObject == null)
        {
            return false;
        }

        if (EditorUtility.IsPersistent(SceneSaveId.gameObject))
        {
            return false;
        }

        if (!SceneSaveId.gameObject.scene.IsValid())
        {
            return false;
        }

        if (PrefabStageUtility.GetPrefabStage(SceneSaveId.gameObject) != null)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Marks all modified objects and scenes as dirty so Unity saves the regenerated ids.
    /// </summary>
    /// <param name="DirtyObjects">Objects modified by the operation.</param>
    /// <param name="DirtyScenes">Scenes modified by the operation.</param>
    private static void MarkDirty(List<UnityEngine.Object> DirtyObjects, HashSet<Scene> DirtyScenes)
    {
        for (int Index = 0; Index < DirtyObjects.Count; Index++)
        {
            if (DirtyObjects[Index] != null)
            {
                EditorUtility.SetDirty(DirtyObjects[Index]);
            }
        }

        foreach (Scene DirtyScene in DirtyScenes)
        {
            if (DirtyScene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(DirtyScene);
            }
        }
    }

    /// <summary>
    /// Marks the scene that owns this id as dirty after an editor-side id change.
    /// </summary>
    private void MarkOwningSceneDirty()
    {
        if (gameObject != null && gameObject.scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
    }
#endif
}
