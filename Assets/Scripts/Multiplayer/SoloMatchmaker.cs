using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;

namespace TanksGame.Managers
{
    public class SoloMatchmaker : MonoBehaviourPunCallbacks
    {
        private const string AvatarPropKey = "avatar"; // byte[] PNG thumbnail shared via Photon
        public enum State
        {
            Idle,
            Connecting,
            Searching,
            InRoomWaiting,
            Paired,
            Error
        }

        [Header("UI References")]
        [SerializeField] private Text statusText;
        [SerializeField] private Button cancelButton;
        // Intended to be the SoloMatchmaker overlay panel. If left null or
        // accidentally set to the Game Modes panel, we'll fall back to this GameObject.
        [SerializeField] private GameObject modesPanel;
        // Panel that contains the selectable game mode buttons (1v1, 5v5 etc.)
        [SerializeField] private GameObject gameModesPanel;
        [SerializeField] private GameObject playerIcon;
        [SerializeField] private GameObject opponentIcon;
        [SerializeField] private GameObject versus;

        [Header("Match Settings")]
        [SerializeField] private byte maxPlayers = 2;
        [SerializeField] private string modePropertyKey = "mode";
        [SerializeField] private string modePropertyValue = "1v1";
        [SerializeField] private string sceneToLoadOnPair = "MultiplayerMain"; // default multiplayer scene

        [Header("Events")]
        public UnityEvent onPaired; // Invoke when second player present

        private State _state = State.Idle;
        private bool _autoStart;
        private Sprite _opponentRuntimeAvatar; // created from received bytes, we manage its lifecycle

        private void Awake()
        {
            if (cancelButton != null)
                cancelButton.onClick.AddListener(CancelMatchmaking);

            // Try to auto-wire the Game Modes panel if not assigned
            if (gameModesPanel == null)
            {
                var found = GameObject.Find("GameModes");
                if (found != null)
                {
                    gameModesPanel = found;
                }
            }

            SetState(State.Idle, "Modes");
        }

        public void StartMatchmaking()
        {
            _autoStart = true;

            // Hide the game mode selection while searching
            if (gameModesPanel != null)
                gameModesPanel.SetActive(false);

            TryApplyLocalProfileToPhoton();

            if (!PhotonNetwork.IsConnected)
            {
                PhotonNetwork.AutomaticallySyncScene = true;
                PhotonNetwork.GameVersion = Application.version;
                SetState(State.Connecting, "Connecting...");
                PhotonNetwork.ConnectUsingSettings();
                return;
            }

            TryApplyLocalProfileToPhoton();
            JoinRandom1v1();
        }

        public void CancelMatchmaking()
        {
            GetMatchPanel().SetActive(false);
            _autoStart = false;

            if (PhotonNetwork.InRoom)
                PhotonNetwork.LeaveRoom();

            SetState(State.Idle, "Modes");

            // Show the game mode selection again
            if (gameModesPanel != null)
                gameModesPanel.SetActive(true);
        }

        private void JoinRandom1v1()
        {
            SetState(State.Searching, "Finding your opponent...");

            var expectedProps = new Hashtable { { modePropertyKey, modePropertyValue } };
            PhotonNetwork.JoinRandomRoom(expectedProps, maxPlayers, MatchmakingMode.FillRoom, TypedLobby.Default, null, null);
        }

        private void CreateRoom1v1()
        {
            string roomName = $"mm_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
            var opts = new RoomOptions
            {
                MaxPlayers = maxPlayers,
                IsOpen = true,
                IsVisible = true,
                CustomRoomProperties = new Hashtable { { modePropertyKey, modePropertyValue } },
                CustomRoomPropertiesForLobby = new[] { modePropertyKey }
            };

            PhotonNetwork.CreateRoom(roomName, opts, TypedLobby.Default);
        }

        private void SetState(State state, string message)
        {
            _state = state;

            if (statusText != null)
                statusText.text = message;

            bool searching = state == State.Searching ||
                             state == State.Connecting ||
                             state == State.InRoomWaiting;

            // Keep GameModes hidden during the entire matchmaking flow (including Paired)
            bool matchmakingInProgress = searching || state == State.Paired;
            if (gameModesPanel != null)
                gameModesPanel.SetActive(!matchmakingInProgress);

            var mmPanel = GetMatchPanel();
            // Keep the matchmaking panel visible even when Paired, so UI stays up
            bool showMatchmakingPanel = searching || state == State.Paired;
            if (mmPanel != null) mmPanel.SetActive(showMatchmakingPanel);
            // Keep the three items (Player, Opponent, Versus) visible while paired too
            bool showIcons = searching || state == State.Paired;
            if (playerIcon != null) playerIcon.SetActive(showIcons);
            if (opponentIcon != null) opponentIcon.SetActive(showIcons);
            if (versus != null) versus.SetActive(showIcons);

            if (cancelButton != null) cancelButton.gameObject.SetActive(searching);
        }

