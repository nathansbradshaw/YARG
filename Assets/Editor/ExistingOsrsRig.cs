using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using YARG.Venue;
using YARG.Venue.Characters;

namespace Editor
{
    public static class ExistingOsrsRig
    {
        private const string RigPath = "Assets/LocalCharacterBuild/ExistingRig/rig.fbx";
        private sealed class SourceMesh {public float[][] positions, uvs; public int[] groups;}
        // Original group names documented by OSRS Blender Character Creator's importer.
        private static readonly string[] GroupNames = {
            "Root","Head","Spine2","Spine3","Stomach","WaistBack","Shoulder_R","Shoulder_L","Chest","Neck","UpperBack","MidBack","LowerBack","Bone_13","Bone_14","Cape_Flow","Bone_16",
            "Arm_R.001","Arm_R.002","Arm_R","Forearm_R","Arm_R.003","Arm_L","Arm_L.001","Arm_L.002","Arm_L.003","Forearm_L","Hand_R","Hand_L","Spine1","WaistFront","Leg_R.001","Leg_R.002","Bone_33","Leg_R","Leg_L","Bone_36","Leg_L.001","Leg_L.002","Spine","Waist_L","Rear","Waist_R","Bone_43","Bone_44","Foot_R","Foot_L","Leg_L.003","Leg_R.003","Neck_front","Bone_50"
        };
        private static Dictionary<HumanBodyBones,string> HumanMap => new()
        {
            [HumanBodyBones.Hips]="Spine",[HumanBodyBones.Spine]="Stomach",[HumanBodyBones.Chest]="Chest",
            [HumanBodyBones.Neck]="Spine2",[HumanBodyBones.Head]="Head",
            // Match the authored band's negative-X left side, rather than reversing limbs.
            [HumanBodyBones.LeftUpperArm]="Arm_L.003",[HumanBodyBones.LeftLowerArm]="Arm_L.001",[HumanBodyBones.LeftHand]="Hand_L",
            [HumanBodyBones.RightUpperArm]="Arm_R.003",[HumanBodyBones.RightLowerArm]="Arm_R.001",[HumanBodyBones.RightHand]="Hand_R",
            [HumanBodyBones.LeftUpperLeg]="Waist_L",[HumanBodyBones.LeftLowerLeg]="Leg_L",[HumanBodyBones.LeftFoot]="Leg_L.003",[HumanBodyBones.LeftToes]="Foot_L",
            [HumanBodyBones.RightUpperLeg]="Waist_R",[HumanBodyBones.RightLowerLeg]="Leg_R",[HumanBodyBones.RightFoot]="Leg_R.003",[HumanBodyBones.RightToes]="Foot_R"
        };

        public static void Build()
        {
            var source=Environment.GetEnvironmentVariable("YARG_NPC_ROOT");
            if (string.IsNullOrEmpty(source)) throw new ArgumentException("Set YARG_NPC_ROOT to the local extracted NPC folder");
            foreach (var name in CharacterNames) BuildOne(source,name);
        }

        public static string[] CharacterNames =>
            (Environment.GetEnvironmentVariable("YARG_NPC_NAMES") ?? "Hans-3105|Cook-4626|Father_Aereck-2812")
            .Split('|', StringSplitOptions.RemoveEmptyEntries);

