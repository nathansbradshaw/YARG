using System;
using UnityEngine;

namespace YARG.Venue.Characters
{
    // Bundle metadata; the existing authored venue actor continues to animate.
    public sealed class BandModelOptions : MonoBehaviour
    {
        public VenueCharacter.CharacterType[] AllowedRoles = Array.Empty<VenueCharacter.CharacterType>();
        public bool ExtrasOnly;
        public Transform[] LowerMouth = Array.Empty<Transform>();
        public Transform[] UpperMouth = Array.Empty<Transform>();
        [Range(0,0.1f)] public float MouthTravel=0.04f;
    }
}
