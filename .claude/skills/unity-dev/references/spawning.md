# Spawning: prefabs, spawners, projectiles, pooling

Everything that appears at runtime — enemies, projectiles, pickups, VFX, decals, popups, loot, UI list rows —
is a **prefab asset** referenced through a **serialized field**. Code never assembles GameObjects
(`new GameObject()`, `AddComponent` chains), never loads by string (`Resources.Load("Enemies/Zombie")`),
and never uses a scene object as a template (it copies runtime state).

## The prefab field

Reference the **component type**, not `GameObject`, whenever the spawner needs to talk to the spawned thing.
`Instantiate<T>` returns that type, so no `GetComponent` after spawning.

```csharp
public class EnemySpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Enemy enemyPrefab;        // Prefabs/Enemy.prefab
    [SerializeField] private Transform[] spawnPoints;  // empty children placed in the scene
    [SerializeField] private Transform target;         // the Player (scene object)
    [SerializeField] private Transform spawnParent;    // optional, keeps the hierarchy tidy

    [Header("Tuning")]
    [SerializeField, Min(0.1f)] private float spawnInterval = 3f;
    [SerializeField, Min(1)] private int maxAlive = 10;

    private readonly List<Enemy> alive = new();
    private float timer;

    private void Awake()
    {
        if (!enemyPrefab || spawnPoints.Length == 0)
        {
            Debug.LogError("EnemySpawner is missing its prefab or spawn points", this);
            enabled = false;
        }
    }

    private void Update()
    {
        alive.RemoveAll(e => e == null);               // destroyed enemies drop out
        timer += Time.deltaTime;
        if (timer < spawnInterval || alive.Count >= maxAlive) return;
        timer = 0f;

        Transform at = spawnPoints[Random.Range(0, spawnPoints.Length)];
        Enemy enemy = Instantiate(enemyPrefab, at.position, at.rotation, spawnParent);
        enemy.Init(target);                            // inject dependencies; the enemy never Find()s anything
        alive.Add(enemy);
    }

    private void OnDrawGizmosSelected()
    {
        if (spawnPoints == null) return;
        Gizmos.color = Color.red;
        foreach (var p in spawnPoints) if (p) Gizmos.DrawWireSphere(p.position, 0.5f);
    }
}
```

Rules that follow from this:
- **Inject, don't search.** Whatever the spawned object needs (player, event channel, stats) is passed in
  `Init(...)` or lives on a ScriptableObject both sides reference (see `scene-wiring.md`).
- **Variety is data, not code.** Enemy types are prefab variants and/or a `[SerializeField] EnemyStats stats`
  asset on the prefab. Many types → `Enemy[] prefabs` or a weighted `SpawnTableSO` (`scriptable-objects.md`).
- **Fail loudly on missing setup** in `Awake` (`Debug.LogError(..., this)` + `enabled = false`) instead of
  creating a fallback object.
- Cleanup: `Destroy(gameObject, lifetime)` or return to a pool. Never `DestroyImmediate` at runtime.
- The spawner/pool component itself is a scene-placed object (or part of the weapon prefab) — rule 1.

## Projectiles

