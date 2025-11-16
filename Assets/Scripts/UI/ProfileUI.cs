using UnityEngine;
using UnityEngine.UI;

#if PHOTON_UNITY_NETWORKING
using Photon.Pun;
#endif

#if UNITY_EDITOR
using UnityEditor; // for EditorUtility.OpenFilePanel
#endif

/// <summary>
/// Hook this to the Profile panel. Connect references to the InputField and Image.
/// Provides buttons for choosing avatar and saving name.
/// </summary>
public class ProfileUI : MonoBehaviour
{
    private const string ImageFilters = "png,jpg,jpeg";
    private const string PlayerNameKey = "player_name";

    [Header("UI Refs")]
    [SerializeField] private InputField nameField; // Legacy InputField per project
    [SerializeField] private Image avatarImage;    // Where to preview avatar
    [SerializeField] private Button saveProfileButton; // Hidden until name changes
    [SerializeField] private Button revertProfileButton; // Hidden until a change exists, discards pending changes

    private string _initialName = string.Empty;
    private string _baselineAvatarFile = string.Empty;
    private string _pendingAvatarPath = null;
    private bool _clearAvatarPending = false;

    private void OnEnable()
    {
        // Load name preference (prefer PlayerPrefs if present)
        _initialName = ProfileManager.Instance != null ? ProfileManager.Instance.Data.userName : string.Empty;
        if (PlayerPrefs.HasKey(PlayerNameKey))
            _initialName = PlayerPrefs.GetString(PlayerNameKey, _initialName);

        if (nameField) nameField.text = _initialName;

        // Establish avatar baseline and update preview from stored ProfileManager state
        _baselineAvatarFile = ProfileManager.Instance != null ? (ProfileManager.Instance.Data.avatarFile ?? string.Empty) : string.Empty;
        _pendingAvatarPath = null;
        _clearAvatarPending = false;

        UpdateUIFromProfile();

        if (nameField)
            nameField.onValueChanged.AddListener(OnNameChanged);

        UpdateSaveButtonVisibility();
    }

    private void OnDisable()
    {
        if (nameField)
            nameField.onValueChanged.RemoveListener(OnNameChanged);
    }

    /// <summary>
    /// Reverts unsaved changes (name input and pending avatar selection/clear) back to baseline.
    /// Does NOT modify persisted data.
    /// </summary>
    public void RevertChanges()
    {
        // Restore name text to baseline
        if (nameField) nameField.text = _initialName;

        // Clear pending avatar operations
        _pendingAvatarPath = null;
        _clearAvatarPending = false;

        // Restore avatar preview from current persisted profile
        if (ProfileManager.Instance != null)
        {
            var sprite = ProfileManager.Instance.GetAvatarSprite();
            UpdateAvatar(sprite);
            _baselineAvatarFile = ProfileManager.Instance.Data.avatarFile ?? string.Empty;
        }
        else
        {
            UpdateAvatar(null);
            _baselineAvatarFile = string.Empty;
        }
        UpdateSaveButtonVisibility();
    }

    public void SaveName()
    {
        // Backward-compat for existing button hookups
        SaveProfile();
    }

    public void SaveProfile()
    {
        if (ProfileManager.Instance == null) return;
        var text = nameField ? (nameField.text ?? string.Empty).Trim() : string.Empty;
        // Store into PlayerPrefs
        PlayerPrefs.SetString(PlayerNameKey, text);
        PlayerPrefs.Save();

        // Persist via ProfileManager as well
        ProfileManager.Instance.SetUserName(text);

    #if PHOTON_UNITY_NETWORKING
        // Apply to Photon nickname so matchmaking UI can show it
        PhotonNetwork.NickName = text;
    #endif

        // Persist avatar changes if any
        if (_clearAvatarPending)
        {
            ProfileManager.Instance.ClearAvatar(true);
        }
        else if (!string.IsNullOrEmpty(_pendingAvatarPath))
        {
            ProfileManager.Instance.SetAvatarFromExternalPath(_pendingAvatarPath);
        }

        // Reset baseline and refresh UI
        _initialName = text;
        _baselineAvatarFile = ProfileManager.Instance.Data.avatarFile ?? string.Empty;
        _pendingAvatarPath = null;
        _clearAvatarPending = false;
        UpdateUIFromProfile();
        UpdateSaveButtonVisibility();
    }

