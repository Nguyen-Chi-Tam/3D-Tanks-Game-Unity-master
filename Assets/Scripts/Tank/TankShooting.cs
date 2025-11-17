using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;

public class TankShooting : MonoBehaviour
{
    public int m_PlayerNumber = 1;              // Used to identify the different players.
    public Rigidbody m_Shell;                   // Prefab of the shell.
    public Transform m_FireTransform;           // A child of the tank where the shells are spawned.
    public Slider m_AimSlider;                  // A child of the tank that displays the current launch force.
    public AudioSource m_ShootingAudio;         // Reference to the audio source used to play the shooting audio. NB: different to the movement audio source.
    public AudioClip m_ChargingClip;            // Audio that plays when each shot is charging up.
    public AudioClip m_FireClip;                // Audio that plays when each shot is fired.
    public float m_MinLaunchForce = 15f;        // The force given to the shell if the fire button is not held.
    public float m_MaxLaunchForce = 30f;        // The force given to the shell if the fire button is held for the max charge time.
    public float m_MaxChargeTime = 0.75f;       // How long the shell can charge for before it is fired at max force.
    public float m_FireCooldown = 1.5f;         // Cooldown between shots to avoid spamming.


    private string m_FireButton;                // The input axis that is used for launching shells.
    private float m_CurrentLaunchForce;         // The force that will be given to the shell when the fire button is released.
    private float m_ChargeSpeed;                // How fast the launch force increases, based on the max charge time.
    private bool m_Fired;                       // Whether or not the shell has been launched with this button press.
    private float m_NextFireTime = 0f;          // Time.time when the next shot is allowed.


    private void OnEnable()
    {
        // When the tank is turned on, reset the launch force and the UI
        m_CurrentLaunchForce = m_MinLaunchForce;
        m_AimSlider.value = m_MinLaunchForce;
    }


    private void Start ()
    {
        // The fire axis is based on the player number.
         if(m_PlayerNumber == 1||m_PlayerNumber == 2)
            m_FireButton = "Fire" + m_PlayerNumber;
        else m_FireButton = "Fire";

        // The rate that the launch force charges up is the range of possible forces by the max charge time.
        m_ChargeSpeed = (m_MaxLaunchForce - m_MinLaunchForce) / m_MaxChargeTime;
    }


    private void Update ()
    {
        // The slider should have a default value of the minimum launch force.
        m_AimSlider.value = m_MinLaunchForce;

        // Block all firing logic during cooldown
        if (Time.time < m_NextFireTime)
        {
            return;
        }

        // If the max force has been exceeded and the shell hasn't yet been launched...
        if (m_CurrentLaunchForce >= m_MaxLaunchForce && !m_Fired)
        {
            // ... use the max force and launch the shell.
            m_CurrentLaunchForce = m_MaxLaunchForce;
            Fire ();
        }
        // Otherwise, if the fire button has just started being pressed...
        else if (Input.GetButtonDown (m_FireButton))
        {
            // ... reset the fired flag and reset the launch force.
            m_Fired = false;
            m_CurrentLaunchForce = m_MinLaunchForce;

            // Change the clip to the charging clip and start it playing.
            m_ShootingAudio.clip = m_ChargingClip;
            m_ShootingAudio.Play ();
        }
        // Otherwise, if the fire button is being held and the shell hasn't been launched yet...
        else if (Input.GetButton (m_FireButton) && !m_Fired)
        {
            // Increment the launch force and update the slider.
            m_CurrentLaunchForce += m_ChargeSpeed * Time.deltaTime;

            m_AimSlider.value = m_CurrentLaunchForce;
        }
        // Otherwise, if the fire button is released and the shell hasn't been launched yet...
        else if (Input.GetButtonUp (m_FireButton) && !m_Fired)
        {
            // ... launch the shell.
            Fire ();
        }
    }


    private void Fire ()
    {
        // Set the fired flag so only Fire is only called once.
        m_Fired = true;

        // Networked fire: only the owner triggers the RPC; all clients spawn locally
        var pv = GetComponent<PhotonView>();
        int teamId = -1;
        var myTeam = GetComponent<TankTeam>();
        if (myTeam != null) teamId = myTeam.TeamId;

        if (pv != null)
        {
            if (pv.IsMine)
                pv.RPC(nameof(RpcFire), RpcTarget.All, m_FireTransform.position, m_FireTransform.rotation, m_CurrentLaunchForce, teamId);
        }
        else
        {
            // Fallback for offline
            RpcFire(m_FireTransform.position, m_FireTransform.rotation, m_CurrentLaunchForce, teamId);
        }

        // Reset the launch force.  This is a precaution in case of missing button events.
        m_CurrentLaunchForce = m_MinLaunchForce;

        // Start cooldown
        m_NextFireTime = Time.time + m_FireCooldown;
    }

    [PunRPC]
    private void RpcFire(Vector3 pos, Quaternion rot, float launchForce, int teamId)
    {
        if (m_Shell == null || m_FireTransform == null) return;
        Rigidbody shellInstance = Instantiate(m_Shell, pos, rot) as Rigidbody;
        var shell = shellInstance.GetComponent<ShellExplosion>();
        if (shell != null) shell.m_ShooterTeam = teamId;
        shellInstance.linearVelocity = launchForce * (rot * Vector3.forward);

        if (m_ShootingAudio)
        {
            m_ShootingAudio.clip = m_FireClip;
            m_ShootingAudio.mute = AudioSettingsGlobal.SfxMuted;
            if (!AudioSettingsGlobal.SfxMuted)
                m_ShootingAudio.Play();
        }
    }

    // Public AI hook: instantly fires a shell at current forward direction using max launch force.
    public void AIInstantFire()
    {
        if (Time.time < m_NextFireTime) return; // respect cooldown for AI too
        if (m_Shell == null || m_FireTransform == null) return;
        Rigidbody shellInstance = Instantiate(m_Shell, m_FireTransform.position, m_FireTransform.rotation) as Rigidbody;
        float launchForce = m_MaxLaunchForce;
        shellInstance.linearVelocity = launchForce * m_FireTransform.forward;
        var shell = shellInstance.GetComponent<ShellExplosion>();
        var myTeam = GetComponent<TankTeam>();
        if (shell != null && myTeam != null)
            shell.m_ShooterTeam = myTeam.TeamId;
        if (m_ShootingAudio && m_FireClip)
        {
            m_ShootingAudio.clip = m_FireClip;
            m_ShootingAudio.mute = AudioSettingsGlobal.SfxMuted;
            if (!AudioSettingsGlobal.SfxMuted)
                m_ShootingAudio.Play();
        }

        // Start cooldown after AI shot
        m_NextFireTime = Time.time + m_FireCooldown;
    }
}