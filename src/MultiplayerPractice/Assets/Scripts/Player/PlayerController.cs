using UnityEngine;

namespace Player
{
    public class PlayerController : MonoBehaviour
    {
        public struct SimulationInput
        {
            public Vector2 Move;
            public float CameraYaw;
            public bool JumpPressed;
            public bool DashPressed;
        }

        public struct MotorState
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public Quaternion VisualRotation;
            public Vector3 MoveDirection;
            public Vector3 Impact;
            public Vector3 DashDirection;
            public float FallTime;
            public float CurrentDashTime;
            public bool CanDash;
            public bool CanJump;
            public bool WasGrounded;
            public bool Hit;
            public bool CanMove;
        }

        [HideInInspector] public CharacterController charCont;
        [HideInInspector] public Animator anim;
        public GameObject childPlayer;
        public Camera cam;
        public GameObject movIndicator;
        [HideInInspector] public SoundManager soundMan;

        public float speed = 6.0f;
        public float jumpSpeed = 8.0f;
        public float gravity = 20.0f;
        public bool airControl = true;
        public float maxDashTime = 0.25f;
        public float dashSpeed = 20f;
        [HideInInspector] public bool canMove = true;

        private bool canJump = true;
        private bool wasGrounded;
        private readonly float mass = 60.0f;
        private Vector3 impact = Vector3.zero;
        private float fallTime;
        private float currentDashTime;
        private Vector3 dashDir;
        private bool canDash = true;
        private Vector3 moveDirection = Vector3.zero;
        private float distToGround;
        private Vector3 groundNormal;
        private bool hit;
        private bool localInputEnabled;

        public Vector3 FacingForward => childPlayer != null ? childPlayer.transform.forward : transform.forward;
        public bool IsControllerEnabled => charCont != null && charCont.enabled;
        public bool HasLocalControl => localInputEnabled;

        private void Awake()
        {
            charCont = GetComponent<CharacterController>();
            soundMan = GetComponent<SoundManager>();
            anim = GetComponentInChildren<Animator>();
            currentDashTime = maxDashTime;

            if (charCont != null)
            {
                distToGround = charCont.bounds.extents.y;
                charCont.enabled = false;
            }
        }

        public void Simulate(in SimulationInput input, float delta, bool allowPresentationEvents)
        {
            if (charCont == null || !charCont.enabled)
            {
                return;
            }

            UpdateGroundState(delta, allowPresentationEvents);

            if (hit)
            {
                UpdateHitRecovery(delta, allowPresentationEvents);
                return;
            }

            if (!canMove)
            {
                moveDirection.y -= gravity * delta;
                charCont.Move(moveDirection * delta);
                return;
            }

            if (charCont.isGrounded)
            {
                HandleGroundMovement(input, delta, allowPresentationEvents);
            }
            else
            {
                HandleAirMovement(input, delta);
            }

            moveDirection.y -= gravity * delta;
            charCont.Move(moveDirection * delta);
        }

        public MotorState CaptureState()
        {
            return new MotorState
            {
                Position = transform.position,
                Rotation = transform.rotation,
                VisualRotation = childPlayer != null ? childPlayer.transform.rotation : transform.rotation,
                MoveDirection = moveDirection,
                Impact = impact,
                DashDirection = dashDir,
                FallTime = fallTime,
                CurrentDashTime = currentDashTime,
                CanDash = canDash,
                CanJump = canJump,
                WasGrounded = wasGrounded,
                Hit = hit,
                CanMove = canMove
            };
        }

        public void RestoreState(MotorState state)
        {
            bool wasEnabled = charCont != null && charCont.enabled;
            if (charCont != null)
            {
                charCont.enabled = false;
            }

            transform.SetPositionAndRotation(state.Position, state.Rotation);
            if (childPlayer != null)
            {
                childPlayer.transform.rotation = state.VisualRotation;
            }

            moveDirection = state.MoveDirection;
            impact = state.Impact;
            dashDir = state.DashDirection;
            fallTime = state.FallTime;
            currentDashTime = state.CurrentDashTime;
            canDash = state.CanDash;
            canJump = state.CanJump;
            wasGrounded = state.WasGrounded;
            hit = state.Hit;
            canMove = state.CanMove;

            if (charCont != null)
            {
                charCont.enabled = wasEnabled;
            }
        }

