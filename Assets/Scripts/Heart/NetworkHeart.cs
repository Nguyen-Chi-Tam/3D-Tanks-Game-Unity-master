using Photon.Pun;
using UnityEngine;

[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(PhotonView))]
public class NetworkHeart : MonoBehaviourPun
{
    private bool _picked;

    private void Reset()
    {
        var col = GetComponent<Collider>();
        if (col) { col.isTrigger = true; }
    }

    private void OnTriggerEnter(Collider other)
    {
        // Find a TankHealth on the thing that collided (or its parents)
        var tankHealth = other.GetComponentInParent<TankHealth>();
        if (tankHealth == null)
            return;

        // Determine the owner's actor number from the tank's PhotonView
        var tankView = tankHealth.GetComponentInParent<PhotonView>();
        if (tankView == null || tankView.Owner == null)
            return;

        int actor = tankView.OwnerActorNr;

        if (PhotonNetwork.IsMasterClient)
        {
            ProcessPickup(actor);
        }
        else
        {
            photonView.RPC(nameof(RpcRequestPickup), RpcTarget.MasterClient, actor, photonView.ViewID);
        }
    }

    [PunRPC]
    private void RpcRequestPickup(int actorNumber, int heartViewId)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        // Only master processes to avoid race conditions
        ProcessPickup(actorNumber);
    }

    private void ProcessPickup(int actorNumber)
    {
        if (_picked) return;
        _picked = true;

        // Heal the actor's tank on all clients
        HealActorTank(actorNumber);

        // Destroy this heart across the network
        if (photonView != null)
        {
            PhotonNetwork.Destroy(photonView);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void HealActorTank(int actorNumber)
    {
        // Find the tank for this actor in the scene and heal it via RPC
        var setups = Object.FindObjectsByType<MultiplayerTankSetup>(FindObjectsSortMode.None);
        foreach (var setup in setups)
        {
            var pv = setup.photonView;
            if (pv != null && pv.OwnerActorNr == actorNumber)
            {
                var th = setup.GetComponent<TankHealth>();
                if (th != null)
                {
                    th.RestoreFullHealthNetworked();
                }
                return;
            }
        }

        // Fallback: heal any TankHealth with matching owner in parents
        var all = Object.FindObjectsByType<TankHealth>(FindObjectsSortMode.None);
        foreach (var th in all)
        {
            var pv = th.GetComponentInParent<PhotonView>();
            if (pv != null && pv.OwnerActorNr == actorNumber)
            {
                th.RestoreFullHealthNetworked();
                return;
            }
        }
    }
}
