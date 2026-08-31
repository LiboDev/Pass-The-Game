# UI

Canvases, panels, `EventSystem`, HUD elements and menus are placed in the scene by the user. Scripts hold
`[SerializeField]` references to the elements they drive; they never build widgets in code.

## uGUI + TextMeshPro (`using TMPro;` — TMP ships inside com.unity.ugui 2.0)

```csharp
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class HealthHUD : MonoBehaviour
{
    [SerializeField] private PlayerHealth player;      // scene reference
    [SerializeField] private Slider healthBar;
    [SerializeField] private TMP_Text healthLabel;

    private void OnEnable()
    {
        player.HealthChanged += Refresh;
        Refresh(player.Current, player.Max);
    }
    private void OnDisable() => player.HealthChanged -= Refresh;

    private void Refresh(float current, float max)
    {
        healthBar.value = current / max;
        healthLabel.SetText("{0}/{1}", current, max);   // no string allocation
    }
}
```
- Event-driven refresh, never `text = ...` every frame.
- Buttons: wire `onClick` in the Inspector to public methods, or `AddListener` in `OnEnable` +
  `RemoveListener` in `OnDisable`.
- Canvas: *Screen Space – Overlay* for the HUD; `CanvasScaler` = Scale With Screen Size, 1920×1080,
  match 0.5. World-space canvases for in-world labels (a prefab, spawned like anything else when dynamic).
- **Dynamic lists** (inventory slots, objectives, damage popups): a *row prefab* + `[SerializeField] Transform
  container` → `Instantiate(rowPrefab, container)`; pool when churn is high. This is rule 2, not an exception.
- Toggle-able elements (interaction prompt, crosshair states, "objective updated"): scene objects switched
  with `SetActive` or a `CanvasGroup.alpha` fade — never instantiated per use.
- Separate canvases for static vs frequently changing elements (a change rebuilds the whole canvas).
- Horror feel: `CanvasGroup` fades via coroutine/`Animator`; sanity/vignette effects belong in the URP
  Volume on the camera, not in UI.

## UI Toolkit (only if the user chose it for a screen)

`UIDocument` component on a scene object with a `.uxml` asset. Query in `OnEnable`:
`var root = GetComponent<UIDocument>().rootVisualElement; healthLabel = root.Q<Label>("health");`
Element names are design-time data in the UXML, so `Q<T>("name")` strings are fine. Styles in `.uss`.
Pick one system per screen; don't mix uGUI and UI Toolkit on the same screen.

## Menus and flow

- Main/pause menu scripts call the scene loader (`scene-wiring.md`) and the input map switch (`input.md`).
- Quit: `#if UNITY_EDITOR UnityEditor.EditorApplication.isPlaying = false; #else Application.Quit(); #endif`.
- First-selected button for gamepad: `EventSystem.current.SetSelectedGameObject(firstButton.gameObject)`
  in `OnEnable` of the menu.

## Checklist items for the user

Hierarchy to build (`Canvas → HUD → HealthBar (Slider) → Fill`…), which script goes on which object, which
element into which field, `EventSystem` uses `InputSystemUIInputModule`, TMP font asset if a custom font.
