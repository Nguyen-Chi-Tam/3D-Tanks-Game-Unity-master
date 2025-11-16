using System.IO;
using UnityEditor;
using UnityEngine;

public static class ClearSavedData
{
    [MenuItem("Tools/Clear Saved Data/Clear PlayerPrefs (Editor)")]
    public static void ClearEditorPlayerPrefs()
    {
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
        EditorUtility.DisplayDialog("Clear PlayerPrefs", "Editor PlayerPrefs cleared.", "OK");
    }

    [MenuItem("Tools/Clear Saved Data/Open Save Folder")] 
    public static void OpenSaveFolder()
    {
        EditorUtility.RevealInFinder(Application.persistentDataPath);
    }

    [MenuItem("Tools/Clear Saved Data/Clear Profile Folder")] 
    public static void ClearProfileFolder()
    {
        string profileDir = Path.Combine(Application.persistentDataPath, "profile");
        if (Directory.Exists(profileDir))
        {
            try
            {
                Directory.Delete(profileDir, true);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Failed to delete profile folder: {e.Message}");
            }
        }
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Clear Profile", "Profile folder under persistentDataPath removed.", "OK");
    }

    [MenuItem("Tools/Clear Saved Data/Clear All (Prefs + Profile)")]
    public static void ClearAll()
    {
        ClearEditorPlayerPrefs();
        ClearProfileFolder();
    }
}