        private void UpdateGroundState(float delta, bool allowPresentationEvents)
        {
            if (anim == null || charCont == null)
            {
                wasGrounded = charCont != null && charCont.isGrounded;
                return;
            }

            if (charCont.isGrounded)
            {
                if (!wasGrounded)
                {
                    canJump = true;
                    anim.SetBool("Jump", false);
                    if (allowPresentationEvents && fallTime > 0.2f)
                    {
                        PlaySound("Land");
                        if (!hit)
                        {
                            anim.CrossFade("FallingEnd", 0.1f);
                        }
                    }

                    fallTime = 0f;
                }
            }
            else
            {
                anim.SetFloat("SpeedY", charCont.velocity.y);
                if (wasGrounded && canJump && DistToGround() > 0.3f)
                {
                    moveDirection.y = 0f;
                    wasGrounded = false;
                    anim.SetBool("Jump", true);
                    if (allowPresentationEvents)
                    {
                        anim.CrossFade("Falling", 0.2f);
                    }
                }

                if (charCont.velocity.y < 0f)
                {
                    fallTime += delta;
                }
            }

            wasGrounded = charCont.isGrounded;
        }

        private void UpdateHitRecovery(float delta, bool allowPresentationEvents)
        {
            if (!hit || charCont == null)
            {
                return;
            }

            moveDirection.y -= gravity * delta;
            Vector3 impactWithGravity = new Vector3(impact.x, impact.y + moveDirection.y, impact.z);
            if (impact.magnitude > 0.2f || !charCont.isGrounded)
            {
                charCont.Move(impactWithGravity * delta);
            }

            impact = Vector3.Lerp(impact, Vector3.zero, 5f * delta);
            if (!charCont.isGrounded || impact.magnitude > 0.2f)
            {
                return;
            }

            hit = false;
            canMove = true;
            if (allowPresentationEvents)
            {
                anim?.Play("Idle");
            }
        }

        private void HandleGroundMovement(in SimulationInput input, float delta, bool allowPresentationEvents)
        {
            Vector2 planarInput = Vector2.ClampMagnitude(input.Move, 1f);
            UpdatePlanarAnimation(planarInput);

            if (movIndicator != null)
            {
                movIndicator.transform.localPosition = new Vector3(planarInput.x, 0f, planarInput.y);
            }

            Vector3 worldMove = GetWorldMoveDirection(planarInput, input.CameraYaw);
            if (HandleDash(worldMove, input.DashPressed, delta, allowPresentationEvents))
            {
                return;
            }

            if (worldMove.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.Euler(0f, input.CameraYaw, 0f);
                RotateVisualTowardsDirection(worldMove, false, delta);
            }

            moveDirection = worldMove * speed;
            moveDirection.y = -10f;

            if (!IsGrounded())
            {
                moveDirection.x += (1f - groundNormal.y) * groundNormal.x;
                moveDirection.z += (1f - groundNormal.y) * groundNormal.z;
            }

            if (input.JumpPressed && canJump)
            {
                moveDirection.y = jumpSpeed;
                canJump = false;
                if (anim != null)
                {
                    anim.SetFloat("SpeedY", moveDirection.y);
                    anim.SetBool("Jump", true);
                    if (allowPresentationEvents)
                    {
                        anim.Play("Falling");
                    }
                }

                if (allowPresentationEvents)
                {
                    PlaySound("Jump");
                }
            }
        }

        private void HandleAirMovement(in SimulationInput input, float delta)
        {
            if (currentDashTime < maxDashTime)
            {
                currentDashTime = maxDashTime;
                canDash = true;
            }

            if (!airControl)
            {
                return;
            }

            Vector2 planarInput = Vector2.ClampMagnitude(input.Move, 1f);
            Vector3 worldMove = GetWorldMoveDirection(planarInput, input.CameraYaw);

            if (movIndicator != null)
            {
                movIndicator.transform.localPosition = new Vector3(planarInput.x, 0f, planarInput.y);
            }

            if (worldMove.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.Euler(0f, input.CameraYaw, 0f);
                RotateVisualTowardsDirection(worldMove, false, delta);
            }

            moveDirection = new Vector3(worldMove.x * speed * 0.8f, moveDirection.y, worldMove.z * speed * 0.8f);
        }

        private bool HandleDash(Vector3 worldMove, bool dashPressed, float delta, bool allowPresentationEvents)
        {
            if (dashPressed && canDash)
            {
                currentDashTime = 0f;
                canDash = false;
                if (allowPresentationEvents)
                {
                    anim?.Play("Slide");
                    PlaySound("Dash");
                }

                if (worldMove.sqrMagnitude > 0.001f)
                {
                    dashDir = worldMove.normalized;
                    RotateVisualTowardsDirection(dashDir, true, delta);
                }
                else
                {
                    dashDir = FacingForward;
                }
            }

            if (currentDashTime >= maxDashTime)
            {
                canDash = true;
                return false;
            }

            dashDir.y = -10f;
            currentDashTime += delta;
            charCont.Move(dashDir * delta * dashSpeed);
            return true;
        }

