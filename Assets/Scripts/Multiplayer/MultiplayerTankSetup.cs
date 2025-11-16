using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

[RequireComponent(typeof(PhotonView))]
public class MultiplayerTankSetup : MonoBehaviourPun, IPunOwnershipCallbacks
{
    public Renderer[] coloredRenderers;
    public Color blueColor = new Color(0.2f, 0.5f, 1f);
    public Color redColor  = new Color(1f, 0.3f, 0.3f);

    int _teamId;
    int _ownerActorNumber;
    bool _roundLocked;
    bool _isBot;

    void Start()
    {
        if (photonView.InstantiationData != null && photonView.InstantiationData.Length >= 2)
        {
            _teamId = (int)photonView.InstantiationData[0];
            _ownerActorNumber = (int)photonView.InstantiationData[1];
            if (photonView.InstantiationData.Length >= 3)
                _isBot = ((int)photonView.InstantiationData[2]) == 1;
        }

        // Propagate team id to TankTeam component for friendly-fire checks and UI
        var teamTag = GetComponent<TankTeam>();
        if (teamTag != null)
            teamTag.TeamId = _teamId;

        ApplyTeamColor();
        ConfigureInputAndCamera();
        EnsureBotAIIfNeeded();
    }

    void ApplyTeamColor()
    {
        var c = _teamId == 0 ? blueColor : redColor;
        // Fallback: if not assigned in inspector, grab all renderers under the tank
        if (coloredRenderers == null || coloredRenderers.Length == 0)
            coloredRenderers = GetComponentsInChildren<Renderer>(true);

        if (coloredRenderers == null) return;
        foreach (var r in coloredRenderers)
        {
            if (r == null) continue;
            r.material.color = c;
        }
    }

    void ConfigureInputAndCamera()
    {
        bool isLocal = photonView.IsMine;

        var movement = GetComponent<TankMovement>();
        var shooting = GetComponent<TankShooting>();
        bool enable = isLocal && !_roundLocked;
        if (movement) movement.enabled = enable;
        if (shooting) shooting.enabled = enable;
        if (isLocal)
        {
            Debug.Log("[MultiplayerTankSetup] ConfigureInputAndCamera local=" + isLocal + " roundLocked=" + _roundLocked + " movementEnabled=" + (movement? movement.enabled : false));
        }

        if (isLocal)
        {
            var cam = FindObjectOfType<CameraControl>();
            if (cam != null)
            {
                var list = new System.Collections.Generic.List<Transform>(cam.m_Targets ?? new Transform[0]);
                if (!list.Contains(transform))
                {
                    list.Add(transform);
                    cam.m_Targets = list.ToArray();
                }
            }
        }
    }

    void EnsureBotAIIfNeeded()
    {
        // Only the owner (master) runs the bot AI to avoid double-control.
        if (!_isBot || !photonView.IsMine) return;
        var ai = GetComponent<TankAI>();
        if (ai == null) ai = gameObject.AddComponent<TankAI>();
        // Enable/disable with round lock
        ai.enabled = !_roundLocked;
    }

    // Public API called from manager via SendMessage; also callable via RPC if desired
    public void SetRoundLocked(bool locked)
    {
        _roundLocked = locked;
        ConfigureInputAndCamera();
        EnsureBotAIIfNeeded();
        if (photonView.IsMine)
        {
            Debug.Log("[MultiplayerTankSetup] SetRoundLocked locked=" + locked);
        }
    }

    void OnEnable()
    {
        PhotonNetwork.AddCallbackTarget(this);
    }

    void OnDisable()
    {
        PhotonNetwork.RemoveCallbackTarget(this);
    }

    // React to late ownership transfers (e.g., when Master hands ownership to the joining client)
    public void OnOwnershipTransfered(PhotonView targetView, Player previousOwner)
    {
        if (targetView == photonView)
            ConfigureInputAndCamera();
    }

    public void OnOwnershipRequest(PhotonView targetView, Player requestingPlayer)
    {
        // Not used here; no runtime requests from clients.
    }

    public void OnOwnershipTransferFailed(PhotonView targetView, Player senderOfFailedRequest)
    {
        // Optional: log if needed.
    }
}