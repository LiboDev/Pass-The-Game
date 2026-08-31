# Scene-placed objects, references, managers, audio, scene flow

## What lives in the scene (placed by the user, never created by code)

Player, cameras, managers (Game/Audio/UI/Save), level geometry, lights, volumes, `NavMeshSurface`,
`Canvas` + `EventSystem`, the single `AudioListener`, spawners, trigger zones, doors, checkpoints —
anything unique to a level or to the game session. Scripts refer to these through `[SerializeField]`
fields that the user fills in the Inspector.

If a script needs something it cannot be given in the Inspector, the design is wrong: inject it, or share
it through a ScriptableObject. Never `Find` it, never create a fallback copy in code.

## Getting a reference — take the first rung that works

1. `[SerializeField] private X x;` wired in the Inspector (the default, also for children and siblings).
2. `GetComponent<T>()` / `GetComponentInChildren<T>()` in `Awake`, for components on the **same
   prefab/hierarchy**; add `[RequireComponent(typeof(T))]` when it is a hard dependency.
3. Injection: the spawner/parent calls `child.Init(dependency)` right after `Instantiate`.
4. ScriptableObject runtime reference (below) for "the current player"-style lookups from prefabs.
5. `FindFirstObjectByType<T>()` once in `Awake`/`Start`, only as a fallback for a singular scene service,
   always with a `LogError` when null. (`FindObjectOfType` is obsolete in Unity 6.)

Never: `GameObject.Find("Player")`, `FindWithTag` in hot paths, `transform.Find("Model/Arm/Hand")` chains,
`Camera.main` every frame (cache it in `Awake`), `Resources.Load`.

## Managers and singletons

A manager is an ordinary scene-placed GameObject. A static `Instance` accessor is fine; a **self-creating**
singleton is not (it hides missing scene setup and violates the placement rule).

```csharp
[DisallowMultipleComponent]
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [SerializeField] private PlayerHealth player;           // wired in the Inspector
    [SerializeField] private VoidEventChannelSO onPlayerDied;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable()  => onPlayerDied.Raised += HandlePlayerDied;
    private void OnDisable() => onPlayerDied.Raised -= HandlePlayerDied;
    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void HandlePlayerDied() { /* ... */ }
}
```
`DontDestroyOnLoad(gameObject)` only on scene-placed objects in a boot/persistent scene, with the duplicate
guard above. Prefer an additive "Persistent" scene over `DontDestroyOnLoad`.

## Prefab → scene references: ScriptableObject runtime reference

A prefab asset cannot reference a scene object. Share a tiny SO asset between both sides:

```csharp
[CreateAssetMenu(menuName = "Game/Runtime Refs/Transform Ref", fileName = "PlayerRef")]
public class TransformRefSO : ScriptableObject
{
    [NonSerialized] public Transform Value;   // NonSerialized: never saved into the asset
}

// Player.cs (scene object)
[SerializeField] private TransformRefSO playerRef;
private void OnEnable()  => playerRef.Value = transform;
private void OnDisable() { if (playerRef.Value == transform) playerRef.Value = null; }

// Enemy.cs (prefab)
[SerializeField] private TransformRefSO playerRef;
private void Update() { if (!playerRef.Value) return; /* chase playerRef.Value */ }
```
The user assigns the same `PlayerRef.asset` on both. Event channels and runtime sets follow the same idea —
see `scriptable-objects.md`.

## Lifecycle across objects

`Awake`: initialise *yourself* (cache components, validate fields). `OnEnable`: subscribe.
`Start`: talk to *other* objects (every `Awake` has run). `OnDisable`: unsubscribe.
Use Script Execution Order (Project Settings) only for a genuine ordering need, and say so in the checklist.

## Audio

- `AudioSource` components sit on the scene objects/prefabs that emit sound; exactly one `AudioListener`,
  on the player camera.
- Clips are serialized: `[SerializeField] private AudioClip[] footsteps;` or a `SoundSO`
  (clips + volume/pitch ranges + mixer group). `source.PlayOneShot(clip, volume)` for one-shots;
  `AudioSource.PlayClipAtPoint` is acceptable for fire-and-forget 3D one-shots.
- Route through an `AudioMixer` (`Assets/Audio`) with exposed volume parameters for settings menus.
- Horror ambience: looping 2D sources on an `AudioManager` object; 3D sources with a custom rolloff curve on
  emitters; mixer snapshots for tension states (`mixer.TransitionToSnapshots`).

## Cameras

The player camera is a child of the Player in the scene. Mouse look rotates the body (yaw) and a camera
pivot (pitch); the pivot is a `[SerializeField] Transform`. Cinemachine is not installed; do not add it
unless asked.

## Scenes and loading

- `SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single | Additive)`; scene names/indices are
  serialized fields on the loader, and every scene must be in Build Settings (put that in the checklist).
- Structure: `Boot`/`Persistent` (managers, UI) + level scenes loaded additively;
  `SceneManager.SetActiveScene` so the level's lighting settings apply.
- Inspector references cannot cross scenes — use the SO patterns above.