        private Vector3 GetWorldMoveDirection(Vector2 planarInput, float cameraYaw)
        {
            if (planarInput.sqrMagnitude <= 0.001f)
            {
                return Vector3.zero;
            }

            Quaternion yawRotation = Quaternion.Euler(0f, cameraYaw, 0f);
            return (yawRotation * new Vector3(planarInput.x, 0f, planarInput.y)).normalized;
        }

        private void UpdatePlanarAnimation(Vector2 planarInput)
        {
            if (anim != null)
            {
                anim.SetFloat("Speed", planarInput.magnitude);
            }
        }

        private void RotateVisualTowardsDirection(Vector3 worldDirection, bool snap, float delta)
        {
            if (childPlayer == null || worldDirection.sqrMagnitude <= 0.001f)
            {
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(worldDirection.normalized, Vector3.up);
            childPlayer.transform.rotation = snap
                ? targetRotation
                : Quaternion.Slerp(childPlayer.transform.rotation, targetRotation, delta * 10f);
        }

        public void AddImpact(Vector3 dir, float force)
        {
            moveDirection = Vector3.zero;
            anim?.Play("Hit");

            dir.Normalize();
            if (dir.y < 0f)
            {
                dir.y = -dir.y;
            }

            impact += dir.normalized * force / mass;
        }

        public void ApplyDMG(Vector3 dir, float force)
        {
            if (hit)
            {
                return;
            }

            hit = true;
            canMove = false;
            PlaySound("Hit");
            currentDashTime = maxDashTime;
            if (anim != null)
            {
                anim.SetFloat("Speed", 0f);
                AnimatorEvents animatorEvents = anim.GetComponent<AnimatorEvents>();
                if (animatorEvents != null)
                {
                    animatorEvents.DisableWeaponColl();
                }
            }

            AddImpact(dir, force);
        }

        public void SetLocalControl(bool enabled)
        {
            localInputEnabled = enabled;
        }

        public void SetControllerEnabled(bool enabled)
        {
            if (charCont == null || charCont.enabled == enabled)
            {
                return;
            }

            charCont.enabled = enabled;
            if (!enabled)
            {
                moveDirection = Vector3.zero;
                impact = Vector3.zero;
                dashDir = Vector3.zero;
            }
        }

        public void ResetMotionState()
        {
            moveDirection = Vector3.zero;
            impact = Vector3.zero;
            dashDir = Vector3.zero;
            hit = false;
            canMove = true;
            canJump = true;
            canDash = true;
            wasGrounded = false;
            fallTime = 0f;
            currentDashTime = maxDashTime;

            if (anim == null)
            {
                return;
            }

            anim.SetFloat("Speed", 0f);
            anim.SetFloat("SpeedY", 0f);
            anim.SetBool("Jump", false);
            anim.Play("Idle");
        }

        public void AssignCamera(Camera targetCamera)
        {
            cam = targetCamera;
        }

        public void SetNetworkTransform(Vector3 position, Quaternion rotation, bool teleport = false)
        {
            bool toggleController = teleport && charCont != null && charCont.enabled;
            if (toggleController)
            {
                charCont.enabled = false;
            }

            transform.SetPositionAndRotation(position, rotation);

            if (toggleController)
            {
                charCont.enabled = true;
            }

            if (!teleport)
            {
                return;
            }

            moveDirection = Vector3.zero;
            impact = Vector3.zero;
            currentDashTime = maxDashTime;
            canDash = true;
        }

        public void TeleportTo(Vector3 position, Quaternion rotation)
        {
            SetNetworkTransform(position, rotation, true);
            if (childPlayer != null)
            {
                childPlayer.transform.rotation = rotation;
            }

            ResetMotionState();
        }

        public bool CanStartAttack()
        {
            return localInputEnabled &&
                   canMove &&
                   !hit &&
                   charCont != null &&
                   charCont.enabled &&
                   charCont.isGrounded &&
                   !IsDashing();
        }

        public void PlaySound(string soundName)
        {
            if (soundMan != null)
            {
                soundMan.PlaySound(soundName);
            }
        }

        private float DistToGround()
        {
            if (Physics.Raycast(transform.position, -Vector3.up, out RaycastHit hitInfo, distToGround + 999f))
            {
                return hitInfo.distance - distToGround;
            }

            return 999f;
        }

        private bool IsGrounded()
        {
            return Physics.Raycast(transform.position, -Vector3.up, distToGround + 0.1f);
        }

        private void OnControllerColliderHit(ControllerColliderHit hitInfo)
        {
            groundNormal = hitInfo.normal;
        }

        public bool IsDashing()
        {
            return currentDashTime < maxDashTime;
        }

        public void EnableMove(bool canMoveState)
        {
            if (!hit)
            {
                canMove = canMoveState;
            }
        }
    }
}
