#if YARG_DANCE_CHECKS
using System;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using YARG.Core;
using YARG.Core.Dance;
using YARG.Core.Game;
using YARG.Gameplay.Player;
using YARG.Gameplay.Visuals;
using YARG.Helpers;
using YARG.Menu.Persistent;
using YARG.Player;
using YARG.Settings;
using YARG.Scores;
using YARG.Song;

namespace YARG.Gameplay
{
    // Compiled only into the acceptance build. Runs the normal scanner, players,
    // audio clock, rendering and score screen with isolated application data.
    public sealed class DanceAcceptance : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!Environment.GetCommandLineArgs().Contains("-dance-check")) return;
            var go = new GameObject("Dance acceptance");
            DontDestroyOnLoad(go);
            go.AddComponent<DanceAcceptance>().Run().Forget();
        }

        private async UniTaskVoid Run()
        {
            var args = Environment.GetCommandLineArgs();
            bool visualRun = args.Contains("-dance-visual-check");
            var index = Array.IndexOf(args, "-dance-check");
            var folder = index + 1 < args.Length ? args[index + 1] : "";
            string output = CommandLineArgs.PersistentDataPath;
            try
            {
                if (string.IsNullOrEmpty(output) || !Path.GetFileName(output).StartsWith("yarg-dance-acceptance-"))
                    throw new InvalidOperationException("Acceptance requires an isolated -persistent-data-path yarg-dance-acceptance-* directory");
                await UniTask.WaitUntil(() => GlobalVariables.Instance != null && GlobalVariables.Instance.CurrentScene == SceneIndex.Menu)
                    .Timeout(TimeSpan.FromSeconds(60));
                await UniTask.WaitUntil(() => !LoadingScreen.IsActive).Timeout(TimeSpan.FromSeconds(60));
                DialogManager.Instance.ClearDialog();
                SettingsManager.Settings.SongFolders.Clear();
                SettingsManager.Settings.SongFolders.Add(folder);
                SettingsManager.Settings.SaveScoresWithBots.Value = true;
                SettingsManager.Settings.PauseOnFocusLoss.Value = false;
                using (var context = new LoadingContext()) await SongContainer.RunRefresh(false, context);
                var song = SongContainer.Songs.Single(s => s.HasInstrument(Instrument.Dance) && s.HasInstrument(Instrument.FourKey)
                    && s.HasInstrument(Instrument.FiveFretGuitar) && s.HasInstrument(Instrument.FourLaneDrums) && s.HasInstrument(Instrument.Vocals));
                YargProfile keyboardProfile = null;
                Keyboard keyboard = null;
                foreach (var instrument in new[] { Instrument.FiveFretGuitar, Instrument.FourLaneDrums, Instrument.Vocals, Instrument.Dance, Instrument.FourKey })
                {
                    var profile = new YargProfile
                    {
                        Name = instrument.ToString() + " Check", IsBot = visualRun || instrument != Instrument.FourKey,
                        GameMode = instrument.ToNativeGameMode(), CurrentInstrument = instrument,
                        PreferredInstrument = instrument, CurrentDifficulty = Difficulty.Expert
                    };
                    PlayerContainer.AddProfile(profile);
                    var player = PlayerContainer.CreatePlayerFromProfile(profile, false);
                    if (instrument == Instrument.FourKey)
                    {
                        keyboardProfile = profile;
                        if (visualRun) continue;
                        player.DisableInputs();
                        keyboard = InputSystem.AddDevice<Keyboard>("Dance acceptance keyboard");
                        player.Bindings.AddDevice(keyboard);
                        player.Bindings.SetDefaultBinds(keyboard);
                        player.EnableInputs();
                    }
                }
                GlobalVariables.State.CurrentSong = song;
                GlobalVariables.State.ShowSongs.Clear(); GlobalVariables.State.ShowSongs.Add(song);
                GlobalVariables.State.PlayingAShow = false;
                GlobalVariables.State.IsPractice = false;
                GlobalVariables.Instance.LoadScene(SceneIndex.Gameplay);
                await UniTask.WaitUntil(() => FindAnyObjectByType<GameManager>() is { IsSongStarted: true })
                    .Timeout(TimeSpan.FromSeconds(90));
                var manager = FindAnyObjectByType<GameManager>();
                if (manager.Players.Count != 5) throw new Exception($"Expected five players, found {manager.Players.Count}");
                var keyboardNotes = manager.Chart.DanceParts.GetChart(Instrument.FourKey, Difficulty.Expert).Notes;
                int previousMask = 0;
                bool captured = false;
                (DanceHighwayNote Note, int Index, float Z)[] probes = null;
                bool upwardFlowVerified = false;
                var deadline = DateTime.UtcNow.AddSeconds(40);
                while (manager != null && GlobalVariables.Instance.CurrentScene == SceneIndex.Gameplay)
                {
                    if (DateTime.UtcNow > deadline) throw new TimeoutException("Gameplay did not reach results");
                    var time = manager.InputTime;
                    if (probes == null && manager.VisualTime >= 3)
                    {
                        var arrows = FindObjectsByType<DanceHighwayNote>(FindObjectsSortMode.None);
                        probes = manager.Players.OfType<DancePlayer>().Select(p => arrows.First(n =>
                            n.Owner == p && n.Render.State == 0 && p.Engine.Chart.Notes[n.Index].Time > manager.VisualTime + 1))
                            .Select(n => (n, n.Index, n.transform.localPosition.z)).ToArray();
                    }
                    if (!upwardFlowVerified && probes != null && manager.VisualTime >= 3.3)
                    {
                        if (probes.Length != 2 || probes.Any(p => p.Note.Index != p.Index || p.Note.transform.localPosition.z <= p.Z))
                            throw new Exception("YACE arrows did not move upward toward their fixed receptors");
                        upwardFlowVerified = true;
                    }
                    int mask = 0;
                    foreach (var note in keyboardNotes)
                        if (note.Judgable && note.Kind != "mine" && time >= note.Time && time < Math.Max(note.EndTime + .02, note.Time + .15)) mask |= 1 << note.Lane;
                    if (keyboard != null && mask != previousMask)
                    {
                        var keys = new[] { Key.D, Key.F, Key.J, Key.K };
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys.Where((_, lane) => (mask & 1 << lane) != 0).ToArray()));
                        previousMask = mask;
                    }
                    // PNG capture can block input injection. The visual run uses
                    // bots; the keyboard run captures during the fixture lead-in.
                    if (!captured && manager.VisualTime >= (visualRun ? 6 : 1))
                    {
                        var fields = manager.Players.OfType<DancePlayer>().ToArray();
                        if (fields.Length != 2 || fields.Any(p => !p.HasVisibleField)) throw new Exception("Dance profiles are not using the normal four-receptor highways");
                        Debug.developerConsoleVisible = false;
                        ScreenCapture.CaptureScreenshot(Path.Combine(output, "mixed-band.png"));
                        captured = true;
                    }
                    await UniTask.NextFrame();
                }
                await UniTask.WaitUntil(() => GlobalVariables.Instance.CurrentScene == SceneIndex.Score)
                    .Timeout(TimeSpan.FromSeconds(40));
                await UniTask.Delay(1000);
                var scores = GlobalVariables.State.ScoreScreenStats.Value.PlayerScores;
                if (!upwardFlowVerified) throw new Exception("Arrow motion was not verified");
                File.WriteAllText(Path.Combine(output, "player-stats.json"), JsonConvert.SerializeObject(scores.Select(s => new {
                    instrument = s.Player.Profile.CurrentInstrument.ToString(), score = s.Stats.TotalScore,
                    hits = s.Stats.NotesHit, total = s.Stats.TotalNotes }), Formatting.Indented));
                if (scores.Length != 5 || scores.Any(s => s.Stats.NotesHit == 0)) throw new Exception("Mixed-band results are incomplete");
                var keyboardScore = scores.Single(s => s.Player.Profile.CurrentInstrument == Instrument.FourKey);
                if (keyboardScore.Stats.NotesHit != keyboardScore.Stats.TotalNotes) throw new Exception("Keyboard binding path missed fixture notes");
                var saved = ScoreContainer.GetHighScore(song.Hash, keyboardProfile.Id, Instrument.FourKey, false);
                if (!visualRun && (saved == null || saved.Score != keyboardScore.Stats.TotalScore)) throw new Exception("4-key score was not persisted");
                var result = scores.Select(s => new { instrument = s.Player.Profile.CurrentInstrument.ToString(),
                    score = s.Stats.TotalScore, hits = s.Stats.NotesHit, total = s.Stats.TotalNotes, fullCombo = s.Stats.IsFullCombo }).ToArray();
                File.WriteAllText(Path.Combine(output, "result.json"), JsonConvert.SerializeObject(new { passed = true, upwardFlowVerified, keyboardInputVerified = !visualRun, scorePersistenceVerified = !visualRun, players = result }, Formatting.Indented));
                ScreenCapture.CaptureScreenshot(Path.Combine(output, "results.png"));
                Debug.Log("DANCE_ACCEPTANCE_PASSED: five players, shared audio clock, dance fields, and score screen");
                await UniTask.Delay(1000);
                Application.Quit(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                if (!string.IsNullOrEmpty(output) && Directory.Exists(output))
                    File.WriteAllText(Path.Combine(output, "failure.txt"), ex.ToString());
                Application.Quit(1);
            }
        }
    }
}
#endif
