// Editor-only bridge for the Claude Code Stop hook (.claude/hooks/unity-refresh.ps1).
// The hook drops Library/ClaudeBridge/refresh.request; this script refreshes the
// AssetDatabase, waits for compilation, then writes Library/ClaudeBridge/result.json
// with the Console's current errors. No runtime footprint (Editor folder only).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

[InitializeOnLoad]
static class ClaudeBridge
{
    static readonly string Dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Library", "ClaudeBridge");
    static readonly string RequestPath = Path.Combine(Dir, "refresh.request");
    static readonly string PendingPath = Path.Combine(Dir, "refresh.pending");
    static readonly string ResultPath = Path.Combine(Dir, "result.json");

    const double PollInterval = 0.25;   // seconds between checks
    const double CompileGrace = 1.5;    // seconds to wait for a compile to start after Refresh()
    const int MaxErrors = 40;           // entries written to result.json

    // Bits of UnityEditor.LogEntry.mode that mean "error" (ConsoleWindow.Mode).
    const int ErrorMask = 1 << 0 | 1 << 1 | 1 << 4 | 1 << 6 | 1 << 8 | 1 << 11 | 1 << 13 | 1 << 17 | 1 << 20 | 1 << 21 | 1 << 22;
    const int CompileErrorBit = 1 << 11;

    static readonly List<string> compileErrors = new List<string>(); // survives failed compiles (no domain reload)
    static double nextPoll;

    [Serializable]
    class Result
    {
        public bool ok;
        public bool refreshed;
        public bool recompiled;
        public bool playing;
        public int errorCount;
        public int warningCount;
        public string[] errors;
        public string note;
    }

    static ClaudeBridge()
    {
        Directory.CreateDirectory(Dir);
        CompilationPipeline.compilationStarted += _ => { compileErrors.Clear(); SessionState.SetBool("ClaudeBridge.recompiled", true); };
        CompilationPipeline.assemblyCompilationFinished += (asm, msgs) =>
        {
            foreach (var m in msgs)
                if (m.type == CompilerMessageType.Error) compileErrors.Add($"{m.file}({m.line},{m.column}): error: {m.message}");
        };
        EditorApplication.update += Poll;
    }

    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + PollInterval;

        try
        {
            if (File.Exists(RequestPath))
            {
                File.Delete(RequestPath);
                if (File.Exists(ResultPath)) File.Delete(ResultPath);

                if (EditorApplication.isPlaying)
                {
                    // Don't recompile under a running play session; just report what the Console shows.
                    WriteResult(refreshed: false, note: "Editor is in Play Mode; refresh skipped, errors below are from the running session.");
                    return;
                }

                SessionState.SetBool("ClaudeBridge.recompiled", false);
                File.WriteAllText(PendingPath, DateTime.UtcNow.ToString("o"));
                AssetDatabase.Refresh();
                return;
            }

            if (!File.Exists(PendingPath)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(PendingPath)).TotalSeconds < CompileGrace) return;

            WriteResult(refreshed: true, note: null);   // result first: if this throws, pending stays and we retry next poll
            File.Delete(PendingPath);
        }
        catch (IOException) { /* file still being written by the hook; retry next poll */ }
    }

    static void WriteResult(bool refreshed, string note)
    {
        var r = new Result
        {
            refreshed = refreshed,
            recompiled = SessionState.GetBool("ClaudeBridge.recompiled", false),
            playing = EditorApplication.isPlaying,
            note = note,
        };

        var errors = new List<string>();
        try
        {
            ReadConsole(errors, out r.errorCount, out r.warningCount);
        }
        catch (Exception e)
        {
            // Internal console API changed; fall back to the compiler's own messages.
            errors.AddRange(compileErrors);
            r.errorCount = errors.Count;
            r.note = (r.note == null ? "" : r.note + " ") + "Console read failed (" + e.GetType().Name + "); listing compiler errors only.";
        }

        if (errors.Count == 0 && compileErrors.Count > 0) { errors.AddRange(compileErrors); r.errorCount = Math.Max(r.errorCount, errors.Count); }
        if (errors.Count > MaxErrors) { errors = errors.Take(MaxErrors).ToList(); errors.Add($"... {r.errorCount - MaxErrors} more"); }

        r.errors = errors.ToArray();
        r.ok = r.errorCount == 0;

        var tmp = ResultPath + ".tmp";
        File.WriteAllText(tmp, JsonUtility.ToJson(r, true));
        if (File.Exists(ResultPath)) File.Delete(ResultPath);
        File.Move(tmp, ResultPath);
    }

    // Reads the Console window's entries via UnityEditor.LogEntries (internal but stable since 2017).
    static void ReadConsole(List<string> errors, out int errorCount, out int warningCount)
    {
        var asm = typeof(EditorWindow).Assembly;
        var logEntries = asm.GetType("UnityEditor.LogEntries");
        var logEntry = asm.GetType("UnityEditor.LogEntry");
        var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        var counts = new object[] { 0, 0, 0 };
        logEntries.GetMethod("GetCountsByType", flags).Invoke(null, counts);
        errorCount = (int)counts[0];
        warningCount = (int)counts[1];

        var modeField = logEntry.GetField("mode");
        var messageField = logEntry.GetField("message");
        var getEntry = logEntries.GetMethod("GetEntryInternal", flags);
        var entry = Activator.CreateInstance(logEntry);
        var seen = new Dictionary<string, int>();
        var order = new List<string>();

        int rows = (int)logEntries.GetMethod("StartGettingEntries", flags).Invoke(null, null);
        try
        {
            for (int i = 0; i < rows; i++)
            {
                getEntry.Invoke(null, new object[] { i, entry });
                int mode = (int)modeField.GetValue(entry);
                if ((mode & ErrorMask) == 0) continue;
                string msg = Trim((string)messageField.GetValue(entry), (mode & CompileErrorBit) != 0 ? 1 : 4);
                if (seen.ContainsKey(msg)) seen[msg]++; else { seen[msg] = 1; order.Add(msg); }
            }
        }
        finally { logEntries.GetMethod("EndGettingEntries", flags).Invoke(null, null); }

        foreach (var msg in order) errors.Add(seen[msg] > 1 ? $"{msg}  (x{seen[msg]})" : msg);
        int hidden = errorCount - seen.Values.Sum();
        if (hidden > 0) errors.Add($"({hidden} error(s) hidden by the Console window's filter toggles)");
    }

    static string Trim(string message, int maxLines)
    {
        var lines = message.Replace("\r", "").Split('\n').Where(l => l.Trim().Length > 0).Take(maxLines);
        var s = string.Join("\n    ", lines);
        return s.Length > 700 ? s.Substring(0, 700) + "..." : s;
    }
}
