using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YARG.Core.Chart;

namespace YARG.Venue.Characters
{
    // Uses existing facial controls, never remeshes a face or creates a new rig.
    public sealed class BandMouthAnimation : IDisposable
    {
        private readonly Transform _head;
        private readonly Vector3 _headUp,_headForward;
        private readonly float _travel;
        private readonly (Transform bone, Vector3 rest, float direction)[] _bones;
        private readonly (SkinnedMeshRenderer renderer, int index, float rest)[] _shapes;
        public bool IsSupported => _bones.Length > 0 || _shapes.Length > 0;

        public BandMouthAnimation(Animator rig)
        {
            _head=rig.GetBoneTransform(HumanBodyBones.Head);
            var axis=_head.position-rig.GetBoneTransform(HumanBodyBones.Hips).position;
            var options=rig.GetComponent<BandModelOptions>();
            _headUp=_head.InverseTransformDirection(axis.normalized);
            var across=rig.GetBoneTransform(HumanBodyBones.RightUpperArm).position-rig.GetBoneTransform(HumanBodyBones.LeftUpperArm).position;
            _headForward=_head.InverseTransformDirection(Vector3.Cross(across,axis).normalized);
            _travel=axis.magnitude*Mathf.Clamp(options!=null&&options.MouthTravel>0?options.MouthTravel:0.04f,0.005f,0.1f);
            var lower=new HashSet<Transform>(options?.LowerMouth??Array.Empty<Transform>());
            var upper=new HashSet<Transform>(options?.UpperMouth??Array.Empty<Transform>());
            if(lower.Count==0&&upper.Count==0)
                foreach(var bone in rig.GetComponentsInChildren<Transform>(true))
                {
                    if(bone.name=="Jaw"||bone.name=="Jaw_L"||bone.name=="UnderJaw"||bone.name.StartsWith("Lip_btm_",StringComparison.Ordinal))lower.Add(bone);
                    if(bone.name.StartsWith("Lip_top_",StringComparison.Ordinal))upper.Add(bone);
                }
            _bones=lower.Where(t=>t!=null).Select(t=>(t,t.localPosition,-1f))
                .Concat(upper.Where(t=>t!=null&&!lower.Contains(t)).Select(t=>(t,t.localPosition,0.25f))).ToArray();
            var shapes=new List<(SkinnedMeshRenderer,int,float)>();
            foreach(var renderer in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>_bones.Length==0))
            {
                var mesh=renderer.sharedMesh;
                if(mesh==null)continue;
                for(int i=0;i<mesh.blendShapeCount;i++)
                {
                    string name=mesh.GetBlendShapeName(i).ToLowerInvariant();
                    if(name!="jawopen"&&name!="mouthopen"&&name!="vrc.v_aa"&&name!="fcl_mth_a"&&name!="aa")continue;
                    shapes.Add((renderer,i,renderer.GetBlendShapeWeight(i)));break;
                }
            }
            _shapes=shapes.ToArray();
        }

        public void Apply(float amount)
        {
            amount=Mathf.Clamp01(amount);
            var travel=_head.TransformDirection(_headUp)*(_travel*amount);
            var forward=_head.TransformDirection(_headForward)*(_travel*amount*0.35f);
            foreach(var channel in _bones)
                channel.bone.localPosition=channel.rest+channel.bone.parent.InverseTransformVector(travel*channel.direction+forward*Mathf.Abs(channel.direction));
            foreach(var channel in _shapes)
                channel.renderer.SetBlendShapeWeight(channel.index,Mathf.Lerp(channel.rest,100,amount));
        }

        // Binary search makes seeking, restarting and long charts independent of
        // the animation lookahead cursor. Rests and percussion do not open mouths.
        public static float AtTime(IReadOnlyList<VocalNote> notes,double time)
        {
            if(notes==null||notes.Count==0||double.IsNaN(time)||double.IsInfinity(time))return 0;
            int low=0,high=notes.Count;
            while(low<high){int middle=(low+high)/2;if(notes[middle].Time<=time)low=middle+1;else high=middle;}
            if(low==0)return 0;
            var note=notes[low-1];if(note.IsPercussion||note.IsPhrase)return 0;
            double length=Math.Max(note.TotalTimeLength,0.08),elapsed=time-note.Time;
            if(elapsed<0||elapsed>=length)return 0;
            double ramp=Math.Min(0.045,length*0.2);
            return (float)Math.Clamp(Math.Min(elapsed/ramp,(length-elapsed)/ramp),0,1);
        }

        public void Dispose()
        {
            foreach(var channel in _bones)if(channel.bone!=null)channel.bone.localPosition=channel.rest;
            foreach(var channel in _shapes)if(channel.renderer!=null)channel.renderer.SetBlendShapeWeight(channel.index,channel.rest);
        }
    }
}
