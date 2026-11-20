using System;
using System.Collections.Generic;
using UnityEngine;
using YARG.Core;
using YARG.Core.Dance;
using YARG.Gameplay;
using YARG.Gameplay.Player;
using YARG.Gameplay.Visuals;
using YARG.Helpers.Extensions;
using YARG.Settings.Customization;
using YARG.Settings.Metadata;
using YARG.Themes;

namespace YARG.Settings.Preview
{
    public class FakeNote : MonoBehaviour, IPoolable
    {
        [Serializable]
        public struct NoteTypePair
        {
            public ThemeNoteType NoteType;
            public NoteGroup Group;
        }

        public Pool ParentPool { get; set; }

        public FakeNoteData NoteRef { get; set; }
        public FakeTrackPlayer FakeTrackPlayer { get; set; }

        private NoteGroup _currentNoteGroup;
        [SerializeField] private MeshRenderer _danceGlyph;

        // We can't use a dictionary here (Unity L)
        [SerializeField]
        private List<NoteTypePair> _noteGroups;

        private readonly List<Material> _materials = new();

        public void EnableFromPool()
        {
            if (_danceGlyph != null)
            {
                transform.localPosition = new Vector3(-.75f + (NoteRef.Fret - 1) * .5f, 0, 0);
                OnSettingChanged(); Update(); gameObject.SetActive(true);
                return;
            }
            // Disable all note groups
            foreach (var noteGroup in _noteGroups)
            {
                noteGroup.Group.SetActive(false);
            }

            // Find the correct note group
            _currentNoteGroup = _noteGroups.Find(i => i.NoteType == NoteRef.NoteType).Group;

            if (!NoteRef.CenterNote)
            {
                // Set the position
                int fretCount = FakeTrackPlayer.CurrentGameModeInfo.LaneCount;
                transform.localPosition = new Vector3(
                    TrackPlayer.TRACK_WIDTH / fretCount * NoteRef.Fret - TrackPlayer.TRACK_WIDTH / 2f - 1f / fretCount,
                    0f, 0f);
            }
            else
            {
                // Set the position
                transform.localPosition = Vector3.zero;
            }

            _currentNoteGroup.SetActive(true);
            _currentNoteGroup.Initialize();

            // Get all materials
            _materials.Clear();
            var meshRenderers = GetComponentsInChildren<MeshRenderer>(true);
            foreach (var meshRenderer in meshRenderers)
            {
                foreach (var material in meshRenderer.materials)
                {
                    _materials.Add(material);
                }
            }

            // Force update position and other properties
            OnSettingChanged();
            Update();

            gameObject.SetActive(true);
        }

        public void OnSettingChanged()
        {
            var cameraPreset = PresetsTab.GetLastSelectedPreset(CustomContentManager.CameraSettings);
            var colorProfile = PresetsTab.GetLastSelectedPreset(CustomContentManager.ColorProfiles);
            var highwayPreset = PresetsTab.GetLastSelectedPreset(CustomContentManager.HighwayPresets);
            if (_danceGlyph != null)
            {
                _danceGlyph.transform.localRotation = FakeTrackPlayer.DanceSkin.Rotation(NoteRef.Fret - 1);
                FakeTrackPlayer.DanceSkin.Paint(_danceGlyph, DanceHighwaySkin.NoteColor(NoteRef.Time * 2));
                return;
            }

            // Update color
            var color = FakeTrackPlayer.CurrentGameModeInfo.NoteColorProvider(colorProfile, NoteRef);
            _currentNoteGroup.SetColorWithEmission(color, color);

            // Update height
            transform.localScale = new Vector3(1f, highwayPreset.NoteHeight, 1f);
        }

        protected void Update()
        {
            if (_danceGlyph != null)
            {
                transform.localPosition = transform.localPosition.WithZ(DanceNative.yarg_dance_position(
                    (float)(NoteRef.Time - FakeTrackPlayer.PreviewTime) * 2, FakeTrackPlayer.NOTE_SPEED));
                if (NoteRef.Time < FakeTrackPlayer.PreviewTime) ParentPool.Return(this);
                return;
            }
            float z =
                TrackPlayer.STRIKE_LINE_POS                            // Shift origin to the strike line
                + (float) (NoteRef.Time - FakeTrackPlayer.PreviewTime) // Get time of note relative to now
                * FakeTrackPlayer.NOTE_SPEED;                          // Adjust speed (units/s)

            var cacheTransform = transform;
            cacheTransform.localPosition = cacheTransform.localPosition.WithZ(z);

            if (z < -4f)
            {
                ParentPool.Return(this);
            }
        }

        public void DisableIntoPool()
        {
            gameObject.SetActive(false);
        }

        public static GameObject CreateFakeNoteFromTheme(ThemePreset themePreset, VisualStyle style)
        {
            // Create GameObject
            var notePrefab = new GameObject("Note Prefab");
            notePrefab.transform.localPosition = Vector3.zero;
            var fakeNote = notePrefab.AddComponent<FakeNote>();

            // Get models
            var themeContainer = ThemeManager.Instance.GetThemeContainer(themePreset, style);
            var models = themeContainer.GetThemeComponent().GetNoteModelsForVisualStyle(style, false);

            // Create note groups
            fakeNote._noteGroups = new List<NoteTypePair>();
            foreach (var (type, gameObject) in models)
            {
                fakeNote._noteGroups.Add(new NoteTypePair
                {
                    NoteType = type,
                    Group = NoteGroup.CreateNoteGroupFromTheme(notePrefab.transform, gameObject)
                });
            }

            // Set layer
            fakeNote.transform.SetLayerRecursive(LayerMask.NameToLayer("Settings Preview"));

            return notePrefab;
        }

        public static GameObject CreateFakeDanceNote(DanceHighwaySkin skin)
        {
            var prefab = new GameObject("YACE preview arrow");
            var note = prefab.AddComponent<FakeNote>();
            note._danceGlyph = skin.Create(prefab.transform, "YACE arrow", skin.Arrow, LayerMask.NameToLayer("Settings Preview"));
            return prefab;
        }
    }
}
