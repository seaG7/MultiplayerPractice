using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Networking
{
    public class PickupManager : MonoBehaviour
    {
        [SerializeField] private GameObject healthPickupPrefab;
        [SerializeField] private Transform[] spawnPoints;
        [SerializeField] private float respawnDelay = 10f;

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
            if (!callbacksRegistered || NetworkManager.Singleton == null)
            {
                return;
            }

            NetworkManager.Singleton.OnServerStarted -= HandleServerStarted;
            NetworkManager.Singleton.OnServerStopped -= HandleServerStopped;
            callbacksRegistered = false;
        }

        public void OnPickedUp(Vector3 position)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                return;
            }

            StartCoroutine(RespawnAfterDelay(position));
        }

        private void HandleServerStarted()
        {
            if (initialSpawnDone)
            {
                return;
            }

            initialSpawnDone = true;
            SpawnAll();
        }

        private void HandleServerStopped(bool _)
        {
            initialSpawnDone = false;
        }

        private void RegisterCallbacks()
        {
            if (callbacksRegistered || NetworkManager.Singleton == null)
            {
                return;
            }

            NetworkManager.Singleton.OnServerStarted += HandleServerStarted;
            NetworkManager.Singleton.OnServerStopped += HandleServerStopped;
            callbacksRegistered = true;

            if (NetworkManager.Singleton.IsServer)
            {
                HandleServerStarted();
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
            NetworkObject pickupNetworkObject = pickupInstance.GetComponent<NetworkObject>();

            if (pickup == null || pickupNetworkObject == null)
            {
                Debug.LogError("PickupManager: Health pickup prefab must contain HealthPickup and NetworkObject components.");
                Destroy(pickupInstance);
                return;
            }

            pickup.Initialize(this, position);
            pickupNetworkObject.Spawn();
        }
    }
}
