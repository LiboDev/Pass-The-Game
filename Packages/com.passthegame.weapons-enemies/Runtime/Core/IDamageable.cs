using UnityEngine;

namespace PassTheGame.WeaponsEnemies
{
    public interface IDamageable
    {
        void TakeDamage(float amount, GameObject source);
    }
}
