using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Guarantees there is exactly one EventSystem alive at runtime.
/// Attach this to the EventSystem in your first loaded scene.
/// It will persist across scene loads and destroy any duplicates that appear.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-10000)] // run very early
public class EnsureSingleEventSystem : MonoBehaviour
{
    private static EventSystem s_existing;

    private void Awake()
    {
        var current = GetComponent<EventSystem>();
        if (current == null)
        {
            Debug.LogWarning("EnsureSingleEventSystem is on a GameObject without an EventSystem. Removing component.");
            Destroy(this);
            return;
        }

        if (s_existing != null && s_existing != current)
        {
            // Another EventSystem already exists; destroy this duplicate.
            Debug.LogWarning("Duplicate EventSystem detected. Destroying the newer one on '" + gameObject.name + "'.");
            Destroy(gameObject);
            return;
        }

        // Keep this one and persist across scenes.
        s_existing = current;
        DontDestroyOnLoad(gameObject);
    }
}
