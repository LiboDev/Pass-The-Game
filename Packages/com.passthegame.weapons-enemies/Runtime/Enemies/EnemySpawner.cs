using System.Collections.Generic;
using UnityEngine;

namespace PassTheGame.WeaponsEnemies
{
    public class EnemySpawner : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Enemy enemyPrefab;
        [SerializeField] private Transform[] spawnPoints;
        [SerializeField] private Transform target;
        [SerializeField] private Transform spawnParent;

        [Header("Tuning")]
        [SerializeField, Min(0.1f)] private float spawnInterval = 3f;
        [SerializeField, Min(1)] private int maxAlive = 10;

        private readonly List<Enemy> alive = new();
        private float timer;

        private void Awake()
        {
            if (!enemyPrefab || spawnPoints == null || spawnPoints.Length == 0 || !target)
            {
                Debug.LogError("EnemySpawner is missing its prefab, spawn points, or target", this);
                enabled = false;
            }
        }

        private void Update()
        {
            alive.RemoveAll(enemy => enemy == null);

            timer += Time.deltaTime;
            if (timer < spawnInterval || alive.Count >= maxAlive) return;
            timer = 0f;

            Transform spawnPoint = spawnPoints[Random.Range(0, spawnPoints.Length)];
            Enemy enemy = Instantiate(enemyPrefab, spawnPoint.position, spawnPoint.rotation, spawnParent);
            enemy.Init(target);
            alive.Add(enemy);
        }

        private void OnDrawGizmosSelected()
        {
            if (spawnPoints == null) return;
            Gizmos.color = Color.red;
            foreach (Transform point in spawnPoints)
                if (point) Gizmos.DrawWireSphere(point.position, 0.5f);
        }
    }
}
