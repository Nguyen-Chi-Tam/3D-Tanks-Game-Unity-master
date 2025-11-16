using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays live team member counts for 5v5 modes.
/// Works with GameManager.TryGetTeamAliveCounts and updates at a small interval.
/// Attach this to your TeamCount Text object.
/// </summary>
[RequireComponent(typeof(Text))]
public class TeamCountUI : MonoBehaviour
{
    [SerializeField] private GameManager m_GameManager;
    [SerializeField] private MultiplayerGameManager m_MultiplayerManager;
    [SerializeField] private Text m_Text;
    [SerializeField, Tooltip("Seconds between text refreshes")] private float m_RefreshInterval = 0.2f;

    [Header("Appearance")]
    [SerializeField] private bool m_ShowLabels = false; // default to "5 – 5" style
    [SerializeField] private string m_BlueLabel = "BLUE";
    [SerializeField] private string m_RedLabel  = "RED";
    [SerializeField] private string m_Separator = "  –  ";
    [SerializeField] private Color m_BlueColor = new Color(0.2f, 0.5f, 1f);
    [SerializeField] private Color m_RedColor  = new Color(1f, 0.3f, 0.3f);

    private WaitForSeconds m_Wait;

    private void Awake()
    {
        if (m_Text == null) m_Text = GetComponent<Text>();
        if (m_GameManager == null) m_GameManager = Object.FindFirstObjectByType<GameManager>();
        if (m_MultiplayerManager == null) m_MultiplayerManager = Object.FindFirstObjectByType<MultiplayerGameManager>();
        if (m_Text != null) m_Text.supportRichText = true;
        m_Wait = new WaitForSeconds(Mathf.Max(0.05f, m_RefreshInterval));
    }

    private void OnEnable()
    {
        StartCoroutine(UpdateLoop());
    }

    private IEnumerator UpdateLoop()
    {
        while (isActiveAndEnabled)
        {
            UpdateText();
            yield return m_Wait;
        }
    }

    private void UpdateText()
    {
        if (m_Text == null)
            return;

        int blue, red;
        bool ok = false;
        // Prefer Multiplayer in this scene; fall back to local GameManager if multiplayer isn't present
        if (m_MultiplayerManager != null)
            ok = m_MultiplayerManager.TryGetTeamAliveCounts(out blue, out red);
        else if (m_GameManager != null)
            ok = m_GameManager.TryGetTeamAliveCounts(out blue, out red);
        else
            return;

        if (ok)
        {
            m_Text.enabled = true;

            string blueHex = ColorUtility.ToHtmlStringRGB(m_BlueColor);
            string redHex  = ColorUtility.ToHtmlStringRGB(m_RedColor);

            string left  = m_ShowLabels ? ($"{m_BlueLabel} {blue}") : blue.ToString();
            string right = m_ShowLabels ? ($"{m_RedLabel} {red}")  : red.ToString();

            m_Text.text = $"<color=#{blueHex}>{left}</color>{m_Separator}<color=#{redHex}>{right}</color>";
        }
        else
        {
            // Hide text for non-team modes to avoid confusion
            m_Text.enabled = false;
        }
    }
}
