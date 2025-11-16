using UnityEngine;

// Attach this to any always-present object in the main scene (e.g., GameManager)
public class AudioTagMuteEnforcer : MonoBehaviour
{
    void Start()
    {
        ApplyMuteState();
    }

    void OnEnable()
    {
        ApplyMuteState();
    }

    private void ApplyMuteState()
    {
        MuteByTag("BGM", AudioSettingsGlobal.MusicMuted);
        MuteByTag("SFX", AudioSettingsGlobal.SfxMuted);
    }

    private void MuteByTag(string tag, bool mute)
    {
        var sources = GameObject.FindGameObjectsWithTag(tag);
        foreach (var go in sources)
        {
            var audioSources = go.GetComponents<AudioSource>();
            foreach (var src in audioSources)
                src.mute = mute;
        }
    }
}