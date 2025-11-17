// 16/11/2025 AI-Tag
// Simple room chat for PUN. Creates one Text line per message under a vertical layout.

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Photon.Pun;

namespace Tanks.UI.Chat
{
    [RequireComponent(typeof(PhotonView))]
    public class ChatManager : MonoBehaviour
    {
        [Header("UI Refs")]
        [Tooltip("Root panel to show/hide chat")] public GameObject ChatPanel;
        [Tooltip("Input field where the user types")] public InputField Input;
        [Tooltip("Send button (optional) – call OnSendClicked")] public Button SendButton;
        [Tooltip("ScrollRect containing the messages")] public ScrollRect Scroll;
        [Tooltip("Content transform that holds message items")] public Transform Content;
        [Tooltip("Prefab with a Unity UI Text component for one message line")] public Text MessagePrefab;
        [Tooltip("Pop-up panel for new message notification")] public GameObject NewMessagePopUp;
        [Tooltip("Optional CanvasGroup on chat panel for hiding without deactivation")] public CanvasGroup ChatPanelGroup;
        [Tooltip("Start with the chat panel visible")] public bool StartVisible = false;
        [Tooltip("Optional: the Open Chat UI Button to clear selection so Space won't trigger it")] public Button OpenChatUIButton;

        private PhotonView _pv;
        private bool _chatPanelActive = false;
        private bool _hasUnread = false;
        private Coroutine _popupRoutine;


        private void Awake()
        {
            _pv = GetComponent<PhotonView>();
            if (SendButton) SendButton.onClick.AddListener(OnSendClicked);
            // Force single-line input so Enter doesn't insert newlines
            if (Input) Input.lineType = InputField.LineType.SingleLine;
            // Prevent keyboard-submit retrigger on the Open button by removing navigation and selection
            if (OpenChatUIButton)
            {
                var nav = OpenChatUIButton.navigation;
                nav.mode = Navigation.Mode.None;
                OpenChatUIButton.navigation = nav;
            }

            // Ensure panel uses CanvasGroup so object stays active (RPCs work)
            if (ChatPanel != null && ChatPanelGroup == null)
            {
                ChatPanelGroup = ChatPanel.GetComponent<CanvasGroup>();
                if (ChatPanelGroup == null)
                    ChatPanelGroup = ChatPanel.AddComponent<CanvasGroup>();
            }
            // Apply initial visibility
            if (ChatPanelGroup != null)
            {
                if (StartVisible) {
                    ChatPanelGroup.alpha = 1f;
                    ChatPanelGroup.blocksRaycasts = true;
                    ChatPanelGroup.interactable = true;
                    _chatPanelActive = true;
                } else {
                    ChatPanelGroup.alpha = 0f;
                    ChatPanelGroup.blocksRaycasts = false;
                    ChatPanelGroup.interactable = false;
                    _chatPanelActive = false;
                }
            }
        }

        private void OnDestroy()
        {
            if (SendButton) SendButton.onClick.RemoveListener(OnSendClicked);
        }

        public void OnSendClicked()
        {
            if (Input == null) return;
            var raw = (Input.text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(raw)) return;

            string senderName = !string.IsNullOrEmpty(PhotonNetwork.NickName)
                ? PhotonNetwork.NickName
                : $"Player {PhotonNetwork.LocalPlayer?.ActorNumber ?? 0}";

            Color senderColor = GetLocalPlayerColor();
            string colorHex = ColorUtility.ToHtmlStringRGB(senderColor);
            int actor = PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : -1;

            if (_pv != null)
                _pv.RPC(nameof(RpcReceiveChat), RpcTarget.All, senderName, colorHex, raw, actor);
            else
                RpcReceiveChat(senderName, colorHex, raw, actor);

            Input.text = string.Empty;
            Input.ActivateInputField();
        }

        // Optional: wire your OpenChatButton and ExitBtn to these

        public void OpenChat()
        {
            SetChatVisible(true);
            if (Input) Input.ActivateInputField();
            DeselectAllUI();
            SetTankShootingEnabled(false);
            SetTankMovementEnabled(false);
        }

        public void CloseChat()
        {
            SetChatVisible(false);
            DeselectAllUI();
            SetTankShootingEnabled(true);
            SetTankMovementEnabled(true);
        }

        // Convenience for UI buttons: toggles between open/close
        public void ToggleChat()
        {
            if (_chatPanelActive) CloseChat();
            else OpenChat();
        }

        private void Update()
        {
            // Toggle with 'C' unless typing in the chat input
            bool inputFocused = Input != null && Input.isFocused;
            if (UnityEngine.Input.GetKeyDown(KeyCode.C))
            {
                if (!inputFocused)
                {
                    ToggleChat();
                    if (_chatPanelActive && Input != null)
                    {
                        Input.ActivateInputField();
                    }
                }
            }

            // Press Enter to send when the chat panel is active
            if (_chatPanelActive && (UnityEngine.Input.GetKeyDown(KeyCode.Return) || UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter)))
            {
                if (Input != null)
                {
                    if (!Input.isFocused)
                    {
                        // Focus first if not focused; don't select-all
                        Input.ActivateInputField();
                        return;
                    }

                    var toSend = (Input.text ?? string.Empty).Trim();
                    if (toSend.Length > 0)
                        OnSendClicked();
                }
            }

            // Press Esc to close chat when active
            if (_chatPanelActive && UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                CloseChat();
            }
        }

