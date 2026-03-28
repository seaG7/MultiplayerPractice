using FishNet.Managing.Timing;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Networking;
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

        private readonly SyncVar<int> currentAmmo = new(0);

        private NetworkPlayer networkPlayer;
        private bool localInputEnabled;
        private uint nextServerShotTick;

        public int CurrentAmmo => currentAmmo.Value;
        public int MaxAmmo => maxAmmo;

        private void Awake()
        {
            networkPlayer = GetComponent<NetworkPlayer>();
            currentAmmo.OnChange += HandleAmmoChanged;
        }

        private void OnDestroy()
        {
            currentAmmo.OnChange -= HandleAmmoChanged;
        }

        public override void OnStartNetwork()
        {
            if (IsServerInitialized)
            {
                currentAmmo.Value = maxAmmo;
                nextServerShotTick = 0u;
            }
        }

        private void Update()
        {
            if (!base.IsOwner || !localInputEnabled || !IsClientInitialized)
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
            if (!IsServerInitialized)
            {
                return;
            }

            currentAmmo.Value = maxAmmo;
        }

        [ServerRpc]
        private void ShootServerRpc(Vector3 position, Vector3 direction)
        {
            if (projectilePrefab == null || networkPlayer == null || !networkPlayer.IsAlive || currentAmmo.Value <= 0)
            {
                return;
            }

            if (TimeManager.Tick < nextServerShotTick)
            {
                return;
            }

            if (direction.sqrMagnitude <= 0.001f)
            {
                direction = transform.forward;
            }

            direction.Normalize();
            nextServerShotTick = TimeManager.Tick + TimeManager.TimeToTicks(cooldown, TickRounding.RoundUp);
            currentAmmo.Value--;

            Vector3 spawnPosition = position + direction * projectileSpawnOffset;
            Quaternion spawnRotation = Quaternion.LookRotation(direction);

            GameObject projectileInstance = Instantiate(projectilePrefab, spawnPosition, spawnRotation);
            Projectile projectile = projectileInstance.GetComponent<Projectile>();
            FishNet.Object.NetworkObject projectileNetworkObject = projectileInstance.GetComponent<FishNet.Object.NetworkObject>();

            if (projectile == null || projectileNetworkObject == null)
            {
                Debug.LogError("PlayerShooting: Projectile prefab must contain Projectile and NetworkObject components.");
                Destroy(projectileInstance);
                return;
            }

            projectile.Initialize(direction);
            ServerManager.Spawn(projectileNetworkObject, base.Owner);
        }

        private void HandleAmmoChanged(int previousValue, int newValue, bool asServer)
        {
            networkPlayer?.RefreshInfoLabel();
        }
    }
}
