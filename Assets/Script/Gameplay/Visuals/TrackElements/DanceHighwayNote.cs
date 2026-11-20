using UnityEngine;
using YARG.Gameplay.Player;
using YARG.Core.Dance;
using YARG.Helpers.Extensions;

namespace YARG.Gameplay.Visuals
{
    // Standard YARG theme models and sustain renderer, positioned on the same
    // world-space highway by the independent Rust simfile clock.
    public sealed class DanceHighwayNote : BaseElement
    {
        [SerializeField] public NoteGroup Normal, Lift, Mine;
        [SerializeField] public SustainLine Sustain;
        public DancePlayer Owner;
        public int Index;
        public DanceRenderNote Render;
        public override double ElementTime => Owner.Engine.Chart.Notes[Index].Time;

        protected override void InitializeElement()
        {
            foreach (var group in new[] { Normal, Lift, Mine }) group.Initialize();
            transform.localScale = new Vector3(1.25f, Owner.Player.HighwayPreset.NoteHeight, 1);
            UpdateElement();
        }

        protected override bool UpdateElementPosition()
        {
            transform.localPosition = new Vector3(-.75f + Render.Lane * .5f, 0,
                TrackPlayer.STRIKE_LINE_POS + Render.Head * Owner.UnitsPerBeat);
            return true;
        }

        protected override void UpdateElement()
        {
            HideElement();
            var color = Owner.Player.ColorProfile.FiveFretGuitar.GetNoteColor(Render.Lane + 1).ToUnityColor();
            if (Render.Kind == 3) color = new Color(1, .08f, .08f);
            else if (Render.Kind == 5 || Render.State == 7) color = new Color(.2f, .2f, .2f);
            var group = Render.Kind == 3 ? Mine : Render.Kind == 4 ? Lift : Normal;
            if (Render.State != 3)
            {
                group.SetActive(true);
                group.SetColorWithEmission(color, color);
            }
            if (Render.Kind is 1 or 2)
            {
                Sustain.gameObject.SetActive(true);
                float length = (Render.Tail - Render.Head) * Owner.UnitsPerBeat;
                Sustain.transform.localRotation = Quaternion.Euler(0, length < 0 ? 180 : 0, 0);
                Sustain.Initialize(Mathf.Abs(length));
                Sustain.SetState(Render.State == 3 ? SustainState.Hitting : SustainState.Waiting,
                    Render.Kind == 2 ? new Color(.8f, .3f, 1) : color);
                Sustain.UpdateSustainLine();
            }
        }

        protected override void HideElement()
        {
            Normal?.SetActive(false); Lift?.SetActive(false); Mine?.SetActive(false);
            if (Sustain != null) Sustain.gameObject.SetActive(false);
        }
    }
}
