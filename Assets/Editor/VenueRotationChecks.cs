using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using YARG.Venue;
using YARG.Venue.Characters;
using YARG.Gameplay;
using Object = UnityEngine.Object;

namespace Editor
{
    public static class VenueRotationChecks
    {
        public static void Run()
        {
            // Batch edit-mode checks do not execute PathHelper's runtime initializer.
            var persistentPath = typeof(YARG.Helpers.PathHelper).GetProperty("PersistentDataPath");
            var previousPath = persistentPath.GetValue(null);
            if (previousPath == null) persistentPath.SetValue(null, Application.temporaryCachePath);
            var take = typeof(VenueLoader).GetMethod("TakeNextGlobalVenue", BindingFlags.Static | BindingFlags.NonPublic);
            string Next(List<string> paths) => (string) take.Invoke(null, new object[] { paths });
            var state = UnityEngine.Random.state;
            try
            {
                var paths = new List<string> { "stage-a", "stage-b", "stage-c" };
                string last = null;
                for (int cycle = 0; cycle < 30; cycle++)
                {
                    var visited = new HashSet<string>();
                    for (int song = 0; song < paths.Count; song++)
                    {
                        var selected = Next(paths);
                        if (selected == last || !visited.Add(selected))
                            throw new InvalidOperationException("Repeated a venue before completing the rotation");
                        last = selected;
                    }
                }
                var changed = new List<string> { "new-stage", "another-stage" };
                for (int i = 0; i < 12; i++)
                {
                    var selected = Next(changed);
                    if (!changed.Contains(selected) || selected == last)
                        throw new InvalidOperationException("Changed venue pool retained a removed or repeated venue");
                    last = selected;
                }
                if (Next(new List<string> { "only-stage" }) != "only-stage" ||
                    Next(new List<string> { "only-stage" }) != "only-stage" ||
                    Next(new List<string>()) != null)
                    throw new InvalidOperationException("Empty/single venue fallback failed");
                Debug.Log("VENUE_ROTATION_PASS: 30 complete cycles, no adjacent repeats, changed pool, single and empty pools");
            }
            finally
            {
                UnityEngine.Random.state = state;
                persistentPath.SetValue(null, previousPath);
            }
            Inspect();
        }

        public static void Inspect()
        {
            var paths = Environment.GetEnvironmentVariable("YARG_VENUE_INSPECT") ?? "";
            foreach (var path in paths.Split(Path.PathSeparator).Where(File.Exists))
            {
                var bundle = AssetBundle.LoadFromFile(path);
                if (bundle == null) throw new InvalidOperationException("Cannot inspect " + path);
                try
                {
                    var prefab = bundle.LoadAsset<GameObject>(BundleBackgroundManager.BACKGROUND_PREFAB_PATH.ToLowerInvariant());
                    if (prefab == null) throw new InvalidOperationException("No venue prefab in " + path);
                    var cast = prefab.GetComponentsInChildren<VenueCharacter>(true);
                    Debug.Log($"VENUE_CAST {Path.GetFileName(path)}: {cast.Length} performers; " + string.Join("; ", cast.Select(c => $"{c.Type}={c.name} [{string.Join(",", c.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r => r.name))}]")));
                    foreach (var character in cast)
                    {
                        var animator = character.GetComponent<Animator>();
                        Debug.Log($"VENUE_RIG {character.Type}: avatarHuman={animator?.avatar?.isHuman}; humanoidClips={animator?.runtimeAnimatorController?.animationClips.Count(c => c.humanMotion)}; clips={animator?.runtimeAnimatorController?.animationClips.Length}");
                    }
                    if (Environment.GetEnvironmentVariable("YARG_VENUE_CAST_CHECK") == "1") CheckCast(prefab);
                }
                finally { bundle.Unload(true); }
            }
        }

        private static void CheckCast(GameObject prefab)
        {
            var method = typeof(BackgroundManager).GetMethod("ShuffleVenueVocalist", BindingFlags.Instance | BindingFlags.NonPublic);
            var helper = new GameObject("Cast check helper");
            var manager = helper.AddComponent<BackgroundManager>();
            var seen = new HashSet<string>();
            string last = null;
            try
            {
                for (int i = 0; i < 12; i++)
                {
                    var stage = Object.Instantiate(prefab);
                    try
                    {
                        var before = stage.GetComponentsInChildren<VenueCharacter>();
                        method.Invoke(manager, new object[] {stage});
                        var after = stage.GetComponentsInChildren<VenueCharacter>();
                        var vocalist = after.Single(c => c.Type == VenueCharacter.CharacterType.Vocals);
                        var animator = vocalist.GetComponent<Animator>();
                        var body = vocalist.GetComponentInChildren<SkinnedMeshRenderer>().name;
                        if (body == last || before.Length != after.Length || !animator.isHuman ||
                            animator.runtimeAnimatorController.animationClips.Any(c => !c.humanMotion))
                            throw new InvalidOperationException($"Cast check: body={body}, previous={last}, roles={before.Length}/{after.Length}, human={animator.isHuman}, human clips={animator.runtimeAnimatorController.animationClips.Count(c => c.humanMotion)}/{animator.runtimeAnimatorController.animationClips.Length}");
                        if (vocalist.GetType() == typeof(VenueCharacter)) vocalist.Initialize(null);
                        animator.Update(0f);
                        seen.Add(body);
                        last = body;
                    }
                    finally { Object.DestroyImmediate(stage); }
                }
                if (seen.Count < 2) throw new InvalidOperationException("No visible vocalist model change");
                Debug.Log($"VENUE_CAST_PASS: 12 casts without adjacent repeats; {seen.Count} distinct body models; humanoid controllers and band roles retained");
            }
            finally { Object.DestroyImmediate(helper); }
        }
    }
}