        private static void BuildOne(string source,string name)
        {
            var input=Path.Combine(source,name);
            var data=JsonConvert.DeserializeObject<SourceMesh>(File.ReadAllText(Path.Combine(input,"source-mesh.json")));
            if (data?.positions==null || data.groups==null || data.uvs==null || data.positions.Length==0 || data.positions.Length%3!=0 || data.groups.Length!=data.positions.Length || data.uvs.Length!=data.positions.Length)
                throw new InvalidDataException("Invalid grouped NPC mesh");
            if(data.positions.Any(v=>v==null||v.Length!=3||v.Any(x=>!float.IsFinite(x))) || data.uvs.Any(v=>v==null||v.Length!=2||v.Any(x=>!float.IsFinite(x))) || data.groups.Any(g=>g<0))
                throw new InvalidDataException("Invalid NPC coordinates or group IDs");
            string folder="Assets/LocalCharacterBuild/ExistingRig/"+name;
            Directory.CreateDirectory(folder);AssetDatabase.Refresh();
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(RigPath);
            if(asset==null)throw new FileNotFoundException("Export the supplied rig.blend to "+RigPath);
            var temporary=UnityEngine.Object.Instantiate(asset);
            var root=new GameObject(name);
            try
            {
                var skeleton=temporary.GetComponentsInChildren<Transform>().Single(t=>t.name=="RuneScape_Skeleton_Male");
                skeleton.SetParent(root.transform,true);
                UnityEngine.Object.DestroyImmediate(temporary);
                // Normalize FBX's centimetre scale while preserving every authored rest transform.
                var transforms=skeleton.GetComponentsInChildren<Transform>();
                var positions=transforms.Select(t=>t.position).ToArray();
                var rotations=transforms.Select(t=>t.rotation).ToArray();
                for(int i=0;i<transforms.Length;i++)
                {transforms[i].localScale=Vector3.one;transforms[i].SetPositionAndRotation(positions[i],rotations[i]);}
                var bones=transforms.ToDictionary(t=>t.name);
                // The supplied file calls these loose apron controls Bone_6/7, while its
                // importer labels them as shoulders. Keep their original weights/rest poses,
                // and make the apron follow the existing waist during humanoid playback.
                bones["Bone_6"].SetParent(bones["Stomach"],true);
                bones["Bone_7"].SetParent(bones["Spine"],true);
                // The source robe controls are loose siblings of the entire armature.
                // Attach them to existing legs/hips so the robe follows the singer's steps.
                foreach(var pair in new Dictionary<string,string>
                {
                    ["Bone_76"]="Leg_L.003",["Bone_77"]="Leg_R.003",["Bone_79"]="Spine",
                    ["Bone_80"]="Waist_R",["Bone_81"]="Waist_L",["Bone_82"]="Leg_L",["Bone_83"]="Leg_R",
                    ["Bone_84"]="Spine",["Bone_85"]="Spine"
                })bones[pair.Key].SetParent(bones[pair.Value],true);
                var groups=data.groups.Concat(new[]{39,4,8}).Distinct().OrderBy(g=>g).ToArray();
                var indices=groups.Select((g,i)=>(g,i)).ToDictionary(x=>x.g,x=>x.i);
                // These mesh-region labels have anatomical equivalents in the supplied rig.
                string BoneName(int group)=>group switch
                {
                    9=>HumanMap[HumanBodyBones.Neck],10=>HumanMap[HumanBodyBones.Chest],
                    11=>HumanMap[HumanBodyBones.Spine],12=>HumanMap[HumanBodyBones.Hips],
                    6 or 7=>$"Bone_{group}",
                    _=>group<GroupNames.Length?GroupNames[group]:$"Bone_{group:00}"
                };
                var skinBones=groups.Select(g=>bones.TryGetValue(BoneName(g),out var bone)?bone:throw new InvalidDataException("Existing rig lacks group "+g+" "+BoneName(g))).ToArray();
                var mesh=new Mesh{name=name+" original groups"};
                mesh.vertices=data.positions.Select(v=>new Vector3(v[0],v[1],v[2])).ToArray();
                mesh.uv=data.uvs.Select(v=>new Vector2(v[0],v[1])).ToArray();
                mesh.triangles=Enumerable.Range(0,data.positions.Length).ToArray();
                mesh.boneWeights=data.groups.Select((g,i)=>
                {
                    var weight=new BoneWeight{boneIndex0=indices[g],weight0=1f};
                    // The original rigid belt/stomach seam stretches under Unity torso
                    // curves. Blend only this torso band across existing anatomical joints.
                    if((g>=17&&g<=28)||(g>=31&&g<=38)||g==40||g==42||(g>=45&&g<=48))return weight;
                    float y=data.positions[i][1];
                    float bottom=bones["Spine1"].position.y;
                    if(g>=76&&g<=85&&y<bottom)return weight;
                    if(g==0&&y<bottom)
                        return new BoneWeight{boneIndex0=indices[data.positions[i][0]<0?47:48],weight0=1f};
                    float top=(bones["Arm_L.003"].position.y+bones["Arm_R.003"].position.y)*0.5f;
                    float middle=(bottom+top)*0.5f;
                    if(y>=top&&g!=4&&g!=5&&g!=6&&g!=7&&g!=8&&g!=29&&g!=30&&g!=39&&g!=41)return weight;
                    int lower=y<middle?39:4,upper=lower==39?4:8;
                    if(!indices.ContainsKey(lower)||!indices.ContainsKey(upper))return weight;
                    float t=Mathf.InverseLerp(lower==39?bottom:middle,lower==39?middle:top,y);
                    return new BoneWeight{boneIndex0=indices[lower],boneIndex1=indices[upper],weight0=1-t,weight1=t};
                }).ToArray();
                mesh.bindposes=skinBones.Select(t=>t.worldToLocalMatrix*root.transform.localToWorldMatrix).ToArray();
                mesh.RecalculateNormals();mesh.RecalculateBounds();
                Save(mesh,folder+"/body.asset");
                var texturePath=folder+"/palette.tga";
                File.Copy(Path.Combine(input,"palette.tga"),texturePath,true);AssetDatabase.ImportAsset(texturePath);
                var importer=(TextureImporter)AssetImporter.GetAtPath(texturePath);
                importer.filterMode=FilterMode.Point;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
                var material=new Material(Shader.Find("Universal Render Pipeline/Unlit")){name=name+" palette"};
                material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));material.SetFloat("_Cull",0);Save(material,folder+"/body.mat");
                var visual=new GameObject("Original NPC mesh");visual.transform.SetParent(root.transform,false);
                var renderer=visual.AddComponent<SkinnedMeshRenderer>();renderer.sharedMesh=mesh;renderer.sharedMaterial=material;
                renderer.bones=skinBones;renderer.rootBone=bones["Spine"];renderer.updateWhenOffscreen=true;
                var output=Path.Combine(input,"existing-rig");Directory.CreateDirectory(output);
                OsrsCharacterBuilder.Render(root,Path.Combine(output,"rest.png"));
                // Pose the existing armature into Unity's required calibration pose; preserve its weights and hierarchy.
                foreach(var side in new[]{"R","L"})
                {
                    var upper=bones[$"Arm_{side}.003"];var lower=bones[$"Arm_{side}.001"];var hand=bones[$"Hand_{side}"];
                    var direction=side=="R"?Vector3.right:Vector3.left;
                    upper.rotation=Quaternion.FromToRotation(lower.position-upper.position,direction)*upper.rotation;
                    lower.rotation=Quaternion.FromToRotation(hand.position-lower.position,direction)*lower.rotation;
                }
                var map=HumanMap;
                var description=new HumanDescription
                {
                    human=map.Select(p=>new HumanBone{humanName=p.Key.ToString(),boneName=p.Value,limit=new HumanLimit{useDefaultValues=true}}).ToArray(),
                    skeleton=root.GetComponentsInChildren<Transform>().Select(t=>new SkeletonBone{name=t.name,position=t.localPosition,rotation=t.localRotation,scale=t.localScale}).ToArray(),
                    upperArmTwist=0.5f,lowerArmTwist=0.5f,upperLegTwist=0.5f,lowerLegTwist=0.5f,armStretch=0.05f,legStretch=0.05f
                };
                var avatar=AvatarBuilder.BuildHumanAvatar(root,description);avatar.name=name+" existing rig humanoid";
                if (!avatar.isHuman || !avatar.isValid) throw new InvalidOperationException("Supplied skeleton did not make a valid humanoid for "+name);
                Save(avatar,folder+"/humanoid.asset");
                var animator=root.AddComponent<Animator>();animator.avatar=avatar;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                var humanoid=root.AddComponent<UniHumanoid.Humanoid>();humanoid.AssignBones(map.Select(p=>(p.Key,bones[p.Value])));
                OsrsCharacterBuilder.Render(root,Path.Combine(output,"tpose.png"));
                CheckArmCalibration(root,output);
                var prefabPath=folder+"/"+name+".prefab";PrefabUtility.SaveAsPrefabAsset(root,prefabPath);AssetDatabase.SaveAssets();
                var bundleName=name.ToLowerInvariant()+".yargchar";
                var manifest=BuildPipeline.BuildAssetBundles(output,new[]{new AssetBundleBuild{assetBundleName=bundleName,assetNames=new[]{prefabPath},addressableNames=new[]{BundleBackgroundManager.CHARACTER_PREFAB_PATH.ToLowerInvariant()}}},BuildAssetBundleOptions.ChunkBasedCompression,BuildTarget.StandaloneOSX);
                if(manifest==null)throw new InvalidOperationException("Existing rig bundle export failed");
                Environment.SetEnvironmentVariable("YARG_BAND_MODEL",Path.Combine(output,bundleName));
                BandSkinChecks.Run();
                Debug.Log($"EXISTING_OSRS_RIG_PASS {name}: valid humanoid; {skinBones.Length} original weighted bones; original groups with waist blend; four band roles");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);if(temporary!=null)UnityEngine.Object.DestroyImmediate(temporary);}
        }

        private static void Save(UnityEngine.Object asset,string path)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path)!=null)AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset,path);
        }
        private static void CheckArmCalibration(GameObject original,string output)
        {
            var root=UnityEngine.Object.Instantiate(original);
            try
            {
                original.SetActive(false);
                var animator=root.GetComponent<Animator>();
                using var handler=new HumanPoseHandler(animator.avatar,root.transform);
                var pose=new HumanPose
                {
                    bodyPosition=new Vector3(0,1,0),bodyRotation=Quaternion.identity,
                    muscles=new float[HumanTrait.MuscleCount]
                };
                handler.SetHumanPose(ref pose);
                foreach(var side in new[]{"Left","Right"})
                {
                    Vector3 Joint(string suffix)=>root.transform.InverseTransformPoint(
                        animator.GetBoneTransform(Enum.Parse<HumanBodyBones>(side+suffix)).position);
                    var shoulder=Joint("UpperArm");var elbow=Joint("LowerArm");var wrist=Joint("Hand");
                    // Same neutral muscle pose as the authored venue: left is -X,
                    // elbows are below shoulders, and wrists bend toward the +Z front.
                    if((side=="Left"?shoulder.x>=0:shoulder.x<=0) ||
                        elbow.y>=shoulder.y || wrist.z<=elbow.z+0.05f)
                        throw new InvalidOperationException($"Reversed limb calibration: {side}, shoulder={shoulder}, elbow={elbow}, wrist={wrist}");
                    Debug.Log($"OSRS_LIMB_PASS {original.name} {side}: shoulder={shoulder}, elbow={elbow}, wrist={wrist}");
                }
                OsrsCharacterBuilder.Render(root,Path.Combine(output,"neutral-pose.png"));
            }
            finally {UnityEngine.Object.DestroyImmediate(root);original.SetActive(true);}
        }
    }
}
