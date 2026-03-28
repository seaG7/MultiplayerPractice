using Networking;
using Unity.Netcode;
using UnityEngine;

namespace Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkPlayer))]
    public class PlayerShooting : NetworkBehaviour
    {
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField] private Transform firePoint;
        [SerializeField] private float cooldown = 0.4f;
        [SerializeField] private int maxAmmo = 10;
        [SerializeField] private float projectileSpawnOffset = 1.2f;

        private readonly NetworkVariable<int> currentAmmo =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private NetworkPlayer networkPlayer;
        private bool localInputEnabled;
        private double nextServerShotTime;

        public int CurrentAmmo => currentAmmo.Value;
        public int MaxAmmo => maxAmmo;

        private void Awake()
        {
            networkPlayer = GetComponent<NetworkPlayer>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                currentAmmo.Value = maxAmmo;
            }
        }

        private void Update()
        {
            if (!IsOwner || !localInputEnabled || !IsSpawned)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.Mouse1))
            {
                Vector3 shootPosition = firePoint != null ? firePoint.position : transform.position;
                Vector3 shootDirection = firePoint != null ? firePoint.forward : transform.forward;
                ShootServerRpc(shootPosition, shootDirection);
            }
        }

        public void SetLocalInputEnabled(bool enabled)
        {
            localInputEnabled = enabled;
        }

        public void RefillAmmoOnServer()
        {
            if (!IsServer)
            {
                return;
            }

            currentAmmo.Value = maxAmmo;
        }

        [ServerRpc]
        private void ShootServerRpc(Vector3 position, Vector3 direction, ServerRpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                return;
            }

            if (projectilePrefab == null || networkPlayer == null || !networkPlayer.IsAlive || currentAmmo.Value <= 0)
            {
                return;
            }

            double serverTime = NetworkManager.ServerTime.Time;
            if (serverTime < nextServerShotTime)
            {
                return;
            }

            if (direction.sqrMagnitude <= 0.001f)
            {
                direction = transform.forward;
            }

            direction.Normalize();
            nextServerShotTime = serverTime + cooldown;
            currentAmmo.Value--;

            Vector3 spawnPosition = position + direction * projectileSpawnOffset;
            Quaternion spawnRotation = Quaternion.LookRotation(direction);

            GameObject projectileInstance = Instantiate(projectilePrefab, spawnPosition, spawnRotation);
            Projectile projectile = projectileInstance.GetComponent<Projectile>();
            NetworkObject projectileNetworkObject = projectileInstance.GetComponent<NetworkObject>();

            if (projectile == null || projectileNetworkObject == null)
            {
                Debug.LogError("PlayerShooting: Projectile prefab must contain Projectile and NetworkObject components.");
                Destroy(projectileInstance);
                return;
            }

            projectile.Initialize(direction);
            projectileNetworkObject.SpawnWithOwnership(OwnerClientId);
        }
    }
}
