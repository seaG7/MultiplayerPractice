using FishNet.Object;
using UnityEngine;

namespace Networking
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FishNet.Object.NetworkObject))]
    public class HealthPickup : NetworkBehaviour
    {
        [SerializeField] private int healAmount = 40;

        private PickupManager pickupManager;
        private Vector3 spawnPosition;

        public void Initialize(PickupManager manager, Vector3 position)
        {
            pickupManager = manager;
            spawnPosition = position;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsServerInitialized)
            {
                return;
            }

            NetworkPlayer player = other.GetComponent<NetworkPlayer>();
            if (player == null || !player.IsAlive)
            {
                return;
            }

            if (!player.TryRestoreHealthOnServer(healAmount))
            {
                return;
            }

            pickupManager?.OnPickedUp(spawnPosition);
            ServerManager.Despawn(NetworkObject);
        }
    }
}
