using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace Kamgam.SettingsGenerator.Examples
{
    public class SceneLoader : MonoBehaviour
    {
#if UNITY_EDITOR
        public UnityEditor.SceneAsset SceneToLoad;
#endif
        public string SceneName;
        public bool LoadAdditively = true;
        public float Delay = 0f;

        protected SettingInt audioMusicVolumeSetting;

        void Start()
        {
            if (Delay > 0.001f)
            {
                StartCoroutine(LoadDelayed(Delay));
            }
            else
            {
                Load();
            }
        }

        public IEnumerator LoadDelayed(float delay)
        {
            yield return new WaitForSeconds(delay);
            Load();
        }
        
        public void Load()
        {
#if UNITY_EDITOR
            SceneName = SceneToLoad.name;
            EditorSceneManager.LoadSceneInPlayMode(
                UnityEditor.AssetDatabase.GetAssetPath(SceneToLoad),
                new LoadSceneParameters(LoadAdditively ? LoadSceneMode.Additive : LoadSceneMode.Single)
                );
#else
            SceneManager.LoadScene(
                SceneName,
                LoadAdditively ? LoadSceneMode.Additive : LoadSceneMode.Single
            );
#endif
        }

#if UNITY_EDITOR
        public void OnValidate()
        {
            if (SceneToLoad != null && SceneToLoad.name != null)
                SceneName = SceneToLoad.name;
        }
#endif
    }
}
