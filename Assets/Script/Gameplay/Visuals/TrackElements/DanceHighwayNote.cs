using UnityEngine;
using YARG.Gameplay.Player;
using YARG.Core.Dance;

namespace YARG.Gameplay.Visuals
{
    // YACE arrows on the normal world-space highway and sustain renderer.
    public sealed class DanceHighwayNote : BaseElement
    {
        [SerializeField] public MeshRenderer Glyph;
        [SerializeField] public SustainLine Sustain;
        public DancePlayer Owner;
        public int Index;
        public DanceRenderNote Render;
        public override double ElementTime => Owner.Engine.Chart.Notes[Index].Time;

        protected override void InitializeElement()
        {
            transform.localScale = Vector3.one;
            Glyph.GetComponent<MeshFilter>().sharedMesh = Render.Kind == 3 ? Owner.Skin.Mine :
                Render.Kind == 4 ? Owner.Skin.Lift : Owner.Skin.Arrow;
            UpdateElement();
        }

        protected override bool UpdateElementPosition()
        {
            transform.localPosition = new Vector3(-.75f + Render.Lane * .5f, 0,
                DanceNative.yarg_dance_position(Render.Head, Owner.NoteSpeed));
            Glyph.transform.localRotation = Owner.Skin.Rotation(Render.Lane);
            return true;
        }

        protected override void UpdateElement()
        {
            Glyph.enabled = Render.State != 3;
            bool fake = Render.Kind == 5 || (Render.State == 7 && Render.Kind != 3);
            Color color = fake ? new Color32(129, 149, 171, 255) :
                Owner.Engine.IsStarPowerNote(Index) ? new Color32(80, 230, 255, 255) : DanceHighwaySkin.NoteColor(Owner.Engine.Chart.Notes[Index].Beat);
            Owner.Skin.Paint(Glyph, color, fake ? .38f : 1, Owner.Engine.Stats.IsStarPowerActive ? .6f : 0);
            if (Render.Kind == 3)
                Glyph.transform.localRotation *= Quaternion.Euler(0, (float)Owner.Engine.VisualBeat(Owner.Engine.CurrentTime) * 40, 0);
            if (Render.Kind is 1 or 2)
            {
                Sustain.gameObject.SetActive(true);
                float length = (Render.Tail - Render.Head) * Owner.UnitsPerBeat;
                Sustain.transform.localRotation = Quaternion.Euler(0, length < 0 ? 180 : 0, 0);
                Sustain.Initialize(Mathf.Abs(length));
                Sustain.SetState(Render.State == 3 ? SustainState.Hitting : SustainState.Waiting,
                    Render.Kind == 2 ? new Color32(200, 138, 255, 255) : new Color32(241, 207, 118, 255));
                Sustain.UpdateSustainLine(Owner.Skin.Data.ReceptorZ);
            }
            else Sustain.gameObject.SetActive(false);
        }

        protected override void HideElement()
        {
            if (Glyph != null) Glyph.enabled = false;
            if (Sustain != null) Sustain.gameObject.SetActive(false);
        }
    }
}
