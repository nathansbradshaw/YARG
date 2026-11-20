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
using YARG.Helpers.Extensions;
using YARG.Player;
using YARG.Playback;
using YARG.Themes;

namespace YARG.Gameplay.Player
{
    // Uses the same prefab, camera, material, receptors, pools and HUD as the
    // existing highways. Only chart timing and judgments come from the Rust mode.
    public sealed class DancePlayer : TrackPlayer
    {
        public DanceEngine Engine { get; private set; }
        public override BaseEngine BaseEngine => Engine;
        public override bool ShouldUpdateInputsOnResume => true;
        protected override float[] StarMultiplierThresholds { get; set; } = new[] { .10f, .25f, .45f, .65f, .85f, 1f };
        public float UnitsPerBeat { get; private set; }
        private FretArray _frets;
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
            _frets = GetComponentInChildren<FretArray>(true);
            _frets.Initialize(new Dictionary<int, int> { {1,0}, {2,1}, {3,2}, {4,3} }, 4,
                null, player.ColorProfile.FiveFretGuitar, player.ThemePreset, VisualStyle.FiveFretGuitar);
            _noteTemplate = CreateNoteTemplate();
            NotePool.SetPrefabAndReset(_noteTemplate);
            GameManager.BeatEventHandler.Visual.Subscribe(_frets.PulseFretColors, BeatEventType.StrongBeat);
            UpdateUnitsPerBeat();
        }

        private GameObject CreateNoteTemplate()
        {
            var theme = ThemeManager.Instance.GetThemeContainer(Player.ThemePreset, VisualStyle.FiveFretGuitar).GetThemeComponent();
            var models = theme.GetNoteModelsForVisualStyle(VisualStyle.FiveFretGuitar, false);
            var defaults = ThemeManager.Instance.GetThemeContainer(ThemePreset.Default, VisualStyle.FiveFretGuitar)
                .GetThemeComponent().GetNoteModelsForVisualStyle(VisualStyle.FiveFretGuitar, false);
            foreach (var pair in defaults) if (!models.ContainsKey(pair.Key)) models.Add(pair.Key, pair.Value);
            var template = new GameObject("Dance themed highway note");
            template.SetActive(false);
            template.transform.SetParent(transform, false);
            template.layer = NotePool.Prefab.layer;
            var visual = template.AddComponent<DanceHighwayNote>();
            visual.Normal = NoteGroup.CreateNoteGroupFromTheme(template.transform, models[ThemeNoteType.Normal]);
            visual.Lift = NoteGroup.CreateNoteGroupFromTheme(template.transform, models[ThemeNoteType.HOPO]);
            var drums = ThemeManager.Instance.GetThemeContainer(ThemePreset.Default, VisualStyle.FourLaneDrums)
                .GetThemeComponent().GetNoteModelsForVisualStyle(VisualStyle.FourLaneDrums, false);
            visual.Mine = NoteGroup.CreateNoteGroupFromTheme(template.transform,
                drums.TryGetValue(ThemeNoteType.Cymbal, out var mine) ? mine : models[ThemeNoteType.Tap]);
            foreach (var group in new[] { visual.Normal, visual.Lift, visual.Mine })
                group.transform.SetLayerRecursive(template.layer);
            var sustain = NotePool.Prefab.GetComponentsInChildren<SustainLine>(true)
                .First(s => s.gameObject.name == "Normal Sustain Line");
            visual.Sustain = Instantiate(sustain.gameObject, template.transform).GetComponent<SustainLine>();
            return template;
        }

        private void UpdateUnitsPerBeat()
        {
            var bpm = Engine.Chart.Timing["events"]?.FirstOrDefault(e => (string)e["kind"] == "BPMS" && (double)e["beat"] == 0)?["values"]?[0];
            UnitsPerBeat = (float)(60 / (bpm == null ? 120 : (double)bpm)) * NoteSpeed;
        }

        protected override void UpdateVisuals(double time)
        {
            if (Engine == null) return;
            UpdateUnitsPerBeat();
            TrackView.UpdateHUDPosition(HighwayIndex, HighwayCount);
            TrackMaterial.SetTrackScroll(time, NoteSpeed);
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
            UpdateBeatlines(time);
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
                    _frets.PlayHitAnimation(visual.Render.Lane + 1);
            }
            int count = Engine.Render(time, -2 / UnitsPerBeat, (ZeroFadePosition + 4 - STRIKE_LINE_POS) / UnitsPerBeat, _visible);
            _visibleIndices.Clear();
            for (int i = 0; i < count; i++) _visibleIndices.Add(_visible[i].Index);
            foreach (var visual in _previous)
                if (!_visibleIndices.Contains(visual.Index)) NotePool.Return(visual);
            for (int i = 0; i < count; i++)
            {
                var render = _visible[i];
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

        private void UpdateBeatlines(double time)
        {
            while (BeatlineIndex < Beatlines.Count && Beatlines[BeatlineIndex].Time <= time + SpawnTimeOffset)
            {
                var beat = Beatlines[BeatlineIndex++];
                if (beat.Time < time - .5 || !BeatlinePool.CanSpawnAmount(1)) continue;
                var line = BeatlinePool.TakeWithoutEnabling() as BeatlineElement;
                if (line == null) continue;
                line.BeatlineRef = beat;
                line.EnableFromPool();
            }
        }

        public bool HasVisibleField => TrackCamera != null && CameraPositioner != null && TrackMaterial != null
            && _frets != null && _frets.Frets.Count == 4 && NotePool.Prefab.GetComponent<DanceHighwayNote>() != null;

        protected override void UpdateInputs(double time)
        {
            if (time + InputCalibration < Engine.CurrentTime) return;
            base.UpdateInputs(time);
        }
        protected override bool InterceptInput(ref GameInput input) => input.Action is < 0 or > 3;
        protected override void OnInputQueued(GameInput input)
        {
            if (input.Action is >= 0 and < 4) _frets.SetPressed(input.Action + 1, input.Button);
        }
        protected override void ResetVisuals()
        {
            base.ResetVisuals();
            _frets?.ResetAll();
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
            if (_frets != null) GameManager.BeatEventHandler.Visual.Unsubscribe(_frets.PulseFretColors);
            Engine?.Dispose();
            if (_noteTemplate != null) Destroy(_noteTemplate);
        }
    }
}
