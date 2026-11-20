using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YARG.Core;
using YARG.Core.Audio;
using YARG.Core.Chart;
using YARG.Core.Dance;
using YARG.Core.Engine;
using YARG.Core.Engine.Keys;
using YARG.Core.Input;
using YARG.Core.Replays;
using YARG.Gameplay.HUD;
using YARG.Gameplay.Visuals;
using YARG.Player;

namespace YARG.Gameplay.Player
{
    // Uses the existing highway prefab, camera, material, pools and HUD.
    // Rust supplies YACE's arrows, beat colors, motion and judgments.
    public sealed class DancePlayer : TrackPlayer
    {
        public DanceEngine Engine { get; private set; }
        public override BaseEngine BaseEngine => Engine;
        public override bool ShouldUpdateInputsOnResume => true;
        protected override float[] StarMultiplierThresholds { get; set; } = new[] { .10f, .25f, .45f, .65f, .85f, 1f };
        public float UnitsPerBeat { get; private set; }
        public DanceHighwaySkin Skin { get; private set; }
        private readonly MeshRenderer[] _receptors = new MeshRenderer[4];
        private readonly double[] _hitTimes = { -100, -100, -100, -100 };
        private readonly Color[] _hitColors = new Color[4];
        private readonly bool[] _pressed = new bool[4], _held = new bool[4];
        private GameObject _noteTemplate;
        private readonly DanceRenderNote[] _visible = new DanceRenderNote[500];
        private readonly HashSet<int> _visibleIndices = new();
        private readonly List<DanceHighwayNote> _previous = new();
        private bool _lowered;

        public static DancePlayer Create(GameObject highwayPrefab, Vector3 position)
        {
            // Instantiate inactive so every child finds the replacement player
            // when Awake runs. All serialized highway references stay intact.
            var staging = new GameObject("Dance highway staging");
            staging.SetActive(false);
            var highway = Instantiate(highwayPrefab, staging.transform);
            highway.SetActive(false);
            var original = highway.GetComponent<TrackPlayer>();
            var serialized = JsonUtility.ToJson(original);
            DestroyImmediate(original);
            var player = highway.AddComponent<DancePlayer>();
            JsonUtility.FromJsonOverwrite(serialized, player);
            highway.transform.SetParent(null, false);
            highway.transform.SetPositionAndRotation(position, highwayPrefab.transform.rotation);
            highway.name = "Dance Highway";
            Destroy(staging);
            highway.SetActive(true);
            return player;
        }

        public override void Initialize(int index, YargPlayer player, SongChart chart, TrackView trackView,
            StemMixer mixer, int? lastHighScore)
        {
            HighwayCount = index == 0 ? 1 : Math.Max(HighwayCount, index + 1);
            LaneCount = 4;
            transform.SetParent(GameObject.Find("Visuals").transform);
            base.Initialize(index, player, chart, trackView, mixer, lastHighScore);
            var notes = chart.DanceParts?.GetChart(player.Profile.CurrentInstrument, player.Profile.CurrentDifficulty)
                ?? throw new InvalidOperationException("Dance charts are missing; rescan songs");
            Engine = new DanceEngine(notes, chart.SyncTrack, player.Profile.IsBot,
                player.EngineParameterOverride as KeysEngineParameters);
            Engine.SetSpeed(player.IsReplay ? Engine.Parameters.SongSpeed : GameManager.SongSpeed);
            Engine.OnComboIncrement += OnComboIncrement;
            Engine.OnComboReset += OnComboReset;
            EngineContainer = GameManager.EngineManager.Register(Engine, player.Profile.CurrentInstrument, chart, player.RockMeterPreset);
            TotalNotes = Engine.Stats.TotalNotes;
            HitWindow = Engine.Parameters.HitWindow;
            ComboMeter.Initialize(player.EnginePreset, 1, GameManager.Players.Count > 1);
            TrackMaterial.Initialize(player.HighwayPreset);
            CameraPositioner.Initialize(player.CameraPreset);
            GetComponentInChildren<FretArray>(true).gameObject.SetActive(false);
            Skin = new DanceHighwaySkin();
            for (int lane = 0; lane < 4; lane++)
            {
                _receptors[lane] = Skin.Create(transform, "YACE arrow receptor", Skin.Receptor, NotePool.Prefab.layer);
                _receptors[lane].transform.localPosition = new Vector3(-.75f + .5f * lane, 0, Skin.Data.ReceptorZ);
                _receptors[lane].transform.localRotation = Skin.Rotation(lane, player.CameraPreset.Rotation);
            }
            _noteTemplate = CreateNoteTemplate();
            NotePool.SetPrefabAndReset(_noteTemplate);
            UpdateUnitsPerBeat();
        }

        private GameObject CreateNoteTemplate()
        {
            var template = new GameObject("Dance themed highway note");
            template.SetActive(false);
            template.transform.SetParent(transform, false);
            template.layer = NotePool.Prefab.layer;
            var visual = template.AddComponent<DanceHighwayNote>();
            visual.Glyph = Skin.Create(template.transform, "YACE vector arrow", Skin.Arrow, template.layer);
            var sustain = NotePool.Prefab.GetComponentsInChildren<SustainLine>(true)
                .First(s => s.gameObject.name == "Normal Sustain Line");
            visual.Sustain = Instantiate(sustain.gameObject, template.transform).GetComponent<SustainLine>();
            return template;
        }

        private void UpdateUnitsPerBeat()
        {
            UnitsPerBeat = NoteSpeed * Skin.Data.ScrollFactor;
        }

