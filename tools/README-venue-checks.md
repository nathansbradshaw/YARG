# Venue checks

Run the focused Unity checks in a separate, otherwise idle editor process:

```sh
Unity -batchmode -projectPath "$PWD" \
  -executeMethod Editor.VenueRegressionChecks.RunBatch \
  -logFile /tmp/yarg-venue-checks.log
```

Do not pass `-quit`: the entry point enters Play Mode and exits after the checks.
It opens an empty scene, so save editor scene changes before running it.
Look for `VENUE_REGRESSION_PASS` and exit code 0. The checks exercise a legacy
stage without a serialized root, cleanup of an active notification tween,
Hard-only difficulty fallback, Expert preference, missing bass, note lead-in,
sustain duration, and a rest between notes using the actual guitar channel.

Generate three controlled charts with the standard-library-only Rust tool:

```sh
rustc tools/venue_fixture_generator.rs -o /tmp/yarg-venue-fixtures
/tmp/yarg-venue-fixtures /path/to/test-songs /path/to/45-second-audio.ogg
```

Use an isolated test song folder, scan it in YARG, and select a full-band venue.
The charts contain Expert full-band, Hard-only full-band, and Expert guitar-only
variants. Notes occur at 4–8, 12–20, and 24–33 seconds, with tempo changes from
120 to 180 to 80 BPM. The supplied audio is only a timing carrier: these are
deliberately synthetic charts, not transcriptions of the audio.

On macOS, the 2026-11-20 checks passed in Unity 6000.3.5f2 and the standalone
Mac build succeeded. GH3 Backyard Bash rendered with its full band, lights,
crowd and camera cuts on all three fixture variants; Expert completed, Hard
was restarted, and song transitions were exercised. No StageManager null
reference, DOTween missing-target exception, or duplicate-bundle load error
appeared in that controlled session.

Keep the evidence boundaries clear: command timing checks do not prove
frame-perfect rendered hits, synthesized vocals, lip sync, or musical mood
matching. Missing parts have no generated note hits; characters can still use
their venue's idle animations. The old venue's Windows shaders still emit
unsupported-shader warnings before the Metal replacements are applied, and
some authored camera views can intersect stage geometry.
