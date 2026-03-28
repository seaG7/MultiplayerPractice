using Unity.Netcode;
using UnityEngine;

namespace Networking
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public class Projectile : NetworkBehaviour
    {
        [SerializeField] private float speed = 18f;
        [SerializeField] private int damage = 20;
        [SerializeField] private float lifetime = 4f;

        private Rigidbody cachedRigidbody;
        private Vector3 moveDirection = Vector3.forward;
        private float despawnAtTime;

        private void Awake()
        {
            cachedRigidbody = GetComponent<Rigidbody>();
        }

        public void Initialize(Vector3 direction)
        {
            moveDirection = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.forward;
            transform.rotation = Quaternion.LookRotation(moveDirection);
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                despawnAtTime = Time.time + lifetime;
            }
        }

        private void FixedUpdate()
        {
            if (!IsServer)
            {
                return;
            }

            Vector3 delta = moveDirection * speed * Time.fixedDeltaTime;
            if (cachedRigidbody != null)
            {
                cachedRigidbody.MovePosition(cachedRigidbody.position + delta);
            }
            else
            {
                transform.position += delta;
            }

            if (Time.time >= despawnAtTime)
            {
                DespawnProjectile();
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsServer)
            {
                return;
            }

            NetworkPlayer target = other.GetComponent<NetworkPlayer>();
            if (target == null)
            {
                target = other.GetComponentInParent<NetworkPlayer>();
            }
            if (target != null)
            {
                if (target.OwnerClientId == OwnerClientId || !target.IsAlive)
                {
                    return;
                }

                if (target.TryApplyDamageOnServer(damage, OwnerClientId, moveDirection))
                {
                    DespawnProjectile();
                }

                return;
            }

            if (!other.isTrigger)
            {
                DespawnProjectile();
            }
        }

        private void DespawnProjectile()
        {
            if (NetworkObject != null && NetworkObject.IsSpawned)
            {
                NetworkObject.Despawn(true);
                return;
            }

            Destroy(gameObject);
        }
    }
}
