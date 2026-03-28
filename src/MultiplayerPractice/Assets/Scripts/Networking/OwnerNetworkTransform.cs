using Unity.Netcode.Components;
using UnityEngine;

namespace Networking
{
    [DisallowMultipleComponent]
    public class OwnerNetworkTransform : NetworkTransform
    {
        protected override void Awake()
        {
            base.Awake();

            AuthorityMode = AuthorityModes.Owner;
            Interpolate = true;
            SyncRotAngleX = false;
            SyncRotAngleY = true;
            SyncRotAngleZ = false;
            SyncScaleX = false;
            SyncScaleY = false;
            SyncScaleZ = false;
            PositionThreshold = 0.01f;
            RotAngleThreshold = 0.5f;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Debug.Log(
                $"OwnerNetworkTransform.OnNetworkSpawn owner={OwnerClientId} local={NetworkManager.LocalClientId} " +
                $"isOwner={IsOwner} canCommit={CanCommitToTransform} authority={AuthorityMode}");
        }

        public void TeleportTo(Vector3 position, Quaternion rotation)
        {
            if (!CanCommitToTransform)
            {
                return;
            }

            Teleport(position, rotation, transform.localScale);
        }
    }
}
