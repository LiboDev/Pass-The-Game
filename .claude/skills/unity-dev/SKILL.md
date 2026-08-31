---
name: unity-dev
description: Unity 6 C# game development for this project (URP, new Input System, uGUI/TextMeshPro, AI Navigation). Use for any task that touches Assets/ — MonoBehaviours, ScriptableObjects, editor tools, prefabs, scenes, UI, physics, input, audio, enemy AI — and when planning or reviewing Unity code. Enforces scene-placed persistent objects and prefab-based spawning.
---

# Unity development

Unity 6000.3 · URP 17 · Input System 1.17 (project-wide actions asset: `Assets/InputSystem_Actions.inputactions`) ·
uGUI 2 (TextMeshPro built in) · AI Navigation 2 · Force Text serialization · no asmdefs yet.
Re-check `ProjectSettings/ProjectVersion.txt` and `Packages/manifest.json` if anything looks off.

## Hard rules

1. **Persistent objects live in the scene, never in code.** Player, cameras, managers, level geometry, lights,
   UI canvases, `EventSystem`, `AudioListener`, spawners, trigger zones — the user places them in the hierarchy.
   Scripts never `new GameObject()`, `AddComponent`, or `Instantiate` these at runtime, never build UI from
   code, never lazily create a missing manager: log an error with `this` as context and disable the component.
2. **Everything that appears at runtime is a prefab in a serialized field.** Enemies, projectiles, pickups, VFX,
   decals, popups, UI rows: `[SerializeField] private Enemy enemyPrefab;` → `Instantiate(enemyPrefab, pos, rot)`.
   Never assemble GameObjects in code, never `Resources.Load`, never hardcode asset paths, object names, tags,
   or layer names as string literals.
3. **References are wired in the Inspector, not searched for.** `[SerializeField]` › `GetComponent` in `Awake`
   on your own hierarchy › injection (`Init(...)` after spawn) › shared ScriptableObject reference.
   No `GameObject.Find`, `FindWithTag`, `FindFirstObjectByType` in hot paths, no `Camera.main` per frame.
4. **Tunables are serialized fields or ScriptableObject data** — speeds, damage, ranges, timings, curves,
   layer masks. No magic numbers.
5. **Never author a Unity-imported asset by hand — Unity generates it, or a `[MenuItem]` does.**
   `.meta` `.unity` `.prefab` `.asset` `.mixer` `.controller` `.anim` `.mat` `.mask` `.preset` and friends are
   object graphs, not text: they carry GUIDs and cross-document `fileID` pointers. A wrong byte is **not** a
   compile error — the native importer follows a dangling pointer and **crashes the editor on the next
   refresh**, before you ever press Play. This has already happened here: a hand-transcribed `.mixer` was
   written with 7 of its 8 YAML documents while the SFX group still referenced the missing one, and Unity
   died in `AudioMixerController::RemoveInvalidSendLevelGuidsRecursive`. Never hand-write, transcribe, or
   copy one between projects; never write a `.meta` or invent a GUID; never delete a `.meta`. Moving or
   renaming an asset means moving its `.meta` with it, or handing the move to the user.
   *A `PreToolUse` hook blocks these writes. If it fires, the answer is a generator, never a workaround.*
6. **You cannot click in the editor.** Every task ends with a **Wire in Unity** checklist:
   object → component → field → what to drop in, plus layers/tags/collision matrix/build-settings changes.
   `ProjectSettings/` is the user's — build list, tags, layers, physics matrix go in the checklist, whether
   you would have changed them by hand or from a generator script.

## Workflow

1. **Look first.** `Assets/Scripts` layout and namespaces, existing managers, prefabs, ScriptableObjects,
   event channels, and the input actions asset. Reuse what exists and match its style.
2. **Write** following [scripting.md](references/scripting.md), plus the topic file(s) below.
3. **Verify it compiles** before writing the checklist:
   `powershell -NoProfile -ExecutionPolicy Bypass -File .claude/hooks/unity-refresh.ps1 -Manual`
   (refreshes the editor, prints Console errors; exit 0 = clean). The Stop hook runs the same check when you
   finish and sends you back while the Console has errors.
   **If you added or changed an asset Unity must import, verifying is not optional and not the same check.**
   A bad asset kills the editor during import, so the Console never gets to report it. The editor also
   auto-refreshes whenever the user focuses its window — you do not get to choose when your file is read.
   Generate assets through a `[MenuItem]` and prove it in a headless run before the user's editor sees it:
   `& "C:\Program Files\Unity\Hub\Editor\<version>\Editor\Unity.exe" -batchmode -quit -nographics ``
   -projectPath "<project>" -executeMethod <Class>.<Method> -logFile <log>`
   then read the log for `Exception` / `error CS` and confirm the asset exists with the contents you expect.
   A batch run needs the editor closed; if it is open, hand the user the menu item instead of improvising.
4. **Finish** with the Wire in Unity checklist. Setup problems only the user can fix (unassigned field, missing
   layer, unbaked NavMesh) belong in the checklist, not in a fix loop.

## Topic files — read the matching one before writing

| When the task involves | Read |
|---|---|
| Spawning anything: enemies, projectiles, weapons, pickups, VFX, pooling, spawners, waves | [spawning.md](references/spawning.md) |
| Scene setup, managers/singletons, references between objects, prefab→scene refs, audio, cameras, scene loading | [scene-wiring.md](references/scene-wiring.md) |
| Data assets, event channels, runtime sets, spawn tables | [scriptable-objects.md](references/scriptable-objects.md) |
| Class layout, lifecycle, coroutines/async, events, interfaces, state machines, Unity null gotchas | [scripting.md](references/scripting.md) |
| Player movement, Rigidbody, colliders/triggers, raycasts, layers, NavMesh AI | [physics.md](references/physics.md) |
| Reading input, action maps, mouse look, cursor lock, UI input | [input.md](references/input.md) |
| HUD, menus, interaction prompts, TextMeshPro, UI Toolkit, dynamic lists | [ui.md](references/ui.md) |
| Editor scripts, menu items, custom inspectors, gizmos, folders, asmdefs, editing `.unity`/`.prefab`/`.asset` text | [editor-and-assets.md](references/editor-and-assets.md) |
| Frame time, allocations, physics query cost, URP rendering cost, profiling | [performance.md](references/performance.md) |
| The refresh hook or bridge misbehaves, reading `result.json`, `Editor.log` | [bridge.md](references/bridge.md) |
