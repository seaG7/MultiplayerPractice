using Unity.Netcode.Components;
using UnityEngine;

namespace Networking
{
    [DisallowMultipleComponent]
    public class OwnerNetworkAnimator : NetworkAnimator
    {
        protected override void Awake()
        {
            if (Animator == null)
            {
                Animator = GetComponentInChildren<Animator>(true);
            }

            base.Awake();
        }

        protected override bool OnIsServerAuthoritative()
        {
            return false;
        }
    }
}
