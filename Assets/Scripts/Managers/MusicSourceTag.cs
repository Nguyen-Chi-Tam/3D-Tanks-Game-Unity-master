using UnityEngine;

/// <summary>
/// Marker component to classify an AudioSource as Music so global Music toggle can target it reliably.
/// Attach this to any GameObject that has an AudioSource for background/menu music.
/// </summary>
[DisallowMultipleComponent]
public class MusicSourceTag : MonoBehaviour { }
