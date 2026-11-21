using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YARG.Core.Logging;
using YARG.Helpers;
using YARG.Settings;
using YARG.Settings.Customization;

namespace YARG.Venue.Characters
{
    // Unity's humanoid APIs retain the authored rig, controllers, props and chart cues.
    [DefaultExecutionOrder(10000)]
    public sealed class BandCharacterSkin : MonoBehaviour
    {
        private HumanPoseHandler _sourcePose, _targetPose;
        private HumanPose _pose, _targetPoseSample;
        private Transform _sourceHips, _targetHips;
        private ArmGrip[] _grips;
        private readonly List<Renderer> _hidden = new();
        public static string Folder => Path.Combine(CustomContentManager.CustomizationDirectory, "characters", "band");

        public static async UniTask Load(GameObject venue, BundleBackgroundManager owner)
        {
            if (!SettingsManager.Settings.ShuffleCustomBand.Value || !Directory.Exists(Folder)) return;
            var models = new List<GameObject>();
            foreach (var file in Directory.GetFiles(Folder, "*.yargchar"))
            {
                var bundle = AssetBundle.LoadFromFile(file);
                if (bundle == null) continue;
                // The venue owns all loaded assets, even if a later model is invalid.
                owner.CharacterBundles.Add(bundle);
                var prefab = bundle.LoadAsset<GameObject>(BundleBackgroundManager.CHARACTER_PREFAB_PATH.ToLowerInvariant());
                var rig = prefab == null ? null : prefab.GetComponent<Animator>();
                if (rig?.avatar == null || !rig.avatar.isValid || !rig.avatar.isHuman) continue;
                var shaders = await BackgroundHelper.LoadMetalShaders(bundle, prefab, BackgroundHelper.ExportType.Character);
                if (shaders != null) owner.ShaderBundles.Add(shaders);
                models.Add(prefab);
            }
            if (models.Count == 0) return;
            // Shuffle without replacement, then cycle when fewer models than roles exist.
            for (int i = models.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (models[i], models[j]) = (models[j], models[i]);
            }
            int index = 0;
            foreach (var actor in venue.GetComponentsInChildren<VenueCharacter>())
            {
                if (actor.Type == VenueCharacter.CharacterType.Vocals &&
                    !string.IsNullOrEmpty(SettingsManager.Settings.CustomVocalsCharacter.Value)) continue;
                var rig = actor.GetComponent<Animator>();
                if (!actor.isActiveAndEnabled || rig?.avatar == null || !rig.isHuman || actor.GetComponent<BandCharacterSkin>() != null) continue;
                var model = models[index++ % models.Count];
                var body = Instantiate(model, actor.transform);
                try
                {
                    Attach(rig, body);
                    YargLogger.LogInfo($"Custom band skin: {actor.Type} = {model.name}");
                }
                catch (Exception exception)
                {
                    Destroy(body);
                    YargLogger.LogException(exception);
                }
            }
        }

