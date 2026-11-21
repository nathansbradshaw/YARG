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
        public static void RunVenues()
        {
            CheckRules();
            foreach(var path in Environment.GetEnvironmentVariable("YARG_BAND_VENUES").Split('|',StringSplitOptions.RemoveEmptyEntries))
            {Environment.SetEnvironmentVariable("YARG_BAND_VENUE",path);RunModels();}
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
                int expected=actors.Count(a=>a.GetComponent<Animator>()?.isHuman==true&&BandCharacterSkin.CanPlay(model,a.Type));
                foreach (var actor in actors)
                {
                    var animator = actor.GetComponent<Animator>();
                    if (animator == null || !animator.isHuman) continue;
                    animator.Rebind(); animator.Update(0f);
                    var controller = animator.runtimeAnimatorController;
                    bool rootMotion = animator.applyRootMotion;
                    var skin = Object.Instantiate(model, actor.transform);
                    if(!BandCharacterSkin.CanPlay(model,actor.Type))
                    {
                        bool rejected=false;
                        try{BandCharacterSkin.Attach(animator,skin);}catch(ArgumentException){rejected=true;}
                        finally{Object.DestroyImmediate(skin);}
                        if(!rejected||animator.applyRootMotion!=rootMotion)throw new InvalidOperationException("Forbidden role was not rejected safely");
                        Debug.Log($"BAND_ROLE_REJECT_PASS {model.name}: {actor.Type}");continue;
                    }
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
                    CheckFingers(target,actor.Type);
                    if(actor.Type==VenueCharacter.CharacterType.Vocals)CheckMouth(target);
                    // The standalone pose probes above do not run LateUpdate.
                    // Reapply the live follower before checking prop/world anchors.
                    follower.CopyPose();
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
                    if((Environment.GetEnvironmentVariable("YARG_OSRS_WAIST_CHECK")=="1"||
                        (Environment.GetEnvironmentVariable("YARG_OSRS_WAIST_CHECK")=="auto"&&renderer.sharedMesh.name.EndsWith(" original groups",StringComparison.Ordinal))||
                        model.name=="Hans-3105"||model.name=="Cook-4626"||model.name=="Father_Aereck-2812") &&
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
                if(expected==0&&model.GetComponent<BandModelOptions>()?.ExtrasOnly==true)
                {Debug.Log($"BAND_EXTRA_ONLY_PASS {model.name}: excluded from all band roles");return;}
                if (expected==0||count!=expected) throw new InvalidOperationException("Expected every supported band role");
                Debug.Log($"BAND_SKIN_PASS: {count} allowed humanoid roles retargeted without replacing chart actors");
            }
            finally
            {
                if (stage != null) Object.DestroyImmediate(stage);
                if (bodyBundle != null) bodyBundle.Unload(true);
                if (venueBundle != null) venueBundle.Unload(true);
            }
        }

        private static void CheckMouth(Animator rig)
        {
            using var face=new BandMouthAnimation(rig);
            if(!face.IsSupported){Debug.Log("BAND_MOUTH_UNSUPPORTED "+rig.name);return;}
            var controls=rig.GetComponentsInChildren<Transform>().ToDictionary(t=>t,t=>t.localPosition);
            var renderers=rig.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();
            var before=renderers.Select(r=>{var m=new Mesh();r.BakeMesh(m);return m;}).ToArray();
            int moved=0;
            try
            {
                face.Apply(1);
                for(int i=0;i<renderers.Length;i++)
                {
                    var mesh=new Mesh();try{renderers[i].BakeMesh(mesh);moved+=before[i].vertices.Zip(mesh.vertices,(a,b)=>Vector3.Distance(a,b)).Count(d=>d>0.0001f);}
                    finally{Object.DestroyImmediate(mesh);}
                }
                if(moved==0)throw new InvalidOperationException("Mouth controls did not move visible mesh vertices: "+rig.name);
                var open=controls.Keys.ToDictionary(t=>t,t=>t.localPosition);
                for(int frame=0;frame<120;frame++)face.Apply(1);
                if(open.Any(p=>Vector3.Distance(p.Key.localPosition,p.Value)>0.00001f))throw new InvalidOperationException("Mouth drifted across repeated updates");
                face.Apply(0);
                if(controls.Any(p=>Vector3.Distance(p.Key.localPosition,p.Value)>0.00001f))throw new InvalidOperationException("Mouth did not return to its closed pose");
                Debug.Log($"BAND_MOUTH_PASS {rig.name}: {moved} visible vertices, closed pose restored, no drift");
            }
            finally{foreach(var mesh in before)Object.DestroyImmediate(mesh);}
        }

        private static void CheckFingers(Animator rig,VenueCharacter.CharacterType role)
        {
            var grips=new BandFingerGrips(rig,role);if(grips.ControlCount==0)return;
            using var handler=new HumanPoseHandler(rig.avatar,rig.transform);
            var pose=new HumanPose();handler.GetHumanPose(ref pose);
            var original=pose;original.muscles=(float[])pose.muscles.Clone();
            var names=HumanTrait.MuscleName;
            try
            {
                for(int i=0;i<names.Length;i++)if(names[i].EndsWith("Stretched",StringComparison.Ordinal)&&
                    (names[i].Contains("Thumb")||names[i].Contains("Index")||names[i].Contains("Middle")||names[i].Contains("Ring")||names[i].Contains("Little")))pose.muscles[i]=1;
                handler.SetHumanPose(ref pose);
                var hands=new[]{false,true}.Where(left=>!(role==VenueCharacter.CharacterType.Vocals&&left)).ToArray();
                float Distance(bool left)
                {
                    var palm=new BandPalm(rig,left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);
                    return new[]{"Index","Middle","Ring","Little"}.Select(f=>rig.GetBoneTransform(Enum.Parse<HumanBodyBones>((left?"Left":"Right")+f+"Distal")))
                        .Where(t=>t!=null).Select(t=>Vector3.Distance(t.position,palm.Position)).DefaultIfEmpty(0).Average();
                }
                var before=hands.Select(Distance).ToArray();grips.Apply(ref pose);handler.SetHumanPose(ref pose);
                for(int hand=0;hand<hands.Length;hand++)
                {
                    Debug.Log($"BAND_FINGER_DISTANCE {rig.name} {role} left={hands[hand]} open={before[hand]:F5} grip={Distance(hands[hand]):F5}");
                    if(before[hand]>0&&Distance(hands[hand])>=before[hand]-0.0005f)
                    {
                        foreach(float sample in new[]{-1f,-0.5f,0f,0.5f,1f})
                        {
                            for(int i=0;i<names.Length;i++)if(names[i].StartsWith(hands[hand]?"Left":"Right")&&names[i].EndsWith("Stretched"))pose.muscles[i]=sample;
                            handler.SetHumanPose(ref pose);Debug.Log($"BAND_FINGER_SAMPLE {role} left={hands[hand]} curl={sample} distance={Distance(hands[hand]):F5}");
                        }
                        throw new InvalidOperationException($"Finger grip did not close toward the palm: {rig.name} {role} left={hands[hand]}");
                    }
                }
                Debug.Log($"BAND_FINGER_GRIP_PASS {rig.name} {role}: {grips.ControlCount} muscles, fingertips closed toward held-object palms");
            }
            finally{handler.SetHumanPose(ref original);}
        }

        public static void CheckRules()
        {
            Debug.Log("BAND_FINGER_NAMES "+HumanTrait.BoneName[(int)HumanBodyBones.LeftIndexProximal]+"; "+string.Join("|",HumanTrait.MuscleName.Where(n=>n.Contains("Index")).Take(4)));
            var singer=new GameObject("Singer only");var general=new GameObject("General band member");var extra=new GameObject("Extra only");
            try
            {
                singer.AddComponent<BandModelOptions>().AllowedRoles=new[]{VenueCharacter.CharacterType.Vocals};
                extra.AddComponent<BandModelOptions>().ExtrasOnly=true;
                foreach(VenueCharacter.CharacterType role in Enum.GetValues(typeof(VenueCharacter.CharacterType)))
                {
                    var only=BandCharacterSkin.PickForRole(new[]{singer},new System.Collections.Generic.HashSet<GameObject>(),role);
                    if((only!=null)!=(role==VenueCharacter.CharacterType.Vocals))throw new InvalidOperationException("Singer-only pool violated role restrictions");
                    if(BandCharacterSkin.PickForRole(new[]{extra},new System.Collections.Generic.HashSet<GameObject>(),role)!=null)
                        throw new InvalidOperationException("An extra entered the band");
                    var used=new System.Collections.Generic.HashSet<GameObject>{general};
                    var mixed=BandCharacterSkin.PickForRole(new[]{singer,general},used,role);
                    if(mixed==null||!BandCharacterSkin.CanPlay(mixed,role))throw new InvalidOperationException("Casting fallback selected a forbidden role");
                }
                var notes=new[]{new YARG.Core.Chart.VocalNote(60,0,YARG.Core.Chart.VocalNoteType.Lyric,1,0.5,0,0),
                    new YARG.Core.Chart.VocalNote(62,0,YARG.Core.Chart.VocalNoteType.Lyric,2,0.5,0,0)};
                foreach(var sample in new[]{(0d,false),(1.1,true),(1.8,false),(2.1,true),(3d,false),(1.1,true),(0d,false)})
                    if((BandMouthAnimation.AtTime(notes,sample.Item1)>0)!=sample.Item2)throw new InvalidOperationException("Vocal mouth timing failed note/rest/seek check");
                if(BandMouthAnimation.AtTime(Array.Empty<YARG.Core.Chart.VocalNote>(),2)!=0)throw new InvalidOperationException("Missing vocals moved the mouth");
                Debug.Log("BAND_ROLE_MOUTH_RULES_PASS: singer-only casting, compatible fallback, vocal onsets, rests and seeks");
            }
            finally{Object.DestroyImmediate(singer);Object.DestroyImmediate(general);Object.DestroyImmediate(extra);}
        }
    }
}
