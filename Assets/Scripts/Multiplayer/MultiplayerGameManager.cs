using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Photon.Pun;
using Photon.Realtime;

[RequireComponent(typeof(PhotonView))]
public class MultiplayerGameManager : MonoBehaviourPunCallbacks
{
    [Header("Tank")]
    [Tooltip("Tank prefab with PhotonView, in a Resources folder.")]
    public GameObject tankPrefab;

    [Header("1v1 Spawn Points")]
    public Transform blueSpawn;
    public Transform redSpawn;

    private readonly Dictionary<int, GameObject> _playerTanks = new Dictionary<int, GameObject>();
    private readonly Dictionary<int, int> _playerTeam = new Dictionary<int, int>();

    [Header("UI")]
    [Tooltip("Overlay message text (MessageCanvas/Text) shown for rounds and results.")]
    public Text messageText;
    [Tooltip("Root object for match-end buttons to show when the match is over.")]
    public GameObject matchEndButtons;
    [Tooltip("Optional TeamCount UI root to disable for 1v1 mode.")]
    public GameObject teamCountUIRoot;

    [Header("Heart Spawning (Master Only)")]
    [Tooltip("Networked heart prefab with PhotonView + NetworkHeart component, placed under a Resources folder.")]
    public GameObject heartPrefab;
    [Tooltip("Optional fixed spawn points for hearts. If empty, will randomize between blue/red spawn.")]
    public Transform[] heartSpawnPoints;
    [Tooltip("Seconds between heart spawns during a round.")]
    public float heartSpawnInterval = 20f;
    [Tooltip("Vertical offset so the heart appears slightly above ground.")]
    public float heartSpawnVerticalOffset = 0.5f;

    [Header("Match Rules")]
    public int numRoundsToWin = 3;
    public int maxRounds = 5;
    public float startDelay = 2f;
    public float endDelay = 2f;

    private int _roundNumber = 0;
    private readonly Dictionary<int, int> _wins = new Dictionary<int, int>();
    private bool _gameOver = false;
    private bool _roundActive = false;
    // Auto-return disabled; match ends and waits for user to press Back to Menu.
    [Header("Navigation")]
    [Tooltip("Scene name to load when returning to menu.")]
    [SerializeField] private string menuSceneName = "_Complete-Game";
    private bool _leavingRoom = false;
    private bool _menuLoaded = false;

    [Header("Audio")]
    [Tooltip("Sound to play when the game ends (wins or opponent leaves).")]
    public AudioClip gameEndClip;
    [Tooltip("Background music AudioSource on GameManager2 to turn off at match end.")]
    [SerializeField] private AudioSource backgroundMusicSource; // BGM to turn off on game end
    private AudioSource _sfxSource; // dedicated for one-shots
    private readonly List<AudioSource> _pausedSources = new List<AudioSource>();

    private void Start()
    {
        if (!PhotonNetwork.IsConnected)
        {
            Debug.LogError("[MultiplayerGameManager] Not connected to Photon. Returning to menu scene.");
            UnityEngine.SceneManagement.SceneManager.LoadScene("_Complete-Game"); // or your menu scene name
            return;
        }

        if (tankPrefab == null)
        {
            Debug.LogError("[MultiplayerGameManager] Tank prefab not assigned.");
            return;
        }

        // Hide team count UI in 1v1 if provided and reset match-end buttons.
        if (teamCountUIRoot != null) teamCountUIRoot.SetActive(false);
        if (matchEndButtons != null) matchEndButtons.SetActive(false);

        // Cache the existing background music source on this GameObject (GameManager2).
        if (backgroundMusicSource == null)
            backgroundMusicSource = GetComponent<AudioSource>();

        // Ensure a dedicated SFX audio source exists (separate from BGM).
        _sfxSource = gameObject.AddComponent<AudioSource>();
        _sfxSource.playOnAwake = false;
        _sfxSource.loop = false;

        // Safety: if this instance starts with lingering paused sources (edge case), restore them.
        AttemptAutoRestorePausedAudio();

        // Master handles spawning for all; simpler & avoids RPC null when no PhotonView.
        if (PhotonNetwork.IsMasterClient)
        {
            SpawnAllPlayers1v1();
            StartCoroutine(GameLoop());
            photonView.RPC(nameof(RpcRebuildCameraTargets), RpcTarget.All);
        }
    }

