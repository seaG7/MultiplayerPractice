using UnityEngine;

namespace Player
{
    public class Attack : MonoBehaviour
    {
        private Animator anim;
        private PlayerController playerCont;
        private NetworkPlayer networkPlayer;
        private bool localInputEnabled;

        private void Awake()
        {
            playerCont = GetComponent<PlayerController>();
            anim = GetComponentInChildren<Animator>();
            networkPlayer = GetComponent<NetworkPlayer>();
        }

        private void Update()
        {
            if (!localInputEnabled || !Input.GetButtonDown("Attack") || !CanPlayAttackAnimation())
            {
                return;
            }

            if (networkPlayer != null)
            {
                networkPlayer.TryPerformAttack();
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
            return playerCont != null && playerCont.canMove && playerCont.charCont.isGrounded && !playerCont.IsDashing();
        }

        public void PlayAttackAnimation(bool ignoreStateChecks = false)
        {
            if (!ignoreStateChecks && !CanPlayAttackAnimation())
            {
                return;
            }

            anim.CrossFade("Attack", 0.05f);
            playerCont.PlaySound("Attack");
        }
    }
}
