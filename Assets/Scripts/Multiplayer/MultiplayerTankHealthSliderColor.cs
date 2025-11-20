using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;

/// <summary>
/// Sets the health slider color for multiplayer tanks: green for local player, red for opponents, both darken as damaged.
/// Attach to the health slider GameObject in the multiplayer tank prefab.
/// </summary>
public class MultiplayerTankHealthSliderColor : MonoBehaviour
{
    [Tooltip("Reference to the slider's fill image.")]
    public Image m_FillImage;
    [Tooltip("Reference to the TankHealth component.")]
    public TankHealth m_TankHealth;
    [Tooltip("PhotonView of the tank.")]
    public PhotonView m_PhotonView;

    private Color m_PlayerFull = new Color(0.1f, 0.7f, 0.1f); // dark green
    private Color m_PlayerZero = new Color(0.0f, 0.2f, 0.0f); // darker green
    private Color m_OpponentFull = new Color(0.7f, 0.1f, 0.1f); // dark red
    private Color m_OpponentZero = new Color(0.2f, 0.0f, 0.0f); // darker red

    void Awake()
    {
        if (!m_TankHealth) m_TankHealth = GetComponentInParent<TankHealth>();
        if (!m_PhotonView) m_PhotonView = GetComponentInParent<PhotonView>();
        if (!m_FillImage && m_TankHealth)
        {
            // Try to auto-find the fill image from TankHealth
            var slider = m_TankHealth.m_Slider;
            if (slider != null)
                m_FillImage = slider.fillRect?.GetComponent<Image>();
        }
    }

    void Update()
    {
        if (!m_TankHealth || !m_FillImage || !m_PhotonView) return;
        float t = Mathf.Clamp01(m_TankHealth.m_CurrentHealth / m_TankHealth.m_StartingHealth);

        // Respect forced-fill overrides (e.g., bots should always be blue in 5v5).
        if (m_TankHealth.m_UseForcedFill)
        {
            m_FillImage.color = m_TankHealth.m_ForcedFillColor;
            return;
        }

        if (m_PhotonView.IsMine)
        {
            m_FillImage.color = Color.Lerp(m_PlayerZero, m_PlayerFull, t);
        }
        else
        {
            m_FillImage.color = Color.Lerp(m_OpponentZero, m_OpponentFull, t);
        }
    }
}
