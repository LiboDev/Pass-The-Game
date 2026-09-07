using UnityEngine;

namespace PassTheGame.WeaponsEnemies
{
    [RequireComponent(typeof(Rigidbody), typeof(Collider))]
    public class Projectile : MonoBehaviour
    {
        [Header("Tuning")]
        [SerializeField, Min(0f)] private float speed = 30f;
        [SerializeField, Min(0.1f)] private float lifetime = 5f;
        [SerializeField] private LayerMask hitMask = ~0;

        [Header("Feedback (optional)")]
        [SerializeField] private ParticleSystem impactVfxPrefab;

        private Rigidbody rb;
        private float damage;
        private GameObject owner;

        private void Awake() => rb = GetComponent<Rigidbody>();

        public void Launch(float damageAmount, GameObject firedBy)
        {
            damage = damageAmount;
            owner = firedBy;
        }

        private void OnEnable()
        {
            rb.linearVelocity = transform.forward * speed;
            Destroy(gameObject, lifetime);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if ((hitMask & (1 << collision.gameObject.layer)) == 0)
            {
                Destroy(gameObject);
                return;
            }

            if (collision.collider.TryGetComponent(out IDamageable damageable))
                damageable.TakeDamage(damage, owner);
            else if (collision.collider.GetComponentInParent<IDamageable>() is { } parentDamageable)
                parentDamageable.TakeDamage(damage, owner);

            if (impactVfxPrefab)
            {
                ContactPoint contact = collision.GetContact(0);
                Instantiate(impactVfxPrefab, contact.point, Quaternion.LookRotation(contact.normal));
            }

            Destroy(gameObject);
        }
    }
}
