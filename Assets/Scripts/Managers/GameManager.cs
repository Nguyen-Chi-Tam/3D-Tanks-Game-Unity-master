// 07/11/2025 AI-Tag
// This was created with the help of Assistant, a Unity Artificial Intelligence product.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public enum GameMode
    {
        OneVsOne_PvP,   // Two human players
        OneVsOne_PvE,   // One human vs one bot
        // FiveVsFive_PvP, // Ten human players (5 per side) – requires 10 spawn points
        FiveVsFive_PvP,
        FiveVsFive_PvE// Two humans (one per team), rest are bots
    }

    [Header("Game Mode")]
    public GameMode m_GameMode = GameMode.OneVsOne_PvP;
    public int m_NumRoundsToWin = 3;             // The number of rounds a single player has to win to win the game.
    public int m_MaxRounds = 5;                 // The maximum number of rounds in the game.
    public float m_StartDelay = 3f;             // The delay between the start of RoundStarting and RoundPlaying phases.
    public float m_EndDelay = 3f;               // The delay between the end of RoundPlaying and RoundEnding phases.
    public CameraControl m_CameraControl;       // Reference to the CameraControl script for control during different phases.
    public Text m_MessageText;                  // Reference to the overlay Text to display winning text, etc.
    public GameObject m_TankPrefab;             // Reference to the prefab the players will control.
    public TankManager[] m_Tanks;               // A collection of managers for enabling and disabling different aspects of the tanks.

    [Header("Spawn Groups (Optional)")]
    [Tooltip("Root transform whose first 5 children are Blue team spawn points (FiveVsFive_Mixed) or first child for PvE human")] public Transform m_BlueTeamSpawnRoot;
    [Tooltip("Root transform whose first 5 children are Red team spawn points (FiveVsFive_Mixed) or first child for PvE bot")] public Transform m_RedTeamSpawnRoot;

    [Header("Heart Spawning")]
    [Tooltip("Prefab for the health-restoring Heart power-up.")] public GameObject m_HeartPrefab;
    [Tooltip("Optional fixed spawn points for hearts. If empty, will use tank spawn points.")]
    public Transform[] m_HeartSpawnPoints;
    [Tooltip("Seconds between heart spawns during a round.")]
    public float m_HeartSpawnInterval = 20f;
    [Tooltip("Vertical offset applied when spawning the heart so it appears slightly above the ground.")]
    public float m_HeartSpawnVerticalOffset = 0.5f;

    [Header("Audio")]
    [Tooltip("Sound to play when the game ends (someone wins or max rounds reached)")]
    public AudioClip m_GameEndClip;

    [Header("HUD / Overlay")]
    [Tooltip("If true, show live team counts in center during team modes. If false, keep center clear during play.")]
    public bool m_ShowTeamCountOverlay = false;

    [Header("Match End UI")]
    [Tooltip("Container (panel) holding Rematch / Back to Menu buttons. Hidden until the game (match) ends.")]
    public GameObject m_MatchEndButtons;

    [Header("Pause UI")]
    [Tooltip("Panel shown when the game is paused (Time.timeScale=0).")]
    public GameObject m_GamePausedPanel;

    private AudioSource m_AudioSource;          // (Optional) existing AudioSource on this object (may be music)
    private AudioSource m_SfxSource;            // Dedicated AudioSource for one-shot SFX (game-end clip)
    private List<AudioSource> m_PausedAudioSources = new List<AudioSource>();
    private int m_RoundNumber;                  // Which round the game is currently on.
    private WaitForSeconds m_StartWait;         // Used to have a delay whilst the round starts.
    private WaitForSeconds m_EndWait;           // Used to have a delay whilst the round or game ends.
    private TankManager m_RoundWinner;          // Reference to the winner of the current round. Used to make an announcement of who won.
    private TankManager m_GameWinner;           // Reference to the winner of the game. Used to make an announcement of who won.
    private bool m_GameOver;                    // True when the game has finished (someone won or max rounds reached)
    private bool m_RoundActive;                 // True while RoundPlaying is in progress (used for heart spawning loop).
    private int m_ActiveTankCount;              // Number of tanks actually spawned for current mode.
    private Coroutine m_TeamCountRoutine;       // UI updater for showing live team counts during team modes.
    private bool m_IsPaused;                    // True while player has paused the match.

    private struct SpawnSpec
    {
        public int playerNumber;   // Used for input mapping and UI
        public bool isHuman;       // Human-controlled vs AI
    }

    private void Start()
    {
        // If a menu selected a mode earlier, apply it.
        try { m_GameMode = GameSettings.SelectedMode; } catch { /* GameSettings may not exist in editor tests */ }

        // Create the delays so they only have to be made once.
        m_StartWait = new WaitForSeconds(m_StartDelay);
        m_EndWait = new WaitForSeconds(m_EndDelay);

        // Ensure there's an AudioSource to play sounds from this manager.
        // Try to keep any existing AudioSource (likely used for background music) but also
        // create a dedicated SFX AudioSource so we can play the game-end clip while pausing music.
        m_AudioSource = GetComponent<AudioSource>();
        if (m_AudioSource == null)
            m_AudioSource = gameObject.AddComponent<AudioSource>();
        // Mark this as music for global mute logic
        if (m_AudioSource != null && m_AudioSource.GetComponent<MusicSourceTag>() == null)
            m_AudioSource.gameObject.AddComponent<MusicSourceTag>();

        // SFX source: don't play on awake or loop
        m_SfxSource = gameObject.AddComponent<AudioSource>();
        m_SfxSource.playOnAwake = false;
        m_SfxSource.loop = false;

        // Auto-configure spawn points for certain modes if user hasn't manually set them.
        AutoConfigureOneVsOnePvEFromScene();
    AutoConfigureFiveVsFiveMixedFromScene();

        ValidateSpawnConfiguration();

        SpawnAllTanks();
        SetCameraTargets();

        // Once the tanks have been created and the camera is using them as targets, start the game.
        StartCoroutine(GameLoop());

        // Hide match-end buttons until the entire game concludes.
        if (m_MatchEndButtons)
            m_MatchEndButtons.SetActive(false);

        // Hide pause panel at start.
        if (m_GamePausedPanel)
            m_GamePausedPanel.SetActive(false);
    }

    private bool IsTeamMode()
    {
        return m_GameMode == GameMode.FiveVsFive_PvP || m_GameMode == GameMode.FiveVsFive_PvE;
    }

    private void AutoConfigureFiveVsFiveMixedFromScene()
    {
        // Support both 5v5 modes: PvP and PvE need the same spawn layout (5 spawns per team)
        if (m_GameMode != GameMode.FiveVsFive_PvP && m_GameMode != GameMode.FiveVsFive_PvE)
            return;

        // If already configured with 10 tanks having spawn points, keep user's setup.
        if (m_Tanks != null && m_Tanks.Length >= 10)
        {
            int assigned = 0;
            for (int i = 0; i < 10; i++)
            {
                if (i < m_Tanks.Length && m_Tanks[i] != null && m_Tanks[i].m_SpawnPoint != null)
                    assigned++;
            }
            if (assigned == 10)
                return;
        }

        // Try to find grouped spawn parents by name.
        var blueParent = m_BlueTeamSpawnRoot != null ? m_BlueTeamSpawnRoot : GameObject.Find("BlueTeamSpawnPoints")?.transform;
        var redParent = m_RedTeamSpawnRoot != null ? m_RedTeamSpawnRoot : GameObject.Find("RedTeamSpawnPoints")?.transform;
        if (blueParent == null || redParent == null)
        {
            Debug.LogWarning("[GameManager] FiveVsFive: Could not find 'BlueTeamSpawnPoints' and 'RedTeamSpawnPoints' in scene. Using existing m_Tanks setup if any.");
            return;
        }

        // Collect first 5 children (hierarchy order) from each parent.
        var blue = new List<Transform>(5);
        var red = new List<Transform>(5);
        for (int i = 0; i < blueParent.childCount && blue.Count < 5; i++)
            blue.Add(blueParent.GetChild(i));
        for (int i = 0; i < redParent.childCount && red.Count < 5; i++)
            red.Add(redParent.GetChild(i));

        if (blue.Count < 5 || red.Count < 5)
        {
            Debug.LogWarning("[GameManager] FiveVsFive: Need 5 spawn points under each team parent. Found Blue=" + blue.Count + ", Red=" + red.Count + ". Using existing m_Tanks setup if any.");
            return;
        }

        // Build a fresh 10-entry TankManager array mapped to these spawn points.
        var tanks = new TankManager[10];
        // Team colors (slightly varied shades optional)
        Color blueTeam = new Color(0.2f, 0.5f, 1f);
        Color redTeam = new Color(1f, 0.3f, 0.3f);

        for (int i = 0; i < 5; i++)
        {
            tanks[i] = new TankManager();
            tanks[i].m_SpawnPoint = blue[i];
            tanks[i].m_PlayerColor = blueTeam;
        }
        for (int i = 0; i < 5; i++)
        {
            int idx = i + 5;
            tanks[idx] = new TankManager();
            tanks[idx].m_SpawnPoint = red[i];
            tanks[idx].m_PlayerColor = redTeam;
        }

        m_Tanks = tanks;
    }

    /// <summary>
    /// For OneVsOne_PvE, if there are not already 2 TankManager entries with spawn points,
    /// automatically assign BlueTeamSpawnPoints child[0] for the human and RedTeamSpawnPoints child[0] for the bot.
    /// Keeps existing manual configuration if present.
    /// </summary>
    private void AutoConfigureOneVsOnePvEFromScene()
    {
        if (m_GameMode != GameMode.OneVsOne_PvE)
            return;

        // If user already has 2 tanks both with spawn points, do nothing.
        if (m_Tanks != null && m_Tanks.Length >= 2)
        {
            int assigned = 0;
            for (int i = 0; i < 2; i++)
            {
                if (i < m_Tanks.Length && m_Tanks[i] != null && m_Tanks[i].m_SpawnPoint != null)
                    assigned++;
            }
            if (assigned == 2)
                return;
        }

        var blueParent = m_BlueTeamSpawnRoot != null ? m_BlueTeamSpawnRoot : GameObject.Find("BlueTeamSpawnPoints")?.transform;
        var redParent = m_RedTeamSpawnRoot != null ? m_RedTeamSpawnRoot : GameObject.Find("RedTeamSpawnPoints")?.transform;
        if (blueParent == null || redParent == null)
        {
            Debug.LogWarning("[GameManager] OneVsOne_PvE: Could not find 'BlueTeamSpawnPoints' or 'RedTeamSpawnPoints'. Provide 2 spawn points manually.");
            return;
        }
        if (blueParent.childCount == 0 || redParent.childCount == 0)
        {
            Debug.LogWarning("[GameManager] OneVsOne_PvE: Need at least one child under each team parent for auto configuration.");
            return;
        }

        var humanSpawn = blueParent.GetChild(0);
        var botSpawn = redParent.GetChild(0);

        var tanks = new TankManager[2];
        Color blueTeam = new Color(0.2f, 0.5f, 1f);
        Color redTeam = new Color(1f, 0.3f, 0.3f);

        tanks[0] = new TankManager { m_SpawnPoint = humanSpawn, m_PlayerColor = blueTeam }; // Player
        tanks[1] = new TankManager { m_SpawnPoint = botSpawn, m_PlayerColor = redTeam }; // Bot

        m_Tanks = tanks;
    }

    private void SpawnAllTanks()
    {
        var specs = BuildSpawnSpecs();
        m_ActiveTankCount = Mathf.Min(specs.Length, m_Tanks.Length);

        Debug.Log($"[GameManager] Mode={m_GameMode} Specs={specs.Length} TankManagers={m_Tanks?.Length} ActiveCount={m_ActiveTankCount}");

        for (int i = 0; i < m_ActiveTankCount; i++)
        {
            var spec = specs[i];
            TankManager tm = m_Tanks[i];
            if (tm == null)
            {
                Debug.LogWarning($"[GameManager] TankManager slot {i} is null. Skipping.");
                continue;
            }
            Vector3 pos = tm.m_SpawnPoint ? tm.m_SpawnPoint.position : Vector3.zero;
            Quaternion rot = tm.m_SpawnPoint ? tm.m_SpawnPoint.rotation : Quaternion.identity;
            Debug.Log($"[GameManager] Spawning tank index={i} playerNumber={spec.playerNumber} human={spec.isHuman} at {pos}");
            tm.m_Instance = Instantiate(m_TankPrefab, pos, rot) as GameObject;
            tm.m_PlayerNumber = spec.playerNumber;
            tm.m_IsHuman = spec.isHuman;
            // Assign team id if in a team mode (currently only FiveVsFive_Mixed)
            // Assign team id for both 5v5 PvP and PvE so UI/team logic works in either mode.
            tm.m_TeamId = IsTeamMode() ? (i < 5 ? 0 : 1) : -1;
            tm.Setup();

            if (!spec.isHuman)
            {
                AttachAI(tm.m_Instance, tm); // convert to bot
            }

            // Apply current SFX mute state to any AudioSources on the tank (movement, firing, engine, etc.)
            var tankSources = tm.m_Instance.GetComponentsInChildren<AudioSource>(true);
            foreach (var src in tankSources)
            {
                src.mute = AudioSettingsGlobal.SfxMuted;
            }
        }

        // Deactivate unused managers (future modes may use them)
        for (int i = m_ActiveTankCount; i < m_Tanks.Length; i++)
        {
            if (m_Tanks[i].m_Instance != null)
                m_Tanks[i].m_Instance.SetActive(false);
        }
    }

    /// <summary>
    /// Validates that required spawn points and tank manager entries exist for the active mode.
    /// Provides detailed logging to help diagnose why spawning may fail or stack tanks at origin.
    /// </summary>
    private void ValidateSpawnConfiguration()
    {
        if (m_Tanks == null)
        {
            Debug.LogError("[GameManager] m_Tanks array is null. No tanks will spawn.");
            return;
        }

        int required = 0;
        switch (m_GameMode)
        {
            case GameMode.OneVsOne_PvP: required = 2; break;
            case GameMode.OneVsOne_PvE: required = 2; break;
            case GameMode.FiveVsFive_PvP: required = 10; break;
            case GameMode.FiveVsFive_PvE: required = 10; break;
            default: required = 0; break;
        }

        if (required > 0 && m_Tanks.Length < required)
        {
            Debug.LogWarning($"[GameManager] TankManager array smaller than required for mode {m_GameMode}. Length={m_Tanks.Length} Required={required}. Some tanks won't spawn.");
        }

        int withSpawn = 0;
        for (int i = 0; i < Mathf.Min(required, m_Tanks.Length); i++)
        {
            var tm = m_Tanks[i];
            if (tm != null && tm.m_SpawnPoint != null)
                withSpawn++;
            else
                Debug.LogWarning($"[GameManager] TankManager[{i}] missing {(tm == null ? "instance" : "spawn point")}.");
        }
        if (required > 0 && withSpawn < required)
        {
            Debug.LogWarning($"[GameManager] Only {withSpawn}/{required} required spawn points assigned for mode {m_GameMode}. Missing ones will spawn at Vector3.zero.");
        }
    }

    private SpawnSpec[] BuildSpawnSpecs()
    {
        switch (m_GameMode)
        {
            case GameMode.OneVsOne_PvP:
                return new[] {
                    new SpawnSpec { playerNumber = 1, isHuman = true },
                    new SpawnSpec { playerNumber = 2, isHuman = true }
                };
            case GameMode.OneVsOne_PvE:
                return new[] {
                    new SpawnSpec { playerNumber = 1, isHuman = true },
                    new SpawnSpec { playerNumber = 2, isHuman = false }
                };
            // case GameMode.FiveVsFive_PvP:
            //     // Ten players total; assumes m_Tanks has length >= 10 and 10 spawn points assigned.
            //     return new[] {
            //         new SpawnSpec { playerNumber = 1, isHuman = true },
            //         new SpawnSpec { playerNumber = 2, isHuman = true },
            //         new SpawnSpec { playerNumber = 3, isHuman = true },
            //         new SpawnSpec { playerNumber = 4, isHuman = true },
            //         new SpawnSpec { playerNumber = 5, isHuman = true },
            //         new SpawnSpec { playerNumber = 6, isHuman = true },
            //         new SpawnSpec { playerNumber = 7, isHuman = true },
            //         new SpawnSpec { playerNumber = 8, isHuman = true },
            //         new SpawnSpec { playerNumber = 9, isHuman = true },
            //         new SpawnSpec { playerNumber = 10, isHuman = true }
            //     };
            case GameMode.FiveVsFive_PvP:
                // Index 0..4 assumed Team A, 5..9 Team B (match your inspector ordering).
                // Humans use playerNumbers 1 and 2 to fit the default 2-player Input axes.
                // All others are bots; their playerNumbers are unique but unused by input.
                return new[] {
                    new SpawnSpec { playerNumber = 1,  isHuman = true  },  // Team A human
                    new SpawnSpec { playerNumber = 3,  isHuman = false },
                    new SpawnSpec { playerNumber = 4,  isHuman = false },
                    new SpawnSpec { playerNumber = 5,  isHuman = false },
                    new SpawnSpec { playerNumber = 6,  isHuman = false },
                    new SpawnSpec { playerNumber = 2,  isHuman = true  },  // Team B human
                    new SpawnSpec { playerNumber = 7,  isHuman = false },
                    new SpawnSpec { playerNumber = 8,  isHuman = false },
                    new SpawnSpec { playerNumber = 9,  isHuman = false },
                    new SpawnSpec { playerNumber = 10, isHuman = false }
                };
            case GameMode.FiveVsFive_PvE:
                // Index 0..4 assumed Team A, 5..9 Team B (match your inspector ordering).
                // Humans use playerNumbers 1 and 2 to fit the default 2-player Input axes.
                // All others are bots; their playerNumbers are unique but unused by input.
                return new[] {
                    new SpawnSpec { playerNumber = 1,  isHuman = true  },  // Team A human
                    new SpawnSpec { playerNumber = 3,  isHuman = false },
                    new SpawnSpec { playerNumber = 4,  isHuman = false },
                    new SpawnSpec { playerNumber = 5,  isHuman = false },
                    new SpawnSpec { playerNumber = 6,  isHuman = false },
                    new SpawnSpec { playerNumber = 2,  isHuman = false  },  // Team B human
                    new SpawnSpec { playerNumber = 7,  isHuman = false },
                    new SpawnSpec { playerNumber = 8,  isHuman = false },
                    new SpawnSpec { playerNumber = 9,  isHuman = false },
                    new SpawnSpec { playerNumber = 10, isHuman = false }
                };
            default:
                return new SpawnSpec[0];
        }
    }

    private void AttachAI(GameObject tankInstance, TankManager manager)
    {
        if (tankInstance == null) return;

        // Try to locate movement & shooting components on root first, then children (covers prefab variations).
        var movement = tankInstance.GetComponent<TankMovement>() ?? tankInstance.GetComponentInChildren<TankMovement>(true);
        var shooting = tankInstance.GetComponent<TankShooting>() ?? tankInstance.GetComponentInChildren<TankShooting>(true);

        // Disable player input scripts (AI will take over movement & firing). Guard nulls.
        if (movement != null) movement.enabled = false;
        if (shooting != null) shooting.enabled = false; // disable Update polling of input axes

        // Attach AI if not already present and initialize with found components.
        if (!tankInstance.GetComponent<TankAI>())
        {
            var ai = tankInstance.AddComponent<TankAI>();
            ai.Initialize(this, manager, shooting, movement);
        }
    }

    private void SetCameraTargets()
    {
        // Build a collection of transforms for the actually spawned tanks
        List<Transform> targets = new List<Transform>();
        for (int i = 0; i < (m_ActiveTankCount > 0 ? m_ActiveTankCount : m_Tanks.Length); i++)
        {
            if (m_Tanks[i].m_Instance != null)
                targets.Add(m_Tanks[i].m_Instance.transform);
        }
        m_CameraControl.m_Targets = targets.ToArray();
    }

    private IEnumerator GameLoop()
    {
        // Start off by running the 'RoundStarting' coroutine but don't return until it's finished.
        yield return StartCoroutine(RoundStarting());

        // Once the 'RoundStarting' coroutine is finished, run the 'RoundPlaying' coroutine but don't return until it's finished.
        yield return StartCoroutine(RoundPlaying());

        // Once execution has returned here, run the 'RoundEnding' coroutine, again don't return until it's finished.
        yield return StartCoroutine(RoundEnding());

        // Check if a game winner has been found or if the maximum number of rounds has been reached.
        if (m_GameWinner != null || m_RoundNumber >= m_MaxRounds)
        {
            // Game over — don't reload the scene. Set the flag and exit the game loop so the final
            // message/state remains visible.
            m_GameOver = true;
            yield break; // exit the GameLoop coroutine
        }
        else
        {
            // If there isn't a winner yet, restart this coroutine so the loop continues.
            StartCoroutine(GameLoop());
        }
    }

    private IEnumerator RoundStarting()
    {
        // As soon as the round starts reset the tanks and make sure they can't move.
        ResetAllTanks();
        DisableTankControl();

        // Mark round inactive (we haven't started playing yet)
        m_RoundActive = false;

        // Snap the camera's zoom and position to something appropriate for the reset tanks.
        m_CameraControl.SetStartPositionAndSize();

        // Increment the round number and display text showing the players what round it is.
        m_RoundNumber++;
        m_MessageText.text = "ROUND " + m_RoundNumber;

        // Wait for the specified length of time until yielding control back to the game loop.
        yield return m_StartWait;
    }

    private IEnumerator RoundPlaying()
    {
        // As soon as the round begins playing let the players control the tanks.
        EnableTankControl();

        // Clear the text from the screen.
        m_MessageText.text = string.Empty;

        // Mark the round active and start spawning hearts periodically.
        m_RoundActive = true;
        if (m_HeartPrefab != null && m_HeartSpawnInterval > 0f)
        {
            StartCoroutine(HeartSpawnLoop());
        }

        // In team modes, show a small live HUD with remaining tanks per team using the same MessageText.
        if (m_ShowTeamCountOverlay && IsTeamMode() && m_MessageText != null)
        {
            // Ensure only one routine is running.
            if (m_TeamCountRoutine != null) StopCoroutine(m_TeamCountRoutine);
            m_TeamCountRoutine = StartCoroutine(TeamCountOverlayLoop());
        }

        // While the round shouldn't end yet (depends on mode)
        while (!RoundShouldEnd())
        {
            // ... return on the next frame.
            yield return null;
        }
    }

    private IEnumerator RoundEnding()
    {
        // Stop tanks from moving.
        DisableTankControl();

        // Round no longer active: stops heart spawn loop.
        m_RoundActive = false;

    // Clear the winner from the previous round.
        m_RoundWinner = null;

        // See if there is a winner now the round is over.
        m_RoundWinner = GetRoundWinner();

        // If there is a winner, increment their score.
        if (m_RoundWinner != null)
            m_RoundWinner.m_Wins++;

        // Now the winner's score has been incremented, see if someone has won the game.
        m_GameWinner = GetGameWinner();

        // Get a message based on the scores and whether or not there is a game winner and display it.
        string message = EndMessage();
        m_MessageText.text = message;

        // Play the game-end sound if the game is over (someone won or max rounds reached).
        bool gameIsOver = (m_GameWinner != null) || (m_RoundNumber >= m_MaxRounds);
        // If the game has a winner, mark it over immediately so no new rounds start.
        if (gameIsOver)
            m_GameOver = true;

        // Show match-end buttons only after the whole match has ended (not after every round).
        if (gameIsOver && m_MatchEndButtons)
            m_MatchEndButtons.SetActive(true);

    if (gameIsOver && m_GameEndClip != null)
        {
            // Pause all currently playing AudioSources except the dedicated SFX source so the
            // end clip can be heard clearly.
            m_PausedAudioSources.Clear();
            // Use new API to avoid obsolete warning; no need for sorted results.
            AudioSource[] allSources = FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            foreach (AudioSource src in allSources)
            {
                if (src == m_SfxSource)
                    continue;

                if (src.isPlaying)
                {
                    src.Pause();
                    m_PausedAudioSources.Add(src);
                }
            }

            // Play end clip on the dedicated sfx source unless music is muted globally.
            if (!AudioSettingsGlobal.MusicMuted)
            {
                if (m_SfxSource != null)
                    m_SfxSource.PlayOneShot(m_GameEndClip);
                else if (m_AudioSource != null)
                    m_AudioSource.PlayOneShot(m_GameEndClip);
            }
        }

        // Stop team count overlay if running (we're about to show end message instead).
        if (m_TeamCountRoutine != null)
        {
            StopCoroutine(m_TeamCountRoutine);
            m_TeamCountRoutine = null;
        }

        // Wait for the specified length of time until yielding control back to the game loop.
        yield return m_EndWait;

        // Resume any audio sources we paused earlier — but if the game is over, leave them paused.
        if (!gameIsOver && m_PausedAudioSources != null && m_PausedAudioSources.Count > 0)
        {
            foreach (AudioSource src in m_PausedAudioSources)
            {
                if (src != null)
                    src.UnPause();
            }
            m_PausedAudioSources.Clear();
        }
    }

    private bool OneTankLeft()
    {
        int alive = 0;
        for (int i = 0; i < m_ActiveTankCount; i++)
        {
            if (m_Tanks[i].m_Instance != null && m_Tanks[i].m_Instance.activeSelf)
                alive++;
        }
        return alive <= 1;
    }

    // Determines if the round should end based on current game mode.
    // - In classic FFA/1v1, ends when <=1 tank remains.
    // - In FiveVsFive_Mixed, ends as soon as a team has no alive tanks (team elimination).
    private bool RoundShouldEnd()
    {
        if (IsTeamMode())
        {
            if (m_GameMode == GameMode.FiveVsFive_PvP || m_GameMode == GameMode.FiveVsFive_PvE)
            {
                // In both PvP (two humans) and PvE (one human) end the round immediately when any human dies.
                for (int i = 0; i < m_ActiveTankCount; i++)
                {
                    var tm = m_Tanks[i];
                    if (tm == null || tm.m_Instance == null) continue;
                    if (!tm.m_IsHuman) continue; // skip bots
                    if (!tm.m_Instance.activeSelf)
                        return true; // a human tank just died
                }
                // If no humans configured (misconfiguration), fall back to team elimination below.
            }

            // Team elimination for all team modes (PvP and PvE)
            int a, b;
            if (TryGetTeamAliveCounts(out a, out b))
            {
                return a == 0 || b == 0;
            }
            return OneTankLeft();
        }
        return OneTankLeft();
    }

    /// <summary>
    /// Returns true and sets out parameters with current alive counts per team for FiveVsFive_Mixed.
    /// Team A assumed indices 0..4, Team B indices 5..9.
    /// Returns false for modes where team counting is not applicable.
    /// </summary>
    public bool TryGetTeamAliveCounts(out int teamAAlive, out int teamBAlive)
    {
        teamAAlive = 0; teamBAlive = 0;
        if (!IsTeamMode() || m_Tanks == null) return false;

        // Use m_ActiveTankCount to avoid iterating unused entries
        int limit = Mathf.Min(m_ActiveTankCount, m_Tanks.Length);
        for (int i = 0; i < limit; i++)
        {
            var tm = m_Tanks[i];
            if (tm != null && tm.m_Instance != null && tm.m_Instance.activeSelf)
            {
                if (tm.m_TeamId == 0) teamAAlive++; else if (tm.m_TeamId == 1) teamBAlive++;
            }
        }
        return true;
    }

    // Live overlay that displays remaining tanks per team during team modes.
    private IEnumerator TeamCountOverlayLoop()
    {
        var refresh = new WaitForSeconds(0.25f);
        while (m_RoundActive && !m_GameOver)
        {
            int a, b;
            if (TryGetTeamAliveCounts(out a, out b))
            {
                string blue = GetTeamColoredText(0, "BLUE TEAM");
                string red = GetTeamColoredText(1, "RED TEAM");
                m_MessageText.text = blue + ": " + a + "    " + red + ": " + b;
            }
            yield return refresh;
        }
    }

    private TankManager GetRoundWinner()
    {
        // Team modes: determine winner based on surviving team; in PvP also honor human-death rule.
        if (IsTeamMode())
        {
            if (m_GameMode == GameMode.FiveVsFive_PvP)
            {
                // Determine alive status of human tanks and teams.
                TankManager humanA = null, humanB = null;
                for (int i = 0; i < m_ActiveTankCount; i++)
                {
                    var tm = m_Tanks[i];
                    if (tm == null || tm.m_Instance == null) continue;
                    if (tm.m_IsHuman)
                    {
                        if (tm.m_TeamId == 0) humanA = tm; else if (tm.m_TeamId == 1) humanB = tm;
                    }
                }

                bool aAlive = humanA != null && humanA.m_Instance.activeSelf;
                bool bAlive = humanB != null && humanB.m_Instance.activeSelf;

                // If one human is dead and the other alive, that alive human's team wins.
                if (aAlive && !bAlive) return humanA; // Team A wins
                if (!aAlive && bAlive) return humanB; // Team B wins
                if (!aAlive && !bAlive) return null;  // Both died -> draw
            }
            else if (m_GameMode == GameMode.FiveVsFive_PvE)
            {
                // Single human (Team A) vs full bot team (Team B). If human dead, bots win immediately.
                TankManager human = null;
                for (int i = 0; i < m_ActiveTankCount; i++)
                {
                    var tm = m_Tanks[i];
                    if (tm == null || tm.m_Instance == null) continue;
                    if (tm.m_IsHuman)
                    {
                        human = tm;
                        break;
                    }
                }
                bool humanAlive = human != null && human.m_Instance != null && human.m_Instance.activeSelf;
                if (human != null && !humanAlive)
                {
                    // Human dead -> opposing team wins (assumes human team is 0; if not, invert).
                    int winningTeam = human.m_TeamId == 0 ? 1 : 0;
                    return GetFirstAliveOnTeam(winningTeam);
                }
            }

            // Team-elimination winner (works for PvP and PvE)
            int aCount, bCount;
            if (TryGetTeamAliveCounts(out aCount, out bCount))
            {
                if (aCount > 0 && bCount == 0) return GetFirstAliveOnTeam(0);
                if (bCount > 0 && aCount == 0) return GetFirstAliveOnTeam(1);
                if (aCount == 0 && bCount == 0) return null;
            }
            // Fallback
        }

        for (int i = 0; i < m_ActiveTankCount; i++)
        {
            if (m_Tanks[i].m_Instance != null && m_Tanks[i].m_Instance.activeSelf)
                return m_Tanks[i];
        }
        return null;
    }

    private TankManager GetFirstAliveOnTeam(int teamId)
    {
        for (int i = 0; i < m_ActiveTankCount; i++)
        {
            var tm = m_Tanks[i];
            if (tm != null && tm.m_TeamId == teamId && tm.m_Instance != null && tm.m_Instance.activeSelf)
                return tm;
        }
        return null;
    }

    private TankManager GetGameWinner()
    {
        for (int i = 0; i < m_Tanks.Length; i++)
        {
            if (m_Tanks[i].m_Wins == m_NumRoundsToWin)
                return m_Tanks[i];
        }

        return null;
    }

    private string EndMessage()
    {
        // Revamped end-of-round / end-of-game messaging supporting PvP, PvE and Team modes.
        // We keep the existing per-player formatting for classic 1v1 PvP.
        // 1v1 PvE: show PLAYER vs BOT labels instead of Player 1 / Player 2.
        // FiveVsFive_Mixed: aggregate wins per team and hide individual player rows.

        bool gameOver = (m_GameWinner != null);

        // Team-based modes share the same team message format
        if (IsTeamMode())
            return BuildTeamModeEndMessage(gameOver);

        switch (m_GameMode)
        {
            case GameMode.OneVsOne_PvE:
                return BuildPvEEndMessage(gameOver);
            case GameMode.OneVsOne_PvP:
            default:
                return BuildPvPEndMessage(gameOver);
        }
    }

    // ---------- Helper formatting methods for end-of-round messaging ----------

    private string BuildPvPEndMessage(bool gameOver)
    {
        // Original behaviour: show each player's wins.
        string message;
        if (gameOver)
        {
            message = m_GameWinner != null ? m_GameWinner.m_ColoredPlayerText + " WINS THE GAME!" : "DRAW!";
        }
        else
        {
            message = m_RoundWinner != null ? m_RoundWinner.m_ColoredPlayerText + " WINS THE ROUND!" : "DRAW!";
        }

        message += "\n\n\n\n"; // spacing before scoreboard
        for (int i = 0; i < m_Tanks.Length; i++)
        {
            message += m_Tanks[i].m_ColoredPlayerText + ": " + m_Tanks[i].m_Wins + " WINS\n";
        }
        return message;
    }

    private string BuildPvEEndMessage(bool gameOver)
    {
        // Determine which tank is human vs bot.
        // Human tanks do NOT have a TankAI component.
        TankManager human = null, bot = null;
        for (int i = 0; i < m_Tanks.Length; i++)
        {
            var tm = m_Tanks[i];
            if (tm?.m_Instance == null) continue;
            bool isBot = tm.m_Instance.GetComponent<TankAI>() != null;
            if (isBot)
                bot = tm;
            else
                human = tm;
        }

        // Fallback if detection fails (e.g. editor misconfiguration)
        if (human == null && m_Tanks.Length > 0) human = m_Tanks[0];
        if (bot == null && m_Tanks.Length > 1) bot = m_Tanks[1];

        string humanLabel = Colorize("PLAYER", human != null ? human.m_PlayerColor : Color.white);
        string botLabel = Colorize("BOT", bot != null ? bot.m_PlayerColor : Color.gray);

        string header;
        if (gameOver && m_GameWinner != null)
        {
            header = (m_GameWinner == human ? humanLabel : botLabel) + " WINS THE GAME!";
        }
        else
        {
            if (m_RoundWinner == null)
                header = "DRAW!";
            else
                header = (m_RoundWinner == human ? humanLabel : botLabel) + " WINS THE ROUND!";
        }

        // Scoreboard (aggregate just shows each side's wins, mapping wins from their tank managers)
        int humanWins = human != null ? human.m_Wins : 0;
        int botWins = bot != null ? bot.m_Wins : 0;

        string scoreboard = "\n\n\n\n" + humanLabel + ": " + humanWins + " WINS\n" + botLabel + ": " + botWins + " WINS\n";
        return header + scoreboard;
    }

    private string BuildTeamModeEndMessage(bool gameOver)
    {
        // Aggregate wins per team (sum of tank wins). Only one tank per team currently accumulates wins,
        // but summing future-proofs if we change how wins are tracked.
        int teamAWins = 0, teamBWins = 0;
        for (int i = 0; i < m_Tanks.Length; i++)
        {
            var tm = m_Tanks[i];
            if (tm == null) continue;
            if (tm.m_TeamId == 0) teamAWins += tm.m_Wins;
            else if (tm.m_TeamId == 1) teamBWins += tm.m_Wins;
        }

        string blueLabel = GetTeamColoredText(0, "BLUE TEAM");
        string redLabel = GetTeamColoredText(1, "RED TEAM");

        string header;
        if (gameOver && m_GameWinner != null)
        {
            // m_GameWinner belongs to the winning team
            header = (m_GameWinner.m_TeamId == 0 ? blueLabel : redLabel) + " WINS THE GAME!";
        }
        else
        {
            if (m_RoundWinner == null)
                header = "DRAW!"; // simultaneous elimination
            else
                header = (m_RoundWinner.m_TeamId == 0 ? blueLabel : redLabel) + " WINS THE ROUND!";
        }

        string scoreboard = "\n\n\n\n" + blueLabel + ": " + teamAWins + " WINS\n" + redLabel + ": " + teamBWins + " WINS\n";
        return header + scoreboard;
    }

    private string GetTeamColoredText(int teamId, string labelFallback)
    {
        // Find first tank on the team to extract its color.
        for (int i = 0; i < m_Tanks.Length; i++)
        {
            var tm = m_Tanks[i];
            if (tm != null && tm.m_TeamId == teamId)
            {
                return Colorize(labelFallback, tm.m_PlayerColor);
            }
        }
        // Fallback color.
        return Colorize(labelFallback, teamId == 0 ? Color.blue : Color.red);
    }

    private string Colorize(string label, Color color)
    {
        string hex = ColorUtility.ToHtmlStringRGB(color);
        return "<color=#" + hex + ">" + label.ToUpperInvariant() + "</color>";
    }

    private void ResetAllTanks()
    {
        for (int i = 0; i < (m_ActiveTankCount > 0 ? m_ActiveTankCount : m_Tanks.Length); i++)
        {
            m_Tanks[i].Reset();
        }
    }

    private void EnableTankControl()
    {
        for (int i = 0; i < (m_ActiveTankCount > 0 ? m_ActiveTankCount : m_Tanks.Length); i++)
        {
            m_Tanks[i].EnableControl();
        }
    }

    private void DisableTankControl()
    {
        for (int i = 0; i < (m_ActiveTankCount > 0 ? m_ActiveTankCount : m_Tanks.Length); i++)
        {
            m_Tanks[i].DisableControl();
        }
    }

    // ---------------- Heart Spawning ----------------
    private IEnumerator HeartSpawnLoop()
    {
        // Wait initial interval from the beginning of round play before first spawn.
        float interval = Mathf.Max(1f, m_HeartSpawnInterval); // clamp to at least 1 second safety
        // Initial wait so the first heart doesn't appear instantly unless interval very small.
        yield return new WaitForSeconds(interval);

        while (m_RoundActive && !m_GameOver)
        {
            SpawnHeart();
            yield return new WaitForSeconds(interval);
        }
    }

    private void SpawnHeart()
    {
        if (!m_RoundActive || m_GameOver) return;
        if (m_HeartPrefab == null) return;

        Transform spawnPoint = ChooseHeartSpawnPoint();
        if (spawnPoint == null)
        {
            // Fallback: choose center between active tanks.
            Vector3 avg = Vector3.zero; int count = 0;
            foreach (var tm in m_Tanks)
            {
                if (tm != null && tm.m_Instance != null && tm.m_Instance.activeSelf)
                {
                    avg += tm.m_Instance.transform.position; count++;
                }
            }
            if (count > 0) avg /= count; else avg = Vector3.zero;
            Instantiate(m_HeartPrefab, avg + Vector3.up * m_HeartSpawnVerticalOffset, Quaternion.identity);
            return;
        }

        Instantiate(m_HeartPrefab, spawnPoint.position + Vector3.up * m_HeartSpawnVerticalOffset, spawnPoint.rotation);
    }

    private Transform ChooseHeartSpawnPoint()
    {
        // Prefer explicit heart spawn points
        if (m_HeartSpawnPoints != null && m_HeartSpawnPoints.Length > 0)
        {
            return m_HeartSpawnPoints[Random.Range(0, m_HeartSpawnPoints.Length)];
        }
        // Fall back to tank spawn points via TankManager array
        List<Transform> tankSpawns = new List<Transform>();
        foreach (var tm in m_Tanks)
        {
            if (tm != null && tm.m_SpawnPoint != null) tankSpawns.Add(tm.m_SpawnPoint);
        }
        if (tankSpawns.Count > 0)
        {
            return tankSpawns[Random.Range(0, tankSpawns.Count)];
        }
        return null;
    }

    // ---------------- Match End Buttons ----------------
    /// <summary>
    /// Reloads the current gameplay scene to play again with the same selected mode.
    /// Wire this to the Rematch button OnClick on MessageCanvas/MatchEndButton.
    /// </summary>
    public void Rematch()
    {
        // In case any audio is paused due to game over, ensure normal timescale and reload.
        Time.timeScale = 1f;
        var active = SceneManager.GetActiveScene();
        SceneManager.LoadScene(active.buildIndex);
    }

    /// <summary>
    /// Returns to the Menu scene.
    /// Wire this to the Back to Menu button OnClick on MessageCanvas/MatchEndButton.
    /// </summary>
    public void BackToMenu()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene("Menu");
    }

    // ---------------- Pause / Continue ----------------
    /// <summary>
    /// Pauses gameplay by setting Time.timeScale to 0, disabling tank control and showing the pause panel.
    /// Wire this to the Pause button OnClick.
    /// </summary>
    public void PauseGame()
    {
        if (m_IsPaused || m_GameOver) return; // don't pause after game over
        m_IsPaused = true;
        Time.timeScale = 0f;

        // Disable control so input doesn't accumulate (optional since timeScale=0, but safer for custom scripts)
        DisableTankControl();

        // Show panel
        if (m_GamePausedPanel)
            m_GamePausedPanel.SetActive(true);

        // Pause all non-music audio (reuse list container)
        m_PausedAudioSources.Clear();
        AudioSource[] allSources = FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
        foreach (var src in allSources)
        {
            if (src == null) continue;
            if (src == m_SfxSource) continue; // leave dedicated sfx source alone
            if (src.isPlaying)
            {
                src.Pause();
                m_PausedAudioSources.Add(src);
            }
        }
    }

    /// <summary>
    /// Resumes gameplay from a paused state. Wire to Continue button OnClick.
    /// </summary>
    public void ContinueGame()
    {
        if (!m_IsPaused) return;
        m_IsPaused = false;
        Time.timeScale = 1f;

        // Re-enable tank control only if the round is active and game not over.
        if (!m_GameOver && m_RoundActive)
            EnableTankControl();

        if (m_GamePausedPanel)
            m_GamePausedPanel.SetActive(false);

        // Resume any audio that was paused for the pause state (unless game ended in meantime)
        if (!m_GameOver && m_PausedAudioSources.Count > 0)
        {
            foreach (var src in m_PausedAudioSources)
            {
                if (src != null)
                    src.UnPause();
            }
        }
        m_PausedAudioSources.Clear();
    }
}