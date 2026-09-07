using UnityEngine;
using UnityEngine.InputSystem;

namespace PassTheGame.WeaponsEnemies
{
    [RequireComponent(typeof(Weapon))]
    public class WeaponInput : MonoBehaviour
    {
        [SerializeField] private InputActionReference fire;

        private Weapon weapon;

        private void Awake()
        {
            weapon = GetComponent<Weapon>();
            if (!fire)
            {
                Debug.LogError("WeaponInput needs a fire action assigned", this);
                enabled = false;
            }
        }

        private void Update()
        {
            if (fire.action.IsPressed()) weapon.TryFire();
        }
    }
}