        private GameObject GetMatchPanel()
        {
            // If misconfigured in the inspector to point at GameModes, prefer our own object
            if (modesPanel != null && modesPanel != gameModesPanel)
                return modesPanel;
            return this.gameObject;
        }

        private void UpdateIcons()
        {
            bool hasOpponent = PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null && PhotonNetwork.CurrentRoom.PlayerCount >= 2;
            if (opponentIcon != null) opponentIcon.SetActive(true);
            if (versus != null) versus.SetActive(true);
            if (statusText != null)
                statusText.text = hasOpponent ? "Paired!" : "Waiting for opponent...";
            UpdateNamesAndPortraits();
        }

        private void UpdateNamesAndPortraits()
        {
            try
            {
                // Player name
                if (playerIcon != null)
                {
                    var t = playerIcon.GetComponentInChildren<Text>(true);
                    if (t != null)
                    {
                        string self = PhotonNetwork.LocalPlayer != null && !string.IsNullOrEmpty(PhotonNetwork.LocalPlayer.NickName)
                            ? PhotonNetwork.LocalPlayer.NickName
                            : "Player";
                        t.text = self;
                    }

                    // Player avatar (local) -> place into child Image (not frame on root)
                    var img = FindPortraitImage(playerIcon);
                    var localSprite = global::ProfileManager.Instance != null ? global::ProfileManager.Instance.GetAvatarSprite() : null;
                    if (img != null && localSprite != null)
                    {
                        img.sprite = localSprite;
                        img.enabled = true;
                    }
                }

                // Opponent name (other player in room)
                if (opponentIcon != null)
                {
                    var t = opponentIcon.GetComponentInChildren<Text>(true);
                    if (t != null)
                    {
                        string oppName = "Opponent";
                        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
                        {
                            foreach (var kv in PhotonNetwork.CurrentRoom.Players)
                            {
                                var p = kv.Value;
                                if (PhotonNetwork.LocalPlayer == null || p.ActorNumber != PhotonNetwork.LocalPlayer.ActorNumber)
                                {
                                    if (!string.IsNullOrEmpty(p.NickName))
                                        oppName = p.NickName;
                                    break;
                                }
                            }
                        }
                        t.text = oppName;
                    }

                    // Opponent avatar from Photon custom property (byte[] PNG) -> into child Image
                    var img = FindPortraitImage(opponentIcon);
                    if (img != null)
                    {
                        Sprite oppSprite = _opponentRuntimeAvatar;
                        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
                        {
                            foreach (var kv in PhotonNetwork.CurrentRoom.Players)
                            {
                                var p = kv.Value;
                                if (PhotonNetwork.LocalPlayer == null || p.ActorNumber != PhotonNetwork.LocalPlayer.ActorNumber)
                                {
                                    if (p.CustomProperties != null && p.CustomProperties.ContainsKey(AvatarPropKey))
                                    {
                                        var bytes = p.CustomProperties[AvatarPropKey] as byte[];
                                        if (bytes != null && bytes.Length > 0)
                                        {
                                            oppSprite = CreateSpriteFromPng(bytes, ref _opponentRuntimeAvatar);
                                        }
                                    }
                                    break;
                                }
                            }
                        }
                        if (oppSprite != null)
                        {
                            img.sprite = oppSprite;
                            img.enabled = true;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SoloMatchmaker] UpdateNames exception: {e.Message}");
            }
        }

        private void OnPaired()
        {
            SetState(State.Paired, "Paired!");
            UpdateNamesAndPortraits();
            onPaired?.Invoke();

            if (!string.IsNullOrEmpty(sceneToLoadOnPair))
            {
                if (PhotonNetwork.IsMasterClient)
                {
                    PhotonNetwork.LoadLevel(sceneToLoadOnPair);
                }
            }
        }

        public override void OnConnectedToMaster()
        {
            Debug.Log("[SoloMatchmaker] Connected to Master.");
            TryApplyLocalProfileToPhoton();
            if (_autoStart)
                JoinRandom1v1();
        }

        public override void OnJoinRandomFailed(short returnCode, string message)
        {
            Debug.LogWarning($"[SoloMatchmaker] JoinRandom failed {returnCode}: {message}. Creating room.");
            CreateRoom1v1();
        }

        public override void OnJoinedRoom()
        {
            Debug.Log("[SoloMatchmaker] Joined room " + PhotonNetwork.CurrentRoom.Name + ", players: " + PhotonNetwork.CurrentRoom.PlayerCount);
            // Ensure GameModes remains hidden once in room
            if (gameModesPanel != null) gameModesPanel.SetActive(false);
            SetState(State.InRoomWaiting, "Waiting for opponent...");
            UpdateIcons();
            if (PhotonNetwork.CurrentRoom.PlayerCount >= maxPlayers)
                OnPaired();
        }

        public override void OnPlayerEnteredRoom(Player newPlayer)
        {
            Debug.Log("[SoloMatchmaker] Player entered: " + newPlayer.ActorNumber + ". Count=" + PhotonNetwork.CurrentRoom.PlayerCount);
            UpdateIcons();
            if (PhotonNetwork.CurrentRoom.PlayerCount >= maxPlayers)
                OnPaired();
        }

        public override void OnPlayerLeftRoom(Player otherPlayer)
        {
            Debug.LogWarning("[SoloMatchmaker] Player left. Restarting search.");
            if (_state == State.Paired || _state == State.InRoomWaiting)
            {
                SetState(State.Searching, "Opponent left. Finding new opponent...");
                JoinRandom1v1();
            }
        }

        public override void OnLeftRoom()
        {
            Debug.Log("[SoloMatchmaker] Left room.");
            SetState(State.Idle, "Modes");
            // Ensure game modes selection returns when leaving room for any reason
            if (gameModesPanel != null)
                gameModesPanel.SetActive(true);
            if (_opponentRuntimeAvatar != null)
            {
                Destroy(_opponentRuntimeAvatar.texture);
                Destroy(_opponentRuntimeAvatar);
                _opponentRuntimeAvatar = null;
            }
        }

        public override void OnDisconnected(DisconnectCause cause)
        {
            Debug.LogError("[SoloMatchmaker] Disconnected: " + cause);
            SetState(State.Error, $"Disconnected: {cause}");
            // Allow user to pick another mode after error
            if (gameModesPanel != null)
                gameModesPanel.SetActive(true);
        }

        private void TryApplyLocalProfileToPhoton()
        {
            try
            {
                // Ensure nickname from PlayerPrefs/Profile
                if (string.IsNullOrEmpty(PhotonNetwork.NickName))
                {
                    string name = string.Empty;
                    if (PlayerPrefs.HasKey("player_name")) name = PlayerPrefs.GetString("player_name", "");
                    if (string.IsNullOrEmpty(name) && global::ProfileManager.Instance != null)
                        name = global::ProfileManager.Instance.Data.userName;
                    if (!string.IsNullOrEmpty(name)) PhotonNetwork.NickName = name;
                }

                // Push avatar thumbnail as PNG bytes into custom properties
                if (PhotonNetwork.LocalPlayer != null)
                {
                    byte[] avatarBytes = CreateLocalAvatarThumbnailPng(128);
                    if (avatarBytes != null)
                    {
                        var props = new Hashtable { { AvatarPropKey, avatarBytes } };
                        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SoloMatchmaker] TryApplyLocalProfileToPhoton error: {e.Message}");
            }
        }

        private Image FindPortraitImage(GameObject root)
        {
            if (root == null) return null;
            // Prefer a child explicitly named "Image" or "Avatar"
            Transform t = root.transform.Find("Image");
            if (t == null) t = root.transform.Find("Avatar");
            if (t != null)
            {
                var img = t.GetComponentInChildren<Image>(true);
                if (img != null) return img;
            }
            // Otherwise, choose the first Image in children that is not on the root object
            var imgs = root.GetComponentsInChildren<Image>(true);
            foreach (var i in imgs)
            {
                if (i != null && i.gameObject != root) return i;
            }
            // Fallback to root's Image if nothing else exists
            return root.GetComponent<Image>();
        }

        private byte[] CreateLocalAvatarThumbnailPng(int maxSize)
        {
            var pm = global::ProfileManager.Instance;
            if (pm == null) return null;
            var sprite = pm.GetAvatarSprite();
            if (sprite == null) return null;
            var tex = sprite.texture;
            if (tex == null) return null;
            int longest = Mathf.Max(tex.width, tex.height);
            Texture2D final = tex;
            if (longest > maxSize)
            {
                float scale = (float)maxSize / longest;
                int w = Mathf.RoundToInt(tex.width * scale);
                int h = Mathf.RoundToInt(tex.height * scale);
                final = ScaleTextureRuntime(tex, w, h);
            }
            byte[] png = final.EncodeToPNG();
            if (final != tex) Destroy(final);
            return png;
        }

        private static Texture2D ScaleTextureRuntime(Texture2D source, int targetWidth, int targetHeight)
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

        private static Sprite CreateSpriteFromPng(byte[] pngBytes, ref Sprite cache)
        {
            if (pngBytes == null || pngBytes.Length == 0) return null;
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tex.LoadImage(pngBytes))
                {
                    if (cache != null)
                    {
                        if (cache.texture != null) Destroy(cache.texture);
                        Destroy(cache);
                    }
                    cache = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                    return cache;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SoloMatchmaker] CreateSpriteFromPng error: {e.Message}");
            }
            return null;
        }
    }
}
