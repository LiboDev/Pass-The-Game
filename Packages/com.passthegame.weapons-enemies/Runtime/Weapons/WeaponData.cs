using UnityEngine;

namespace PassTheGame.WeaponsEnemies
{
    [CreateAssetMenu(fileName = "WeaponData", menuName = "Weapons And Enemies/Weapon Data")]
    public class WeaponData : ScriptableObject
    {
        [Header("Damage & Timing")]
        [Min(0f)] public float damage = 10f;
        [Min(0.01f)] public float shotsPerSecond = 4f;

        [Header("Hitscan (used when Projectile Prefab is empty)")]
        [Min(0.1f)] public float range = 100f;
        public LayerMask hitMask = ~0;

        [Header("Projectile (leave empty for hitscan)")]
        public Projectile projectilePrefab;

        [Header("Feedback (optional)")]
        public ParticleSystem muzzleFlashPrefab;
        public ParticleSystem impactVfxPrefab;
        public AudioClip fireSound;
    }
}
