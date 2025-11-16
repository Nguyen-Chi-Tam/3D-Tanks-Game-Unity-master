using UnityEngine;

/// <summary>
/// Global audio preferences toggled from the Menu.
/// Persist in memory; can be saved to PlayerPrefs in future if desired.
/// </summary>
public static class AudioSettingsGlobal
{
    public static bool MusicMuted = false;
    public static bool SfxMuted = false;

    public static void ApplyToAllAudioSources()
    {
        var sources = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
        foreach (var src in sources)
        {
            if (IsMusicSource(src))
            {
                src.mute = MusicMuted;
            }
            else if (IsTankSfxSource(src))
            {
                src.mute = SfxMuted;
            }
            // Other audio (UI clicks etc.) left unchanged for now.
        }
    }

    // Heuristics so we don't have to tag every source:
    // - Any AudioSource on an object named contains "Music" or with AudioMixer group name containing "Music" is treated as music
    // - GameManager's dedicated SFX source used for game-end clip is treated as music when playing that clip
    public static bool IsMusicSource(AudioSource src)
    {
        if (src == null) return false;
    // Explicit tag component overrides heuristics.
    if (src.GetComponent<MusicSourceTag>() != null) return true;
    string n = src.gameObject.name.ToLowerInvariant();
        if (n.Contains("music") || n.Contains("bgm") || n.Contains("menu music")) return true;
        // If mixer group is named e.g. "Music"
        if (src.outputAudioMixerGroup != null)
        {
            var g = src.outputAudioMixerGroup;
            if (g != null && g.name.ToLowerInvariant().Contains("music")) return true;
        }
        return false;
    }

    public static bool IsTankSfxSource(AudioSource src)
    {
        if (src == null) return false;
        // Tank movement/shooting/explosion usually lives on objects with these components
        if (src.GetComponentInParent<TankShooting>() != null) return true;
        if (src.GetComponentInParent<TankMovement>() != null) return true;
        if (src.GetComponentInParent<ShellExplosion>() != null) return true;
        if (src.gameObject.name.ToLowerInvariant().Contains("explosion")) return true;
        return false;
    }
}
