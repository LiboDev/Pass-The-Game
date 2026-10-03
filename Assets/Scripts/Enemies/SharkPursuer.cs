using UnityEngine;

/// <summary>
/// A relentless hazard that ignores navigation and travels directly toward the player.
/// It never times out or gives up; touching the player is immediately lethal.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class SharkPursuer : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField, Min(0.1f)] private float speed = 7f;
    [SerializeField, Min(0.1f)] private float killDistance = 1.5f;
    [SerializeField, Min(0f)] private float turnSpeed = 8f;

    private PlayerHealth targetHealth;
    private bool hasKilled;

    private void Awake()
    {
        Rigidbody body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        GetComponent<Collider>().isTrigger = true;
        FindTarget();
    }

    private void Update()
    {
        if (hasKilled)
        {
            return;
        }

        if (!target || !targetHealth)
        {
            FindTarget();
            if (!target)
            {
                return;
            }
        }

        Vector3 toPlayer = target.position - transform.position;
        float distance = toPlayer.magnitude;
        if (distance <= killDistance)
        {
            KillPlayer();
            return;
        }

        Vector3 direction = toPlayer / distance;
        transform.position += direction * (speed * Time.deltaTime);

        if (turnSpeed > 0f)
        {
            Quaternion wanted = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, wanted, turnSpeed * Time.deltaTime);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!hasKilled && other.GetComponentInParent<PlayerHealth>() == targetHealth)
        {
            KillPlayer();
        }
    }

    private void FindTarget()
    {
        targetHealth = FindFirstObjectByType<PlayerHealth>();
        target = targetHealth ? targetHealth.transform : null;
    }

    private void KillPlayer()
    {
        hasKilled = true;
        targetHealth.Kill(gameObject);
    }
}
