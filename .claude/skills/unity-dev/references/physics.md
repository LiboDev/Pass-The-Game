# Physics, movement, collisions, NavMesh

## Player movement (first person): CharacterController

- Move in `Update`: `controller.Move(motion * Time.deltaTime)`; apply your own gravity to a `velocity.y`
  you keep; `controller.isGrounded` is only meaningful right after a `Move` call.
- Mouse look: yaw on the body, pitch on the camera pivot (clamped ±85°), in `Update` after reading input.
- Lock the cursor when gameplay starts: `Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;`
  and unlock when a menu opens.
- Crouch: tween `controller.height`/`center` and the pivot's local Y; `Physics.SphereCast` upward before
  standing.
- No Rigidbody on the player unless physics reactions are required — then move it **only** in
  `FixedUpdate` through the Rigidbody API, never both systems.

## Rigidbody rules

- Move with `rb.AddForce`, `rb.MovePosition/MoveRotation`, or `rb.linearVelocity`
  (Unity 6 renames: `velocity → linearVelocity`, `drag → linearDamping`, `angularDrag → angularDamping`).
  Never set `transform.position` on a non-kinematic body.
- Physics code in `FixedUpdate`; read input in `Update` and store it.
- *Interpolate* on bodies the camera follows; *Continuous* collision detection for fast projectiles.
- Kinematic + `MovePosition` for animated doors/platforms that must push things.

## Layers and masks

- Layers are project settings (Tags & Layers). Scripts never call `LayerMask.GetMask("Enemy")` — they hold
  `[SerializeField] private LayerMask hitMask;` and the user picks the layers. List new layers and the
  Layer Collision Matrix changes (Project Settings → Physics) in the checklist, e.g. Projectile × Projectile off.
- Test a layer against a mask: `(mask & (1 << go.layer)) != 0`.
- Typical layers for this genre: `Player`, `Enemy`, `Interactable`, `Projectile`, `Environment`, `IgnoreRaycast`.

## Raycasts and overlaps

```csharp
[SerializeField] private Camera cam;                       // wired: the player camera
[SerializeField] private LayerMask interactMask;
[SerializeField, Min(0f)] private float interactRange = 2.5f;

private void Update()
{
    Ray ray = new(cam.transform.position, cam.transform.forward);
    if (Physics.Raycast(ray, out RaycastHit hit, interactRange, interactMask, QueryTriggerInteraction.Ignore)
        && hit.collider.TryGetComponent(out IInteractable target))
    {
        // show prompt; on Interact input → target.Interact(gameObject)
    }
}
```
- Per-frame queries: `Physics.RaycastNonAlloc` / `OverlapSphereNonAlloc` with a preallocated array.
- `SphereCast` for forgiving aim/interaction; `Debug.DrawRay` while tuning.
- Enemy line of sight: raycast from eye point to the player with an `Environment` mask; hit nothing = visible.

## Triggers and collisions

- `OnTrigger*` / `OnCollision*` need a Rigidbody on at least one side (kinematic is fine for triggers).
  Zone volumes: `BoxCollider` with *Is Trigger* + kinematic Rigidbody on the zone object.
- Identify the other side with `TryGetComponent<PlayerHealth>` (preferred) or `CompareTag("Player")`
  (never `other.tag == "Player"`).
- Callbacks land on the object owning the collider — use `GetComponentInParent<T>()` for compound colliders.
- Damage hitboxes (melee): enable a trigger collider for the swing window from an Animation Event or
  coroutine; ignore repeat hits per swing with a `HashSet<IDamageable>`.

## NavMesh (AI Navigation 2.0)

- `NavMeshSurface` component on a scene object; the user bakes it (Inspector → Bake). Say so in the checklist.
- `NavMeshAgent` on the enemy prefab. `agent.SetDestination(target.position)` a few times per second, not
  every frame (stagger agents). Arrival: `!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance`.
- `NavMeshObstacle` (Carve) on doors/movable props; `NavMesh.SamplePosition` before sending an agent to a
  point that might be off-mesh; Off-Mesh Links for drops/vaults.
- Let the agent drive position; animate with `agent.velocity.magnitude`. If root motion drives movement,
  set `agent.updatePosition = false` and sync manually.

## Physics settings

Fixed Timestep 0.02 s by default. Auto Sync Transforms is off — if a raycast right after
`transform.position = ...` misses, call `Physics.SyncTransforms()`.
