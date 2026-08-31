# Performance

Measure first: Window → Analysis → Profiler (CPU / Rendering / Memory / Physics), Frame Debugger, and
`Profiler.BeginSample("name")`/`EndSample` around suspects. Optimise what the profiler shows, not guesses.

## CPU / scripting

- Cache components and `Camera.main` in `Awake`; no `GetComponent`, `Find*`, or `Camera.main` in `Update`.
- Zero per-frame allocations: no string concat/interpolation, no `new` arrays/lists, no LINQ, no boxing,
  no closures capturing locals in hot paths. UI text: `TMP_Text.SetText("{0}", v)`.
- Fewer `Update`s: one manager ticking a list beats hundreds of `Update` calls; disable components on
  far-away or inactive enemies; use events instead of polling.
- Coroutines: cache `WaitForSeconds`; `yield return null` is fine per frame.
- `Debug.Log` captures a stack trace — never in hot paths; strip with `[Conditional("UNITY_EDITOR")]`
  wrappers if a logging helper exists.
- Animator parameters: `static readonly int SpeedHash = Animator.StringToHash("Speed");` → `SetFloat(SpeedHash, v)`.
- `CompareTag` over `tag ==`; `sqrMagnitude` over `Distance`.

## Spawning

- Pool anything frequent (`spawning.md`). `Instantiate` is the expensive half; `Destroy` is deferred.
- Prewarm pools in `Awake`/`Start` of a scene-placed pool object, not on first shot.

## Physics

- `NonAlloc` query variants with preallocated arrays; tight layer masks; `QueryTriggerInteraction.Ignore`.
- Throttle AI queries and `SetDestination` (every 0.2 s, staggered by agent index).
- Mesh colliders only for static geometry; primitives/compound colliders on moving things.
- Collision matrix: turn off pairs that never need to collide (Projectile × Projectile, Enemy × Enemy if
  they use NavMesh avoidance).

## Rendering (URP)

- Mark level geometry *Static* (batching, occlusion, lightmaps); bake lights for the ambience; keep
  realtime shadow-casting lights to the few that matter (flashlight, one key light per room).
- SRP Batcher is on by default — keep materials on compatible shaders (Lit/Simple Lit/Shader Graph).
- LOD Groups on heavy props; occlusion culling for indoor levels; texture compression + mipmaps.
- Post-processing lives in Volumes (`Assets/Settings`); avoid stacking many full-screen effects.
- Particle systems: cap max particles, use *Stop Action*, avoid per-particle lights.

## Audio and memory

- Short SFX: *Decompress On Load*; long ambience/music: *Streaming* or *Compressed In Memory*.
- Watch `AudioSource` counts; use the mixer instead of per-source volume scripts.
- Addressables/asset bundles are not in use — don't introduce them for a load-time problem without asking.
