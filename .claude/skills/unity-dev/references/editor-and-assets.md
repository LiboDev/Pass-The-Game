# Editor tooling, assets, folders, YAML

## Folder layout (this project)

```
Assets/
  Audio/ Materials/ Models/ Prefabs/ Scenes/ Settings/ Textures/   template folders — use them
  Scripts/        runtime code, grouped by feature: Scripts/Player, Scripts/Enemies, Scripts/Interaction,
                  Scripts/UI, Scripts/Core (managers, event channels, shared interfaces)
  Data/           ScriptableObject assets (create when the first one appears)
  Editor/         editor-only code; excluded from builds (ClaudeBridge.cs lives here)
  InputSystem_Actions.inputactions
  TutorialInfo/   template readme — ignore, don't delete
```
Special folder names Unity treats differently: `Editor`, `Resources` (don't add one — serialized
references instead), `StreamingAssets`, `Plugins`, `Gizmos`, `Editor Default Resources`.
Assembly definitions: none yet. Add `Scripts/Game.asmdef` + `Editor/Game.Editor.asmdef` (referencing
`Game`) only when compile times hurt; the Editor asmdef needs `"includePlatforms": ["Editor"]`.

## .meta files and GUIDs

- Every file and folder under `Assets/` gets a `.meta` holding its GUID, created by Unity on refresh (the
  Stop hook triggers that). Never hand-write, copy or edit `.meta` files; never delete one; when moving or
  renaming an asset move its `.meta` with it — or hand the move to the user so references survive.
- Referencing a script from YAML needs its GUID: `grep guid Assets/Scripts/Player/PlayerHealth.cs.meta`.

## Editing `.unity` / `.prefab` / `.asset` text (Force Text serialization is on)

Fine for: a **targeted** change to a file Unity already wrote — a serialized value (`speed: 3` → `5`),
filling an empty reference when the GUID is known, adding one component block to a prefab.
Not fine: creating any of these files, overwriting one wholesale, authoring scenes or prefabs from scratch,
creating a ScriptableObject `.asset` by hand, editing `m_Modifications` of prefab instances, rewiring many
references. Those go to a `[MenuItem]` generator or to the user with a checklist.

The line is *edit an existing graph* vs *author a new one*. Reaching for Write on one of these files, or a
`cat >` heredoc, means you have crossed it — the guard hook will say so.

Rules when you do edit:
1. Ask the user to save (Ctrl+S) first — the editor's unsaved copy overwrites disk on save.
2. A new component block needs a unique `--- !u!114 &<fileID>` (random positive int64 not present in the
   file), `m_Script: {fileID: 11500000, guid: <script guid>, type: 3}`, and an entry in the owning
   GameObject's `m_Component` list (`- component: {fileID: <id>}`).
3. Field names are the C# field names; only Unity's own fields carry `m_`. Arrays are YAML lists; object
   references are `{fileID: 0}` (none) or `{fileID: <id>, guid: <asset guid>, type: 2|3}`.
4. Refresh afterwards (the hook does); expect the editor to reload the open scene.

## Editor scripts (`Assets/Editor/` or `#if UNITY_EDITOR`)

- **One-off asset/scene generation** — a `[MenuItem]` the user runs once, then delete the script:
  ```csharp
  [MenuItem("Tools/Horror/Create Default Enemy Stats")]
  static void Create()
  {
      var so = ScriptableObject.CreateInstance<EnemyStats>();
      AssetDatabase.CreateAsset(so, "Assets/Data/EnemyStats_Default.asset");
      AssetDatabase.SaveAssets();
      Selection.activeObject = so;
  }
  ```
  Editor-time placement (`new GameObject`, `PrefabUtility.SaveAsPrefabAsset(go, path)`,
  `Undo.RegisterCreatedObjectUndo`) is *authoring* and allowed; the runtime rules are about gameplay code.
- **Buttons/actions on a component**: `[ContextMenu("Snap To Ground")]` on the MonoBehaviour — zero editor
  code. Write a `[CustomEditor(typeof(T))]` only for real previews or complex layouts.
- **Gizmos** (`OnDrawGizmosSelected`) for ranges, spawn points, patrol paths, trigger volumes — cheap and
  the user will thank you.
- **`OnValidate`** for clamping and keeping derived fields in sync; no heavy work, no asset creation.
- Editing serialized data from editor code: `Undo.RecordObject(obj, "label")` before,
  `EditorUtility.SetDirty(obj)` after; scenes: `EditorSceneManager.MarkSceneDirty`.
- Editor-only components (`ScriptableWizard`, `EditorWindow`) go in `Assets/Editor/`.

## Which assets Claude may write — decide by format, never by guessing from a list

The question is never "is this asset type on some allowed list". It is **who parses the file**.

| | Formats | Why | Route |
|---|---|---|---|
| **Write directly** | `.cs`, `.inputactions`, `.uxml`/`.uss`, `.asmdef`, `.shader`/`.hlsl`, `.json`, `.txt`/`.csv` | A compiler or a schema reads it. A mistake is an error message. | Write/Edit as normal |
| **Never author** | `.meta`, `.unity`, `.prefab`, `.asset`, `.mixer`, `.controller`, `.overrideController`, `.anim`, `.mat`, `.mask`, `.playable`, `.signal`, `.physicMaterial`, `.spriteatlas`, `.terrainlayer`, `.renderTexture`, `.cubemap`, `.preset`, `.guiskin`, `.lighting` | A **native importer** reads it as an object graph of GUIDs and cross-document `fileID` pointers. A mistake is an **editor crash during import**. | one-off `[MenuItem]` generator, or the user |

An unlisted extension under `Assets/` is in the second row until proven otherwise. If Unity's own
*Create* menu makes it, Unity's own API should make it too.

**Never hand-write, transcribe, or copy one of these between projects.** Transcribing loses documents —
that is exactly the bug that crashed this project's editor: a `.mixer` written with 7 of its 8 YAML
documents, the SFX group still pointing at the missing `--- !u!244 &8723283981759385009`, and Unity
segfaulting in `AudioMixerController::RemoveInvalidSendLevelGuidsRecursive` inside
`NativeFormatImporter::EndImport` on the next auto-refresh. Copying carries the source project's internal
GUIDs and baked snapshot values with it.

A `PreToolUse` hook (`.claude/hooks/guard-unity-assets.ps1`) blocks these writes, including via Bash
redirection and `sed -i`. When it fires, write a generator — do not look for a way around it.

**A ScriptableObject `.asset` is not an exception.** Make it the way the generator example above does:
`ScriptableObject.CreateInstance<T>()` + `AssetDatabase.CreateAsset(...)`. That is the same amount of work
and cannot produce a malformed graph.

**A generator's blast radius is the asset it creates.** It must not write `ProjectSettings/` —
`EditorBuildSettings.scenes`, tags, layers, the physics matrix are the user's, and a script quietly
changing them is the same violation as editing the file by hand. Put them in the checklist instead.

## Project settings that matter

Tags & Layers, Physics collision matrix, Input System actions, URP assets in `Assets/Settings`,
Quality levels, Script Execution Order, Build Settings scene list. List required changes in the checklist;
only edit `ProjectSettings/*.asset` by hand for trivial, unambiguous values.
