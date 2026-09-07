using UnityEngine;

namespace PassTheGame.WeaponsEnemies
{
    [DisallowMultipleComponent]
    public class Weapon : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private WeaponData data;
        [SerializeField] private Transform muzzle;

        private float nextFireTime;

        private void Awake()
        {
            if (!data || !muzzle)
            {
                Debug.LogError("Weapon is missing its WeaponData or muzzle transform", this);
                enabled = false;
            }
        }

        /// <summary>Call from your own input/AI code. Returns false while on cooldown.</summary>
        public bool TryFire()
        {
            if (Time.time < nextFireTime) return false;
            nextFireTime = Time.time + 1f / data.shotsPerSecond;

            if (data.muzzleFlashPrefab) Instantiate(data.muzzleFlashPrefab, muzzle.position, muzzle.rotation);
            if (data.fireSound) AudioSource.PlayClipAtPoint(data.fireSound, muzzle.position);

            if (data.projectilePrefab)
            {
                Projectile projectile = Instantiate(data.projectilePrefab, muzzle.position, muzzle.rotation);
                projectile.Launch(data.damage, gameObject);
            }
            else
            {
                FireHitscan();
            }

            return true;
        }

        private void FireHitscan()
        {
            Ray ray = new(muzzle.position, muzzle.forward);
            if (!Physics.Raycast(ray, out RaycastHit hit, data.range, data.hitMask, QueryTriggerInteraction.Ignore))
                return;

            if (hit.collider.TryGetComponent(out IDamageable damageable))
                damageable.TakeDamage(data.damage, gameObject);
            else if (hit.collider.GetComponentInParent<IDamageable>() is { } parentDamageable)
                parentDamageable.TakeDamage(data.damage, gameObject);

            if (data.impactVfxPrefab)
                Instantiate(data.impactVfxPrefab, hit.point, Quaternion.LookRotation(hit.normal));
        }
    }
}