    public void ChooseAvatarFromDevice()
    {
        // Try Editor picker first
#if UNITY_EDITOR
        string path = EditorUtility.OpenFilePanel("Choose Profile Picture", "", ImageFilters);
        if (!string.IsNullOrEmpty(path))
            PreviewPendingAvatar(path);
        else
            Debug.Log("ProfileUI: File selection canceled");
#else
#if SIMPLE_FILE_BROWSER
        // Optional support if SimpleFileBrowser is imported (https://github.com/yasirkula/UnitySimpleFileBrowser)
        SimpleFileBrowser.FileBrowser.SetFilters(true, new SimpleFileBrowser.FileBrowser.Filter("Images", ".png", ".jpg", ".jpeg"));
        SimpleFileBrowser.FileBrowser.ShowLoadDialog((paths) => {
            if (paths != null && paths.Length > 0)
                PreviewPendingAvatar(paths[0]);
        }, () => { }, SimpleFileBrowser.FileBrowser.PickMode.Files);
#elif SFB
    // Optional support if StandaloneFileBrowser is imported (https://github.com/gkngkc/UnityStandaloneFileBrowser)
    var extensions = new [] { new SFB.ExtensionFilter("Images", new[] { "png", "jpg", "jpeg" }) };
    var paths = SFB.StandaloneFileBrowser.OpenFilePanel("Choose Profile Picture", "", extensions, false);
    if (paths != null && paths.Length > 0 && !string.IsNullOrEmpty(paths[0]))
        PreviewPendingAvatar(paths[0]);
#else
        Debug.LogWarning("Runtime file picker not available. Import a runtime file browser (e.g., SimpleFileBrowser) or set avatar programmatically.");
#endif
#endif
    }

    public void ClearAvatar()
    {
        // Mark clear pending; defer actual delete to SaveProfile
        _pendingAvatarPath = null;
        _clearAvatarPending = true;
        // Update preview to none
        UpdateAvatar(null);
        UpdateSaveButtonVisibility();
    }

    private void PreviewPendingAvatar(string path)
    {
        _pendingAvatarPath = path;
        _clearAvatarPending = false;

        try
        {
            var bytes = System.IO.File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (tex.LoadImage(bytes))
            {
                var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                UpdateAvatar(sprite);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"ProfileUI PreviewPendingAvatar error: {e.Message}");
        }

        UpdateSaveButtonVisibility();
    }

    private void UpdateUIFromProfile()
    {
        if (ProfileManager.Instance == null) return;
        // Name already set in OnEnable (prefers PlayerPrefs)
        var sprite = ProfileManager.Instance.GetAvatarSprite();
        UpdateAvatar(sprite);
    }

    private void UpdateAvatar(Sprite sprite)
    {
        if (!avatarImage) return;
        avatarImage.sprite = sprite;
        avatarImage.enabled = sprite != null;
    }

    private void OnNameChanged(string _)
    {
        UpdateSaveButtonVisibility();
    }

    private void UpdateSaveButtonVisibility()
    {
        if (!saveProfileButton && !revertProfileButton) return;
        string current = nameField ? (nameField.text ?? string.Empty).Trim() : string.Empty;
        bool nameChanged = !string.Equals(current, _initialName);
        bool avatarChanged = _clearAvatarPending || !string.IsNullOrEmpty(_pendingAvatarPath);
        bool changed = nameChanged || avatarChanged;
        if (saveProfileButton)
        {
            saveProfileButton.gameObject.SetActive(changed);
            saveProfileButton.interactable = changed;
        }
        if (revertProfileButton)
        {
            revertProfileButton.gameObject.SetActive(changed);
            revertProfileButton.interactable = changed;
        }
    }
}
