using UnityEngine;

namespace PassTheGame.WeaponsEnemies
{
    [CreateAssetMenu(fileName = "EnemyData", menuName = "Weapons And Enemies/Enemy Data")]
    public class EnemyData : ScriptableObject
    {
        [Header("Health")]
        [Min(1f)] public float maxHealth = 100f;

        [Header("Movement")]
        [Min(0f)] public float moveSpeed = 3.5f;

        [Header("Attack")]
        [Min(0f)] public float attackDamage = 10f;
        [Min(0.1f)] public float attackRange = 2f;
        [Min(0.05f)] public float attackCooldown = 1f;

        [Header("Death")]
        [Min(0f)] public float despawnDelay = 3f;
    }
}
