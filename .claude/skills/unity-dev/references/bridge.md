# Editor bridge and the Stop hook

## What happens when you stop

`.claude/hooks/unity-refresh.ps1` (registered as a `Stop` hook in `.claude/settings.json`):

1. Checks the editor is open for this project (`Temp/UnityLockfile` + a `Unity.exe` window).
2. Writes `Library/ClaudeBridge/refresh.request`.
3. `Assets/Editor/ClaudeBridge.cs` (an `[InitializeOnLoad]` editor script polling every 0.25 s) deletes the
   request, calls `AssetDatabase.Refresh()`, waits until compilation/import is idle, reads the Console
   window (errors + counts, deduplicated), and writes `Library/ClaudeBridge/result.json`.
4. The hook prints the summary. If the Console has errors it **blocks the stop** (exit 2) and you get the
   errors as feedback — fix them and finish again. The same unchanged error set is retried at most 3 times
   per session; after that the stop goes through and the user is shown the remaining errors.

If the editor is in Play Mode the bridge does not refresh (that would recompile under the user's play
session); it reports the Console as-is with `playing: true`.

## Run it yourself mid-task

```
powershell -NoProfile -ExecutionPolicy Bypass -File .claude/hooks/unity-refresh.ps1 -Manual
```
Exit 0 = clean · 1 = errors listed · 3 = editor not running / bridge did not answer.
Do this after writing scripts and before writing the Wire in Unity checklist.

## Reading the output

- `Assets/Scripts/X.cs(12,5): error CS0246: ...` — compile error; fix it.
- `NullReferenceException` / `UnassignedReferenceException` / `MissingReferenceException` with a stack into
  your script — usually a field the user hasn't wired yet or an object destroyed while still referenced.
  Setup issues go into the Wire in Unity checklist; real bugs get fixed.
- `(x37)` — the same error repeated; typical of an `Update` throwing every frame.
- `recompiled=false` with errors — the errors predate your change (stale runtime errors or an old compile
  failure); read them, don't loop on them.
- `(N error(s) hidden by the Console window's filter toggles)` — the user has the error filter off.

## Troubleshooting

| Message | Meaning / fix |
|---|---|
| `Unity editor is not running for this project` | Nothing to refresh. Tell the user to check the Console when they open the project. |
| `bridge did not answer within 150s` | The bridge isn't loaded: first run before `Assets/Editor/ClaudeBridge.cs` was imported, an Editor-assembly compile error, or a modal dialog in Unity. The hook already tried to focus the Unity window so Auto Refresh imports the script; ask the user to click into Unity / press Ctrl+R once. |
| Bridge itself won't compile | Read `%LOCALAPPDATA%\Unity\Editor\Editor.log` (`grep -n "error CS" ...`) — the Console reader can't run when the Editor assembly is broken. |
| Errors reported but nothing changed | Runtime errors from the user's last play session — see "Reading the output". |
| Hook blocks 3× on the same errors | Cap reached; the user sees the list. Ask them for the missing setup or a repro. |
| `Editor.log` ends with `Crash!!!` + a native stack trace | Unity itself crashed (Temp/UnityLockfile goes stale; the hook then reports "editor is not running"). Look at the last `Start importing ...` line before the crash — a malformed hand-written asset (`.mixer`, `.anim`, `.controller`, `.prefab`) is the usual cause. Fix or delete that file before the user reopens the project, or it crashes again on startup import. |

Files: `Library/ClaudeBridge/{refresh.request, refresh.pending, result.json, hook-state.json}` — all
gitignored, safe to delete. Disable the hook by removing the `Stop` entry from `.claude/settings.json`.