        public static BandCharacterSkin Attach(Animator source, GameObject body)
        {
            var target = body.GetComponent<Animator>();
            if (target?.avatar == null || !target.avatar.isValid || !target.avatar.isHuman)
                throw new ArgumentException("Band skins need a valid humanoid Avatar");
            body.transform.localPosition = Vector3.zero;
            int venueLayer = LayerMask.NameToLayer("Venue");
            foreach (var child in body.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = venueLayer;

            body.transform.localRotation = Quaternion.identity;
            body.transform.localScale = Vector3.one;
            float Leg(Animator rig) =>
                Vector3.Distance(rig.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position, rig.GetBoneTransform(HumanBodyBones.LeftLowerLeg).position) +
                Vector3.Distance(rig.GetBoneTransform(HumanBodyBones.LeftLowerLeg).position, rig.GetBoneTransform(HumanBodyBones.LeftFoot).position);
            body.transform.localScale = Vector3.one * (Leg(source) / Leg(target));
            foreach (var behaviour in body.GetComponentsInChildren<Behaviour>(true)) behaviour.enabled = false;
            target.runtimeAnimatorController = null;
            source.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var follower = body.AddComponent<BandCharacterSkin>();
            try
            {
                follower._sourceHips=source.GetBoneTransform(HumanBodyBones.Hips);
                follower._targetHips=target.GetBoneTransform(HumanBodyBones.Hips);
                follower._grips=new[]
                {
                    new ArmGrip(source,target,HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand),
                    new ArmGrip(source,target,HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand)
                };
                follower._sourcePose = new HumanPoseHandler(source.avatar, source.transform);
                follower._targetPose = new HumanPoseHandler(target.avatar, target.transform);
                follower.CopyPose();
                foreach (var renderer in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (renderer.transform.IsChildOf(body.transform) || !renderer.enabled) continue;
                    renderer.enabled = false;
                    follower._hidden.Add(renderer);
                }
                // Instruments use their original authored renderers and bone constraints.
                foreach (var renderer in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    renderer.updateWhenOffscreen = true;
                return follower;
            }
            catch { DestroyImmediate(follower); throw; }
        }

        public void CopyPose()
        {
            transform.localPosition=Vector3.zero;
            transform.localRotation=Quaternion.identity;
            _sourcePose.GetHumanPose(ref _pose);
            _targetPose.SetHumanPose(ref _pose);
            _targetPose.GetHumanPose(ref _targetPoseSample);
            transform.rotation=(_pose.bodyRotation*Quaternion.Inverse(_targetPoseSample.bodyRotation))*transform.rotation;
            // Muscle-space body motion uses avatar scale, which need not match leg fit.
            // Anchor the imported pelvis to the authored actor instead of accumulating
            // a second root translation through the parented skin.
            transform.position+=_sourceHips.position-_targetHips.position;
            foreach(var grip in _grips)grip.Solve();
        }

        private sealed class ArmGrip
        {
            private readonly Transform _sourceHand,_upper,_lower,_hand;
            private Vector3 _lastBend;
            public ArmGrip(Animator source,Animator target,HumanBodyBones upper,HumanBodyBones lower,HumanBodyBones hand)
            {
                _sourceHand=source.GetBoneTransform(hand);
                _upper=target.GetBoneTransform(upper);_lower=target.GetBoneTransform(lower);_hand=target.GetBoneTransform(hand);
            }
            public void Solve()
            {
                if(_sourceHand==null||_upper==null||_lower==null||_hand==null)return;
                var shoulder=_upper.position;
                float a=Vector3.Distance(shoulder,_lower.position),b=Vector3.Distance(_lower.position,_hand.position);
                var delta=_sourceHand.position-shoulder;
                if(a<0.0001f||b<0.0001f||delta.sqrMagnitude<0.00000001f)return;
                var direction=delta.normalized;
                float distance=Mathf.Clamp(delta.magnitude,Mathf.Abs(a-b)+0.0001f,a+b-0.0001f);
                var bend=Vector3.ProjectOnPlane(_lower.position-shoulder,direction);
                if(bend.sqrMagnitude<0.000001f)bend=Vector3.ProjectOnPlane(_lastBend,direction);
                if(bend.sqrMagnitude<0.000001f)bend=Vector3.Cross(direction,Vector3.up);
                if(bend.sqrMagnitude<0.000001f)bend=Vector3.Cross(direction,Vector3.right);
                bend.Normalize();_lastBend=bend;
                float along=(a*a-b*b+distance*distance)/(2f*distance);
                var elbow=shoulder+direction*along+bend*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
                var wristRotation=_hand.rotation;
                _upper.rotation=Quaternion.FromToRotation(_lower.position-shoulder,elbow-shoulder)*_upper.rotation;
                _lower.rotation=Quaternion.FromToRotation(_hand.position-_lower.position,shoulder+direction*distance-_lower.position)*_lower.rotation;
                _hand.rotation=wristRotation;
            }
        }
        private void LateUpdate() => CopyPose();
        private void OnDestroy()
        {
            _sourcePose?.Dispose();
            _targetPose?.Dispose();
            foreach (var renderer in _hidden) if (renderer != null) renderer.enabled = true;
        }
    }
}
