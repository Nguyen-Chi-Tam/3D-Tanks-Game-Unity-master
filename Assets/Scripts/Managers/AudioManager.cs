using UnityEngine;
using UnityEngine.Audio; // for AudioMixerGroup (used implicitly via AudioSource)

/// <summary>
/// Simple singleton audio manager that centralizes SFX playback so other scripts don't
/// accidentally use/modify scene AudioSources (like the GameManager music source).
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Audio Sources")]
    [Tooltip("Optional: assign a music AudioSource here. If left empty one will be created.")]
    public AudioSource musicSource;

    [Header("Behavior")]
    [Tooltip("If enabled, forces the music source to bypass any assigned Audio Mixer to prevent ducking/side-chain effects.")]
    public bool bypassMixerForMusic = true;

    private AudioSource sfxSource;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (musicSource == null)
        {
            // If there's an AudioSource on this object, use it for music.
            musicSource = GetComponent<AudioSource>();
            if (musicSource == null)
                musicSource = gameObject.AddComponent<AudioSource>();
        }

        // Always detach from any AudioMixerGroup so mixer-side ducking/side-chain compression cannot affect music.
        if (musicSource != null)
        {
            musicSource.outputAudioMixerGroup = null;
            musicSource.spatialBlend = 0f;
            musicSource.dopplerLevel = 0f;
        }

        // Create dedicated SFX source
        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
        sfxSource.loop = false;
    }

    /// <summary>
    /// Play a one-shot SFX via the dedicated SFX AudioSource.
    /// </summary>
    public void PlaySfx(AudioClip clip, float volume = 1f)
    {
        if (clip == null || sfxSource == null)
            return;

        sfxSource.PlayOneShot(clip, volume);
    }

    /// <summary>
    /// Optional helpers for music control.
    /// </summary>
    public void PlayMusic(AudioClip clip, bool loop = true)
    {
        if (musicSource == null)
            return;

        musicSource.clip = clip;
        musicSource.loop = loop;
        musicSource.Play();
    }

    public void StopMusic()
    {
        if (musicSource == null)
            return;

        musicSource.Stop();
    }
}
