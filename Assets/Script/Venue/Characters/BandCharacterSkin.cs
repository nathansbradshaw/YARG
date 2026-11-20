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
        private HumanPose _pose;
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
            _sourcePose.GetHumanPose(ref _pose);
            _targetPose.SetHumanPose(ref _pose);
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
