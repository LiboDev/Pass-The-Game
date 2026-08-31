# Input (Input System 1.17 — the legacy `Input` class is disabled in this project)

`Assets/InputSystem_Actions.inputactions` is the **project-wide actions asset**
(Project Settings → Input System Package → Project-wide Actions), so its maps are enabled automatically.

| Map | Actions |
|---|---|
| `Player` | `Move` (Vector2), `Look` (Vector2), `Attack`, `Interact`, `Crouch`, `Jump`, `Sprint`, `Previous`, `Next` |
| `UI` | `Navigate`, `Submit`, `Cancel`, `Point`, `Click`, `RightClick`, `MiddleClick`, `ScrollWheel`, … |

Need a new action (Flashlight, Pause, Sprint hold)? Add it to that asset — it is JSON, you can edit it
(copy an existing action + binding block, fresh `id` GUIDs) — or ask the user to add it in the Input Actions
editor. Do not create a second actions asset.

## Reading input

Preferred: `InputActionReference` fields wired in the Inspector. No string lookups, refactor-safe.

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputReader : MonoBehaviour
{
    [SerializeField] private InputActionReference move, look, interact, sprint, crouch;

    public Vector2 Move   => move.action.ReadValue<Vector2>();
    public Vector2 Look   => look.action.ReadValue<Vector2>();
    public bool Sprinting => sprint.action.IsPressed();
    public bool CrouchToggled => crouch.action.WasPressedThisFrame();

    public event System.Action Interacted;

    private void OnEnable()  => interact.action.performed += OnInteract;
    private void OnDisable() => interact.action.performed -= OnInteract;
    private void OnInteract(InputAction.CallbackContext _) => Interacted?.Invoke();
}
```
One reader on the Player, other scripts take a `[SerializeField] PlayerInputReader input;` reference.

Acceptable alternatives:
- `PlayerInput` component on the Player with *Behavior = Invoke Unity Events* and handlers wired in the
  Inspector (`void OnMove(InputAction.CallbackContext ctx)`).
- `InputSystem.actions.FindAction("Player/Move")` cached once in `Awake` — one string, one place — only if
  references are impractical.

If a script uses a **non**-project-wide asset, call `action.Enable()` in `OnEnable` and `Disable()` in
`OnDisable`.

## Mouse look

`Look` gives pixel deltas for the mouse (do **not** multiply by `deltaTime`) and stick values for gamepads
(do multiply). Either add a *Scale* processor per binding in the asset, or branch on
`ctx.control.device is Gamepad`. Sensitivity is a serialized field.

## Menus, pause, cursor

- `EventSystem` in the scene must use `InputSystemUIInputModule` (not `StandaloneInputModule`).
- Pause: `Time.timeScale = 0`, unlock cursor, switch maps: disable `Player`, enable `UI`
  (`InputSystem.actions.FindActionMap("Player").Disable()` from the pause menu script, or
  `playerInput.SwitchCurrentActionMap("UI")` when using `PlayerInput`). Reverse on resume.
- Interaction prompt text ("Press E") from `interact.action.GetBindingDisplayString()` — never hardcode keys.

## Don'ts

`Input.GetKey`, `Input.GetAxis`, `Input.mousePosition` — throw with the legacy backend disabled.
Polling `Keyboard.current.eKey` in gameplay scripts — bypasses rebinding and gamepads.
