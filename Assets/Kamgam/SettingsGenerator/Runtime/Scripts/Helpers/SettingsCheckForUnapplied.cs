using System;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Events;
using UnityEngine.Serialization;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Kamgam.SettingsGenerator
{
    /// <summary>
    /// Helper to auto-detect unapplied settings in OnDisable(). Handy for showing confirmation dialogs.<br />
    /// It can also be triggered by the provider once the UI was closed (all resolvers have been set inactive).
    /// </summary>
    public class SettingsCheckForUnapplied : MonoBehaviour
    {
        [System.NonSerialized]
        private static List<SettingsCheckForUnapplied> _registry = new List<SettingsCheckForUnapplied>();
        
        // Reset static variables on play mode enter to support disabling domain reload.
#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlayModeEnter()
        {
            _registry.Clear();
        }
#endif

        public static void TriggerCheck()
        {
            foreach (var checker in _registry)
            {
                if (checker != null && checker.Provider != null && checker.Provider.HasSettings())
                {
                    checker.Check();
                    
                    // Calling check on one should suffice.
                    break;
                }
            }
        }
        
        [System.NonSerialized]
        private static int _lastCheckFrame = 0;
        
        [Tooltip("Turns of the check. If disabled then this component will do nothing.")]
        [FormerlySerializedAs("Enabled")]
        public bool CheckOnDisable = true;
        
        [Tooltip("(Optional) Usually it's fine to leave this empty.\n" +
                 "If set the this settings provider will be used. Otherwise the last used provider (or the configured provider, depending on the flag below) will be used instead.")]
        public SettingsProvider Provider;

        [Tooltip("If enabled then the configured global provider will be used if the Provider on this component is NULL, otherwise the last used provider will be used as fallback.")]
        public bool FallBackOnConfiguredProvider = false;

        protected SettingsProvider getProvider()
        {
            if (Provider != null)
                return Provider;

            if (FallBackOnConfiguredProvider)
                return SettingsGeneratorSettings.GetProvider();
            else
                return SettingsProvider.LastUsedSettingsProvider;
        }

        // public delegate void OnUnappliedSettingsDetectedDelegate(List<ISetting> unappliedSettings);
        public UnityEvent<List<ISetting>> OnUnappliedSettingsDetected;

        /// <summary>
        /// Useful for showing modal confirm dialogs after settings UI has been disabled.
        /// </summary>
        [Tooltip("Useful for showing modal confirm dialogs after settings UI has been disabled.")]
        public List<GameObject> ObjectsToShowOnUnapplied;

        [System.NonSerialized]
        public List<ISetting> _unappliedSettings = new List<ISetting>(10);

        public void OnEnable()
        {
            if (!_registry.Contains(this))
                _registry.Add(this);
        }

        public void OnDisable()
        {
            if (_registry.Contains(this))
                _registry.Remove(this);
            
            if (CheckOnDisable)
                Check();
        }
        
        public void Check()
        {
            if (Provider == null || !Provider.HasSettings())
                return;
            
            // Do not check quickly in a row.
            // Used to avoid double checks from OnDisable and the Provider UnappliedBehaviourOnClose setting. 
            if (Time.frameCount - _lastCheckFrame < 2) // Anything within 2 frames is ignored (kinda arbitrary, could be 1 too).
                return;
            
            _lastCheckFrame = Time.frameCount;

            Provider.Settings.GetUnappliedSettings(_unappliedSettings);
            if(_unappliedSettings.Count > 0)
            {
                OnUnappliedSettingsDetected?.Invoke(_unappliedSettings);

                if (ObjectsToShowOnUnapplied != null && ObjectsToShowOnUnapplied.Count > 0)
                {
                    foreach (var gameObject in ObjectsToShowOnUnapplied)
                    {
                        if (gameObject != null)
                            gameObject.SetActive(true);
                    }
                }
            }
        }

        public void LogSettings(List<ISetting> settings)
        {
            if (settings == null || settings.Count == 0)
            {
                Debug.Log("SettingsCheckForUnapplied: Settings is null!");
                return;
            }
            
            Debug.Log("SettingsCheckForUnapplied: Unapplied settings found:");
            foreach (var setting in settings)
            {
                Debug.Log( " * " + setting.GetID());
            }
        }

        #region Editor Stuff
#if UNITY_EDITOR
        public void Reset()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            editorAutoSelectProvider();
        }

        bool _editorOnValidated;

        public void OnValidate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            var prefabStatus = PrefabUtility.GetPrefabInstanceStatus(this.gameObject);
            if (prefabStatus != PrefabInstanceStatus.Connected)
                return;

            if (!_editorOnValidated)
            {
                _editorOnValidated = true;
                editorAutoSelectProvider();
            }
        }

        protected void editorAutoSelectProvider()
        {
            if (Provider == null)
            {
                var provider = SettingsProvider.LastUsedSettingsProvider;

                if (provider == null)
                {
                    var providerGuids = AssetDatabase.FindAssets("t:SettingsProvider");
                    if (providerGuids.Length > 0)
                    {
                        var path = AssetDatabase.GUIDToAssetPath(providerGuids[0]);
                        if (path != null)
                        {
                            provider = AssetDatabase.LoadAssetAtPath<SettingsProvider>(path);
                        }
                    }
                }

                if (provider != null)
                {
                    Provider = provider;
                    Logger.LogMessage($"Just for info: SettingsCheckOnDisable has automatically chosen the provider: \"{provider.name}\".");
                    EditorUtility.SetDirty(this);
                    EditorUtility.SetDirty(this.gameObject);
                }
            }
        }
#endif
        #endregion
    }
    
#if UNITY_EDITOR
    [CustomEditor(typeof(SettingsCheckForUnapplied))]
    public class SettingsCheckForUnappliedEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("Please check your unapplied options in the settings provider. Maybe this component is not needed anymore. Please consider using SettingsProvider.OnUnappliedOnClose manually. You can still use this component of course if you prefer.", MessageType.Info);
            
            base.OnInspectorGUI();
        }
    }
#endif
}
