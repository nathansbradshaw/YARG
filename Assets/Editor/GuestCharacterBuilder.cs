using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using YARG.Venue;
using Object = UnityEngine.Object;

namespace Editor
{
    // Unity's model, Avatar and AssetBundle APIs require editor C# glue.
    public static class GuestCharacterBuilder
    {
        private const string Folder="Assets/LocalCharacterBuild/Guests";
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
            foreach(var name in new[]{"Sonic","Sora"})BuildOne(name);
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
            string path=Directory.GetFiles(folder).Single(p=>p.EndsWith(".dae",StringComparison.OrdinalIgnoreCase));
            var importer=(ModelImporter)AssetImporter.GetAtPath(path);
            importer.globalScale=name=="Sonic"?0.04f:0.01f;
            importer.animationType=ModelImporterAnimationType.Generic;importer.isReadable=true;importer.SaveAndReimport();
            var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));root.name=name;
            string output=Path.Combine(Environment.GetEnvironmentVariable("YARG_GUEST_OUTPUT"),name);
            Directory.CreateDirectory(output);string generated=folder+"/Generated";Directory.CreateDirectory(generated);AssetDatabase.Refresh();
            void Save(Object asset,string target){AssetDatabase.DeleteAsset(target);AssetDatabase.CreateAsset(asset,target);}
            try
            {
                var old=root.GetComponent<Animator>();if(old!=null)Object.DestroyImmediate(old);
                if(name=="Sonic")
                    foreach(var lid in root.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("EyeLidUp")))
                        lid.rotation=Quaternion.Euler(-60,0,0)*lid.rotation;
                var map=Map(name);
                var bones=root.GetComponentsInChildren<Transform>().GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());
                foreach(var pair in map)if(!bones.ContainsKey(pair.Value))throw new InvalidDataException($"{name} missing {pair.Key}: {pair.Value}");
                // All mesh weights and bone rest transforms come from the source model.
                // Only the humanoid mapping and rendering materials are added here.
                var description=new HumanDescription
                {
                    human=map.Select(p=>new HumanBone{humanName=p.Key.ToString(),boneName=p.Value,limit=new HumanLimit{useDefaultValues=true}}).ToArray(),
                    skeleton=root.GetComponentsInChildren<Transform>().Select(t=>new SkeletonBone{name=t.name,position=t.localPosition,rotation=t.localRotation,scale=t.localScale}).ToArray(),
                    upperArmTwist=0.5f,lowerArmTwist=0.5f,upperLegTwist=0.5f,lowerLegTwist=0.5f,armStretch=0.05f,legStretch=0.05f
                };
                var avatar=AvatarBuilder.BuildHumanAvatar(root,description);
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
                        if(name=="Sora"){material.SetTextureScale("_BaseMap",new Vector2(1,-1));material.SetTextureOffset("_BaseMap",new Vector2(0,1));}
                        Save(material,generated+"/material"+(index++)+".mat");return material;
                    }).ToArray();
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
            foreach(var path in Directory.GetFiles(Folder,"*",SearchOption.AllDirectories).Where(p=>p.EndsWith(".dae",StringComparison.OrdinalIgnoreCase)))
            {
                var importer=(ModelImporter)AssetImporter.GetAtPath(path);
                importer.isReadable=true;importer.animationType=ModelImporterAnimationType.Generic;importer.SaveAndReimport();
                var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var root=Object.Instantiate(model);
                try
                {
                    Debug.Log($"GUEST_MODEL {path}: {root.GetComponentsInChildren<SkinnedMeshRenderer>().Length} skins");
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
