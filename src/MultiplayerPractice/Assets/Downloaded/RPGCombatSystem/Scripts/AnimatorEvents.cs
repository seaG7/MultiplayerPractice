using Player;
using UnityEngine;

public class AnimatorEvents : MonoBehaviour
{
    private PlayerController playerCont;

    public Weapon weapon;

    private void Awake()
    {
        playerCont = GetComponentInParent<PlayerController>();
    }

    public void EnableMove()
    {
        playerCont.EnableMove(true);
    }

    public void DisableMove()
    {
        playerCont.EnableMove(false);
    }

    public void EnableWeaponColl()
    {
        if (weapon != null)
        {
            weapon.EnableColliders();
        }
    }

    public void DisableWeaponColl()
    {
        if (weapon != null)
        {
            weapon.DisableColliders();
        }
    }
}
