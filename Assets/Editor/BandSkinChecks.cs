using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YARG.Venue;
using YARG.Venue.Characters;
using Object = UnityEngine.Object;

namespace Editor
{
    public static class BandSkinChecks
    {
        public static void BuildAndRun() { OsrsCharacterBuilder.Build(); Run(); }

        public static void Run()
        {
            var bodyBundle = AssetBundle.LoadFromFile(Environment.GetEnvironmentVariable("YARG_BAND_MODEL"));
            var venueBundle = AssetBundle.LoadFromFile(Environment.GetEnvironmentVariable("YARG_BAND_VENUE"));
            GameObject stage = null;
            try
            {
                var model = bodyBundle.LoadAsset<GameObject>(BundleBackgroundManager.CHARACTER_PREFAB_PATH.ToLowerInvariant());
                stage = Object.Instantiate(venueBundle.LoadAsset<GameObject>(BundleBackgroundManager.BACKGROUND_PREFAB_PATH.ToLowerInvariant()));
                stage.transform.position += new Vector3(0, 0, 10000);

                var actors = stage.GetComponentsInChildren<VenueCharacter>();
                int count = 0;
                foreach (var actor in actors)
                {
                    var animator = actor.GetComponent<Animator>();
                    if (animator == null || !animator.isHuman) continue;
                    animator.Rebind(); animator.Update(0f);
                    var controller = animator.runtimeAnimatorController;
                    int props = actor.GetComponentsInChildren<MeshRenderer>().Count(r => r.enabled);
                    var skin = Object.Instantiate(model, actor.transform);
                    var follower = BandCharacterSkin.Attach(animator, skin);
                    var renderer = skin.GetComponentInChildren<SkinnedMeshRenderer>();
                    var before = new Mesh(); var after = new Mesh();
                    renderer.BakeMesh(before);
                    using (var poseHandler = new HumanPoseHandler(animator.avatar, animator.transform))
                    {
                        var pose = new HumanPose(); poseHandler.GetHumanPose(ref pose);
                        for (int i = 0; i < pose.muscles.Length; i++)
                            if (HumanTrait.MuscleName[i].Contains("Arm") || HumanTrait.MuscleName[i].Contains("Forearm"))
                                pose.muscles[i] = 0.35f;
                        poseHandler.SetHumanPose(ref pose);
                    }
                    follower.CopyPose(); renderer.BakeMesh(after);
                    int moved = before.vertices.Zip(after.vertices, (a,b) => Vector3.Distance(a,b)).Count(d => d > 0.01f);
                    if (moved < 10 || renderer.gameObject.layer != LayerMask.NameToLayer("Venue") || animator.runtimeAnimatorController != controller ||
                        actor.GetComponentsInChildren<MeshRenderer>().Count(r => r.enabled) != props ||
                        stage.GetComponentsInChildren<VenueCharacter>().Count(c => c.isActiveAndEnabled) != actors.Length ||
                        Vector3.Distance(renderer.bounds.center, animator.transform.position) > 4f)
                        throw new InvalidOperationException($"Skin transfer failed for {actor.Type}: {moved} moved, bounds={renderer.bounds}");
                    Debug.Log($"BAND_SKIN_ROLE_PASS {actor.Type}: {moved} vertices moved; authored controller, props and chart actor retained; bounds={renderer.bounds}");
                    Object.DestroyImmediate(before); Object.DestroyImmediate(after);
                    count++;
                }
                if (count < 4) throw new InvalidOperationException("Expected full band");
                Debug.Log("BAND_SKIN_PASS: four humanoid roles retargeted without replacing chart actors");
            }
            finally
            {
                if (stage != null) Object.DestroyImmediate(stage);
                if (bodyBundle != null) bodyBundle.Unload(true);
                if (venueBundle != null) venueBundle.Unload(true);
            }
        }
    }
}
