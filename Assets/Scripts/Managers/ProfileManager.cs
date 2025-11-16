using System;
using System.IO;
using UnityEngine;

[Serializable]
public class ProfileData
{
    public string userName = "";
    public string avatarFile = ""; // relative to profile folder
}

/// <summary>
/// Persists player profile (name + avatar) under Application.persistentDataPath/profile.
/// Provides helpers to set avatar from an external file and retrieve as Sprite.
/// </summary>
public class ProfileManager : MonoBehaviour
{
    public static ProfileManager Instance { get; private set; }

    public const int MaxAvatarSize = 512; // px, longest side

    private string ProfileDir => Path.Combine(Application.persistentDataPath, "profile");
    private string ProfileJsonPath => Path.Combine(ProfileDir, "profile.json");

    public ProfileData Data { get; private set; } = new ProfileData();

    private Sprite cachedAvatarSprite;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        // Ensure this is a root object before marking as persistent to avoid warnings
        if (transform.parent != null)
            transform.SetParent(null, true);
        DontDestroyOnLoad(gameObject);
        Load();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(ProfileJsonPath))
            {
                var json = File.ReadAllText(ProfileJsonPath);
                Data = JsonUtility.FromJson<ProfileData>(json) ?? new ProfileData();
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"ProfileManager.Load: {e.Message}");
            Data = new ProfileData();
        }

        // Warm the avatar sprite cache if file exists
        cachedAvatarSprite = TryLoadAvatarSprite();
    }

    public void Save()
    {
        try
        {
            if (!Directory.Exists(ProfileDir))
                Directory.CreateDirectory(ProfileDir);
            var json = JsonUtility.ToJson(Data, prettyPrint: true);
            File.WriteAllText(ProfileJsonPath, json);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"ProfileManager.Save: {e.Message}");
        }
    }

    /// <summary>
    /// Sets the display name and saves.
    /// </summary>
    public void SetUserName(string name)
    {
        Data.userName = name ?? string.Empty;
        Save();
    }

    /// <summary>
    /// Copy an external image file into profile directory as avatar.png, optionally downscaling.
    /// Updates Data.avatarFile and saves. Returns loaded Sprite or null on failure.
    /// </summary>
    public Sprite SetAvatarFromExternalPath(string externalPath)
    {
        if (string.IsNullOrEmpty(externalPath) || !File.Exists(externalPath))
        {
            Debug.LogWarning("ProfileManager.SetAvatarFromExternalPath: file not found.");
            return null;
        }

        try
        {
            if (!Directory.Exists(ProfileDir))
                Directory.CreateDirectory(ProfileDir);

            var bytes = File.ReadAllBytes(externalPath);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes))
            {
                UnityEngine.Object.Destroy(tex);
                Debug.LogWarning("ProfileManager: unsupported image format");
                return null;
            }

            // Downscale if needed
            Texture2D finalTex = tex;
            int longest = Mathf.Max(tex.width, tex.height);
            if (longest > MaxAvatarSize)
            {
                float scale = (float)MaxAvatarSize / longest;
                int w = Mathf.RoundToInt(tex.width * scale);
                int h = Mathf.RoundToInt(tex.height * scale);
                finalTex = ScaleTexture(tex, w, h);
                UnityEngine.Object.Destroy(tex);
            }

            byte[] png = finalTex.EncodeToPNG();
            string avatarRel = "avatar.png";
            string avatarPath = Path.Combine(ProfileDir, avatarRel);
            File.WriteAllBytes(avatarPath, png);
            Data.avatarFile = avatarRel;
            Save();

            if (cachedAvatarSprite != null)
                UnityEngine.Object.Destroy(cachedAvatarSprite);
            cachedAvatarSprite = SpriteFromTexture(finalTex);

            return cachedAvatarSprite;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"ProfileManager.SetAvatarFromExternalPath error: {e.Message}");
            return null;
        }
    }

    public Sprite GetAvatarSprite()
    {
        if (cachedAvatarSprite == null)
            cachedAvatarSprite = TryLoadAvatarSprite();
        return cachedAvatarSprite;
    }

    /// <summary>
    /// Clears the avatar. If deleteFile is true, removes the stored avatar file.
    /// </summary>
    public void ClearAvatar(bool deleteFile)
    {
        if (deleteFile && !string.IsNullOrEmpty(Data.avatarFile))
        {
            string path = Path.Combine(ProfileDir, Data.avatarFile);
            if (File.Exists(path))
            {
                try { File.Delete(path); } catch (Exception e) { Debug.LogWarning($"ProfileManager.ClearAvatar delete failed: {e.Message}"); }
            }
        }
        Data.avatarFile = string.Empty;
        Save();
        if (cachedAvatarSprite != null)
        {
            UnityEngine.Object.Destroy(cachedAvatarSprite);
            cachedAvatarSprite = null;
        }
    }

    private Sprite TryLoadAvatarSprite()
    {
        try
        {
            if (!string.IsNullOrEmpty(Data.avatarFile))
            {
                string path = Path.Combine(ProfileDir, Data.avatarFile);
                if (File.Exists(path))
                {
                    var bytes = File.ReadAllBytes(path);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (tex.LoadImage(bytes))
                    {
                        return SpriteFromTexture(tex);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"ProfileManager.TryLoadAvatarSprite: {e.Message}");
        }
        return null;
    }

    private static Texture2D ScaleTexture(Texture2D source, int targetWidth, int targetHeight)
    {
        RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight);
        Graphics.Blit(source, rt);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D result = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
        result.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
        result.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return result;
    }

    private static Sprite SpriteFromTexture(Texture2D tex)
    {
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
    }
}
