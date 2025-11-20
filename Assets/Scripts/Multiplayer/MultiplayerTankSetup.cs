using UnityEngine;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;

[RequireComponent(typeof(PhotonView))]
public class MultiplayerTankSetup : MonoBehaviourPun, IPunOwnershipCallbacks, IPunObservable
{
    public Renderer[] coloredRenderers;
    public Color blueColor = new Color(0.2f, 0.5f, 1f);
    public Color redColor  = new Color(1f, 0.3f, 0.3f);

    int _teamId;
    int _ownerActorNumber;
    bool _roundLocked;
    bool _isBot;
    Rigidbody _rb;

    // Network interpolation state (for non-owners)
    Vector3 _netPos;
    Quaternion _netRot;
    bool _netInitialized;
    [SerializeField] float _posLerp = 12f;
    [SerializeField] float _rotLerp = 12f;

    void Start()
    {
        _rb = GetComponent<Rigidbody>();
        if (photonView.InstantiationData != null && photonView.InstantiationData.Length >= 2)
        {
            _teamId = (int)photonView.InstantiationData[0];
            _ownerActorNumber = (int)photonView.InstantiationData[1];
            // Bots in 5v5 are spawned with a third instantiation value == 1 and
            // use negative actor numbers. Add a fallback so health color logic
            // always treats negative actor numbers as bots even if init data
            // changes or a prefab override drops the flag.
            if (photonView.InstantiationData.Length >= 3)
                _isBot = ((int)photonView.InstantiationData[2]) == 1;
            if (!_isBot && _ownerActorNumber < 0)
                _isBot = true;
        }

        // Make sure movement is network-synchronized for bots and remote players
        TryEnsureNetworkSyncComponents();

        // Ensure/propagate team id to TankTeam component for friendly-fire checks and UI
        var teamTag = GetComponent<TankTeam>();
        if (teamTag == null) teamTag = gameObject.AddComponent<TankTeam>();
        teamTag.TeamId = _teamId;

        ApplyTeamColor();
        ConfigureInputAndCamera();
        EnsureBotAIIfNeeded();
        MarkBotHealthIfNeeded();
    }

    void TryEnsureNetworkSyncComponents()
    {
        var view = photonView;
        if (view == null) return;

        var observed = view.ObservedComponents ?? new List<Component>();

        var trSync = GetComponent<PhotonTransformViewClassic>();
        if (trSync == null)
            trSync = gameObject.AddComponent<PhotonTransformViewClassic>();
        if (!observed.Contains(trSync)) observed.Add(trSync);

        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            var rbSync = GetComponent<PhotonRigidbodyView>();
            if (rbSync == null)
                rbSync = gameObject.AddComponent<PhotonRigidbodyView>();
            if (!observed.Contains(rbSync)) observed.Add(rbSync);
        }

        // Ensure this component is also observed to stream transforms if needed
        if (!observed.Contains(this)) observed.Add(this);

        view.ObservedComponents = observed;
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
        // Bots should NEVER use local player input even for the master client.
        bool enable = isLocal && !_roundLocked && !_isBot;
        if (movement) movement.enabled = enable;
        if (shooting) shooting.enabled = enable;
        if (isLocal)
        {
            Debug.Log("[MultiplayerTankSetup] ConfigureInputAndCamera local=" + isLocal + " roundLocked=" + _roundLocked + " movementEnabled=" + (movement? movement.enabled : false));
        }

        if (isLocal)
        {
            var cam = Object.FindFirstObjectByType<CameraControl>();
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
        // Auto-wire local components so AI works without GameManager context
        var shoot = GetComponent<TankShooting>();
        var move  = GetComponent<TankMovement>();
        ai.Initialize(null, null, shoot, move);
        // Enable/disable with round lock
        ai.enabled = !_roundLocked;
    }

    void MarkBotHealthIfNeeded()
    {
        if (!_isBot) return;
        var health = GetComponent<TankHealth>();
        if (health != null)
        {
            health.m_UseForcedFill = true;
            // Force bot health bar to blue regardless of team (5v5 requirement).
            // Chosen tint matches multiplayer blue tank color for consistency.
            health.m_ForcedFillColor = new Color(0.2f, 0.5f, 1f);
            health.SetHealthUI();
        }
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

    // -------- Basic transform replication fallback (bots/remote tanks) --------
    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(transform.position);
            stream.SendNext(transform.rotation);
            stream.SendNext(_rb != null ? _rb.linearVelocity : Vector3.zero);
        }
        else
        {
            _netPos = (Vector3)stream.ReceiveNext();
            _netRot = (Quaternion)stream.ReceiveNext();
            Vector3 vel = (Vector3)stream.ReceiveNext();
            if (_rb != null && !_rb.isKinematic)
            {
                // For owners this won't run; for remotes keep rb quiet
                _rb.linearVelocity = vel;
            }
            _netInitialized = true;
        }
    }

    void Update()
    {
        if (photonView.IsMine) return;
        if (!_netInitialized) return;

        // Smoothly move towards networked state for remote clients
        transform.position = Vector3.Lerp(transform.position, _netPos, Time.deltaTime * _posLerp);
        transform.rotation = Quaternion.Slerp(transform.rotation, _netRot, Time.deltaTime * _rotLerp);
    }
}