using UnityEngine;
using Photon.Pun;

public class ShellExplosion : MonoBehaviour
{
    public LayerMask m_TankMask;                        // Used to filter what the explosion affects, this should be set to "Players".
    public ParticleSystem m_ExplosionParticles;         // Reference to the particles that will play on explosion.
    public AudioSource m_ExplosionAudio;                // Reference to the audio that will play on explosion.
    public float m_MaxDamage = 100f;                    // The amount of damage done if the explosion is centred on a tank.
    public float m_ExplosionForce = 1000f;              // The amount of force added to a tank at the centre of the explosion.
    public float m_MaxLifeTime = 2f;                    // The time in seconds before the shell is removed.
    public float m_ExplosionRadius = 5f;                // The maximum distance away from the explosion tanks can be and are still affected.

    [HideInInspector] public int m_ShooterTeam = -1;     // Team id of shooter; prevents friendly fire when >=0


    private void Start ()
    {
        // If it isn't destroyed by then, destroy the shell after it's lifetime.
        Destroy (gameObject, m_MaxLifeTime);
    }


    private void OnTriggerEnter (Collider other)
    {
        // Collect all the colliders in a sphere from the shell's current position to a radius of the explosion radius.
        Collider[] colliders = Physics.OverlapSphere (transform.position, m_ExplosionRadius, m_TankMask);

        // Go through all the colliders...
        for (int i = 0; i < colliders.Length; i++)
        {
            // Use attachedRigidbody (works for child colliders) or climb to parent.
            Rigidbody targetRigidbody = colliders[i].attachedRigidbody;
            if (targetRigidbody == null)
                targetRigidbody = colliders[i].GetComponentInParent<Rigidbody>();

            // If still no rigidbody, skip.
            if (targetRigidbody == null)
                continue;

            // Friendly-fire filter: skip if same team
            int targetTeam = -1;
            var teamComp = colliders[i].GetComponentInParent<TankTeam>();
            if (teamComp != null) targetTeam = teamComp.TeamId;
            if (m_ShooterTeam >= 0 && targetTeam == m_ShooterTeam)
                continue;

            // Add an explosion force.
            targetRigidbody.AddExplosionForce (m_ExplosionForce, transform.position, m_ExplosionRadius);

            // Find the TankHealth script associated with the rigidbody.
            // Find TankHealth on same object or its parents (handles child collider cases).
            TankHealth targetHealth = targetRigidbody.GetComponent<TankHealth>();
            if (targetHealth == null)
                targetHealth = targetRigidbody.GetComponentInParent<TankHealth>();

            // If there is no TankHealth script attached to the gameobject, go on to the next collider.
            if (!targetHealth)
                continue;

            // Calculate the amount of damage the target should take based on it's distance from the shell.
            float damage = CalculateDamage (targetRigidbody.position);

            // Deal this damage in a multiplayer-safe way: only Master replicates to all.
            if (PhotonNetwork.IsConnected)
            {
                // Only master applies damage to avoid double hits; if no master (offline mode), fall back.
                if (PhotonNetwork.IsMasterClient)
                {
                    targetHealth.TakeDamageNetworked(damage);
                }
                else if (!PhotonNetwork.IsMasterClient && !PhotonNetwork.IsConnectedAndReady)
                {
                    // Safety fallback if Photon is half-initialized.
                    targetHealth.TakeDamage(damage);
                }
            }
            else
            {
                targetHealth.TakeDamage(damage);
            }
        }

        // Unparent the particles from the shell.
        m_ExplosionParticles.transform.parent = null;

        // Play the particle system.
        m_ExplosionParticles.Play();

        // Play the explosion sound effect (respect global SFX mute)
        if (m_ExplosionAudio)
        {
            m_ExplosionAudio.mute = AudioSettingsGlobal.SfxMuted;
            if (!AudioSettingsGlobal.SfxMuted)
                m_ExplosionAudio.Play();
        }

        // Once the particles have finished, destroy the gameobject they are on.
    Destroy (m_ExplosionParticles.gameObject, m_ExplosionParticles.main.duration);

        // Destroy the shell.
        Destroy (gameObject);
    }


    private float CalculateDamage (Vector3 targetPosition)
    {
        // Create a vector from the shell to the target.
        Vector3 explosionToTarget = targetPosition - transform.position;

        // Calculate the distance from the shell to the target.
        float explosionDistance = explosionToTarget.magnitude;

        // Calculate the proportion of the maximum distance (the explosionRadius) the target is away.
        float relativeDistance = (m_ExplosionRadius - explosionDistance) / m_ExplosionRadius;

        // Calculate damage as this proportion of the maximum possible damage.
        float damage = relativeDistance * m_MaxDamage;

        // Make sure that the minimum damage is always 0.
        damage = Mathf.Max (0f, damage);

        return damage;
    }
}