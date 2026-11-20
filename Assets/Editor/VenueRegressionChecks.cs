using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DG.Tweening;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core;
using YARG.Core.Chart;
using YARG.Gameplay.HUD;
using YARG.Venue;
using YARG.Venue.Stage;
using Object = UnityEngine.Object;

namespace Editor
{
    // These checks use the existing editor assembly so no production assembly layout
    // or additional test package is required. Run with -executeMethod Editor.VenueRegressionChecks.RunBatch (without -quit).
    public static class VenueRegressionChecks
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string BatchKey = "YARG.VenueRegressionChecks.Batch";

        [InitializeOnLoadMethod]
        private static void RegisterBatch()
        {
            if (SessionState.GetBool(BatchKey, false))
            {
                EditorApplication.playModeStateChanged -= RunInPlayMode;
                EditorApplication.playModeStateChanged += RunInPlayMode;
            }
        }

        public static void RunBatch()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.playModeStartScene = null;
            SessionState.SetBool(BatchKey, true);
            RegisterBatch();
            EditorApplication.EnterPlaymode();
        }

        private static void RunInPlayMode(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            SessionState.SetBool(BatchKey, false);
            try
            {
                Run();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static void Run()
        {
            CheckStageRoot();
            CheckNotificationCleanup();
            CheckChartTiming();
            Debug.Log("VENUE_REGRESSION_PASS: legacy root, active tween cleanup, difficulty fallback, note timing and rests");
        }

        private static void CheckStageRoot()
        {
            var root = new GameObject("Legacy venue check");
            root.SetActive(false);
            try
            {
                root.AddComponent<BundleBackgroundManager>();
                var stage = new GameObject("Stage");
                stage.transform.SetParent(root.transform);
                var manager = stage.AddComponent<StageManager>();
                var fog = new GameObject("Fog");
                fog.transform.SetParent(root.transform);
                fog.AddComponent<StageElement>().ElementType = StageElementType.Fog;
                Invoke(manager, "OnChartLoaded", new SongChart(192));
                Require(ReferenceEquals(Field<GameObject>(manager, "_venue"), root), "Legacy root was not resolved");
                Require(Field<List<StageElement>>(manager, "_fogElements").Count == 1,
                    "Stage root fallback lost sibling stage effects");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void CheckNotificationCleanup()
        {
            var root = new GameObject("Notification check", typeof(RectTransform));
            root.SetActive(false);
            Sequence animation = null;
            try
            {
                var notification = root.AddComponent<TextNotifications>();
                var background = root.AddComponent<Image>();
                SetField(notification, "_containerRect", root.GetComponent<RectTransform>());
                SetField(notification, "_notificationBackground", background);
                animation = (Sequence) Invoke(notification, "CreateAnimationSequence");
                SetField(notification, "_animationSequence", animation);
                animation.Goto(0.05f, true);
                Require(animation.IsActive(), "Notification did not start");
                Require(ReferenceEquals(Field<Sequence>(notification, "_animationSequence"), animation), "Test did not retain active sequence");
                Invoke(notification, "OnDisable");
                Require(!animation.IsActive(), "Disabling notification left its active tween alive");
                Require(Field<Sequence>(notification, "_animationSequence") == null,
                    "Disabled notification retained its tween");
                Invoke(notification, "OnDisable");
            }
            finally
            {
                animation?.Kill();
                Object.DestroyImmediate(root);
            }
        }

        private static void CheckChartTiming()
        {
            var chart = new SongChart(192);
            var hard = new InstrumentDifficulty<GuitarNote>(Instrument.FiveFretGuitar, Difficulty.Hard);
            hard.Notes.Add(new GuitarNote(FiveFretGuitarFret.Green, default, default, default, 2, 0.5, 768, 192));
            hard.Notes.Add(new GuitarNote(FiveFretGuitarFret.Yellow, default, default, default, 7, 0, 2688, 0));
            chart.FiveFretGuitar.AddDifficulty(Difficulty.Hard, hard);
            // Empty Expert placeholders are common in loaded charts.
            chart.FiveFretGuitar.AddDifficulty(Difficulty.Expert,
                new InstrumentDifficulty<GuitarNote>(Instrument.FiveFretGuitar, Difficulty.Expert));
            Require(ReferenceEquals(chart.FiveFretGuitar.GetAnimationDifficulty(), hard),
                "Hard-only chart did not fall back from empty Expert");
            Require(chart.FiveFretBass.GetAnimationDifficulty().Notes.Count == 0,
                "Absent bass part should remain idle");

            var names = new[] { "n0", "n1", "n2", "n3", "n4", "n5", "n6" };
            var hashes = new VenueHashLibrary(names, names, names, names, names, names, names, names, "Light", "Post");
            var queue = new AnimatorCommandQueue();
            new GuitarChannel(null, hashes, 6).BuildCommands(chart, queue);
            queue.Sort();
            var commands = Field<List<AnimatorCommand>>(queue, "_commands")
                .Where(command => command.Type == AnimatorCommandType.BoolOn).ToList();
            Require(commands.Count == 2, "Unexpected number of chart-driven guitar hits");
            Require(Math.Abs(commands[0].Time - 1.9) < 0.0001 && Math.Abs(commands[1].Time - 6.9) < 0.0001,
                "Animation commands did not retain chart timing and lead-in");
            Require(Math.Abs(commands[0].Value - 0.5) < 0.0001,
                "Guitar sustain duration was not retained");
            Require(!commands.Any(command => command.Time > 2.5 && command.Time < 6),
                "Animation generated guitar hits during a charted rest");

            var expert = chart.FiveFretGuitar.GetDifficulty(Difficulty.Expert);
            expert.Notes.Add(new GuitarNote(1, default, default, default, 1, 0, 384, 0));
            Require(ReferenceEquals(chart.FiveFretGuitar.GetAnimationDifficulty(), expert),
                "Existing Expert charts changed difficulty selection");
        }

        private static T Field<T>(object target, string name) =>
            (T) target.GetType().GetField(name, PrivateInstance).GetValue(target);

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, PrivateInstance).SetValue(target, value);

        private static object Invoke(object target, string name, params object[] args) =>
            target.GetType().GetMethod(name, PrivateInstance).Invoke(target, args);

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
