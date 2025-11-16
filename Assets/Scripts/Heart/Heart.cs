// 07/11/2025 AI-Tag
// This was created with the help of Assistant, a Unity Artificial Intelligence product.

using UnityEngine;

public class Heart : MonoBehaviour
{
    // Use 3D trigger callback because the project uses 3D colliders and Rigidbody.
    private void OnTriggerEnter(Collider other)
    {
        // Try to find TankHealth on the collider itself or any of its parents.
        TankHealth tankHealth = other.GetComponentInParent<TankHealth>();
        if (tankHealth != null)
        {
            // Restore the tank's health.
            tankHealth.RestoreFullHealth();

            // Destroy the heart GameObject after it's collected.
            Destroy(gameObject);
        }
    }
}