```csharp
[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class Projectile : MonoBehaviour
{
    [SerializeField, Min(0f)] private float speed = 30f;
    [SerializeField, Min(0.1f)] private float lifetime = 5f;
    [SerializeField, Min(0f)] private float damage = 10f;
    [SerializeField] private LayerMask hitMask = ~0;
    [SerializeField] private ParticleSystem impactVfxPrefab;   // optional prefab

    private Rigidbody rb;

    private void Awake() => rb = GetComponent<Rigidbody>();

    private void OnEnable()
    {
        rb.linearVelocity = transform.forward * speed;         // Unity 6: linearVelocity, not velocity
        Destroy(gameObject, lifetime);
    }

    private void OnCollisionEnter(Collision c)
    {
        if ((hitMask & (1 << c.gameObject.layer)) == 0) return;
        if (c.collider.TryGetComponent(out IDamageable target)) target.TakeDamage(damage);
        if (impactVfxPrefab)
        {
            ContactPoint cp = c.GetContact(0);
            Instantiate(impactVfxPrefab, cp.point, Quaternion.LookRotation(cp.normal));
        }
        Destroy(gameObject);
    }
}

public class Weapon : MonoBehaviour
{
    [SerializeField] private Projectile projectilePrefab;
    [SerializeField] private Transform muzzle;               // child transform inside the weapon prefab
    [SerializeField, Min(0.01f)] private float shotsPerSecond = 4f;
    private float nextShot;

    public void TryFire()
    {
        if (Time.time < nextShot) return;
        nextShot = Time.time + 1f / shotsPerSecond;
        Instantiate(projectilePrefab, muzzle.position, muzzle.rotation);
    }
}
```
Hitscan weapons raycast (see `physics.md`) and spawn only the impact VFX/decal prefab.
Give the projectile's Rigidbody *Continuous* collision detection; give VFX prefabs a `ParticleSystem`
with *Stop Action: Destroy* (or *Callback* when pooled) so they clean themselves up.

## Pooling (`UnityEngine.Pool.ObjectPool<T>`)

Pool anything spawned more than a few times per second: bullets, hit VFX, decals, damage popups.

```csharp
using UnityEngine.Pool;

public class ProjectilePool : MonoBehaviour
{
    [SerializeField] private Projectile prefab;
    [SerializeField, Min(0)] private int prewarm = 20;
    [SerializeField, Min(1)] private int maxSize = 200;

    private ObjectPool<Projectile> pool;

    private void Awake()
    {
        pool = new ObjectPool<Projectile>(
            createFunc: () => { var p = Instantiate(prefab, transform); p.ReturnTo = pool; return p; },
            actionOnGet: p => p.gameObject.SetActive(true),
            actionOnRelease: p => p.gameObject.SetActive(false),
            actionOnDestroy: p => Destroy(p.gameObject),
            collectionCheck: true, defaultCapacity: prewarm, maxSize: maxSize);
    }

    public Projectile Get(Vector3 position, Quaternion rotation)
    {
        Projectile p = pool.Get();
        p.transform.SetPositionAndRotation(position, rotation);
        return p;
    }
}
// Projectile gains:  public ObjectPool<Projectile> ReturnTo { get; set; }
// and replaces Destroy(gameObject) with:  if (ReturnTo != null) ReturnTo.Release(this); else Destroy(gameObject);
// Lifetime becomes a coroutine started in OnEnable and stopped in OnDisable.
```
Pooled objects **reset all state in `OnEnable`** (velocity, timers, health, `TrailRenderer.Clear()`) and
**stop coroutines in `OnDisable`**. Never keep references to a released object.

## Spawn triggers, waves, ambushes

- Zone/ambush spawn: scene-placed trigger collider + `SpawnTrigger` script with
  `[SerializeField] Enemy prefab; Transform spawnPoint; bool once = true;` — on `OnTriggerEnter`,
  check `other.TryGetComponent<PlayerMarker>(...)` (or `CompareTag("Player")`), spawn, then disable itself.
- Waves: a `WaveSpawner` driven by `WaveSO[]` (entries of prefab + count + delay) in a coroutine.
- Timed loops: `Update` timer or coroutine; never `InvokeRepeating("Spawn", ...)`.

## Placing things in a level is authoring, not spawning

Twelve lockers in a corridor, the boss in its arena, the HUD canvas — the user places these in the editor
(or a one-off editor script does; see `editor-and-assets.md`). Runtime `Instantiate` is only for things that
*appear because of gameplay*.

## What to tell the user afterwards

1. Which GameObject becomes the prefab, which components it needs, where to save it
   (`Assets/Prefabs/<Name>.prefab`).
2. Which scene object hosts the spawner script and exactly which prefab/reference goes into which field.
3. Layer and collision-matrix changes the projectile/enemy needs.
