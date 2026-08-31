# PreToolUse guard. Blocks Claude from authoring the Unity assets that an importer parses as an
# object graph rather than as text. Getting one byte wrong in these does not surface as a compile
# error - the native importer follows a dangling reference and takes the whole editor down.
#
# This exists because of a real crash: a hand-transcribed .mixer was written with 7 of its 8 YAML
# documents, the SFX group still pointed at the missing one, and Unity segfaulted in
# AudioMixerController::RemoveInvalidSendLevelGuidsRecursive on the next AssetDatabase.Refresh.
#
# The correct route for every format listed here is a one-off [MenuItem] generator that builds the
# asset through Unity's own API, or handing it to the user. See references/editor-and-assets.md.
#
# Wired as PreToolUse on Write|Edit|Bash. Exit 2 blocks the call and feeds stderr back to Claude.

$ErrorActionPreference = 'Stop'

try { $payload = [Console]::In.ReadToEnd() | ConvertFrom-Json } catch { exit 0 }
if ($null -eq $payload) { exit 0 }

$tool = $payload.tool_name
$toolInput = $payload.tool_input
if ($null -eq $toolInput) { exit 0 }

# Graph-structured assets: cross-document fileID references, effect chains, prefab overrides.
$graph = 'unity|prefab|asset|mixer|controller|overrideController|anim|mat|mask|playable|signal|' +
         'physicMaterial|physicsMaterial2D|spriteatlas|terrainlayer|renderTexture|cubemap|' +
         'guiskin|fontsettings|preset|lighting|shadervariants'

# Targeted value edits inside these are sanctioned by the skill; whole-file authoring is not.
$editable = @('.unity', '.prefab', '.asset')

function Deny([string]$reason, [string]$target, [string]$route) {
    [Console]::Error.WriteLine("BLOCKED by guard-unity-assets: $reason")
    [Console]::Error.WriteLine("  target: $target")
    [Console]::Error.WriteLine("  do this instead: $route")
    [Console]::Error.WriteLine("  see .claude/skills/unity-dev/references/editor-and-assets.md")
    exit 2
}

$generatorRoute = 'write a one-off [MenuItem] in Assets/Editor that builds it through Unity''s API, ' +
                  'then run it (Unity.exe -batchmode -quit -executeMethod ...) or ask the user to click it'
$metaRoute = 'let Unity generate it - the Stop hook refreshes the AssetDatabase and it appears'

if ($tool -eq 'Write' -or $tool -eq 'Edit') {
    $path = [string]$toolInput.file_path
    if ([string]::IsNullOrWhiteSpace($path)) { exit 0 }

    $ext = [System.IO.Path]::GetExtension($path).ToLowerInvariant()
    $normalized = $path -replace '\\', '/'

    if ($ext -eq '.meta') {
        Deny "$tool on a .meta file. GUIDs are Unity's to mint; a hand-written one collides or orphans references." $path $metaRoute
    }

    if ($ext -match "^\.($graph)$") {
        # An Edit is a surgical change to a file Unity already wrote, which the skill allows for
        # scene/prefab/ScriptableObject values. Everything else, and every Write, is authoring.
        if ($tool -eq 'Edit' -and $editable -contains $ext) { exit 0 }
        Deny "$tool on $ext - Unity parses this as an object graph, and a malformed one crashes the importer, not the compiler." $path $generatorRoute
    }

    if ($tool -eq 'Write' -and $normalized -match '(^|/)ProjectSettings/') {
        Deny 'Write to ProjectSettings. Build list, tags, layers and physics matrix are the user''s to change.' $path 'put it in the Wire in Unity checklist, or Edit a single trivial value'
    }

    exit 0
}

if ($tool -eq 'Bash') {
    $command = [string]$toolInput.command
    if ([string]::IsNullOrWhiteSpace($command)) { exit 0 }

    # Reads are fine and the skill recommends some of them (grep guid Foo.cs.meta). Only the
    # constructs that put bytes on disk are worth blocking.
    $redirect = ">>?\s*[`"']?[^`"'\s|&;]*\.($graph|meta)\b"
    $writers = "\b(tee|install|truncate)\b[^;|&]*\.($graph|meta)\b"
    $sedInPlace = "sed\s+(-[^\s]+\s+)*-i[^;|&]*\.($graph|meta)\b"
    $copyOver = "\bcp\b[^;|&]*\.($graph)\b"
    $removeMeta = "\brm\b[^;|&]*\.meta\b"

    if ($command -match $redirect -or $command -match $writers -or $command -match $sedInPlace) {
        Deny 'Bash write to a Unity-imported asset. Redirecting into one bypasses Write/Edit but lands the same malformed bytes.' $command $generatorRoute
    }

    if ($command -match $copyOver) {
        Deny 'Bash copy of a Unity asset. Copying between projects carries internal GUIDs and stale snapshot data.' $command $generatorRoute
    }

    if ($command -match $removeMeta) {
        Deny 'Bash delete of a .meta file. Deleting one orphans every reference to that asset.' $command 'leave it alone; to move an asset, move its .meta with it (mv is allowed)'
    }

    exit 0
}

exit 0
