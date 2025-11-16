using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;

public class TankHealth : MonoBehaviour
{
    public float m_StartingHealth = 100f;          
    public Slider m_Slider;                        
    public Image m_FillImage;                      
    public Color m_FullHealthColor = Color.green;  
    public Color m_ZeroHealthColor = Color.red;    
    public GameObject m_ExplosionPrefab;
    
    [Header("UI Overrides")]
    [Tooltip("When true, the health fill will use the forced color instead of gradient.")]
    public bool m_UseForcedFill = false;
    [Tooltip("Color to use when forced fill is enabled (e.g., blue for bot tanks).")]
    public Color m_ForcedFillColor = Color.blue;
    
    private AudioSource m_ExplosionAudio;          
    private ParticleSystem m_ExplosionParticles;   
    private float m_CurrentHealth;  
    private bool m_Dead;            
    private PhotonView m_PhotonView;


    private void Awake()
    {
        m_ExplosionParticles = Instantiate(m_ExplosionPrefab).GetComponent<ParticleSystem>();
        m_ExplosionAudio = m_ExplosionParticles.GetComponent<AudioSource>();

        m_ExplosionParticles.gameObject.SetActive(false);
        // Cache a PhotonView from this object or its parent (tank root typically has it).
        m_PhotonView = GetComponent<PhotonView>();
        if (m_PhotonView == null)
            m_PhotonView = GetComponentInParent<PhotonView>();
    }


    private void OnEnable()
    {
        m_CurrentHealth = m_StartingHealth;
        m_Dead = false;

        // Ensure slider max is set so the value maps correctly to the UI.
        if (m_Slider != null)
        {
            m_Slider.maxValue = m_StartingHealth;
        }

        SetHealthUI();
    }
    
    public void RestoreFullHealth()
    {
        m_CurrentHealth = m_StartingHealth; // Restore health to full.
        // If the tank was marked as dead, revive it logically.
        m_Dead = false;

        // Immediately update the UI so the player sees the change.
        SetHealthUI();
    }

    // Multiplayer-safe heal entry point used by networked pickups.
    public void RestoreFullHealthNetworked()
    {
        if (PhotonNetwork.IsConnected)
        {
            if (PhotonNetwork.IsMasterClient)
            {
                if (m_PhotonView != null)
                {
                    m_PhotonView.RPC(nameof(RpcRestoreFullHealth), RpcTarget.AllBuffered);
                }
                else
                {
                    RestoreFullHealth();
                }
            }
            else
            {
                // Non-master does not apply directly; master will broadcast.
            }
        }
        else
        {
            RestoreFullHealth();
        }
    }

    [PunRPC]
    private void RpcRestoreFullHealth()
    {
        RestoreFullHealth();
    }
    public void TakeDamage(float amount)
    {
        // Adjust the tank's current health, update the UI based on the new health and check whether or not the tank is dead.
        m_CurrentHealth -= amount;
        SetHealthUI();

        if(m_CurrentHealth <=0f && !m_Dead){
            OnDeath();
        }
    }

    // Multiplayer-safe damage entry point. Master applies and replicates; offline or no Photon uses local.
    public void TakeDamageNetworked(float amount)
    {
        if (PhotonNetwork.IsConnected)
        {
            if (PhotonNetwork.IsMasterClient)
            {
                if (m_PhotonView != null)
                {
                    m_PhotonView.RPC(nameof(RpcTakeDamage), RpcTarget.All, amount);
                }
                else
                {
                    // Fallback: apply locally
                    TakeDamage(amount);
                }
            }
            else
            {
                // Non-master: do not apply locally to avoid desync. Master will apply via its own collision.
            }
        }
        else
        {
            // Not connected to Photon: single-player/local
            TakeDamage(amount);
        }
    }

    [PunRPC]
    private void RpcTakeDamage(float amount)
    {
        TakeDamage(amount);
    }


    public void SetHealthUI()
    {
        // Adjust the value and colour of the slider.
        m_Slider.value = m_CurrentHealth;
        if (m_UseForcedFill)
        {
            m_FillImage.color = m_ForcedFillColor;
        }
        else
        {
            m_FillImage.color = Color.Lerp(m_ZeroHealthColor, m_FullHealthColor, m_CurrentHealth/m_StartingHealth);
        }
    }


    private void OnDeath()
    {
        // Play the effects for the death of the tank and deactivate it.
        m_Dead = true;
        m_ExplosionParticles.transform.position=transform.position;
        m_ExplosionParticles.gameObject.SetActive(true);

        m_ExplosionParticles.Play();
        m_ExplosionAudio.Play();

        gameObject.SetActive(false);
    }
}