    private void SpawnAllPlayers1v1()
    {
        Player[] players = PhotonNetwork.PlayerList;
        if (players.Length == 0)
        {
            Debug.LogWarning("[MultiplayerGameManager] No players in room.");
            return;
        }

        // First player -> blue, second player -> red
        if (players.Length >= 1 && blueSpawn != null)
        {
            var (p, r) = GetSpawn(blueSpawn);
            SpawnTankFor(players[0], p, r, 0);
        }
        if (players.Length >= 2 && redSpawn != null)
        {
            var (p, r) = GetSpawn(redSpawn);
            SpawnTankFor(players[1], p, r, 1);
        }
    }
    private void SpawnTankFor(Player owner, Vector3 pos, Quaternion rot, int teamId)
    {
        object[] initData = { teamId, owner.ActorNumber };
        GameObject tank = PhotonNetwork.Instantiate(tankPrefab.name, pos, rot, 0, initData);

        // Transfer ownership so only that player controls it (requires prefab OwnershipTransfer != Fixed)
        var view = tank.GetComponent<PhotonView>();
        if (view != null && view.Owner != owner)
            view.TransferOwnership(owner);

        _playerTanks[owner.ActorNumber] = tank;
        _playerTeam[owner.ActorNumber] = teamId;
        if (!_wins.ContainsKey(owner.ActorNumber))
            _wins[owner.ActorNumber] = 0;
    }

    #region Photon Callbacks

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        Debug.Log($"[MultiplayerGameManager] Player entered: {newPlayer.NickName} ({newPlayer.ActorNumber})");

        if (!PhotonNetwork.IsMasterClient)
            return;

