using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace YARG.Venue.Characters
{
    // Grip points are palm centres, not wrist joints. Fingerless skins can still
    // provide a useful centre from the vertices weighted to the hand bone.
    public sealed class BandPalm
    {
        public readonly Transform Hand;
        public readonly Vector3 LocalOffset;
        public Vector3 Position => Hand.TransformPoint(LocalOffset);

        public BandPalm(Animator animator, HumanBodyBones hand)
        {
            Hand = animator.GetBoneTransform(hand);
            var finger = animator.GetBoneTransform(hand == HumanBodyBones.LeftHand
                ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            if (finger != null)
            {
                LocalOffset = Hand.InverseTransformPoint(Vector3.Lerp(Hand.position, finger.position, 0.5f));
                return;
            }
            foreach (var renderer in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = renderer.sharedMesh;
                int bone = Array.IndexOf(renderer.bones, Hand);
                if (mesh == null || !mesh.isReadable || bone < 0) continue;
                var vertices = mesh.vertices;
                var weights = mesh.boneWeights;
                var bind = mesh.bindposes[bone];
                var bounds = new Bounds();
                int count = 0;
                for (int i = 0; i < weights.Length; i++)
                {
                    var w = weights[i];
                    float influence = (w.boneIndex0 == bone ? w.weight0 : 0) +
                        (w.boneIndex1 == bone ? w.weight1 : 0) +
                        (w.boneIndex2 == bone ? w.weight2 : 0) +
                        (w.boneIndex3 == bone ? w.weight3 : 0);
                    if (influence < 0.6f) continue;
                    var point = bind.MultiplyPoint3x4(vertices[i]);
                    if (count++ == 0) bounds = new Bounds(point, Vector3.zero);
                    else bounds.Encapsulate(point);
                }
                if (count == 0) continue;
                LocalOffset = bounds.center;
                return;
            }
        }
    }

    internal sealed class BandHandProps : IDisposable
    {
        private readonly List<GameObject> _objects = new();
        private readonly List<Material> _materials = new();
        private readonly BandPalm _left, _right;
        private readonly Transform _leftElbow, _rightElbow, _head;
        private Transform _leftStick, _rightStick, _mic;
        private Transform _nativeMic;
        private readonly Shader _propShader;

        public BandHandProps(Animator source, Animator target, Transform owner, List<Renderer> hidden)
        {
            // Reference the shader already loaded with the skin. Shader.Find can
            // miss bundle-only shaders in a stripped standalone player.
            _propShader=target.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMaterial.shader;
            _left = new BandPalm(target, HumanBodyBones.LeftHand);
            _right = new BandPalm(target, HumanBodyBones.RightHand);
            _leftElbow = target.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            _rightElbow = target.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _head = target.GetBoneTransform(HumanBodyBones.Head);
            var actor = source.GetComponent<VenueCharacter>();
            if (actor == null) return;
            if (actor.Type == VenueCharacter.CharacterType.Drums)
            {
                // These venues contain no stick meshes. Replace any named legacy
                // sticks as well, so the visible pair stays attached to the new palms.
                foreach (var r in source.GetComponentsInChildren<Renderer>(true))
                    if (!r.transform.IsChildOf(owner) && r.name.ToLowerInvariant().Contains("stick") && r.enabled)
                    { r.enabled = false; hidden.Add(r); }
                var wood = Material(new Color(0.7f, 0.44f, 0.19f));
                _leftStick = Root("Band drumstick left", owner);
                _rightStick = Root("Band drumstick right", owner);
                Primitive(_leftStick, "Stick", PrimitiveType.Cylinder, new Vector3(0, 0.11f, 0), new Vector3(0.014f, 0.165f, 0.014f), wood);
                Primitive(_rightStick, "Stick", PrimitiveType.Cylinder, new Vector3(0, 0.11f, 0), new Vector3(0.014f, 0.165f, 0.014f), wood);
            }
            if (actor.Type != VenueCharacter.CharacterType.Vocals) return;
            _mic = Root("Band microphone", owner);
            foreach (var r in source.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.transform.IsChildOf(owner) || !r.name.ToLowerInvariant().Contains("microphone")) continue;
                var filter = r.GetComponent<MeshFilter>();
                if (filter?.sharedMesh == null) continue;
                _nativeMic = r.transform;
                var mesh = filter.sharedMesh;
                var child = new GameObject("Microphone mesh");
                child.transform.SetParent(_mic, false);
                child.layer = owner.gameObject.layer;
                child.AddComponent<MeshFilter>().sharedMesh = mesh;
                child.AddComponent<MeshRenderer>().sharedMaterials = r.sharedMaterials;
                // Locate the lower handle, keeping the native mesh's orientation.
                var size = mesh.bounds.size;
                int axis = size.x > size.y ? (size.x > size.z ? 0 : 2) : (size.y > size.z ? 1 : 2);
                var grip = mesh.bounds.center;
                grip[axis] -= size[axis] * 0.25f;
                child.transform.localScale = r.transform.lossyScale;
                child.transform.localPosition = -Vector3.Scale(grip, child.transform.localScale);
                if (r.enabled) { r.enabled = false; hidden.Add(r); }
                return;
            }
            var dark = Material(new Color(0.08f, 0.09f, 0.1f));
            var grille = Material(new Color(0.45f, 0.47f, 0.5f));
            Primitive(_mic, "Handle", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.026f, 0.08f, 0.026f), dark);
            Primitive(_mic, "Grille", PrimitiveType.Sphere, new Vector3(0, 0.11f, 0), Vector3.one * 0.065f, grille);
        }

        private Transform Root(string name, Transform owner)
        {
            var obj = new GameObject(name);
            obj.layer = owner.gameObject.layer;
            obj.transform.SetParent(owner, false);
            var scale = owner.lossyScale;
            obj.transform.localScale = new Vector3(1 / scale.x, 1 / scale.y, 1 / scale.z);
            _objects.Add(obj);
            return obj.transform;
        }

        private Material Material(Color color)
        {
            var material = new Material(_propShader);
            if(material.HasProperty("_BaseColor"))material.SetColor("_BaseColor", color);
            if(material.HasProperty("_Color"))material.SetColor("_Color", color);
            _materials.Add(material);
            return material;
        }

        private static void Primitive(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            var obj = GameObject.CreatePrimitive(type);
            obj.name = name;
            obj.layer = parent.gameObject.layer;
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position;
            obj.transform.localScale = scale;
            var collider = obj.GetComponent<Collider>();
            collider.enabled = false;
            if (Application.isPlaying) Object.Destroy(collider); else Object.DestroyImmediate(collider);
            obj.GetComponent<Renderer>().sharedMaterial = material;
        }

        public void Update()
        {
            Stick(_leftStick, _left, _leftElbow);
            Stick(_rightStick, _right, _rightElbow);
            if (_mic == null) return;
            _mic.position = _right.Position;
            _mic.rotation = _nativeMic != null ? _nativeMic.rotation :
                Quaternion.FromToRotation(Vector3.up, (_head.position - _right.Position).normalized);
        }

        private static void Stick(Transform prop, BandPalm palm, Transform elbow)
        {
            if (prop == null) return;
            prop.position = palm.Position;
            prop.rotation = Quaternion.FromToRotation(Vector3.up, (palm.Hand.position - elbow.position).normalized);
        }

        public void Dispose()
        {
            foreach (var obj in _objects)
                if (obj != null) { if (Application.isPlaying) Object.Destroy(obj); else Object.DestroyImmediate(obj); }
            foreach (var material in _materials)
                if (material != null) { if (Application.isPlaying) Object.Destroy(material); else Object.DestroyImmediate(material); }
        }
    }
}
