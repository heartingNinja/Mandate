// WebBuild — Mandate > Build Web (WebGL): builds the game for a browser and zips it ready for itch.io.
//
// Output, next to Assets (ignored by git):
//   Builds/WebGL/            the site: open index.html through a web server (not by double-clicking it)
//   Builds/Mandate-web.zip   the same files zipped with index.html at the top, which is what itch.io wants
//   Builds/web-build.txt     how the last build went (time, size, errors), also printed to the Console
//
// Settings it sets every time, so any host works without special server setup:
//   Gzip compression with decompression fallback   (the page unpacks the files itself if the host does not say
//                                                    they are compressed; itch.io and a plain local server both work)
//   1280 x 720 canvas, keep running in the background (AI seats take minutes; a tab you look away from keeps going)
//   the Mandate page template (Assets/WebGLTemplates/Mandate): fills the window, Mandate icon, saves files
//
// Players bring their own API keys in the browser (Docs/Agents.md, "Playing in a browser"). Never put a key of
// your own in a seat before building: it would ship inside the build files for anyone to read.
//
// The build switches the editor to WebGL. Switch back in File > Build Profiles for desktop builds; Play mode in
// the editor works the same either way.

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Mandate.EditorTools
{
    public static class WebBuild
    {
        const string OutputDir = "Builds/WebGL";
        const string ZipPath = "Builds/Mandate-web.zip";
        const string ReportPath = "Builds/web-build.txt";

        [MenuItem("Mandate/Build Web (WebGL)", priority = 60)]
        public static void Build()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0) { Debug.LogError("Mandate web build: no scenes in the build list (File > Build Profiles)."); return; }
            // The build uses the saved scene files, so offer to save first (Cancel stops the build).
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (KeysInScenes(scenes)) return;
            // Git ignores the relay settings, so a fresh clone builds without them. Still builds; Send only saves.
            if (!File.Exists("Assets/Resources/MandateResultsRelay.json"))
                Debug.LogWarning("Mandate web build: no Assets/Resources/MandateResultsRelay.json, so 'Send results...' will only save "
                               + "reports on the player's machine.");

            PlayerSettings.WebGL.template = "PROJECT:Mandate";   // Assets/WebGLTemplates/Mandate: full-window page, Mandate icon
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.defaultWebScreenWidth = 1280;
            PlayerSettings.defaultWebScreenHeight = 720;
            PlayerSettings.runInBackground = true;

            Directory.CreateDirectory("Builds");
            var started = DateTime.Now;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutputDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            });
            var summary = report.summary;
            var minutes = (DateTime.Now - started).TotalMinutes;

            string text;
            if (summary.result == BuildResult.Succeeded)
            {
                if (File.Exists(ZipPath)) File.Delete(ZipPath);
                ZipFile.CreateFromDirectory(OutputDir, ZipPath);
                text = $"Succeeded {DateTime.Now:yyyy-MM-dd HH:mm} in {minutes:0.0} min. Size {summary.totalSize / 1048576.0:0.0} MB. "
                     + $"Folder {Path.GetFullPath(OutputDir)}  Zip {Path.GetFullPath(ZipPath)} ({new FileInfo(ZipPath).Length / 1048576.0:0.0} MB)";
                Debug.Log("Mandate web build: " + text);
            }
            else
            {
                var errors = report.steps.SelectMany(s => s.messages).Where(m => m.type == LogType.Error || m.type == LogType.Exception)
                                   .Select(m => "  " + m.content).Take(20);
                text = $"{summary.result} {DateTime.Now:yyyy-MM-dd HH:mm} after {minutes:0.0} min, {summary.totalErrors} errors:\n" + string.Join("\n", errors);
                Debug.LogError("Mandate web build: " + text);
            }
            File.WriteAllText(ReportPath, text);
        }

        // Refuses to build if a seat in any build scene has an API key typed into its apiKey field: it would ship in
        // the build. Reads the saved scene files, so it also covers scenes that are not open (save first).
        static bool KeysInScenes(string[] scenes)
        {
            foreach (var path in scenes)
            {
                var keyed = File.ReadAllLines(path).Select(l => l.Trim())
                                .Where(l => l.StartsWith("apiKey:") && l.Substring("apiKey:".Length).Trim().Trim('\'', '"').Length > 0);
                if (!keyed.Any()) continue;
                Debug.LogError($"Mandate web build stopped: a seat in {path} has an API key in its apiKey field. "
                             + "Clear it first; players paste their own keys on the Table screen.");
                return true;
            }
            return false;
        }
    }
}
