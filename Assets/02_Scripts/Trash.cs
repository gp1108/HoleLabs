using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Small debug helper used to reload the current scene or quit the application with keyboard shortcuts.
/// </summary>
public sealed class Trash : MonoBehaviour
{
    /// <summary>
    /// Checks debug hotkeys every frame.
    /// P reloads the active scene.
    /// K quits the application.
    /// </summary>
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.P))
        {
            ReloadCurrentScene();
        }

        if (Input.GetKeyDown(KeyCode.K))
        {
            QuitApplication();
        }
    }

    /// <summary>
    /// Reloads the currently active scene.
    /// </summary>
    private void ReloadCurrentScene()
    {
        Scene CurrentScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(CurrentScene.buildIndex);
    }

    /// <summary>
    /// Closes the application.
    /// In the Unity Editor this will not stop Play Mode unless you add editor-only code.
    /// </summary>
    private void QuitApplication()
    {
        Application.Quit();
    }
}