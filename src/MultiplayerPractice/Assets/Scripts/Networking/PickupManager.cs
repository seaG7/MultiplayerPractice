using System.Collections;
using FishNet;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;

namespace Networking
{
    public class PickupManager : MonoBehaviour
    {
        [SerializeField] private GameObject healthPickupPrefab;
        [SerializeField] private Transform[] spawnPoints;
        [SerializeField] private float respawnDelay = 10f;

        private NetworkManager networkManager;
        private bool callbacksRegistered;
        private bool initialSpawnDone;

        private void OnEnable()
        {
            RegisterCallbacks();
        }

        private void Start()
        {
            RegisterCallbacks();
        }

        private void OnDisable()
        {
            if (!callbacksRegistered || networkManager == null)
            {
                return;
            }

            networkManager.ServerManager.OnServerConnectionState -= HandleServerConnectionState;
            callbacksRegistered = false;
        }

        public void OnPickedUp(Vector3 position)
        {
            if (networkManager == null || !networkManager.IsServerStarted)
            {
                return;
            }

            StartCoroutine(RespawnAfterDelay(position));
        }

        private void HandleServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started)
            {
                if (!initialSpawnDone)
                {
                    initialSpawnDone = true;
                    SpawnAll();
                }

                return;
            }

            initialSpawnDone = false;
        }

        private void RegisterCallbacks()
        {
            if (callbacksRegistered)
            {
                return;
            }

            networkManager = InstanceFinder.NetworkManager ?? FindFirstObjectByType<NetworkManager>();
            if (networkManager == null)
            {
                return;
            }

            networkManager.ServerManager.OnServerConnectionState += HandleServerConnectionState;
            callbacksRegistered = true;

            if (networkManager.IsServerStarted)
            {
                HandleServerConnectionState(new ServerConnectionStateArgs(LocalConnectionState.Started, -1));
            }
        }

        private void SpawnAll()
        {
            if (healthPickupPrefab == null || spawnPoints == null)
            {
                return;
            }

            foreach (Transform point in spawnPoints)
            {
                if (point != null)
                {
                    SpawnPickup(point.position);
                }
            }
        }

        private IEnumerator RespawnAfterDelay(Vector3 position)
        {
            yield return new WaitForSeconds(respawnDelay);
            SpawnPickup(position);
        }

        private void SpawnPickup(Vector3 position)
        {
            if (healthPickupPrefab == null)
            {
                Debug.LogError("PickupManager: Health pickup prefab is not assigned.");
                return;
            }

            GameObject pickupInstance = Instantiate(healthPickupPrefab, position, Quaternion.identity);
            HealthPickup pickup = pickupInstance.GetComponent<HealthPickup>();
            FishNet.Object.NetworkObject pickupNetworkObject = pickupInstance.GetComponent<FishNet.Object.NetworkObject>();

            if (pickup == null || pickupNetworkObject == null)
            {
                Debug.LogError("PickupManager: Health pickup prefab must contain HealthPickup and NetworkObject components.");
                Destroy(pickupInstance);
                return;
            }

            pickup.Initialize(this, position);
            networkManager.ServerManager.Spawn(pickupNetworkObject);
        }
    }
}
