using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI; // For Image

public class MenuUI : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject gameModes; // Assign in Inspector (GameModes panel root)
    [SerializeField] private GameObject multiplayerGameModesRoot; // Parent that holds GameModes + Warning
    [SerializeField] private GameObject multiplayerWarningPanel;  // Panel shown when name missing

    [Header("Audio Settings UI")]
    [SerializeField] private GameObject musicDisabledIcon; // The red crossed music image
    [SerializeField] private GameObject audioDisabledIcon; // The red crossed audio image

    [Header("Profile")]
    [SerializeField] private Image avatarImage; // Assign the avatar image UI component
    [SerializeField] private Sprite defaultAvatarSprite; // Assign a default sprite to show when none set

    private const string PlayerNameKey = "player_name";

    public void OpenGameModes()
    {
        if (!multiplayerGameModesRoot)
        {
            Debug.LogWarning("MenuUI.OpenGameModes: MultiplayerGameModesRoot reference missing.");
            return;
        }

        // Root must be visible first
        multiplayerGameModesRoot.SetActive(true);

        // Decide which child panel to show based on PlayerPrefs name
        bool hasName = HasStoredPlayerName();

        if (gameModes) gameModes.SetActive(hasName);
        if (multiplayerWarningPanel) multiplayerWarningPanel.SetActive(!hasName);
    }

    public void CloseGameModes()
    {
        if (gameModes) gameModes.SetActive(false);
    }

    // --- Mode Selection Buttons ---
    public void Start_1v1_Player()
    {
        GameSettings.SelectedMode = GameManager.GameMode.OneVsOne_PvP;
        LoadMainScene();
    }

    public void Start_1v1_Bot()
    {
        GameSettings.SelectedMode = GameManager.GameMode.OneVsOne_PvE;
        LoadMainScene();
    }

    public void Start_5v5_Player()
    {
        GameSettings.SelectedMode = GameManager.GameMode.FiveVsFive_PvP;
        LoadMainScene();
    }

    public void Start_5v5_Bot()
    {
        GameSettings.SelectedMode = GameManager.GameMode.FiveVsFive_PvE;
        LoadMainScene();
    }

    private void LoadMainScene()
    {
        // Assumes a scene named "Main" is added to Build Settings
        SceneManager.LoadScene("Main");
    }

    // --- Audio toggles ---

    public void ToggleMusic()
    {
        AudioSettingsGlobal.MusicMuted = !AudioSettingsGlobal.MusicMuted;
        MuteByTag("BGM", AudioSettingsGlobal.MusicMuted);
        if (musicDisabledIcon) musicDisabledIcon.SetActive(AudioSettingsGlobal.MusicMuted);
    }

    public void ToggleSound()
    {
        AudioSettingsGlobal.SfxMuted = !AudioSettingsGlobal.SfxMuted;
        MuteByTag("SFX", AudioSettingsGlobal.SfxMuted);
        if (audioDisabledIcon) audioDisabledIcon.SetActive(AudioSettingsGlobal.SfxMuted);
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

    private void OnEnable()
    {
        // Ensure icons reflect current state when opening menu
        if (musicDisabledIcon) musicDisabledIcon.SetActive(AudioSettingsGlobal.MusicMuted);
        if (audioDisabledIcon) audioDisabledIcon.SetActive(AudioSettingsGlobal.SfxMuted);
        // Enforce mute state on all tagged sources
        MuteByTag("BGM", AudioSettingsGlobal.MusicMuted);
        MuteByTag("SFX", AudioSettingsGlobal.SfxMuted);
        // Refresh avatar (show default if empty)
        RefreshAvatar();
    }

    private void RefreshAvatar()
    {
        if (!avatarImage) return;

        // If there is profile logic elsewhere, replace this condition with a check
        // against the player's saved avatar sprite/texture. For now we just ensure
        // the UI isn't left blank.
        if (avatarImage.sprite == null && defaultAvatarSprite != null)
        {
            avatarImage.sprite = defaultAvatarSprite;
        }
    }

    /// <summary>
    /// Returns true if a non-empty player name exists in PlayerPrefs.
    /// Does not modify any UI state.
    /// </summary>
    private bool HasStoredPlayerName()
    {
        string name = string.Empty;
        if (PlayerPrefs.HasKey(PlayerNameKey))
            name = PlayerPrefs.GetString(PlayerNameKey, string.Empty);

        return !string.IsNullOrWhiteSpace(name);
    }
}
