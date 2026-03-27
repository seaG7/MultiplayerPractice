using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkManager))]
public class PlayerSpawnManager : MonoBehaviour
{
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private Transform spawnCenter;
    [SerializeField] private float spawnRadius = 3f;
    [SerializeField] private bool randomizeYaw = true;

    private NetworkManager networkManager;

    private void Awake()
    {
        networkManager = GetComponent<NetworkManager>();
    }

    private void OnEnable()
    {
        networkManager.OnClientConnectedCallback += HandleClientConnected;
    }

    private void OnDisable()
    {
        networkManager.OnClientConnectedCallback -= HandleClientConnected;
    }

    private void HandleClientConnected(ulong clientId)
    {
        
        if (networkManager == null || !networkManager.IsServer)
        {
            return;
        }

        if (networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient connectedClient)
            && connectedClient.PlayerObject != null)
        {
            return;
        }
        
        Debug.Log("Client connected");

        Vector3 spawnPosition = GetSpawnPosition();
        Quaternion spawnRotation = randomizeYaw
            ? Quaternion.Euler(0f, Random.Range(0f, 360f), 0f)
            : Quaternion.identity;

        GameObject playerInstance = Instantiate(playerPrefab, spawnPosition, spawnRotation);
        NetworkObject playerNetworkObject = playerInstance.GetComponent<NetworkObject>();
        if (playerNetworkObject == null)
        {
            Debug.LogError("PlayerSpawnManager: Player prefab does not contain a NetworkObject.");
            Destroy(playerInstance);
            return;
        }

        playerNetworkObject.SpawnAsPlayerObject(clientId, true);
    }

    private Vector3 GetSpawnPosition()
    {
        Vector3 centerPosition = GetCenterPosition();
        Vector2 randomOffset = Random.insideUnitCircle * Mathf.Max(0f, spawnRadius);
        return new Vector3(
            centerPosition.x + randomOffset.x,
            centerPosition.y,
            centerPosition.z + randomOffset.y);
    }

    private Vector3 GetCenterPosition()
    {
        return spawnCenter.position;
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
