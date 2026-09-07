# Weapons & Enemies System

A small, self-contained combat toolkit: data-driven weapons (hitscan or projectile) and enemies that chase
and attack over NavMesh. No scene dependencies are baked in — you wire it up per-project.

## Install

This is a local Unity package. To use it in another project:

1. Copy the whole `com.passthegame.weapons-enemies` folder into that project's `Packages/` directory, **or**
2. In the Package Manager window: `+` → *Install package from disk...* → pick this folder's `package.json`.

Either way it shows up in Package Manager as **Weapons & Enemies System**.

## One-click setup (this project)

`Assets/Editor/WeaponsEnemiesBuilder.cs` adds **Tools → Add Weapons And Enemies** to the menu bar. Open the
scene with the player in it and run it once: it creates the `WeaponData`/`EnemyData` assets and a pistol
and enemy prefab if they don't exist yet, equips the player's camera rig with the pistol wired to the
existing **Attack** input action, adds a `PlayerHealth` (implements `IDamageable`) if the player doesn't
already have something that does, drops an `EnemySpawner` with four spawn points around the player, and
bakes a `NavMeshSurface`. Safe to run again — it re-links what exists instead of duplicating it. Save the
scene afterwards (Ctrl+S). That script is project-specific glue (it knows about `FirstPersonController` and
`PlayerLook`), not part of the portable package above — a different project wires the package by hand per
the steps below, or writes its own version of that builder.

## What's in it

| Script | Type | Purpose |
|---|---|---|
| `IDamageable` | interface | `TakeDamage(float amount, GameObject source)` — implement this on anything that can be hurt (player health, breakables, enemies). |
| `WeaponData` | ScriptableObject | Damage, fire rate, hitscan range/mask, or a projectile prefab; optional muzzle flash / impact VFX / fire sound. |
| `Weapon` | MonoBehaviour | Put on a weapon prefab. Call `weapon.TryFire()` from your own input code. Fires hitscan if `WeaponData.projectilePrefab` is empty, otherwise spawns the projectile. |
| `WeaponInput` | MonoBehaviour | Optional. Reads an `InputActionReference` and calls `TryFire()` for you, for a plain "hold to shoot" setup. |
| `Projectile` | MonoBehaviour | Physics projectile; deals damage and spawns impact VFX on collision. |
| `EnemyData` | ScriptableObject | Max health, move speed, attack damage/range/cooldown, despawn delay. |
| `Enemy` | MonoBehaviour | Implements `IDamageable`. Chases a target via `NavMeshAgent`, attacks in range, dies at 0 health. |
| `EnemySpawner` | MonoBehaviour | Scene-placed. Spawns `Enemy` prefabs at spawn points on an interval up to a max alive count. |

## Wire in Unity

### Weapon

1. Build a weapon prefab (model + a child `Transform` at the barrel tip named e.g. `Muzzle`).
2. Add the `Weapon` component. Assign **Data** and **Muzzle**.
3. Create a data asset: right-click in the Project window → *Create → Weapons And Enemies → Weapon Data*.
   Fill in damage, fire rate, range/hit mask (hitscan) or a `Projectile` prefab (for physical bullets/rockets).
4. Optional: assign a muzzle flash `ParticleSystem` prefab, an impact `ParticleSystem` prefab, and a fire
   `AudioClip` on the data asset.
5. From your player/AI input script, call `GetComponent<Weapon>().TryFire()` on the fire action. This
   package does not read input itself, so it works with any input setup.
6. If using a projectile: build a projectile prefab with a `Rigidbody` + `Collider`, add the `Projectile`
   component, set **Continuous** collision detection on the `Rigidbody`, and assign it to the weapon data's
   **Projectile Prefab** field.

### Enemy

1. Build an enemy prefab (model + `NavMeshAgent` + a `Collider`). Add the `Enemy` component and assign an
   **Enemy Data** asset (*Create → Weapons And Enemies → Enemy Data*).
2. Make sure whatever `Enemy` will chase (usually the player root) has a component implementing
   `IDamageable` so enemy attacks can deal damage — `Enemy` looks this up once, in `Init`, not every frame.
3. In the scene, add an empty GameObject with the `EnemySpawner` component. Assign:
   - **Enemy Prefab** — the prefab from step 1.
   - **Spawn Points** — empty child transforms placed where enemies should appear.
   - **Target** — the player's `Transform` (scene reference).
   - **Spawn Parent** *(optional)* — a transform to keep spawned enemies tidy in the hierarchy.
4. Bake a `NavMeshSurface` in the scene (AI Navigation package, already in this project) covering the
   walkable area, or enemies won't be able to path to the target.

### Layers (recommended, not required)

Add `Player`, `Enemy`, and `Projectile` layers if you don't have them, and turn off Projectile × Projectile
collisions in *Project Settings → Physics* so enemy/player projectiles don't collide with each other.
Set each weapon's/projectile's **Hit Mask** to exclude the shooter's own layer.
