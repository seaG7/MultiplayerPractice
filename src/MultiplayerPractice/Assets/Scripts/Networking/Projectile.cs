using FishNet.Object;
using UnityEngine;

namespace Networking
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FishNet.Object.NetworkObject))]
    public class Projectile : NetworkBehaviour
    {
        [SerializeField] private float speed = 18f;
        [SerializeField] private int damage = 20;
        [SerializeField] private float lifetime = 4f;

        private Rigidbody cachedRigidbody;
        private Vector3 moveDirection = Vector3.forward;
        private float despawnAtTime;
        private int attackerClientId = -1;

        private void Awake()
        {
            cachedRigidbody = GetComponent<Rigidbody>();
        }

        public void Initialize(Vector3 direction, int attackerClientId)
        {
            moveDirection = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.forward;
            this.attackerClientId = attackerClientId;
            transform.rotation = Quaternion.LookRotation(moveDirection);
        }

        public override void OnStartServer()
        {
            despawnAtTime = Time.time + lifetime;
        }

        private void FixedUpdate()
        {
            if (!IsServerInitialized)
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
            if (!IsServerInitialized)
            {
                return;
            }

            NetworkPlayer target = other.GetComponent<NetworkPlayer>() ?? other.GetComponentInParent<NetworkPlayer>();
            if (target != null)
            {
                int sourceClientId = attackerClientId >= 0 ? attackerClientId : OwnerId;
                if (sourceClientId < 0 || target.OwnerId == sourceClientId || !target.IsAlive)
                {
                    return;
                }

                if (target.TryApplyDamageOnServer(damage, sourceClientId, moveDirection))
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
                ServerManager.Despawn(NetworkObject);
                return;
            }

            Destroy(gameObject);
        }
    }
}
