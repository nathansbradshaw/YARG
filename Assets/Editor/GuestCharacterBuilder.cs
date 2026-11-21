using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using YARG.Venue;
using Newtonsoft.Json;
using Object = UnityEngine.Object;

namespace Editor
{
    // Unity's model, Avatar and AssetBundle APIs require editor C# glue.
    public static class GuestCharacterBuilder
    {
        private const string Folder="Assets/LocalCharacterBuild/Guests";
        private sealed class Profile
        {
            public string model,texture,template;
            public float scale=1;
            public bool flipV,tpose,openEyelids;
            public float[] rotation;
            public string[] excludeWeightedBones;
            public Dictionary<string,string> bones,materials;
        }
        private static string[] Names => (Environment.GetEnvironmentVariable("YARG_GUEST_NAMES")??"Sonic|Sora").Split('|',StringSplitOptions.RemoveEmptyEntries);
        public static void PreviewVariants()
        {
            foreach(var name in new[]{"Sora","Sonic"})
            {
                var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/"+name+"/Generated/"+name+".prefab"));
                string output=Path.Combine(Environment.GetEnvironmentVariable("YARG_GUEST_OUTPUT"),name);
                try
                {
                    if(name=="Sora")
                    {
                        foreach(var r in root.GetComponentsInChildren<Renderer>())foreach(var m in r.materials)
                        {m.SetTextureScale("_BaseMap",new Vector2(1,-1));m.SetTextureOffset("_BaseMap",new Vector2(0,1));}
                        OsrsCharacterBuilder.Render(root,Path.Combine(output,"uv-flip.png"));
                    }
                    else
                    {
                        var lids=root.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("EyeLidUp")).ToArray();
                        var rotations=lids.Select(t=>t.rotation).ToArray();
                        foreach(int angle in new[]{-60,60})
                        {
                            for(int i=0;i<lids.Length;i++)lids[i].rotation=Quaternion.Euler(angle,0,0)*rotations[i];
                            OsrsCharacterBuilder.Render(root,Path.Combine(output,"eyelids-"+angle+".png"));
                        }
                    }
                }
                finally{Object.DestroyImmediate(root);}
            }
        }
        public static void Build()
        {
            var failures=new List<string>();
            foreach(var name in Names)
            {try{BuildOne(name);}catch(Exception error){failures.Add(name);Debug.LogError($"GUEST_ROSTER_REJECT {name}: {error}");}}
            if(failures.Count>0)throw new InvalidDataException("Rejected guests: "+string.Join(", ",failures));
        }
        private static Dictionary<HumanBodyBones,string> Map(string name)
        {
            var map=new Dictionary<HumanBodyBones,string>();
            void Add(HumanBodyBones human,string bone)=>map.Add(human,bone);
            Add(HumanBodyBones.Hips,name=="Sonic"?"Hips":"bone000");
            Add(HumanBodyBones.Spine,name=="Sonic"?"Spine":"bone066");
            Add(HumanBodyBones.Chest,name=="Sonic"?"Spine1":"bone067");
            Add(HumanBodyBones.Neck,name=="Sonic"?"Neck":"bone137");
            Add(HumanBodyBones.Head,name=="Sonic"?"Head":"bone_head");
            for(int side=0;side<2;side++)
            {
                string prefix=side==0?"Left":"Right",suffix=side==0?"_L":"_R";
                string Bone(int i)=>"bone"+i.ToString("000");
                var parts=new[]{"Shoulder","UpperArm","LowerArm","Hand","UpperLeg","LowerLeg","Foot","Toes"};
                var sonic=new[]{"Shoulder","UpperArm","ForeArm","Hand","Thigh","Calf","Foot","Toe"};
                var sora=side==0?new[]{75,78,79,82,11,12,15,18}:new[]{167,170,171,174,40,41,44,47};
                for(int i=0;i<parts.Length;i++)Add(Enum.Parse<HumanBodyBones>(prefix+parts[i]),name=="Sonic"?sonic[i]+suffix:Bone(sora[i]));
                var fingers=new[]{"Thumb","Index","Middle","Ring","Little"};
                var soraFingers=new[]{new[]{88,91,92},new[]{94,95,96},new[]{98,99,100},new[]{102,103,104},new[]{106,107,108}};
                var segments=new[]{"Proximal","Intermediate","Distal"};
                for(int finger=0;finger<5;finger++)for(int segment=0;segment<3;segment++)
                    Add(Enum.Parse<HumanBodyBones>(prefix+fingers[finger]+segments[segment]),name=="Sonic"?
                        (finger==4?"Pinky":fingers[finger])+(segment+1)+suffix:Bone(soraFingers[finger][segment]+side*92));
            }
            return map;
        }
        private static void BuildOne(string name)
        {
            string folder=Folder+"/"+name;
            string profilePath="tools/character-profiles/"+name+".json";
            var profile=File.Exists(profilePath)?JsonConvert.DeserializeObject<Profile>(File.ReadAllText(profilePath)):null;
            if(profile==null&&name!="Sonic"&&name!="Sora")throw new InvalidDataException("Missing explicit character profile: "+name);
            string path=profile==null?Directory.GetFiles(folder).Single(p=>p.EndsWith(".dae",StringComparison.OrdinalIgnoreCase)):folder+"/"+profile.model;
            var importer=(ModelImporter)AssetImporter.GetAtPath(path);
            importer.globalScale=profile?.scale??(name=="Sonic"?0.04f:0.01f);
            bool automatic=profile!=null&&profile.bones==null&&profile.template==null;
            importer.animationType=automatic?ModelImporterAnimationType.Human:ModelImporterAnimationType.Generic;
            if(automatic)importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
            importer.isReadable=true;importer.SaveAndReimport();
            var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));root.name=name;
            if(profile?.rotation!=null)
            {
                if(automatic)throw new InvalidDataException("Axis correction requires an explicit source bone map");
                var model=root;root=new GameObject(name);model.name=name+" source";
                model.transform.SetParent(root.transform,false);
                model.transform.localRotation=Quaternion.Euler(profile.rotation[0],profile.rotation[1],profile.rotation[2])*model.transform.localRotation;
            }
            string output=Path.Combine(Environment.GetEnvironmentVariable("YARG_GUEST_OUTPUT"),name);
            Directory.CreateDirectory(output);string generated=folder+"/Generated";Directory.CreateDirectory(generated);AssetDatabase.Refresh();
            void Save(Object asset,string target){AssetDatabase.DeleteAsset(target);AssetDatabase.CreateAsset(asset,target);}
            try
            {
                var old=root.GetComponentInChildren<Animator>();
                Avatar importedAvatar=null;
                Dictionary<HumanBodyBones,string> map;
                if(automatic)
                {
                    if(old?.avatar==null||!old.avatar.isHuman||!old.avatar.isValid)throw new InvalidDataException("Automatic humanoid mapping failed: "+name);
                    importedAvatar=Object.Instantiate(old.avatar);
                    map=Enumerable.Range(0,(int)HumanBodyBones.LastBone).Select(i=>(HumanBodyBones)i)
                        .Where(b=>old.GetBoneTransform(b)!=null).ToDictionary(b=>b,b=>old.GetBoneTransform(b).name);
                }
                else map=profile?.bones?.ToDictionary(p=>Enum.Parse<HumanBodyBones>(p.Key),p=>p.Value)??Map(profile?.template??name);
                if(old!=null)Object.DestroyImmediate(old);
                if(name=="Sonic"||profile?.openEyelids==true)
                    foreach(var lid in root.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("EyelidUp",StringComparison.OrdinalIgnoreCase)))
                        lid.rotation=Quaternion.Euler(-60,0,0)*lid.rotation;
                var bones=root.GetComponentsInChildren<Transform>().GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());
                var required=new HashSet<HumanBodyBones>{HumanBodyBones.Hips,HumanBodyBones.Spine,HumanBodyBones.Head,
                    HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand,HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand,
                    HumanBodyBones.LeftUpperLeg,HumanBodyBones.LeftLowerLeg,HumanBodyBones.LeftFoot,HumanBodyBones.RightUpperLeg,HumanBodyBones.RightLowerLeg,HumanBodyBones.RightFoot};
                foreach(var pair in map.ToArray())if(!bones.ContainsKey(pair.Value))
                {if(required.Contains(pair.Key))throw new InvalidDataException($"{name} missing {pair.Key}: {pair.Value}");map.Remove(pair.Key);}
                if(profile?.tpose==true)
                    foreach(var side in new[]{"Left","Right"})
                    {
                        var upper=bones[map[Enum.Parse<HumanBodyBones>(side+"UpperArm")]];
                        var lower=bones[map[Enum.Parse<HumanBodyBones>(side+"LowerArm")]];
                        var hand=bones[map[Enum.Parse<HumanBodyBones>(side+"Hand")]];
                        var direction=side=="Left"?Vector3.left:Vector3.right;
                        upper.rotation=Quaternion.FromToRotation(lower.position-upper.position,direction)*upper.rotation;
                        lower.rotation=Quaternion.FromToRotation(hand.position-lower.position,direction)*lower.rotation;
                    }
                // All mesh weights and bone rest transforms come from the source model.
                // Only the humanoid mapping and rendering materials are added here.
                var description=new HumanDescription
                {
                    human=map.Select(p=>new HumanBone{humanName=p.Key.ToString(),boneName=p.Value,limit=new HumanLimit{useDefaultValues=true}}).ToArray(),
                    skeleton=root.GetComponentsInChildren<Transform>().Select(t=>new SkeletonBone{name=t.name,position=t.localPosition,rotation=t.localRotation,scale=t.localScale}).ToArray(),
                    upperArmTwist=0.5f,lowerArmTwist=0.5f,upperLegTwist=0.5f,lowerLegTwist=0.5f,armStretch=0.05f,legStretch=0.05f
                };
                var avatar=importedAvatar??AvatarBuilder.BuildHumanAvatar(root,description);
                if(!avatar.isValid||!avatar.isHuman)throw new InvalidDataException("Invalid humanoid: "+name);
                Save(avatar,generated+"/humanoid.asset");
                var animator=root.AddComponent<Animator>();animator.avatar=avatar;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                var humanoid=root.AddComponent<UniHumanoid.Humanoid>();humanoid.AssignBones(map.Select(p=>(p.Key,bones[p.Value])));
                int index=0;
                foreach(var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if(name=="Sonic"&&renderer.name.IndexOf("MouthR",StringComparison.OrdinalIgnoreCase)>=0){renderer.gameObject.SetActive(false);continue;}
                    renderer.updateWhenOffscreen=true;
                    renderer.sharedMaterials=renderer.sharedMaterials.Select(original=>
                    {
                        var texture=original.mainTexture;
                        if(profile?.texture!=null)texture=AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"/"+profile.texture);
                        if(profile?.materials!=null&&profile.materials.TryGetValue(original.name,out var textureFile))
                            texture=AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"/"+textureFile);
                        if(name=="Sonic")
                        {
                            string part=original.name.Contains("37")?"eye":original.name.Contains("38")?"shoes":original.name.Contains("39")?"cloth":"body01";
                            string texturePath=Directory.GetFiles(folder,"*.png").Single(p=>p.EndsWith("chr_sonic_"+part+"_dif_HD.png"));
                            texture=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                        }
                        if(texture==null)throw new InvalidDataException("Missing texture for "+name+" "+original.name);
                        var material=new Material(Shader.Find("Universal Render Pipeline/Unlit")){name=name+" "+original.name};
                        material.SetTexture("_BaseMap",texture);material.SetFloat("_Cull",0);
                        // This KH exporter writes image-space V; Unity expects bottom-up V.
                        if(name=="Sora"||profile?.flipV==true){material.SetTextureScale("_BaseMap",new Vector2(1,-1));material.SetTextureOffset("_BaseMap",new Vector2(0,1));}
                        Save(material,generated+"/material"+(index++)+".mat");return material;
                    }).ToArray();
                }
                if(profile?.excludeWeightedBones?.Length>0)
                {
                    int removed=0,meshIndex=0;
                    foreach(var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        var mesh=Object.Instantiate(renderer.sharedMesh);var weights=mesh.boneWeights;
                        var excluded=renderer.bones.Select((b,i)=>(b,i)).Where(p=>profile.excludeWeightedBones.Any(n=>p.b.name==n||p.b.GetComponentsInParent<Transform>().Any(t=>t.name==n))).Select(p=>p.i).ToHashSet();
                        bool Omit(int vertex)
                        {
                            var w=weights[vertex];return (excluded.Contains(w.boneIndex0)?w.weight0:0)+(excluded.Contains(w.boneIndex1)?w.weight1:0)+
                                (excluded.Contains(w.boneIndex2)?w.weight2:0)+(excluded.Contains(w.boneIndex3)?w.weight3:0)>0.5f;
                        }
                        for(int submesh=0;submesh<mesh.subMeshCount;submesh++)
                        {
                            var triangles=mesh.GetTriangles(submesh);var keep=new List<int>();
                            for(int i=0;i<triangles.Length;i+=3)
                                if(Omit(triangles[i])&&Omit(triangles[i+1])&&Omit(triangles[i+2]))removed++;
                                else{keep.Add(triangles[i]);keep.Add(triangles[i+1]);keep.Add(triangles[i+2]);}
                            mesh.SetTriangles(keep,submesh,false);
                        }
                        mesh.RecalculateBounds();Save(mesh,generated+"/body-without-equipment"+(meshIndex++)+".asset");renderer.sharedMesh=mesh;
                    }
                    if(removed==0)throw new InvalidDataException("No equipment triangles matched the explicit source bones for "+name);
                    Debug.Log($"GUEST_EQUIPMENT_OMITTED {name}: {removed} triangles; original source and body weights retained");
                }
                OsrsCharacterBuilder.Render(root,Path.Combine(output,"rest.png"));
                string prefab=generated+"/"+name+".prefab";PrefabUtility.SaveAsPrefabAsset(root,prefab);AssetDatabase.SaveAssets();
                string bundle=name.ToLowerInvariant()+".yargchar";
                if(BuildPipeline.BuildAssetBundles(output,new[]{new AssetBundleBuild{assetBundleName=bundle,assetNames=new[]{prefab},addressableNames=new[]{BundleBackgroundManager.CHARACTER_PREFAB_PATH.ToLowerInvariant()}}},BuildAssetBundleOptions.ChunkBasedCompression,BuildTarget.StandaloneOSX)==null)
                    throw new InvalidDataException("Guest bundle build failed: "+name);
                Environment.SetEnvironmentVariable("YARG_BAND_MODEL",Path.Combine(output,bundle));BandSkinChecks.Run();
                Debug.Log("GUEST_BAND_PASS "+name);
            }
            finally{Object.DestroyImmediate(root);}
        }
        public static void Inspect()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach(var path in Directory.GetFiles(Folder,"*",SearchOption.AllDirectories).Where(p=>
                (p.EndsWith(".dae",StringComparison.OrdinalIgnoreCase)||p.EndsWith(".fbx",StringComparison.OrdinalIgnoreCase))&&Names.Contains(new DirectoryInfo(Path.GetDirectoryName(p)).Name)))
            {
                var importer=(ModelImporter)AssetImporter.GetAtPath(path);
                importer.isReadable=true;importer.animationType=ModelImporterAnimationType.Generic;importer.SaveAndReimport();
                var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var root=Object.Instantiate(model);
                try
                {
                    var renderers=root.GetComponentsInChildren<Renderer>();
                    if(renderers.Length>0)
                    {
                        var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                        root.transform.localScale*=1.7f/Mathf.Max(bounds.size.y,0.001f);
                        bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                        root.transform.position-=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
                        string output=Path.Combine(Environment.GetEnvironmentVariable("YARG_GUEST_OUTPUT")??".local/band-guests/inspection",root.name);
                        Directory.CreateDirectory(output);OsrsCharacterBuilder.Render(root,Path.Combine(output,"source.png"));
                    }
                    Debug.Log($"GUEST_MODEL {path}: {root.GetComponentsInChildren<SkinnedMeshRenderer>().Length} skins");
                    foreach(var t in root.GetComponentsInChildren<Transform>())Debug.Log($"GUEST_NODE {root.name} {t.name} parent={t.parent?.name} pos={t.position}");
                    foreach(var bone in root.GetComponentsInChildren<SkinnedMeshRenderer>().SelectMany(r=>r.bones).Distinct())
                        Debug.Log($"GUEST_BONE {root.name} {bone.name} parent={bone.parent?.name} pos={bone.position}");
                    foreach(var r in root.GetComponentsInChildren<Renderer>())
                        foreach(var mat in r.sharedMaterials)Debug.Log($"GUEST_MATERIAL {root.name} {mat?.name} texture={mat?.mainTexture?.name}");
                }
                finally{Object.DestroyImmediate(root);}
            }
        }
    }
}
