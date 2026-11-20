using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Editor
{
    // Unity build lifecycle glue. The build implementation is Rust.
    public sealed class DanceBuild : IPreprocessBuildWithReport
    {
        public int callbackOrder => -100;
        public void OnPreprocessBuild(BuildReport report)
        {
            var platform = report.summary.platform switch
            {
                BuildTarget.StandaloneOSX => "macos",
                BuildTarget.StandaloneWindows64 => "windows",
                BuildTarget.StandaloneLinux64 => "linux",
                _ => throw new BuildFailedException("Dance native builds support desktop targets")
            };
            EnsureNative(platform);
        }

        [MenuItem("YARG/Build Dance Native Library")]
        public static void BuildNative() => EnsureNative(Application.platform == RuntimePlatform.OSXEditor ? "macos" :
            Application.platform == RuntimePlatform.WindowsEditor ? "windows" : "linux");

        private static void EnsureNative(string platform)
        {
            var project = Directory.GetParent(Application.dataPath)!.FullName;
            var name = platform == "macos" ? "libyarg_dance.dylib" : platform == "windows" ? "yarg_dance.dll" : "libyarg_dance.so";
            var binary = Path.Combine(project, "Assets", "Plugins", "Dance", name);
            var source = Path.Combine(project, "YARG.Core", "Native", "dance");
            var latest = Directory.EnumerateFiles(Path.Combine(source, "src"), "*.rs", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(Path.Combine(source, "vendor"), "*.rs", SearchOption.AllDirectories))
                .Concat(Directory.EnumerateFiles(Path.Combine(source, "vendor"), "Cargo.toml", SearchOption.AllDirectories))
                .Concat(new[] { Path.Combine(source, "Cargo.lock"), Path.Combine(source, "Cargo.toml"), Path.Combine(project, "tools", "build-dance.rs") })
                .Max(File.GetLastWriteTimeUtc);
            if (File.Exists(binary) && File.GetLastWriteTimeUtc(binary) >= latest) return;
            var cargoBin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cargo", "bin");
            var compiler = Path.Combine(cargoBin, Application.platform == RuntimePlatform.WindowsEditor ? "rustc.exe" : "rustc");
            var helper = Path.Combine(project, "Library", Application.platform == RuntimePlatform.WindowsEditor ? "build-dance.exe" : "build-dance");
            Run(compiler, Quote(Path.Combine(project, "tools", "build-dance.rs")) + " -o " + Quote(helper), project, cargoBin);
            Run(helper, platform, project, cargoBin);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
        private static void Run(string file, string args, string directory, string cargoBin)
        {
            var start = new ProcessStartInfo(file, args)
            {
                WorkingDirectory = directory, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
            };
            start.EnvironmentVariables["PATH"] = cargoBin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
            using var process = new Process { StartInfo = start };
            process.OutputDataReceived += (_, e) => { if (e.Data != null) Debug.Log(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) Debug.Log(e.Data); };
            process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine(); process.WaitForExit();
            if (process.ExitCode != 0) throw new BuildFailedException("Dance native build failed; see compiler output");
        }

        public static void BuildMac()
        {
            EnsureNative("macos");
            PlayerSettings.SetArchitecture(NamedBuildTarget.Standalone, 2);
            var output = Environment.GetEnvironmentVariable("YARG_DANCE_BUILD_OUTPUT")
                ?? Path.GetFullPath("Builds/YARG.app");
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = output, target = BuildTarget.StandaloneOSX,
                options = Environment.GetEnvironmentVariable("YARG_DANCE_ACCEPTANCE") == "1" ? BuildOptions.Development : BuildOptions.None,
                extraScriptingDefines = Environment.GetEnvironmentVariable("YARG_DANCE_ACCEPTANCE") == "1" ? new[] { "YARG_DANCE_CHECKS" } : Array.Empty<string>()
            };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException($"YARG build failed: {report.summary.result}");
            Debug.Log($"Dance build succeeded: {output}");
        }
    }
}
