# YARG on the removable drive

Open **YARG/YARG.app** on the drive. No Unity or separate launcher is needed.

- `YARG/Data`: settings, profiles, scores, custom characters and actual venue files.
- `YARG/Logs/Player.log`: portable player log.
- `Library`: the existing songs beside the YARG folder.

Keep the app inside this layout. The Rust bootstrap finds the drive from its own
location, passes YARG's existing `-persistent-data-path` and `-download-location`
options, and relocates volume-prefixed settings paths if the drive name changes.
The Mac build includes Apple Silicon and Intel launchers. This is not a Windows build.

The original Mac data stays intact. Changes made in the portable copy are independent.

## Updating the app

Build this fork to the same app location. Before replacing it, save the current
portable `Data` folder. Rename Unity's `Contents/MacOS/YARG` to `YARG-Player`,
compile `tools/portable-yarg.rs` for `aarch64-apple-darwin` and `x86_64-apple-darwin`,
and combine them with `lipo -create` as `Contents/MacOS/YARG`. Set the portable
bundle ID to `in.yarg.portable`, then ad-hoc sign the player and complete bundle.
Never copy data with venue symlinks: dereference them so the drive owns the assets.

Regression check: `rustc --edition 2021 --test tools/portable-yarg.rs -o /tmp/portable-yarg-tests`
then `/tmp/portable-yarg-tests`. The installed app's executable accepts
`--portable-check` to print resolved locations without launching Unity.

The updated installation passed path resolution, checksum comparison and deep
strict signature verification. All 28 checked settings/profile/score files were
unchanged by installation. Live process arguments and the drive-local log confirmed
playback from the removable drive in Festival and Backyard Bash; the expanded
band loaded and the user confirmed Sandwich Lady's baguette microphone follows
her hand. The obsolete local test app was moved to Trash.
