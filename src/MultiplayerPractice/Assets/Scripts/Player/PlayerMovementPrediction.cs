using FishNet.Object.Prediction;
using FishNet.Transporting;
using FishNet.Utility.Template;
using Networking;
using UnityEngine;

namespace Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerController))]
    public class PlayerMovementPrediction : TickNetworkBehaviour
    {
        public struct MoveData : IReplicateData
        {
            public MoveData(Vector2 move, float cameraYaw, bool jumpPressed, bool dashPressed, bool attackPressed)
            {
                Move = move;
                CameraYaw = cameraYaw;
                JumpPressed = jumpPressed;
                DashPressed = dashPressed;
                AttackPressed = attackPressed;
                _tick = 0;
            }

            public Vector2 Move;
            public float CameraYaw;
            public bool JumpPressed;
            public bool DashPressed;
            public bool AttackPressed;

            private uint _tick;
            public void Dispose() { }
            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;
        }

        public struct ReconcileData : IReconcileData
        {
            public ReconcileData(PlayerController.MotorState motorState)
            {
                MotorState = motorState;
                _tick = 0;
            }

            public PlayerController.MotorState MotorState;

            private uint _tick;
            public void Dispose() { }
            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;
        }

        private PlayerController playerController;
        private NetworkPlayer networkPlayer;
        private MoveData lastReplicateData;
        private bool jumpPressed;
        private bool dashPressed;
        private bool attackPressed;

        private void Awake()
        {
            playerController = GetComponent<PlayerController>();
            networkPlayer = GetComponent<NetworkPlayer>();
            SetTickCallbacks(TickCallback.Tick | TickCallback.PostTick);
        }

        private void Update()
        {
            if (!base.IsOwner || playerController == null || !playerController.HasLocalControl)
            {
                return;
            }

            if (Input.GetButtonDown("Jump"))
            {
                jumpPressed = true;
            }

            if (Input.GetButtonDown("Dash"))
            {
                dashPressed = true;
            }

            if (Input.GetButtonDown("Attack") && networkPlayer != null && networkPlayer.TryQueuePredictedAttack())
            {
                attackPressed = true;
            }
        }

        protected override void TimeManager_OnTick()
        {
            if (!CanSimulateGameplay())
            {
                return;
            }

            PerformReplicate(BuildMoveData());
        }

        protected override void TimeManager_OnPostTick()
        {
            if (!CanSimulateGameplay())
            {
                return;
            }

            CreateReconcile();
        }

        private MoveData BuildMoveData()
        {
            if (!base.IsOwner || playerController == null || !playerController.HasLocalControl)
            {
                return default;
            }

            Vector2 move = new(
                Input.GetAxisRaw("Horizontal"),
                Input.GetAxisRaw("Vertical"));

            float cameraYaw = playerController.cam != null
                ? playerController.cam.transform.eulerAngles.y
                : transform.eulerAngles.y;

            MoveData moveData = new(move, cameraYaw, jumpPressed, dashPressed, attackPressed);
            jumpPressed = false;
            dashPressed = false;
            attackPressed = false;
            return moveData;
        }

        public override void CreateReconcile()
        {
            if (playerController == null)
            {
                return;
            }

            ReconcileData rd = new(playerController.CaptureState());
            PerformReconcile(rd);
        }

        [Replicate]
        private void PerformReplicate(MoveData moveData, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
        {
            if (!CanSimulateGameplay())
            {
                return;
            }

            if (!IsServerStarted && !base.IsOwner)
            {
                if (state.ContainsTicked())
                {
                    lastReplicateData = moveData;
                }
                else if (state.IsFuture() && moveData.GetTick() - lastReplicateData.GetTick() <= 1)
                {
                    moveData = lastReplicateData;
                    moveData.JumpPressed = false;
                    moveData.DashPressed = false;
                    moveData.AttackPressed = false;
                }
            }

            bool allowPresentationEvents = state.ContainsTicked() && !state.ContainsReplayed();
            if (moveData.AttackPressed)
            {
                networkPlayer?.ProcessPredictedAttack(allowPresentationEvents);
            }

            PlayerController.SimulationInput input = new()
            {
                Move = moveData.Move,
                CameraYaw = moveData.CameraYaw,
                JumpPressed = moveData.JumpPressed,
                DashPressed = moveData.DashPressed
            };

            playerController.Simulate(input, (float)TimeManager.TickDelta, allowPresentationEvents);
        }

        [Reconcile]
        private void PerformReconcile(ReconcileData reconcileData, Channel channel = Channel.Unreliable)
        {
            playerController?.RestoreState(reconcileData.MotorState);
        }

        public void TeleportTo(Vector3 position, Quaternion rotation)
        {
            playerController?.TeleportTo(position, rotation);
        }

        private bool CanSimulateGameplay()
        {
            if (playerController == null || !playerController.IsControllerEnabled)
            {
                return false;
            }

            if (networkPlayer != null)
            {
                return networkPlayer.CanAct;
            }

            return GameSessionManager.IsGameplayActiveGlobal;
        }
    }
}
