using UnityEngine;

/// <summary>
/// Global runtime selection for game mode made from the Menu scene.
/// Persisted only in-memory between scene loads.
/// </summary>
public static class GameSettings
{
    public static GameManager.GameMode SelectedMode = GameManager.GameMode.OneVsOne_PvP;
}
