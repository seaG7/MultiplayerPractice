using UnityEngine;

namespace Player
{
    public class Attack : MonoBehaviour
    {
        private Animator anim;
        private PlayerController playerCont;
        private NetworkPlayer networkPlayer;
        private bool localInputEnabled;
        [SerializeField] private float movementLockDuration = 0.45f;

        private void Awake()
        {
            playerCont = GetComponent<PlayerController>();
            anim = GetComponentInChildren<Animator>();
            networkPlayer = GetComponent<NetworkPlayer>();
        }

        private void Update()
        {
            if (networkPlayer != null)
            {
                return;
            }

            if (!localInputEnabled || !Input.GetButtonDown("Attack") || !CanPlayAttackAnimation())
            {
                return;
            }

            PlayAttackAnimation();
        }

        public void SetLocalInputEnabled(bool enabled)
        {
            localInputEnabled = enabled;
        }

        public bool CanPlayAttackAnimation()
        {
            return playerCont != null && playerCont.CanStartAttack();
        }

        public void PlayAttackAnimation(bool ignoreStateChecks = false, bool applyMovementLock = true)
        {
            if (playerCont == null || anim == null || !anim.gameObject.activeInHierarchy || (!ignoreStateChecks && !CanPlayAttackAnimation()))
            {
                return;
            }

            if (applyMovementLock)
            {
                BeginAttackMovementLock();
            }

            anim.CrossFade("Attack", 0.05f);
            playerCont.PlaySound("Attack");
        }

        public void BeginAttackMovementLock()
        {
            playerCont?.BeginActionLock(movementLockDuration);
        }
    }
}
