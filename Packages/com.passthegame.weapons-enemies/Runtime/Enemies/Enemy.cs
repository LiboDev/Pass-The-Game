using System;
using UnityEngine;
using UnityEngine.AI;

namespace PassTheGame.WeaponsEnemies
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    public class Enemy : MonoBehaviour, IDamageable
    {
        [Header("References")]
        [SerializeField] private EnemyData data;

        public event Action<Enemy> Died;

        private NavMeshAgent agent;
        private Transform target;
        private IDamageable targetDamageable;
        private float health;
        private float nextAttackTime;
        private float nextRepathTime;
        private bool dead;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            if (!data)
            {
                Debug.LogError("Enemy is missing its EnemyData asset", this);
                enabled = false;
                return;
            }

            health = data.maxHealth;
            agent.speed = data.moveSpeed;
        }

        /// <summary>Called by the spawner right after Instantiate — the enemy never searches for its target.</summary>
        public void Init(Transform chaseTarget)
        {
            target = chaseTarget;
            targetDamageable = chaseTarget ? chaseTarget.GetComponent<IDamageable>() : null;
        }

        private void Update()
        {
            if (dead || !target) return;

            float sqrDistance = (target.position - transform.position).sqrMagnitude;
            if (sqrDistance <= data.attackRange * data.attackRange)
            {
                agent.isStopped = true;
                FaceTarget();
                TryAttack();
            }
            else
            {
                agent.isStopped = false;
                Repath();
            }
        }

        public void TakeDamage(float amount, GameObject source)
        {
            if (dead) return;

            health -= amount;
            if (health <= 0f) Die();
        }

        private void Repath()
        {
            if (Time.time < nextRepathTime) return;
            nextRepathTime = Time.time + 0.2f;
            agent.SetDestination(target.position);
        }

        private void FaceTarget()
        {
            Vector3 direction = target.position - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f) return;
            transform.rotation = Quaternion.LookRotation(direction);
        }

        private void TryAttack()
        {
            if (Time.time < nextAttackTime) return;
            nextAttackTime = Time.time + data.attackCooldown;
            targetDamageable?.TakeDamage(data.attackDamage, gameObject);
        }

        private void Die()
        {
            dead = true;
            agent.isStopped = true;
            if (TryGetComponent(out Collider col)) col.enabled = false;
            Died?.Invoke(this);
            Destroy(gameObject, data.despawnDelay);
        }
    }
}
