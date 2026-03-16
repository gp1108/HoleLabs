using UnityEngine;

public class FpsLimiter : MonoBehaviour
{
    private void Awake()
    {
        QualitySettings.vSyncCount = 1;
        Application.targetFrameRate =-1;
    }
}