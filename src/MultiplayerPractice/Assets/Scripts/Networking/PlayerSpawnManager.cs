using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using UnityEngine;

namespace Networking
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkManager))]
    public class PlayerSpawnManager : MonoBehaviour
    {
        [SerializeField] private GameObject playerPrefab;
        [SerializeField] private Transform[] playerSpawnPoints;
        [SerializeField] private Transform spawnCenter;
        [SerializeField] private float spawnRadius = 3f;
        [SerializeField] private bool randomizeYaw = true;
        [SerializeField] private bool addToDefaultScene = true;

        private NetworkManager networkManager;

        public static PlayerSpawnManager Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
            networkManager = GetComponent<NetworkManager>();
        }

        private void OnEnable()
        {
            if (networkManager == null)
            {
                networkManager = GetComponent<NetworkManager>();
            }

            if (networkManager != null)
            {
                networkManager.SceneManager.OnClientLoadedStartScenes += HandleClientLoadedStartScenes;
            }
        }

        private void OnDisable()
        {
            if (networkManager != null)
            {
                networkManager.SceneManager.OnClientLoadedStartScenes -= HandleClientLoadedStartScenes;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void HandleClientLoadedStartScenes(NetworkConnection connection, bool asServer)
        {
            if (!asServer)
            {
                return;
            }

            SpawnPlayer(connection);
        }

        private void SpawnPlayer(NetworkConnection connection)
        {
            if (playerPrefab == null)
            {
                Debug.LogError("PlayerSpawnManager: Player prefab is not assigned.");
                return;
            }

            TryGetSpawnPose(out Vector3 spawnPosition, out Quaternion spawnRotation);
            GameObject playerInstance = Instantiate(playerPrefab, spawnPosition, spawnRotation);
            NetworkObject playerNetworkObject = playerInstance.GetComponent<NetworkObject>();

            if (playerNetworkObject == null)
            {
                Debug.LogError("PlayerSpawnManager: Player prefab must contain a FishNet NetworkObject.");
                Destroy(playerInstance);
                return;
            }

            networkManager.ServerManager.Spawn(playerNetworkObject, connection);
            if (addToDefaultScene)
            {
                networkManager.SceneManager.AddOwnerToDefaultScene(playerNetworkObject);
            }
        }

        public bool TryGetSpawnPose(out Vector3 spawnPosition, out Quaternion spawnRotation)
        {
            if (playerSpawnPoints != null && playerSpawnPoints.Length > 0)
            {
                Transform spawnPoint = playerSpawnPoints[Random.Range(0, playerSpawnPoints.Length)];
                if (spawnPoint != null)
                {
                    spawnPosition = spawnPoint.position;
                    spawnRotation = spawnPoint.rotation;
                    return true;
                }
            }

            Vector3 centerPosition = GetCenterPosition();
            if (spawnRadius <= 0f)
            {
                spawnPosition = centerPosition;
                spawnRotation = Quaternion.identity;
                return false;
            }

            Vector2 randomOffset = Random.insideUnitCircle * spawnRadius;
            spawnPosition = centerPosition + new Vector3(randomOffset.x, 0f, randomOffset.y);
            spawnRotation = randomizeYaw
                ? Quaternion.Euler(0f, Random.Range(0f, 360f), 0f)
                : Quaternion.identity;
            return false;
        }

        private Vector3 GetCenterPosition()
        {
            return spawnCenter != null ? spawnCenter.position : transform.position;
        }

        private void OnDrawGizmos()
        {
            Vector3 centerPosition = GetCenterPosition();
            float radius = Mathf.Max(0f, spawnRadius);

            Gizmos.color = new Color(0.2f, 0.85f, 0.4f, 1f);
            Gizmos.DrawSphere(centerPosition, 0.15f);

            if (radius <= 0f)
            {
                return;
            }

            const int segments = 48;
            Vector3 previousPoint = centerPosition + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                Vector3 nextPoint = centerPosition + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(previousPoint, nextPoint);
                previousPoint = nextPoint;
            }

            Gizmos.DrawLine(centerPosition, centerPosition + Vector3.right * radius);
        }
    }
}
