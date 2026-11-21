using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UniHumanoid;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using YARG.Venue;
using YARG.Venue.Characters;
using Object = UnityEngine.Object;

namespace Editor
{
    // Unity's avatar, prefab and AssetBundle APIs require an editor C# entry point.
    // The dependency-free Rust tool owns mesh conversion and the editable skinning recipe.
    public static class OsrsCharacterBuilder
    {
        private const string AssetFolder = "Assets/LocalCharacterBuild";
        private sealed class RigData { public BoneData[] bones; public MeshData[] meshes; }
        private sealed class BoneData { public string name; public int parent; public float[] position; }
        private sealed class MeshData
        {
            public string name, texture;
            public float[][] positions, normals, uvs, weights;
            public int[][] boneIndices;
            public int[] triangles;
        }

        public static void Build()
        {
            var input = Environment.GetEnvironmentVariable("YARG_OSRS_INPUT");
            var textures = Environment.GetEnvironmentVariable("YARG_OSRS_TEXTURES");
            var output = Environment.GetEnvironmentVariable("YARG_OSRS_OUTPUT");
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(textures) || string.IsNullOrEmpty(output))
                throw new ArgumentException("Set YARG_OSRS_INPUT, YARG_OSRS_TEXTURES, YARG_OSRS_OUTPUT");
            var data = JsonConvert.DeserializeObject<RigData>(File.ReadAllText(input));
            Directory.CreateDirectory(AssetFolder);
            Directory.CreateDirectory(output);
            AssetDatabase.Refresh();
            var root = new GameObject("OSRS performer");
            try
            {
                var bones = new Transform[data.bones.Length];
                for (int i = 0; i < bones.Length; i++)
                {
                    var bone = data.bones[i];
                    bones[i] = new GameObject(bone.name).transform;
                    bones[i].SetParent(bone.parent < 0 ? root.transform : bones[bone.parent], false);
                    bones[i].position = Vector(bone.position);
                }
                var humanoid = root.AddComponent<Humanoid>();
                humanoid.AssignBones(data.bones.Select((b, i) => ((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), b.name), bones[i])));
                var description = new HumanDescription
                {
                    human = data.bones.Select(b => new HumanBone { boneName = b.name, humanName = b.name, limit = new HumanLimit { useDefaultValues = true } }).ToArray(),
                    skeleton = root.GetComponentsInChildren<Transform>().Select(t => new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray(),
                    upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                    armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f, hasTranslationDoF = false
                };
                var avatar = AvatarBuilder.BuildHumanAvatar(root, description);
                avatar.name = "OSRS Humanoid";
                if (!avatar.isValid || !avatar.isHuman) throw new InvalidOperationException("Humanoid avatar validation failed");
                SaveAsset(avatar, AssetFolder + "/OSRS.avatar.asset");
                var animator = root.AddComponent<Animator>();
                animator.avatar = avatar;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.applyRootMotion = false;
                animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("Animations/Vocals/Default/DefaultController");

                int meshIndex = 0;
                foreach (var source in data.meshes)
                {
                    var mesh = new Mesh { name = source.name };
                    mesh.vertices = source.positions.Select(Vector).ToArray();
                    mesh.normals = source.normals.Select(v => Vector(v).normalized).ToArray();
                    mesh.uv = source.uvs.Select(v => new Vector2(v[0], v[1])).ToArray();
                    mesh.triangles = source.triangles;
                    mesh.bindposes = bones.Select(b => b.worldToLocalMatrix * root.transform.localToWorldMatrix).ToArray();
                    mesh.boneWeights = source.weights.Select((w, i) =>
                    {
                        if (Math.Abs(w.Sum() - 1f) > 0.001f || w.Any(x => x < 0 || !float.IsFinite(x)))
                            throw new InvalidOperationException("Invalid skin weight");
                        var indices = source.boneIndices[i];
                        return new BoneWeight { boneIndex0 = indices[0], boneIndex1 = indices[1], boneIndex2 = indices[2], boneIndex3 = indices[3], weight0 = w[0], weight1 = w[1], weight2 = w[2], weight3 = w[3] };
                    }).ToArray();
                    mesh.RecalculateBounds();
                    SaveAsset(mesh, $"{AssetFolder}/mesh{meshIndex}.asset");
                    string textureName = Path.GetFileName(source.texture.Replace('\\','/'));
                    string texturePath = AssetFolder + "/" + textureName;
                    File.Copy(Path.Combine(textures, textureName), texturePath, true);
                    AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceUpdate);
                    var textureImporter = (TextureImporter) AssetImporter.GetAtPath(texturePath);
                    textureImporter.filterMode = FilterMode.Point;
                    textureImporter.textureCompression = TextureImporterCompression.Uncompressed;
                    textureImporter.SaveAndReimport();
                    // A flat palette preserves the game's look under legacy venues' very intense lights.
                    var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "OSRS palette" };
                    material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
                    material.SetFloat("_Cull", (float) CullMode.Off);
                    SaveAsset(material, $"{AssetFolder}/material{meshIndex}.mat");
                    var child = new GameObject(source.name);
                    child.transform.SetParent(root.transform, false);
                    var renderer = child.AddComponent<SkinnedMeshRenderer>();
                    renderer.sharedMesh = mesh;
                    renderer.sharedMaterial = material;
                    renderer.bones = bones;
                    renderer.rootBone = bones[0];
                    renderer.updateWhenOffscreen = true;
                    renderer.localBounds = new Bounds(new Vector3(0f, 0.9f, 0f), new Vector3(2.2f, 2.1f, 0.8f));
                    meshIndex++;
                }
                // Do not publish third-party game geometry: generated editable assets remain local.
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, AssetFolder + "/OSRS.prefab");
                AssetDatabase.SaveAssets();
                Render(root, Path.Combine(output, "osrs-tpose.png"));
                CheckSkin(root, bones);
                animator.Rebind();
                animator.Update(0f);
                var build = new AssetBundleBuild
                {
                    assetBundleName = "osrs-vocalist.yargchar",
                    assetNames = new[] {AssetFolder + "/OSRS.prefab"},
                    addressableNames = new[] {BundleBackgroundManager.CHARACTER_PREFAB_PATH.ToLowerInvariant()}
                };
                var manifest = BuildPipeline.BuildAssetBundles(output, new[] {build}, BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneOSX);
                if (manifest == null) throw new InvalidOperationException("Character bundle build failed");
                Debug.Log($"OSRS_RIG_PASS: {bones.Length} humanoid bones; valid Avatar; normalized weights; deforming skin; {prefab.name}");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static Vector3 Vector(float[] v) => new(v[0],v[1],v[2]);
        private static void SaveAsset(Object asset, string path)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);
        }

        private static void CheckSkin(GameObject root, Transform[] bones)
        {
            var renderer = root.GetComponentInChildren<SkinnedMeshRenderer>();
            var before = new Mesh(); var after = new Mesh();
            renderer.BakeMesh(before);
            var rotation = bones[8].localRotation;
            bones[8].localRotation *= Quaternion.Euler(0,0,55);
            renderer.BakeMesh(after);
            bones[8].localRotation = rotation;
            int moved = before.vertices.Zip(after.vertices, (a,b) => Vector3.Distance(a,b)).Count(d => d > 0.01f);
            Object.DestroyImmediate(before); Object.DestroyImmediate(after);
            if (moved < 10) throw new InvalidOperationException("Arm pose did not deform the skin");
            Debug.Log($"OSRS_SKIN_CHECK: {moved} vertices moved with elbow");
        }

        internal static void Render(GameObject root, string output)
        {
            var layers=root.GetComponentsInChildren<Transform>(true).Select(t=>(t,t.gameObject.layer)).ToArray();
            var cameraRoot = new GameObject("Rig preview camera");
            var lightRoot = new GameObject("Rig preview light");
            var rt = new RenderTexture(768,768,24);
            var image = new Texture2D(768,768,TextureFormat.RGB24,false);
            var previous = RenderTexture.active;
            try
            {
                var camera = cameraRoot.AddComponent<Camera>();
                foreach(var pair in layers)pair.t.gameObject.layer=31;
                camera.cullingMask=1<<31;
                camera.transform.position = new Vector3(2.4f,1.6f,4f);
                camera.transform.LookAt(new Vector3(0,0.9f,0));
                camera.orthographic = true; camera.orthographicSize = 1.15f;
                camera.backgroundColor = new Color(0.06f,0.08f,0.11f); camera.clearFlags = CameraClearFlags.SolidColor;
                camera.targetTexture = rt;
                var light = lightRoot.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.8f;
                light.transform.rotation = Quaternion.Euler(35,-25,0);
                RenderSettings.ambientLight = new Color(0.7f,0.7f,0.7f);
                camera.Render(); RenderTexture.active = rt;
                image.ReadPixels(new Rect(0,0,768,768),0,0); image.Apply();
                File.WriteAllBytes(output,image.EncodeToPNG());
            }
            finally
            {
                foreach(var pair in layers)pair.t.gameObject.layer=pair.layer;
                RenderTexture.active = previous;
                Object.DestroyImmediate(image); Object.DestroyImmediate(rt);
                Object.DestroyImmediate(cameraRoot); Object.DestroyImmediate(lightRoot);
            }
        }
    }
}
