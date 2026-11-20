# Dance and 4-key

Create a profile and select **Dance (4-Panel Pad)** or **4-Key Keyboard**.
Dance defaults to the arrow keys; 4-key defaults to **D F J K**, in left/down/up/right
column order. Assign a controller in the profile and use **Edit bindings** for a
pad with independent buttons. Standard gamepad D-pads receive default bindings.
Each profile has its own bindings, calibration, difficulty, note speed and scores.

Add a song folder containing an SM or SSC file and its `#MUSIC` audio to a scanned
song directory. SSC takes precedence over its SM companion. Standalone four-panel
charts can be played with either profile. Challenge appears in YARG's Expert slot;
Edit appears in Expert+. Multiple charts of the same difficulty must be exported
as separate songs. Keep one SSC (or one SM when no SSC exists) per song directory.

## Playing a band song with YACE dance parts

Export the band game files and the dance simfiles from the **same YACE recording**.
Put YACE's two exported song directories inside the band song directory:

```text
Song/
  song.ini
  notes.mid                 # or notes.chart, plus existing vocal files
  song.ogg                  # or the normal YARG stems
  YACE-pad/
    notes.ssc               # the exported simfile's original name also works
    song.ogg
  YACE-keyboard/
    notes.ssc
    song.ogg
```

Retain each exported folder's audio/assets and the relative paths in its simfile.
Rescan songs. The library shows one song with band, Dance, and 4-key parts. Select
those profiles together to play them in the normal multiplayer game. A missing
pad or keyboard directory simply means that particular part is unavailable. A
simfile directly in the band directory supplies both new profiles instead.

Band audio plays once. Dance uses its own chart timing against the same audio
clock; the band tempo map is retained. Song.ini audio delay, SM/SSC offset signs,
chart-specific timing, BPM changes, stops, delays, warps, fake sections, SPEEDS
and SCROLLS are handled by Rust. Simfile edits change the combined song hash,
including after a quick rescan. If a source changes after scanning, rescan before
playing or loading its replay.

## Supported gameplay

- Four columns: taps, simultaneous steps, holds, repeated-tap rolls, release lifts,
  mines and fake notes. Holds allow 250 ms of released contact; rolls need another
  press within 500 ms. Mines use a 90 ms window.
- Timing grades at 22.5 / 45 / 90 / 135 / 180 ms from the note. These are this mode's
  fixed defaults, rather than a claim of parity with every StepMania judge preset.
- Four-lane YARG highways using the existing highway prefab, theme note models,
  receptors, sustain renderer, cameras and HUD alongside the other instruments.
  Highway, camera, theme and color presets work through the normal settings.
  Hit feedback, combo, normal results, saved scores, bots and input replays.
- Practice ranges and replay seeking use band song time. Dance does not add
  star-power phrases or mute the shared recording on a miss.

Doubles, Pump layouts, StepMania themes, Lua/mod execution, keysounds, background animations
and exact StepMania scoring compatibility are outside this implementation.
The highway pool draws at most 500 simultaneously visible note objects; all notes
remain in the judgment engine. Physical pad behavior, latency and musical chart
quality require human playtesting. Automated bot checks do not establish those.

## Building and checking

Rust must be installed. On macOS install both `aarch64-apple-darwin` and
`x86_64-apple-darwin` with rustup. The normal Unity desktop build hook builds the
native library when its source is newer. **YARG → Build Dance Native Library**
also builds it explicitly. Restart Unity after rebuilding an already loaded
native library before running editor gameplay checks.

From the repository root:

```sh
rustc tools/build-dance.rs -o /tmp/yarg-build-dance
/tmp/yarg-build-dance macos   # linux or windows on those native hosts
cargo test --locked --manifest-path YARG.Core/Native/dance/Cargo.toml
cargo build --release --locked --manifest-path YARG.Core/Native/dance/Cargo.toml
dotnet test YARG.Core/YARG.Core.UnitTests/YARG.Core.UnitTests.csproj
```

The .NET integration tests generate a copyright-free fixture with YACE's Rust
exporter and exercise five engines, source changes, offsets and replay round trips.
`Editor.DanceChecks.Run` checks Unity profile/binding/library wiring.
`Editor.DanceBuild.BuildMac` creates a universal Mac app under `Builds/YARG.app`.
Set `YARG_DANCE_ACCEPTANCE=1` for the instrumented verification build, then launch:

```sh
cargo run --manifest-path YARG.Core/Native/dance/Cargo.toml --bin fixture -- /tmp/yarg-dance-fixture
Builds/YARG.app/Contents/MacOS/YARG -offline \
  -persistent-data-path /tmp/yarg-dance-acceptance-check \
  -dance-check /tmp/yarg-dance-fixture
```

That build runs the real scanner, four bots and an injected keyboard player,
audio clock, rendering, results and saved-score checks. It saves screenshots and
`result.json` in its isolated data directory. Injected input verifies software
bindings; it does not replace a physical keyboard/pad latency playtest.
The verification harness is excluded from normal release builds. Build artifacts
are regenerated locally; no Python or separate YACE checkout is required.
Add `-dance-visual-check` for an all-bot run that captures the highways during
gameplay. That run does not claim keyboard input or score persistence coverage.
