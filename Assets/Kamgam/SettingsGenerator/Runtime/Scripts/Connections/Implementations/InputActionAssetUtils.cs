#if ENABLE_INPUT_SYSTEM
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Kamgam.SettingsGenerator
{
    public static class InputActionAssetUtils
    {
        public static float _lastCacheTime = -100_000f;
        public static List<InputActionAsset> _cachedResults = new List<InputActionAsset>();

        /// <summary>
        /// Finds all input action asset and checks if their binding ids match the baseAsset. Returns only those that match.
        /// </summary>
        /// <param name="baseAsset"></param>
        /// <param name="results"></param>
        /// <param name="cacheDurationInSec"></param>
        /// <returns></returns>
        public static List<InputActionAsset> FindInstancesOf(InputActionAsset baseAsset, List<InputActionAsset> results = null, float cacheDurationInSec = 0f)
        {
            if (results == null)
                results = new List<InputActionAsset>();

            results.Clear();
            
            // Copy from cache.
            if (cacheDurationInSec > 0.001f && Time.realtimeSinceStartup - _lastCacheTime < cacheDurationInSec)
            {
                results.AddRange(_cachedResults);
                return results;
            }

            _cachedResults.Clear();
            
            var name = baseAsset.name;
            var assets = Resources.FindObjectsOfTypeAll<InputActionAsset>();

            // The generated class name
            var baseFirstBindingId = getFirstBindingGuid(baseAsset);
            foreach (var asset in assets)
            {
                // We use the first binding id to check if the found instance is a copy of the base. If not then ignore. 
                if (getFirstBindingGuid(asset) != baseFirstBindingId)
                {
                    Logger.LogInEditorOnly("Ignoring input action asset '" + asset.name + "' due to differentiating binding ids.");
                    continue;
                }

                results.Add(asset);
                _cachedResults.Add(asset);
            }

            return results;
        }

        static Guid getFirstBindingGuid(InputActionAsset asset)
        {
            var enumerator = asset.bindings.GetEnumerator();
            if (enumerator.MoveNext())
            {
                return enumerator.Current.id;
            }

            return default;
        }
    }
}

#endif