        protected override void UpdateVisuals(double time)
        {
            if (Engine == null) return;
            UpdateUnitsPerBeat();
            TrackView.UpdateHUDPosition(HighwayIndex, HighwayCount);
            TrackMaterial.SetTrackScroll(Engine.VisualBeat(time), -UnitsPerBeat);
            TrackMaterial.GrooveMode = false;
            TrackMaterial.StarpowerMode = false;
            var stats = Engine.Stats;
            IsFc = Engine.Snapshot.Misses + Engine.Snapshot.Drops + Engine.Snapshot.Mines == 0;
            ComboMeter.SetCombo(1, 1, 1, stats.Combo, false);
            ComboMeter.SetFullCombo(IsFc);
            StarpowerBar.SetStarpower(0, false, false);
            SunburstEffects.SetSunburstEffects(false, false, 1);
            TrackView.UpdateNoteStreak(stats.Combo);
            UpdateHighwayNotes(time);
            UpdateReceptors(time);
            bool lower = time > Engine.Chart.EndTime + 1 || GameManager.PlayerHasFailed;
            if (lower != _lowered)
            {
                if (lower) CameraPositioner.Lower(true); else CameraPositioner.Raise(false);
                _lowered = lower;
            }
        }

        private void UpdateHighwayNotes(double time)
        {
            _previous.Clear();
            foreach (var pooled in NotePool.AllSpawned) _previous.Add((DanceHighwayNote)pooled);
            foreach (var visual in _previous)
            {
                var state = Engine.State(visual.Index);
                if (visual.Render.State == 0 && state is 1 or 3 or 4)
                {
                    _hitTimes[visual.Render.Lane] = time;
                    _hitColors[visual.Render.Lane] = DanceHighwaySkin.NoteColor(Engine.Chart.Notes[visual.Index].Beat);
                }
            }
            int count = Engine.Render(time, (Skin.Data.ReceptorZ - ZeroFadePosition - 1) / UnitsPerBeat,
                (Skin.Data.ReceptorZ - STRIKE_LINE_POS + 1) / UnitsPerBeat, _visible);
            Array.Clear(_held, 0, _held.Length);
            _visibleIndices.Clear();
            for (int i = 0; i < count; i++) _visibleIndices.Add(_visible[i].Index);
            foreach (var visual in _previous)
                if (!_visibleIndices.Contains(visual.Index)) NotePool.Return(visual);
            for (int i = 0; i < count; i++)
            {
                var render = _visible[i];
                if (render.State == 3) _held[render.Lane] = true;
                var visual = NotePool.GetByKey(render.Index) as DanceHighwayNote;
                if (visual == null)
                {
                    visual = NotePool.KeyedTakeWithoutEnabling(render.Index) as DanceHighwayNote;
                    if (visual == null) continue;
                    visual.Owner = this;
                    visual.Index = render.Index;
                    visual.Render = render;
                    visual.EnableFromPool();
                }
                else visual.Render = render;
            }
        }

        private void UpdateReceptors(double time)
        {
            double beat = Engine.VisualBeat(time);
            float pulse = (float)(1 - (beat - Math.Floor(beat))) * .08f;
            for (int lane = 0; lane < 4; lane++)
            {
                float flash = Mathf.Clamp01(1 - (float)(time - _hitTimes[lane]) / .24f);
                bool active = _held[lane] || _pressed[lane];
                Color color = flash > 0 ? _hitColors[lane] : active ? new Color32(241, 215, 140, 255) : new Color32(147, 166, 187, 255);
                Skin.Paint(_receptors[lane], color, 1, active ? .65f : flash * flash);
                _receptors[lane].transform.localRotation = Skin.Rotation(lane, Player.CameraPreset.Rotation);
                _receptors[lane].transform.localScale = Vector3.one * (1 + pulse + flash * .12f);
            }
        }

        public bool HasVisibleField => TrackCamera != null && CameraPositioner != null && TrackMaterial != null
            && _receptors.All(r => r != null) && NotePool.Prefab.GetComponent<DanceHighwayNote>() != null;

        protected override void UpdateInputs(double time)
        {
            if (time + InputCalibration < Engine.CurrentTime) return;
            base.UpdateInputs(time);
        }
        protected override bool InterceptInput(ref GameInput input) => input.Action is < 0 or > 3;
        protected override void OnInputQueued(GameInput input)
        {
            if (input.Action is >= 0 and < 4) _pressed[input.Action] = input.Button;
        }
        protected override void ResetVisuals()
        {
            base.ResetVisuals();
            Array.Clear(_pressed, 0, _pressed.Length);
            Array.Clear(_held, 0, _held.Length);
            for (int i = 0; i < 4; i++) _hitTimes[i] = -100;
            BeatlineIndex = 0;
        }
        public override void Rewind(double visualTime) { }
        public override void PostRewind(double visualTime) { }
        public override void ResetPracticeSection()
        {
            Engine.Reset(); TotalNotes = Engine.Stats.TotalNotes;
            base.ResetPracticeSection();
        }
        public override void SetPracticeSection(uint start, uint end)
        {
            Engine.SetRange(SyncTrack.TickToTime(start), SyncTrack.TickToTime(end));
            TotalNotes = Engine.Stats.TotalNotes; ResetVisuals();
        }
        public override void SetStemMuteState(bool muted) { }
        public override void SetStarPowerFX(bool active) { }
        public override (ReplayFrame Frame, ReplayStats Stats) ConstructReplayData()
        {
            var stats = new DanceStats(Engine.Stats);
            return (new ReplayFrame(Player.Profile, Engine.Parameters, stats, ReplayInputs.ToArray()),
                stats.ConstructReplayStats(Player.Profile.Name, Player.IsReplay));
        }
        protected override void FinishDestruction()
        {
            Engine?.Dispose();
            if (_noteTemplate != null) Destroy(_noteTemplate);
            Skin?.Dispose();
        }
    }
}
