#if ENABLE_INPUT_SYSTEM && UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem;
using System.Text.RegularExpressions;
using UnityEngine.InputSystem.Composites;

namespace Kamgam.SettingsGenerator
{
    public static class InputBindingConnectionCreator
    {
        [MenuItem("Tools/Settings Generator/Create InputBindingConnections", priority = 1)]
        [MenuItem("Assets/Settings Generator/Create InputBindingConnections", priority = 10000)]
        [MenuItem("Assets/Create/SettingsGenerator/Create InputBindingConnections", priority = 10000)]
        public static void CreateBindingConnectionAssets()
        {
            var asset = Selection.activeObject as InputActionAsset;
            CreateBindingConnectionAssets(asset);
        }

        public static void CreateBindingConnectionAssets(InputActionAsset asset)
        {
            if (asset == null)
            {
                // This message is for both. No selection on right click and/or no selection in the provider.
                EditorUtility.DisplayDialog("No InputActionAsset selected.", "Please select the InputActionAsset you want to create the connections from.", "OK");
                return;
            }
            
            var provider = SettingsGeneratorSettings.GetProvider();
            var connections = new List<(InputBindingConnectionSO, string)>();

            var assetPath = AssetDatabase.GetAssetPath(asset);
            foreach (var binding in asset.bindings)
            {
                // Composite bindings are not supported. See manual page ~87 (we can not use ApplyBindingOverrideWithResult() on compositions).
                if (!IsBindingSupported(binding))
                    continue;
                
                var map = asset.GetActionMapOfBinding(binding.id.ToString());
                
                // Skip UI bindings?
                if (!provider.IncludeUIBindingsInAutoCreation && map != null && map.name.ToLower() == "ui")
                    continue;

                string fullBindingPath = map.name + "." + binding.action + "." + (binding.isComposite ? binding.name : binding.path);
                fullBindingPath = Regex.Replace(fullBindingPath, "[<>]+", "");
                fullBindingPath = Regex.Replace(fullBindingPath, "[*\\\\/]+", ".");
                var fileName = string.Format("InputBindingConnection ({0}).asset", fullBindingPath);

                var dir = System.IO.Path.GetDirectoryName(assetPath) + "/SettingsInputBindingConnections/";
                EditorRuntimeUtils.CreateFolder(dir);

                var filePath = dir + fileName;
                // Update if it already exists
                var connectionSO = AssetDatabase.LoadAssetAtPath<InputBindingConnectionSO>(filePath);
                if (connectionSO == null)
                {
                    connectionSO = ScriptableObject.CreateInstance<InputBindingConnectionSO>();
                    connectionSO.InputActionAsset = asset;
                    connectionSO.BindingId = binding.id.ToString();
                    Undo.RegisterCreatedObjectUndo(connectionSO, "Connection Binding Object");
                    AssetDatabase.CreateAsset(connectionSO, filePath);
                }
                connectionSO.InputActionAsset = asset;
                connectionSO.BindingId = binding.id.ToString();
                EditorUtility.SetDirty(connectionSO);
                EditorGUIUtility.PingObject(connectionSO);
                
                connections.Add((connectionSO, fullBindingPath));
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.FocusProjectWindow();
            
            // Ask user to add the connections to the default settings.
            if (connections.Count > 0)
            {
                bool createSettings = EditorUtility.DisplayDialog(
                    "Add bindings to settings list?",
                    "Would you like to add the created connections to your default settings list:\n" +
                    "'" + SettingsGeneratorSettings.GetProvider().name + "'?",
                    "Yes (recommended)", "No (I'll manage them manually");

                if (createSettings)
                {
                    var settings = provider.SettingsAsset;
                    Undo.RegisterCompleteObjectUndo(settings, "Add input bindings to settings.");
                    foreach (var t in connections)
                    {
                        var connectionSO = t.Item1;
                        var sanitizedBindingPath = t.Item2;
                        string id = "InputBinding." + sanitizedBindingPath;
                        var setting = settings.GetOrCreateString(id);
                        Logger.LogMessage("Adding or updating setting " + id + " in " + provider.name);
                        setting.SetConnectionSO(connectionSO);
                    }
                    EditorUtility.SetDirty(settings);
                    AssetDatabase.SaveAssetIfDirty(settings);
                }
            }
        }

        public static bool IsBindingSupported(InputBinding binding)
        {
            if (!binding.isComposite)
                return true;
            
            var compositeType = InputSystem.TryGetBindingComposite( binding.effectivePath );
            // Only "OneModifier" composites are supported.
            if (compositeType != typeof(OneModifierComposite))
            {
                return false;
            }

            return true;
        }

        [MenuItem("Assets/Settings Generator/Create InputBindingConnections", true)]
        static bool ValidateCreateBindingConnectionAssets()
        {
            return Selection.activeObject is InputActionAsset;
        }
    }
}

#endif
