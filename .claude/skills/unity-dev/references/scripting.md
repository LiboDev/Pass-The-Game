# Scripting conventions

## Shape of a MonoBehaviour

```csharp
using UnityEngine;

namespace Horror.Player                      // match existing namespaces in Assets/Scripts; one per feature folder
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMovement : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform cameraPivot;

        [Header("Tuning")]
        [SerializeField, Min(0f)] private float walkSpeed = 3f;
        [SerializeField, Range(0f, 1f)] private float crouchSpeedMultiplier = 0.5f;
        [SerializeField, Tooltip("Negative = down, m/s²")] private float gravity = -20f;

        public bool IsMoving { get; private set; }
        public event System.Action<bool> CrouchChanged;

        private CharacterController controller;
        private Vector3 velocity;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (!cameraPivot) { Debug.LogError("cameraPivot not assigned", this); enabled = false; }
        }
        private void OnEnable()  { /* subscribe */ }
        private void Update()    { /* read input, move */ }
        private void OnDisable() { /* unsubscribe */ }
    }
}
```
- One class per file; file name == class name (Unity requires it for MonoBehaviour/ScriptableObject).
- `[SerializeField] private` over `public` fields; expose state through read-only properties.
- Every tunable is a serialized field with a sane default, grouped with `[Header]`, constrained with
  `[Min]`/`[Range]`, explained with `[Tooltip]` when the name is not enough.
- Validate required references in `Awake`, log with `this` as context, disable the component.
- Member order: fields (refs → tuning → runtime) → properties/events → Unity messages in lifecycle order →
  public methods → private methods.

## Lifecycle

`Awake` (self-init) → `OnEnable` (subscribe) → `Start` (talk to others) → `FixedUpdate` (physics) →
`Update` (input, logic) → `LateUpdate` (camera follow, look-at) → `OnDisable` → `OnDestroy`.
`Awake`/`Start` run once per object; `OnEnable`/`OnDisable` on every toggle — pooling depends on it.

## Time

`Time.deltaTime` in `Update`/`LateUpdate`; `FixedUpdate` is already fixed-step. `Time.unscaledDeltaTime`
for pause menus. `Time.timeScale = 0` freezes physics, animation and `WaitForSeconds`;
`WaitForSecondsRealtime` does not.

## Timers, delays, sequences

- Cooldown: `float next; if (Time.time < next) return; next = Time.time + interval;`
- Sequences and delays: coroutines. Cache `new WaitForSeconds(x)` in a field for loops. Coroutines stop
  silently when the object is disabled and do **not** resume — restart them in `OnEnable`.
- `async`/`await` with Unity 6 `Awaitable`
  (`await Awaitable.WaitForSecondsAsync(1f, destroyCancellationToken)`) is fine for one-off flows;
  always pass `destroyCancellationToken`.
- Never `Invoke("Name", t)` / `InvokeRepeating` — string-based and unrefactorable.

## Events and communication

| Situation | Use |
|---|---|
| Same object / tightly coupled classes | C# `event Action<T>`; raise with `?.Invoke` |
| Designer wires a response in the Inspector (play sound, open door) | `UnityEvent` field |
| Many-to-many, prefab ↔ scene, cross-scene | ScriptableObject event channel (`scriptable-objects.md`) |
| `SendMessage` / `BroadcastMessage` | never |

Always unsubscribe in `OnDisable`/`OnDestroy` — dangling handlers on destroyed objects are the #1 source
of `MissingReferenceException`.

## Interfaces for interactions

```csharp
public interface IDamageable   { void TakeDamage(float amount); }
public interface IInteractable { string Prompt { get; } void Interact(GameObject interactor); }

// Raycast / trigger side:
if (hit.collider.TryGetComponent(out IInteractable target)) target.Interact(gameObject);
```
`TryGetComponent` works with interfaces. Put the implementing component on the collider's object, or use
`GetComponentInParent<T>()` for compound colliders.

## State machines

Enum + `switch` for up to ~5 simple states (idle/patrol/chase/attack). Beyond that, one class per state
(`IState { Enter(); Tick(); Exit(); }`) owned by a `StateMachine` field on the MonoBehaviour. Transitions
live in one place; states receive dependencies through their constructor.

## Unity null semantics

Destroyed objects compare `== null` but are **not** C# `null`: `obj?.Foo()`, `??`, `is null` skip Unity's
check and throw later. Use `if (obj != null)` / `if (obj)` on anything deriving from `UnityEngine.Object`.
Null-check anything that can be destroyed under you (spawned enemies, the player after death).

## Math and random

`Random.Range(int, int)` excludes max; `Random.Range(float, float)` includes it. Compare distances with
`sqrMagnitude`; use `Mathf.MoveTowards` / `SmoothDamp` rather than `Lerp(a, b, Time.deltaTime)`.
`Quaternion.LookRotation` for facing; `Vector3.ProjectOnPlane` for slope movement.

## Logging

`Debug.Log($"[{nameof(EnemySpawner)}] spawned {name}", this)` — the context argument makes the entry ping
the object. `LogError` for misconfiguration (once, in `Awake`). Nothing per frame.

## Editor-time safety

Wrap `UnityEditor` usage in `#if UNITY_EDITOR`. `[ExecuteAlways]` only when edit-mode behaviour is really
wanted, and check `Application.isPlaying` inside.

## Language level

Unity 6 compiles C# 9: target-typed `new()`, switch expressions, pattern matching are fine.
Unity objects are main-thread only — no `Task.Run` touching them; use `Awaitable` or coroutines.
