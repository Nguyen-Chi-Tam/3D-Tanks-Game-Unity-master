using UnityEngine;

/// <summary>
/// Simple component that tags a tank with a TeamId for friendly-fire checks and AI filtering.
/// </summary>
public class TankTeam : MonoBehaviour
{
    [Tooltip("Team identifier: 0=Blue, 1=Red (for 5v5 Mixed)")]
    public int TeamId;
}
