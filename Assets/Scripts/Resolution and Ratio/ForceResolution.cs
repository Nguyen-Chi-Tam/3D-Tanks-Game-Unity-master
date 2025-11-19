using UnityEngine;

public class ForceResolution : MonoBehaviour
{
    void Awake()
    {
        // Set to 1920x1080, fullscreen, maintain aspect ratio
        Screen.SetResolution(1920, 1080, true);
    }
}