        // Simple rule for 1v1: if only one tank exists, spawn the second at red.
        if (!_playerTanks.ContainsKey(newPlayer.ActorNumber) && PhotonNetwork.IsMasterClient)
        {
            bool blueTaken = _playerTanks.ContainsKey(PhotonNetwork.PlayerList[0].ActorNumber);
            int teamId = blueTaken ? 1 : 0;
            Transform spawn = teamId == 0 ? blueSpawn : redSpawn;
            var (p, r) = GetSpawn(spawn);
            SpawnTankFor(newPlayer, p, r, teamId);
            photonView.RPC(nameof(RpcRebuildCameraTargets), RpcTarget.All);
        }
    }

    

    #endregion

    // ------------------- Match / Rounds -------------------
    private System.Collections.IEnumerator GameLoop()
    {
        while (!_gameOver)
        {
            _roundNumber++;
            yield return RoundStarting();
            yield return RoundPlaying();
            var winner = GetRoundWinner();
            yield return RoundEnding(winner);
        }
    }

    private System.Collections.IEnumerator RoundStarting()
    {
        photonView.RPC(nameof(RpcLockControl), RpcTarget.All, true);
        photonView.RPC(nameof(RpcSetMessage), RpcTarget.All, $"ROUND {_roundNumber}");
        ResetAllTanks();
        yield return new WaitForSeconds(startDelay);
        photonView.RPC(nameof(RpcLockControl), RpcTarget.All, false);
        photonView.RPC(nameof(RpcSetMessage), RpcTarget.All, string.Empty);
    }

    private System.Collections.IEnumerator RoundPlaying()
    {
        // Wait until only one tank remains active
        _roundActive = true;

        // Start heart spawn loop on master only
        if (PhotonNetwork.IsMasterClient && heartPrefab != null && heartSpawnInterval > 0f)
        {
            StartCoroutine(HeartSpawnLoop());
        }

        while (AliveCount() > 1)
            yield return null;
    }

    private System.Collections.IEnumerator RoundEnding(int winnerActorNumber)
    {
        // Update wins immediately for end-of-round UI
        if (winnerActorNumber != 0)
        {
            int prev = _wins.ContainsKey(winnerActorNumber) ? _wins[winnerActorNumber] : 0;
            _wins[winnerActorNumber] = prev + 1;
        }

        bool gameIsOver = IsGameOver();

        // Build and show end-of-round or end-of-game message
        string msg = BuildEndMessage(gameIsOver, winnerActorNumber);
        photonView.RPC(nameof(RpcSetMessage), RpcTarget.All, msg);

        // Stop spawning hearts as the round concludes
        _roundActive = false;

        if (gameIsOver)
        {
            _gameOver = true;
            photonView.RPC(nameof(RpcLockControl), RpcTarget.All, true);
            photonView.RPC(nameof(RpcShowMatchEndButtons), RpcTarget.All, true);
            // Turn off background music, then play the game-end clip on all clients
            photonView.RPC(nameof(RpcDisableBackgroundMusic), RpcTarget.All);
            photonView.RPC(nameof(RpcPlayGameEndClip), RpcTarget.All);
            // No auto-return. Players exit via Back to Menu button only.
        }

        // Brief pause
        yield return new WaitForSeconds(endDelay);
    }

    private int AliveCount()
    {
        int alive = 0;
        // Count via known dictionary first
        foreach (var kv in _playerTanks)
        {
            if (kv.Value != null && kv.Value.activeSelf) alive++;
        }
        // Fallback: include any tanks not yet in dictionary (client side)
        var setups = Object.FindObjectsByType<MultiplayerTankSetup>(FindObjectsSortMode.None);
        foreach (var setup in setups)
        {
            var go = setup.gameObject;
            var pv = setup.photonView;
            if (pv != null && pv.Owner != null && !_playerTanks.ContainsKey(pv.OwnerActorNr))
            {
                if (go.activeSelf) alive++;
            }
        }
        return alive;
    }

    private int GetRoundWinner()
    {
        int winner = 0;
        // Check known mappings first
        foreach (var kv in _playerTanks)
        {
            if (kv.Value != null && kv.Value.activeSelf)
            {
                if (winner == 0) winner = kv.Key; else return 0; // more than one alive => no winner
            }
        }
        // Fallback on clients where dictionary may be empty
        if (winner == 0)
        {
            var setups = Object.FindObjectsByType<MultiplayerTankSetup>(FindObjectsSortMode.None);
            foreach (var setup in setups)
            {
                var pv = setup.photonView;
                if (pv != null && pv.Owner != null && setup.gameObject.activeSelf)
                {
                    int actor = pv.OwnerActorNr;
                    if (winner == 0) winner = actor; else return 0;
                }
            }
        }
        return winner;
    }

    private void ResetAllTanks()
    {
        foreach (var kv in _playerTanks)
        {
            int actor = kv.Key;
            var go = kv.Value;
            if (go == null) continue;
            int team = _playerTeam.ContainsKey(actor) ? _playerTeam[actor] : 0;
            Transform root = team == 0 ? blueSpawn : redSpawn;
            var (p, r) = GetSpawn(root);
            photonView.RPC(nameof(RpcResetTank), RpcTarget.All, actor, p, r);
        }
    }

    private (Vector3 pos, Quaternion rot) GetSpawn(Transform root)
    {
        if (root != null && root.childCount > 0)
            return (root.GetChild(0).position, root.GetChild(0).rotation);
        return (root != null ? root.position : Vector3.zero, root != null ? root.rotation : Quaternion.identity);
    }

    [PunRPC]
    private void RpcResetTank(int actorNumber, Vector3 pos, Quaternion rot)
    {
        GameObject go = null;
        if (!_playerTanks.TryGetValue(actorNumber, out go) || go == null)
        {
            // Fallback: find by PhotonView owner on this client and cache it
            var setups = Object.FindObjectsByType<MultiplayerTankSetup>(FindObjectsSortMode.None);
            foreach (var setup in setups)
            {
                var pv = setup.photonView;
                if (pv != null && pv.OwnerActorNr == actorNumber)
                {
                    go = setup.gameObject;
                    _playerTanks[actorNumber] = go;
                    // also backfill team map if possible from TankTeam
                    var tag = go.GetComponent<TankTeam>();
                    if (tag != null) _playerTeam[actorNumber] = tag.TeamId;
                    break;
                }
            }
        }

        if (go == null)
        {
            Debug.LogWarning("[MultiplayerGameManager] RpcResetTank: could not find tank for actor " + actorNumber + " on this client.");
            return;
        }

        var rb = go.GetComponent<Rigidbody>();
        if (rb) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        go.transform.SetPositionAndRotation(pos, rot);
        if (!go.activeSelf) go.SetActive(true);
        var health = go.GetComponent<TankHealth>();
        if (health) health.RestoreFullHealth();
    }

    [PunRPC]
    private void RpcLockControl(bool locked)
    {
        // Apply to all tanks in scene to avoid dependency on local dictionaries
        var setups = Object.FindObjectsByType<MultiplayerTankSetup>(FindObjectsSortMode.None);
        foreach (var setup in setups)
        {
            if (setup != null)
            {
                setup.SetRoundLocked(locked);
            }
        }
    }

    [PunRPC]
    private void RpcSetMessage(string text)
    {
        if (messageText != null)
            messageText.text = text ?? string.Empty;
    }

    [PunRPC]
    private void RpcShowMatchEndButtons(bool visible)
    {
        if (matchEndButtons != null)
            matchEndButtons.SetActive(visible);
    }

    private bool IsGameOver()
    {
        if (_roundNumber >= maxRounds)
            return true;

        // Check any player's wins against threshold
        foreach (var kv in _wins)
        {
            if (kv.Value >= numRoundsToWin)
                return true;
        }
        return false;
    }

    private string BuildEndMessage(bool gameOver, int winnerActor)
    {
        // Map actors to teams
        int blueActor = 0, redActor = 0;
        foreach (var kv in _playerTeam)
        {
            if (kv.Value == 0) blueActor = kv.Key; else if (kv.Value == 1) redActor = kv.Key;
        }

        // Player names with team colors
        string blueName = GetPlayerNickname(blueActor);
        string redName  = GetPlayerNickname(redActor);
        string blueLabel = Colorize(blueName, new Color(0.2f, 0.5f, 1f));
        string redLabel  = Colorize(redName,  new Color(1f, 0.3f, 0.3f));

        string header;
        if (winnerActor == 0)
        {
            header = "DRAW!";
        }
        else
        {
            int teamId = _playerTeam.TryGetValue(winnerActor, out int t) ? t : 0;
            string winnerLabel = teamId == 0 ? blueLabel : redLabel;
            header = gameOver ? ($"{winnerLabel} WINS THE GAME!") : ($"{winnerLabel} WINS THE ROUND!");
        }

        int blueWins = (blueActor != 0 && _wins.ContainsKey(blueActor)) ? _wins[blueActor] : 0;
        int redWins  = (redActor  != 0 && _wins.ContainsKey(redActor))  ? _wins[redActor]  : 0;

        string scoreboard = $"\n\n\n\n{blueLabel}: {blueWins} WINS\n{redLabel}: {redWins} WINS\n";
        return header + scoreboard;
    }

    private string Colorize(string label, Color color)
    {
        string hex = ColorUtility.ToHtmlStringRGB(color);
        return "<color=#" + hex + ">" + (label ?? string.Empty) + "</color>";
    }

    private string GetPlayerNickname(int actorNumber)
    {
        if (actorNumber == 0) return "";
        var room = PhotonNetwork.CurrentRoom;
        if (room != null && room.Players != null && room.Players.TryGetValue(actorNumber, out var p) && p != null)
        {
            if (!string.IsNullOrEmpty(p.NickName)) return p.NickName;
        }
        var list = PhotonNetwork.PlayerList;
        for (int i = 0; i < list.Length; i++)
        {
            var pl = list[i];
            if (pl != null && pl.ActorNumber == actorNumber)
            {
                return string.IsNullOrEmpty(pl.NickName) ? ($"Player {actorNumber}") : pl.NickName;
            }
        }
        return $"Player {actorNumber}";
    }

    // -------------- Hearts --------------
    private IEnumerator HeartSpawnLoop()
    {
        float interval = Mathf.Max(1f, heartSpawnInterval);
        // Small initial wait so first heart doesn't appear instantly
        yield return new WaitForSeconds(interval);

        while (_roundActive && !_gameOver && PhotonNetwork.IsMasterClient)
        {
            SpawnHeartNetworked();
            yield return new WaitForSeconds(interval);
        }
    }

    private void SpawnHeartNetworked()
    {
        if (heartPrefab == null) return;
        var spawn = ChooseHeartSpawnPoint();
        Vector3 pos;
        Quaternion rot;
        if (spawn != null)
        {
            pos = spawn.position + Vector3.up * heartSpawnVerticalOffset;
            rot = spawn.rotation;
        }
        else
        {
            // Fallback: center between alive tanks
            Vector3 avg = Vector3.zero; int count = 0;
            foreach (var kv in _playerTanks)
            {
                var go = kv.Value;
                if (go != null && go.activeSelf) { avg += go.transform.position; count++; }
            }
            if (count > 0) avg /= count; else avg = Vector3.zero;
            pos = avg + Vector3.up * heartSpawnVerticalOffset;
            rot = Quaternion.identity;
        }

        // Must be under a Resources folder for PhotonNetwork.Instantiate.
        PhotonNetwork.Instantiate(heartPrefab.name, pos, rot, 0);
    }

    private Transform ChooseHeartSpawnPoint()
    {
        if (heartSpawnPoints != null && heartSpawnPoints.Length > 0)
        {
            return heartSpawnPoints[Random.Range(0, heartSpawnPoints.Length)];
        }
        // fallback to blue/red spawn roots
        List<Transform> list = new List<Transform>();
        if (blueSpawn != null) list.Add(blueSpawn);
        if (redSpawn != null) list.Add(redSpawn);
        if (list.Count == 0) return null;
        return list[Random.Range(0, list.Count)];
    }

    [PunRPC]
    private void RpcRebuildCameraTargets()
    {
        var cam = Object.FindFirstObjectByType<CameraControl>();
        if (cam == null)
        {
            Debug.LogWarning("[MultiplayerGameManager] CameraControl not found in scene. Add it to ensure equal view.");
            return;
        }
        // Collect all tank transforms in scene
        var setups = Object.FindObjectsByType<MultiplayerTankSetup>(FindObjectsSortMode.None);
        var list = new System.Collections.Generic.List<Transform>();
        for (int i = 0; i < setups.Length; i++)
        {
            var t = setups[i].transform;
            if (t != null && !list.Contains(t)) list.Add(t);
        }
        if (list.Count > 0)
            cam.m_Targets = list.ToArray();
    }

    [PunRPC]
    private void RpcPlayGameEndClip()
    {
        if (gameEndClip == null) return;

        _pausedSources.Clear();
        var all = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
        foreach (var src in all)
        {
            if (src == null) continue;
            if (src == _sfxSource) continue;

            bool isMusic = AudioSettingsGlobal.IsMusicSource(src);
            if (isMusic)
            {
                // For background music on GameManager2, stop and disable entirely.
                if (backgroundMusicSource != null && src == backgroundMusicSource)
                {
                    try { if (src.isPlaying) src.Stop(); } catch { }
                    src.mute = true;
                    src.enabled = false;
                    // Do not track in paused list since it's disabled intentionally.
                    continue;
                }

                // For other music sources, pause and mute so end clip is isolated.
                if (src.isPlaying) src.Pause();
                src.mute = true; // explicit mute
                _pausedSources.Add(src); // track to restore later
                continue;
            }

            // Pause any other currently playing audio (tank SFX, ambient) to reduce clutter.
            if (src.isPlaying)
            {
                src.Pause();
                _pausedSources.Add(src);
            }
        }

        if (!AudioSettingsGlobal.MusicMuted)
        {
            if (_sfxSource != null) _sfxSource.PlayOneShot(gameEndClip);
        }
    }

    [PunRPC]
    private void RpcDisableBackgroundMusic()
    {
        var bg = backgroundMusicSource != null ? backgroundMusicSource : GetComponent<AudioSource>();
        if (bg == null) return;
        try { if (bg.isPlaying) bg.Stop(); } catch { }
        bg.mute = true;
        bg.enabled = false; // fully turn off like local mode
    }

    // ------------------- UI Button Handlers -------------------
    // Wire to Back to Menu button OnClick
    public void OnClick_BackToMenu()
    {
        if (_leavingRoom) return;
        StartCoroutine(CoLeaveToMenu());
    }

    public override void OnLeftRoom()
    {
        if (_leavingRoom && !_menuLoaded)
        {
            PhotonNetwork.AutomaticallySyncScene = false;
            SceneManager.LoadScene(menuSceneName);
            _menuLoaded = true;
        }
    }

    private System.Collections.IEnumerator CoLeaveToMenu()
    {
        _leavingRoom = true;
        PhotonNetwork.AutomaticallySyncScene = false; // prevent SetLevelInProps while exiting

        if (PhotonNetwork.InRoom)
            PhotonNetwork.LeaveRoom();

        float start = Time.unscaledTime;
        // Wait until fully out of room
        while ((PhotonNetwork.InRoom || PhotonNetwork.NetworkClientState == Photon.Realtime.ClientState.Leaving) && Time.unscaledTime < start + 8f)
            yield return null;

        // Fallback: if still stuck, disconnect
        if ((PhotonNetwork.InRoom || PhotonNetwork.NetworkClientState == Photon.Realtime.ClientState.Leaving))
        {
            PhotonNetwork.Disconnect();
            while (PhotonNetwork.IsConnected && Time.unscaledTime < start + 12f)
                yield return null;
        }

        if (!_menuLoaded)
        {
            // Restore audio (unmute/unpause) before switching scenes so menu music can resume.
            RestorePausedAudioSources();
            SceneManager.LoadScene(menuSceneName);
            _menuLoaded = true;
        }
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        base.OnPlayerLeftRoom(otherPlayer);

        // Master cleans up leaver's objects in a single call (avoids permission errors)
        if (PhotonNetwork.IsMasterClient)
        {
            PhotonNetwork.DestroyPlayerObjects(otherPlayer);
        }

        // Remove local references and deactivate any lingering tanks for this actor
        _playerTanks.Remove(otherPlayer.ActorNumber);
        var setups = Object.FindObjectsByType<MultiplayerTankSetup>(FindObjectsSortMode.None);
        foreach (var setup in setups)
        {
            var pv = setup.photonView;
            if (pv != null && pv.OwnerActorNr == otherPlayer.ActorNumber)
            {
                if (setup.gameObject != null)
                    setup.gameObject.SetActive(false);
            }
        }

        photonView.RPC(nameof(RpcRebuildCameraTargets), RpcTarget.All);

        // If a player leaves during/after a round, show end UI (master decides)
        if (PhotonNetwork.IsMasterClient && !_gameOver)
        {
            _gameOver = true;
            string leftMsg = "OPPONENT LEFT – MATCH ENDED";
            photonView.RPC(nameof(RpcSetMessage), RpcTarget.All, leftMsg);
            photonView.RPC(nameof(RpcLockControl), RpcTarget.All, true);
            photonView.RPC(nameof(RpcShowMatchEndButtons), RpcTarget.All, true);
            // Ensure background music is turned off when match ends due to player leaving.
            photonView.RPC(nameof(RpcDisableBackgroundMusic), RpcTarget.All);
        }
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        // If I became the master as a result of someone leaving, apply the same end-match UI.
        if (PhotonNetwork.IsMasterClient && !_gameOver && PhotonNetwork.CurrentRoom != null && PhotonNetwork.CurrentRoom.PlayerCount <= 1)
        {
            _gameOver = true;
            photonView.RPC(nameof(RpcSetMessage), RpcTarget.All, "OPPONENT LEFT – MATCH ENDED");
            photonView.RPC(nameof(RpcLockControl), RpcTarget.All, true);
            photonView.RPC(nameof(RpcShowMatchEndButtons), RpcTarget.All, true);
            photonView.RPC(nameof(RpcDisableBackgroundMusic), RpcTarget.All);
        }
    }

    // -------- Audio Restore Helpers --------
    private void RestorePausedAudioSources()
    {
        if (_pausedSources.Count == 0) return;
        foreach (var src in _pausedSources)
        {
            if (src == null) continue;
            // Unmute music sources based on global setting.
            if (AudioSettingsGlobal.IsMusicSource(src))
            {
                src.mute = AudioSettingsGlobal.MusicMuted;
            }
            // Resume playback if previously paused.
            try { src.UnPause(); } catch { /* ignore destroyed */ }
        }
        _pausedSources.Clear();
    }

    private void AttemptAutoRestorePausedAudio()
    {
        if (_pausedSources.Count > 0)
            RestorePausedAudioSources();
    }
}