        private void SetChatVisible(bool visible)
        {
            if (ChatPanel && !ChatPanel.activeSelf)
            {
                // ensure panel object is active in case something else disabled it
                ChatPanel.SetActive(true);
            }

            if (ChatPanelGroup != null)
            {
                ChatPanelGroup.alpha = visible ? 1f : 0f;
                ChatPanelGroup.blocksRaycasts = visible;
                ChatPanelGroup.interactable = visible;
            }
            else if (ChatPanel && ChatPanel != gameObject)
            {
                ChatPanel.SetActive(visible);
            }

            _chatPanelActive = visible;
            if (visible)
            {
                if (NewMessagePopUp != null) NewMessagePopUp.SetActive(false);
                _hasUnread = false;
            }

            Debug.Log($"[ChatManager] SetChatVisible({visible}) panelActive={ChatPanel?.activeSelf} cg={(ChatPanelGroup!=null ? ChatPanelGroup.alpha.ToString("0.00") : "none")}" );
        }

        private void DeselectAllUI()
        {
            if (EventSystem.current != null)
            {
                // Clear selected so Space key can't trigger last-selected button
                EventSystem.current.SetSelectedGameObject(null);
            }
        }


        [PunRPC]
        private void RpcReceiveChat(string senderName, string colorHex, string message, int senderActor)
        {
            if (MessagePrefab == null || Content == null) return;

            var line = Instantiate(MessagePrefab, Content);
            bool isLocal = PhotonNetwork.LocalPlayer != null && PhotonNetwork.LocalPlayer.ActorNumber == senderActor;
            string nameLabel = isLocal ? "YOU" : senderName;
            string coloredName = $"<color=#{colorHex}>{nameLabel}</color>";
            line.text = $"{coloredName}: {message}";

            // Auto-scroll to bottom next frame
            if (Scroll != null)
                StartCoroutine(ScrollToBottom());

            // Show popup only for opponent messages while panel hidden
            if (!_chatPanelActive && !isLocal && NewMessagePopUp != null)
            {
                ShowNewMessagePopup(nameLabel, message);
            }
        }
        
        private void ShowNewMessagePopup(string senderName, string message)
        {
            if (NewMessagePopUp == null) return;
            NewMessagePopUp.SetActive(true);
            var msgText = NewMessagePopUp.transform.Find("Message")?.GetComponent<Text>();
            if (msgText != null)
            {
                msgText.text = $"{senderName}: {message}";
            }
            _hasUnread = true;
            if (_popupRoutine != null)
                StopCoroutine(_popupRoutine);
            _popupRoutine = StartCoroutine(HidePopupAfterSeconds(5f));
        }

        private System.Collections.IEnumerator HidePopupAfterSeconds(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (!_chatPanelActive && NewMessagePopUp != null)
            {
                NewMessagePopUp.SetActive(false);
            }
            _popupRoutine = null;
        }

        private System.Collections.IEnumerator ScrollToBottom()
        {
            yield return null; // wait 1 frame so layout updates
            if (Scroll != null)
            {
                Scroll.verticalNormalizedPosition = 0f;
                LayoutRebuilder.ForceRebuildLayoutImmediate(Scroll.content);
            }
        }
        private void SetTankShootingEnabled(bool enabled)
        {
            // Disable/enable all local tank shooting scripts
            try
            {
                var views = FindObjectsByType<PhotonView>(FindObjectsSortMode.None);
                foreach (var v in views)
                {
                    if (v == null || !v.IsMine) continue;
                    var shooting = v.GetComponent<TankShooting>() ?? v.GetComponentInChildren<TankShooting>(true);
                    if (shooting != null)
                        shooting.enabled = enabled;
                }
            }
            catch { }
        }

        private void SetTankMovementEnabled(bool enabled)
        {
            // Disable/enable the local player's movement script and stop motion when disabling
            try
            {
                var views = FindObjectsByType<PhotonView>(FindObjectsSortMode.None);
                foreach (var v in views)
                {
                    if (v == null || !v.IsMine) continue;
                    var movement = v.GetComponent<TankMovement>() ?? v.GetComponentInChildren<TankMovement>(true);
                    if (movement != null)
                        movement.enabled = enabled;

                    if (!enabled)
                    {
                        var rb = v.GetComponent<Rigidbody>() ?? v.GetComponentInChildren<Rigidbody>(true);
                        if (rb != null)
                        {
                            rb.linearVelocity = Vector3.zero;
                            rb.angularVelocity = Vector3.zero;
                        }
                    }
                }
            }
            catch { }
        }

        private Color GetLocalPlayerColor()
        {
            // Derive color from the local player's owned tank.
            // Prefer team color via TankTeam; fallback to renderer material color; else white.
            try
            {
                var views = FindObjectsByType<PhotonView>(FindObjectsSortMode.None);
                foreach (var v in views)
                {
                    if (v == null || !v.IsMine) continue;

                    // Team-based color
                    var tt = v.GetComponent<TankTeam>() ?? v.GetComponentInParent<TankTeam>();
                    if (tt != null)
                    {
                        return GetTeamColor(tt.TeamId);
                    }

                    // Material color (works for 1v1 where TankManager tinted meshes)
                    var mr = v.GetComponentInChildren<MeshRenderer>(true);
                    if (mr != null && mr.material != null && mr.material.HasProperty("_Color"))
                    {
                        return mr.material.color;
                    }
                }
            }
            catch { }
            return Color.white;
        }

        private Color GetTeamColor(int teamId)
        {
            if (teamId == 0) return new Color(0.2f, 0.5f, 1f);   // Blue team
            if (teamId == 1) return new Color(1f, 0.3f, 0.3f);    // Red team
            return Color.white;
        }
    }
}
