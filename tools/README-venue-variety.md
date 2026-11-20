# Automatic venue variety

Global backgrounds rotate through a shuffled pool before repeating. With two
or more files, consecutive songs do not pick the same file, including when a
new cycle starts. A changed folder updates the pool automatically. Song-specific
backgrounds keep their existing priority.

Enable **Settings → Experimental → Shuffle Lead Singer** for character variety.
Compatible venues reuse their existing humanoid band members as alternate lead
singers, retaining the vocalist's chart-driven controller and camera targets.
A selected **Custom Vocals Character** takes priority over this shuffle.

This changes the lead singer. The other roles keep their authored models, so
the singer can share a model with another member. Instrument props on the singer
copy are hidden. Custom/VRM performers and non-humanoid vocalist controllers
are preserved rather than converted. New models and full-band casting are
separate work.

Put `.yarground` files in the folder opened by **Open Venue Folder**. Launcher
venues are also discovered when the launcher path is configured. Bundles remain
local; the fork does not redistribute downloaded venue assets.

## Checks

```sh
Unity -batchmode -quit -projectPath "$PWD" \
  -executeMethod Editor.VenueRotationChecks.Run \
  -logFile /tmp/yarg-venue-rotation.log
```

Look for `VENUE_ROTATION_PASS`: 30 full cycles without adjacent repeats, changed
contents, and empty/single pools. To check an existing full-band bundle too:

```sh
YARG_VENUE_INSPECT=/path/to/stage.yarground YARG_VENUE_CAST_CHECK=1 \
Unity -batchmode -quit -projectPath "$PWD" \
  -executeMethod Editor.VenueRotationChecks.Run \
  -logFile /tmp/yarg-venue-casting.log
```

`VENUE_CAST_PASS` checks twelve casts, different body models, retained band
roles and humanoid controllers. Live Mac playback remains the visual check for
pose quality, stage cameras and materials. Selection logs identify the global
background and lead-singer source used for each song.
