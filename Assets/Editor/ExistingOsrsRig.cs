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
        private sealed class SourcePart {public int id,start,count;}
        private sealed class FitProfile {public bool female,weldSeams;public float rigScale=1,torsoBottom,torsoTop;public int vocalPropModel;public Dictionary<int,int> partGroups;public Dictionary<int,string> aliases,parents;}
        private sealed class SourceMesh {public float[][] positions, uvs; public int[] groups;public SourcePart[] parts;}
        public static void InspectParts()
        {
            foreach(var name in CharacterNames)
            {
                string input=Path.Combine(Environment.GetEnvironmentVariable("YARG_NPC_ROOT"),name);
                var data=JsonConvert.DeserializeObject<SourceMesh>(File.ReadAllText(Path.Combine(input,"source-mesh.json")));
                string output=Path.Combine(input,"parts");Directory.CreateDirectory(output);
                foreach(var part in data.parts)
                {
                    var root=new GameObject("NPC part "+part.id);var mesh=new Mesh();var material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                    try
                    {
                        mesh.vertices=data.positions.Skip(part.start).Take(part.count).Select(v=>new Vector3(v[0],v[1],v[2])).ToArray();
                        mesh.uv=data.uvs.Skip(part.start).Take(part.count).Select(v=>new Vector2(v[0],v[1])).ToArray();
                        mesh.triangles=Enumerable.Range(0,part.count).ToArray();mesh.RecalculateNormals();
                        material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/LocalCharacterBuild/ExistingRig/"+name+"/palette.tga"));material.SetFloat("_Cull",0);
                        root.AddComponent<MeshFilter>().sharedMesh=mesh;root.AddComponent<MeshRenderer>().sharedMaterial=material;
                        OsrsCharacterBuilder.Render(root,Path.Combine(output,part.id+".png"));
                    }
                    finally{UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(material);}
                }
            }
            var rig=AssetDatabase.LoadAssetAtPath<GameObject>(RigPath);
            foreach(var skeleton in rig.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("RuneScape_Skeleton_")))
                Debug.Log("OSRS_RIG_BONES "+skeleton.name+" "+string.Join(",",skeleton.GetComponentsInChildren<Transform>().Select(t=>t.name)));
        }
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
            var failures=new List<string>();
            foreach (var name in CharacterNames)
            {
                try{BuildOne(source,name);}
                catch(Exception error){failures.Add(name);Debug.LogError($"OSRS_ROSTER_REJECT {name}: {error}");}
            }
            if(failures.Count>0)throw new InvalidDataException("Rejected NPCs: "+string.Join(", ",failures));
        }

        public static string[] CharacterNames =>
            (Environment.GetEnvironmentVariable("YARG_NPC_NAMES") ?? "Hans-3105|Cook-4626|Father_Aereck-2812")
            .Split('|', StringSplitOptions.RemoveEmptyEntries);

        private static void BuildOne(string source,string name)
        {
            var input=Path.Combine(source,name);
            string profilePath="tools/npc-profiles/"+name+".json";
            var profile=File.Exists(profilePath)?JsonConvert.DeserializeObject<FitProfile>(File.ReadAllText(profilePath)):null;
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
                bool female=profile?.female??(Environment.GetEnvironmentVariable("YARG_NPC_FEMALE_NAMES")??"")
                    .Split('|',StringSplitOptions.RemoveEmptyEntries).Contains(name);
                var skeleton=temporary.GetComponentsInChildren<Transform>().Single(t=>t.name==
                    (female?"RuneScape_Skeleton_Female":"RuneScape_Skeleton_Male"));
                skeleton.SetParent(root.transform,true);
                UnityEngine.Object.DestroyImmediate(temporary);
                // Normalize FBX's centimetre scale while preserving every authored rest transform.
                var transforms=skeleton.GetComponentsInChildren<Transform>();
                float rigScale=profile?.rigScale??1;
                var positions=transforms.Select(t=>t.position*rigScale).ToArray();
                var rotations=transforms.Select(t=>t.rotation).ToArray();
                for(int i=0;i<transforms.Length;i++)
                {transforms[i].localScale=Vector3.one;transforms[i].SetPositionAndRotation(positions[i],rotations[i]);}
                var bones=transforms.ToDictionary(t=>t.name);
                if(profile?.parents!=null)foreach(var pair in profile.parents)bones["Bone_"+pair.Key].SetParent(bones[pair.Value],true);
                Debug.Log($"OSRS_REST_JOINTS {name} head={bones[HumanMap[HumanBodyBones.Head]].position} hips={bones[HumanMap[HumanBodyBones.Hips]].position} shoulder={bones[HumanMap[HumanBodyBones.LeftUpperArm]].position}");
                // The authored cape control is named Bone_15 in the supplied FBX.
                bones["Bone_15"].SetParent(bones["Chest"],true);
                // Wrist armour helpers are loose in the source FBX. Retain the
                // original controls and bind them to their existing forearm joint.
                foreach(int group in new[]{67,68})
                {
                    var points=data.positions.Where((p,i)=>data.groups[i]==group).ToArray();
                    if(points.Length>0&&bones.TryGetValue("Bone_"+group,out var helper))
                        helper.SetParent(bones[points.Average(p=>p[0])<0?"Arm_L.001":"Arm_R.001"],true);
                }
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
                string BoneName(int group)=>profile?.aliases!=null&&profile.aliases.TryGetValue(group,out var alias)?alias:group switch
                {
                    9=>HumanMap[HumanBodyBones.Neck],10=>HumanMap[HumanBodyBones.Chest],
                    11=>HumanMap[HumanBodyBones.Spine],12=>HumanMap[HumanBodyBones.Hips],15=>"Bone_15",
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
                    if(profile?.partGroups!=null&&data.parts!=null)
                    {
                        var part=data.parts.FirstOrDefault(p=>i>=p.start&&i<p.start+p.count);
                        if(part!=null&&profile.partGroups.TryGetValue(part.id,out int group))g=group;
                    }
                    var weight=new BoneWeight{boneIndex0=indices[g],weight0=1f};
                    // The original rigid belt/stomach seam stretches under Unity torso
                    // curves. Blend only this torso band across existing anatomical joints.
                    // Smooth torso/garment groups only. Armour and other helpers
                    // can sit at waist height without belonging to the torso.
                    if(g!=0&&g!=4&&g!=5&&g!=6&&g!=7&&g!=8&&g!=29&&g!=30&&g!=39&&g!=41&&!(g>=76&&g<=85))return weight;
                    float y=data.positions[i][1];
                    float bottom=profile?.torsoBottom>0?profile.torsoBottom:bones["Spine1"].position.y;
                    if(g>=76&&g<=85&&y<bottom)return weight;
                    if(g==0&&y<bottom)
                        return new BoneWeight{boneIndex0=indices[data.positions[i][0]<0?47:48],weight0=1f};
                    float top=profile?.torsoTop>bottom?profile.torsoTop:(bones["Arm_L.003"].position.y+bones["Arm_R.003"].position.y)*0.5f;
                    float middle=(bottom+top)*0.5f;
                    if(y>=top&&g!=4&&g!=5&&g!=6&&g!=7&&g!=8&&g!=29&&g!=30&&g!=39&&g!=41)return weight;
                    int lower=y<middle?39:4,upper=lower==39?4:8;
                    if(!indices.ContainsKey(lower)||!indices.ContainsKey(upper))return weight;
                    float t=Mathf.InverseLerp(lower==39?bottom:middle,lower==39?middle:top,y);
                    return new BoneWeight{boneIndex0=indices[lower],boneIndex1=indices[upper],weight0=1-t,weight1=t};
                }).ToArray();
                if(profile?.weldSeams==true)
                {
                    var weights=mesh.boneWeights;
                    foreach(var seam in mesh.vertices.Select((v,i)=>(v,i)).GroupBy(p=>p.v).Where(g=>g.Count()>1))
                    {
                        var influences=new Dictionary<int,float>();
                        foreach(var vertex in seam)
                        {
                            var w=weights[vertex.i];
                            foreach(var pair in new[]{(w.boneIndex0,w.weight0),(w.boneIndex1,w.weight1),(w.boneIndex2,w.weight2),(w.boneIndex3,w.weight3)})
                                if(pair.Item2>0)influences[pair.Item1]=influences.GetValueOrDefault(pair.Item1)+pair.Item2;
                        }
                        var top=influences.OrderByDescending(p=>p.Value).Take(4).ToArray();float total=top.Sum(p=>p.Value);
                        var merged=new BoneWeight();for(int j=0;j<top.Length;j++)
                        {
                            if(j==0){merged.boneIndex0=top[j].Key;merged.weight0=top[j].Value/total;}
                            if(j==1){merged.boneIndex1=top[j].Key;merged.weight1=top[j].Value/total;}
                            if(j==2){merged.boneIndex2=top[j].Key;merged.weight2=top[j].Value/total;}
                            if(j==3){merged.boneIndex3=top[j].Key;merged.weight3=top[j].Value/total;}
                        }
                        foreach(var vertex in seam)weights[vertex.i]=merged;
                    }
                    mesh.boneWeights=weights;
                }
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
                if(profile?.vocalPropModel>0)
                {
                    var propData=JsonConvert.DeserializeObject<SourceMesh>(File.ReadAllText(Path.Combine(input,"vocal-prop.json")));
                    var part=propData.parts.Single(p=>p.id==profile.vocalPropModel);
                    var vertices=propData.positions.Skip(part.start).Take(part.count).Select(v=>new Vector3(v[0],v[1],v[2])).ToArray();
                    var bounds=new Bounds(vertices[0],Vector3.zero);foreach(var v in vertices)bounds.Encapsulate(v);
                    var size=bounds.size;int axis=size.x>size.y?(size.x>size.z?0:2):(size.y>size.z?1:2);
                    var direction=Vector3.zero;direction[axis]=1;var rotation=Quaternion.FromToRotation(direction,Vector3.up);
                    var propMesh=new Mesh{name="Original baguette"};
                    propMesh.vertices=vertices.Select(v=>rotation*(v-bounds.center)*(0.4f/size[axis])+Vector3.up*0.12f).ToArray();
                    propMesh.uv=propData.uvs.Skip(part.start).Take(part.count).Select(v=>new Vector2(v[0],v[1])).ToArray();
                    propMesh.triangles=Enumerable.Range(0,part.count).ToArray();propMesh.RecalculateNormals();propMesh.RecalculateBounds();
                    Save(propMesh,folder+"/vocal-prop.asset");
                    var template=new GameObject("Vocal microphone template");template.transform.SetParent(root.transform,false);
                    template.AddComponent<MeshFilter>().sharedMesh=propMesh;template.AddComponent<MeshRenderer>().sharedMaterial=material;
                    template.SetActive(false);
                }
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
