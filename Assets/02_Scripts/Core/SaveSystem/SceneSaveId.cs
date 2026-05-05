using System;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
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
            Id = Guid.NewGuid().ToString("N");
            EditorUtility.SetDirty(this);
        }
    }
#endif
}