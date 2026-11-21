using System;
using System.Collections.Generic;
using UnityEngine;

namespace YARG.Venue.Characters
{
    // Humanoid muscles use the imported fingers' authored joint axes. Preserve
    // stronger authored curls and supply a minimum grasp for unanimated fingers.
    public sealed class BandFingerGrips
    {
        private readonly (int index,float curl)[] _controls;
        public int ControlCount => _controls.Length;

        public BandFingerGrips(Animator target,VenueCharacter.CharacterType role)
        {
            var controls=new List<(int,float)>();var names=HumanTrait.MuscleName;
            for(int i=0;i<names.Length;i++)
            {
                string name=names[i];
                if(!name.EndsWith("Stretched",StringComparison.Ordinal))continue;
                bool left=name.StartsWith("LeftHand.",StringComparison.Ordinal)||name.StartsWith("Left ",StringComparison.Ordinal);
                bool right=name.StartsWith("RightHand.",StringComparison.Ordinal)||name.StartsWith("Right ",StringComparison.Ordinal);
                if(!left&&!right)continue;
                if(role==VenueCharacter.CharacterType.Keys||(role==VenueCharacter.CharacterType.Vocals&&left))continue;
                string finger=name.Contains("Thumb")?"Thumb":name.Contains("Index")?"Index":name.Contains("Middle")?"Middle":name.Contains("Ring")?"Ring":name.Contains("Little")?"Little":null;
                if(finger==null)continue;
                var bone=Enum.Parse<HumanBodyBones>((left?"Left":"Right")+finger+"Proximal");
                if(target.GetBoneTransform(bone)==null)continue;
                bool thumb=finger=="Thumb",index=finger=="Index";
                float curl;
                if(role==VenueCharacter.CharacterType.Drums)curl=thumb?-0.25f:-0.7f;
                else if(role==VenueCharacter.CharacterType.Vocals)curl=thumb?-0.2f:-0.55f;
                else if(left)curl=thumb?-0.1f:-0.4f;
                else curl=thumb||index?-0.45f:-0.1f;
                controls.Add((i,curl));
            }
            _controls=controls.ToArray();
        }

        public void Apply(ref HumanPose pose)
        {
            foreach(var control in _controls)pose.muscles[control.index]=Mathf.Min(pose.muscles[control.index],control.curl);
        }
    }
}
