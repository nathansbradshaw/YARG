using System;
using System.Linq;
using UnityEngine;
using YARG.Core.Dance;
using Object = UnityEngine.Object;

namespace YARG.Gameplay.Visuals
{
    // Unity mesh/material transport for YACE's Rust-generated vector noteskin.
    public sealed class DanceHighwaySkin : IDisposable
    {
        public readonly DanceSkinData Data = DanceNative.ReadSkin();
        public readonly Mesh Arrow, Receptor, Lift, Mine;
        public readonly Material Material;
        private readonly MaterialPropertyBlock _properties = new();
        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        private static readonly int GlowId = Shader.PropertyToID("_Glow");

        public DanceHighwaySkin()
        {
            Arrow = CreateMesh("YACE arrow", Data.Arrow);
            Receptor = CreateMesh("YACE receptor", Data.Receptor);
            Lift = CreateMesh("YACE lift", Data.Lift);
            Mine = CreateMesh("YACE mine", Data.Mine);
            Material = new Material(Resources.Load<Shader>("DanceArrows"));
        }
        private static Mesh CreateMesh(string name, DanceMeshData data)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = data.Vertices.Select(v => new Vector3(v[0], v[1], v[2])).ToArray();
            mesh.colors = data.Vertices.Select(v => new Color(v[3], v[4], v[5], v[6])).ToArray();
            mesh.uv = data.Vertices.Select(v => new Vector2(v[7], 0)).ToArray();
            mesh.triangles = data.Triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
        public MeshRenderer Create(Transform parent, string name, Mesh mesh, int layer)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }
        public void Paint(MeshRenderer renderer, Color color, float alpha = 1, float glow = 0)
        {
            // Send sRGB numerically; the shader performs the single conversion.
            _properties.SetVector(TintId, new Vector4(color.r, color.g, color.b, color.a));
            _properties.SetFloat(AlphaId, alpha);
            _properties.SetFloat(GlowId, glow);
            renderer.SetPropertyBlock(_properties);
        }
        public Quaternion Rotation(int lane) => Quaternion.Euler(0, Data.Rotations[lane], 0);
        public static Color NoteColor(double beat)
        {
            uint rgb = DanceNative.yarg_dance_color(beat);
            return new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
        }
        public void Dispose()
        {
            foreach (var mesh in new[] { Arrow, Receptor, Lift, Mine }) Object.Destroy(mesh);
            Object.Destroy(Material);
        }
    }
}
