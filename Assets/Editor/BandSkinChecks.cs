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
        public static void InspectVenues()
        {
            foreach(var file in Environment.GetEnvironmentVariable("YARG_BAND_VENUES").Split('|'))
            {
                var bundle=AssetBundle.LoadFromFile(file);
                var stage=Object.Instantiate(bundle.LoadAsset<GameObject>(BundleBackgroundManager.BACKGROUND_PREFAB_PATH.ToLowerInvariant()));
                try
                {
                    foreach(var actor in stage.GetComponentsInChildren<VenueCharacter>())
                    {
                        var animator=actor.GetComponent<Animator>();
                        Debug.Log($"PROP_ACTOR {Path.GetFileName(file)} {actor.Type} component={actor.GetType().Name} rootMotion={animator?.applyRootMotion}");
                        foreach(var r in actor.GetComponentsInChildren<Renderer>(true))
                            Debug.Log($"PROP_RENDERER {actor.Type} {r.GetType().Name} {AnimationUtility.CalculateTransformPath(r.transform,actor.transform)} enabled={r.enabled} active={r.gameObject.activeSelf}");
                    }
                    foreach(var t in stage.GetComponentsInChildren<Transform>(true))
                        if(t.name.ToLowerInvariant().Contains("mic")||t.name.ToLowerInvariant().Contains("stick"))
                            Debug.Log($"PROP_OBJECT {AnimationUtility.CalculateTransformPath(t,stage.transform)} active={t.gameObject.activeSelf} renderer={t.GetComponent<Renderer>()?.GetType().Name}");
                }
                finally {Object.DestroyImmediate(stage);bundle.Unload(true);}
            }
        }
        public static void BuildAndRun() { OsrsCharacterBuilder.Build(); Run(); }
        public static void RunModels()
        {
            foreach(var path in Environment.GetEnvironmentVariable("YARG_BAND_MODELS").Split('|',StringSplitOptions.RemoveEmptyEntries))
            {
                Environment.SetEnvironmentVariable("YARG_BAND_MODEL",path);Run();
            }
        }
        public static void RunCast()
        {
            var root=Environment.GetEnvironmentVariable("YARG_NPC_ROOT");
            foreach(var npc in ExistingOsrsRig.CharacterNames)
            {
                Environment.SetEnvironmentVariable("YARG_BAND_MODEL",Path.Combine(root,npc,"existing-rig",npc.ToLowerInvariant()+".yargchar"));
                Run();
            }
        }

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
                    bool rootMotion = animator.applyRootMotion;
                    var skin = Object.Instantiate(model, actor.transform);
                    var follower = BandCharacterSkin.Attach(animator, skin);
                    var previews=Environment.GetEnvironmentVariable("YARG_BAND_PREVIEWS");
                    if(!string.IsNullOrEmpty(previews))
                    {
                        Directory.CreateDirectory(previews);
                        var position=actor.transform.position;var rotation=actor.transform.rotation;var scale=actor.transform.localScale;
                        try
                        {
                            actor.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);actor.transform.localScale=scale*0.75f;follower.CopyPose();
                            OsrsCharacterBuilder.Render(actor.gameObject,Path.Combine(previews,model.name+"-"+actor.Type+".png"));
                        }
                        finally{actor.transform.SetPositionAndRotation(position,rotation);actor.transform.localScale=scale;follower.CopyPose();}
                    }
                    var renderer = skin.GetComponentInChildren<SkinnedMeshRenderer>();
                    var before = new Mesh(); var after = new Mesh();
                    renderer.BakeMesh(before);
                    using (var poseHandler = new HumanPoseHandler(animator.avatar, animator.transform))
                    {
                        var pose = new HumanPose(); poseHandler.GetHumanPose(ref pose);
                        for (int i = 0; i < pose.muscles.Length; i++)
                        {
                            if (HumanTrait.MuscleName[i].Contains("Arm") || HumanTrait.MuscleName[i].Contains("Forearm"))
                                pose.muscles[i] = 0.35f;
                            if(HumanTrait.MuscleName[i].Contains("Spine")||HumanTrait.MuscleName[i].Contains("Chest"))pose.muscles[i]=0.65f;
                        }
                        poseHandler.SetHumanPose(ref pose);
                    }
                    follower.CopyPose(); renderer.BakeMesh(after);
                    int moved = before.vertices.Zip(after.vertices, (a,b) => Vector3.Distance(a,b)).Count(d => d > 0.01f);
                    if (moved < 10 || renderer.gameObject.layer != LayerMask.NameToLayer("Venue") || animator.runtimeAnimatorController != controller ||
                        animator.applyRootMotion ||
                        stage.GetComponentsInChildren<VenueCharacter>().Count(c => c.isActiveAndEnabled) != actors.Length ||
                        Vector3.Distance(renderer.bounds.center, animator.transform.position) > 4f)
                        throw new InvalidOperationException($"Skin transfer failed for {actor.Type}: {moved} moved, bounds={renderer.bounds}");
                    Debug.Log($"BAND_SKIN_ROLE_PASS {actor.Type}: {moved} vertices moved; authored controller, props and chart actor retained; bounds={renderer.bounds}");
                    var target=skin.GetComponent<Animator>();
                    var sourcePalms= new[]{new BandPalm(animator,HumanBodyBones.LeftHand),new BandPalm(animator,HumanBodyBones.RightHand)};
                    var targetPalms= new[]{new BandPalm(target,HumanBodyBones.LeftHand),new BandPalm(target,HumanBodyBones.RightHand)};
                    void CheckProp(string name, BandPalm palm)
                    {
                        var prop=skin.GetComponentsInChildren<Transform>().SingleOrDefault(t=>t.name==name);
                        if(prop==null||Vector3.Distance(prop.position,palm.Position)>0.001f||!prop.GetComponentsInChildren<MeshRenderer>().Any(r=>r.enabled))
                            throw new InvalidOperationException($"Missing or floating prop: {actor.Type} {name}");
                    }
                    if(actor.Type==VenueCharacter.CharacterType.Drums)
                    {CheckProp("Band drumstick left",targetPalms[0]);CheckProp("Band drumstick right",targetPalms[1]);}
                    if(actor.Type==VenueCharacter.CharacterType.Vocals)CheckProp("Band microphone",targetPalms[1]);
                    var vocalTemplate=skin.transform.Find("Vocal microphone template");
                    if(vocalTemplate!=null)
                    {
                        var custom=skin.GetComponentsInChildren<Transform>().SingleOrDefault(t=>t.name=="Character vocal prop");
                        if(vocalTemplate.gameObject.activeSelf||((custom!=null)!=(actor.Type==VenueCharacter.CharacterType.Vocals)))
                            throw new InvalidOperationException("Character vocal prop appeared in the wrong band role");
                        if(custom!=null)
                        {
                            if(custom.GetComponent<MeshFilter>().sharedMesh!=vocalTemplate.GetComponent<MeshFilter>().sharedMesh||
                                custom.GetComponent<MeshRenderer>().sharedMaterial!=vocalTemplate.GetComponent<MeshRenderer>().sharedMaterial||
                                custom.gameObject.layer!=LayerMask.NameToLayer("Venue")||
                                Vector3.Dot(custom.up,(target.GetBoneTransform(HumanBodyBones.Head).position-targetPalms[1].Position).normalized)<0.999f)
                                throw new InvalidOperationException("Character vocal prop lost its mesh, material, layer or mouth aim");
                            Debug.Log("BAND_CUSTOM_VOCAL_PROP_PASS: original mesh and palette, palm grip, mouth aim, vocals only");
                        }
                    }
                    using var sourceRead=new HumanPoseHandler(animator.avatar,animator.transform);
                    using var targetRead=new HumanPoseHandler(target.avatar,target.transform);
                    var sourcePose=new HumanPose();var targetPose=new HumanPose();
                    var chains=new[]
                    {
                        new[]{HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand},
                        new[]{HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand}
                    };
                    float Segment(HumanBodyBones a,HumanBodyBones b)=>Vector3.Distance(target.GetBoneTransform(a).position,target.GetBoneTransform(b).position);
                    var lengths=chains.Select(c=>new[]{Segment(c[0],c[1]),Segment(c[1],c[2])}).ToArray();
                    float seamGap=0;string seamDetail="";
                    var waist=renderer.sharedMesh.vertices.Select((v,i)=>(v,i)).Where(p=>p.v.y>0.95f&&p.v.y<1.3f).GroupBy(p=>p.v);
                    var posedVertices=after.vertices;
                    foreach(var seam in waist)
                        foreach(var vertex in seam)
                        {
                            float gap=Vector3.Distance(posedVertices[seam.First().i],posedVertices[vertex.i]);
                            if(gap>seamGap){seamGap=gap;var ws=renderer.sharedMesh.boneWeights;seamDetail=$"at {seam.Key}: {renderer.bones[ws[seam.First().i].boneIndex0].name}/{renderer.bones[ws[vertex.i].boneIndex0].name}";}
                        }
                    Debug.Log($"BAND_WAIST_SEAM {actor.Type} gap={seamGap:F5} {seamDetail}");
                    float worst=1,beforeWorst=1;string edge="";
                    var bind=renderer.sharedMesh.vertices;var triangles=renderer.sharedMesh.triangles;var weights=renderer.sharedMesh.boneWeights;
                    for(int triangle=0;triangle<triangles.Length;triangle+=3)
                    for(int corner=0;corner<3;corner++)
                    {
                        int a=triangles[triangle+corner],b=triangles[triangle+(corner+1)%3];
                        if(bind[a].y<0.98f||bind[a].y>1.3f||bind[b].y<0.98f||bind[b].y>1.3f)continue;
                        float rest=Vector3.Distance(bind[a],bind[b]);if(rest<0.015f)continue;
                        float stretch=Vector3.Distance(posedVertices[a],posedVertices[b])/rest;
                        beforeWorst=Mathf.Max(beforeWorst,Vector3.Distance(before.vertices[a],before.vertices[b])/rest);
                        if(stretch>worst){worst=stretch;edge=$"{renderer.bones[weights[a].boneIndex0].name}/{renderer.bones[weights[b].boneIndex0].name}";}
                    }
                    Debug.Log($"BAND_WAIST_STRETCH {actor.Type} max={worst:F3} before={beforeWorst:F3} bones={edge}");
                    if((Environment.GetEnvironmentVariable("YARG_OSRS_WAIST_CHECK")=="1"||model.name=="Hans-3105"||model.name=="Cook-4626"||model.name=="Father_Aereck-2812") &&
                        (worst>1.6f||seamGap>0.004f))
                        throw new InvalidOperationException($"OSRS waist deformation regressed for {model.name}: stretch={worst}, seam={seamGap}");
                    foreach(var offset in new[]{Vector3.zero,new Vector3(0.7f,0.2f,-0.3f)})
                    {
                        actor.transform.position+=offset;actor.transform.rotation*=Quaternion.Euler(0,35,0);
                        follower.CopyPose();
                        sourceRead.GetHumanPose(ref sourcePose);targetRead.GetHumanPose(ref targetPose);
                        float rotationError=Quaternion.Angle(sourcePose.bodyRotation,targetPose.bodyRotation);
                        var hips=Vector3.Distance(animator.GetBoneTransform(HumanBodyBones.Hips).position,target.GetBoneTransform(HumanBodyBones.Hips).position);
                        var left=Vector3.Distance(animator.GetBoneTransform(HumanBodyBones.LeftHand).position,target.GetBoneTransform(HumanBodyBones.LeftHand).position);
                        var right=Vector3.Distance(animator.GetBoneTransform(HumanBodyBones.RightHand).position,target.GetBoneTransform(HumanBodyBones.RightHand).position);
                        var fixedPosition=target.GetBoneTransform(HumanBodyBones.Hips).position;
                        for(int frame=0;frame<120;frame++)follower.CopyPose();
                        var drift=Vector3.Distance(fixedPosition,target.GetBoneTransform(HumanBodyBones.Hips).position);
                        if(hips>0.003f||drift>0.003f||rotationError>0.5f)
                            throw new InvalidOperationException($"Pelvis transfer drift for {actor.Type}");
                        for(int arm=0;arm<chains.Length;arm++)
                        {
                            var chain=chains[arm];var shoulder=target.GetBoneTransform(chain[0]).position;
                            var goal=sourcePalms[arm].Position-targetPalms[arm].Hand.TransformVector(targetPalms[arm].LocalOffset);
                            float reach=lengths[arm][0]+lengths[arm][1];
                            float unavoidable=Mathf.Max(0,Vector3.Distance(shoulder,goal)-reach);
                            float error=Vector3.Distance(target.GetBoneTransform(chain[2]).position,goal);
                            if(error>unavoidable+0.004f||Mathf.Abs(Segment(chain[0],chain[1])-lengths[arm][0])>0.003f||
                                Mathf.Abs(Segment(chain[1],chain[2])-lengths[arm][1])>0.003f)
                                throw new InvalidOperationException($"Grip missed reachable target or stretched bones for {actor.Type}: {error} vs {unavoidable}");
                        }
                        Debug.Log($"BAND_ANCHOR_METRIC {actor.Type} offset={offset} hips={hips:F5} left={left:F5} right={right:F5} drift={drift:F5} rotation={rotationError:F3}");
                    }
                    Object.DestroyImmediate(before); Object.DestroyImmediate(after);
                    follower.RestoreActor();
                    Object.DestroyImmediate(skin);
                    if(animator.applyRootMotion!=rootMotion)throw new InvalidOperationException("Root motion was not restored");
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
