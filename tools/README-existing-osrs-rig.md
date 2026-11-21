# Existing OSRS skeleton → YARG band

This local workflow reuses the male skeleton from
[Psyda's OSRS Blender Character Creator](https://github.com/Psyda/OSRS-Blender-CharacterCreator)
and the NPC mesh's original vertex groups. It does not invent a new skinning recipe.
The initial cast is Hans (3105), Cook (4626), and Father Aereck (2812).

## Inputs and export

1. Save the source `rig.blend` and its `LICENSE` locally. Its actual license file
   is GPL-3.0. Keep the source assets, game models and generated bundles out of Git.
2. Open `rig.blend` in Blender and use **File → Export → FBX** with both armatures
   and default axis/unit conversion. No Blender addon or Python script is needed.
   Save the FBX as `Assets/LocalCharacterBuild/ExistingRig/rig.fbx` in this checkout.
3. Extract the three NPCs from your own local RuneLite cache, with the client closed:

```sh
rustc --edition=2021 tools/osrs_cache_export.rs -o /tmp/osrs-cache-export
/tmp/osrs-cache-export "$HOME/.runelite/jagexcache/oldschool/LIVE" \
  "$PWD/.local/osrs-band/npcs" 3105 4626 2812
```

The extractor is read-only and bounded to 1–9 requested IDs. It supports the
legacy grouped, untextured mesh layouts needed by this cast. Unknown definition
opcodes, missing groups and textured faces fail explicitly. It is not a universal
OSRS asset converter. Cache layouts follow RuneLite's BSD-licensed
[cache loaders](https://github.com/runelite/runelite/tree/master/cache/src/main/java/net/runelite/cache/definitions/loaders);
see `LICENSE-runelite-cache.txt`.

4. In the same Unity editor version used by this fork:

```sh
YARG_NPC_ROOT="$PWD/.local/osrs-band/npcs" \
YARG_BAND_VENUE=/path/to/full-band.yarground \
Unity -batchmode -quit -projectPath "$PWD" \
  -executeMethod Editor.ExistingOsrsRig.Build -logFile /tmp/existing-osrs-build.log
```

The editor normalizes FBX scale without moving the authored rest pose, preserves
the original mesh groups and skeleton, calibrates a Unity Humanoid avatar, and
exports native Mac `.yargchar` bundles into each NPC's `existing-rig` folder.
The supplied rig's loose apron and robe controls follow existing waist/leg bones.
Waist and lower garment weights are adapted across the existing hips, spine and
chest to distribute torso bending; arms and legs retain their source weights.
Generated editable Unity assets remain in the ignored `Assets/LocalCharacterBuild`.

Copy the bundles into `custom/characters/band` in the fork's data folder and enable
**Shuffle Custom Band**. The existing authored actors continue to drive notes,
rests, tempo, instruments and cameras. See [band setup](README-osrs-band.md).

## Verification

```sh
rustc --edition=2021 --test tools/osrs_cache_export.rs -o /tmp/osrs-cache-tests
/tmp/osrs-cache-tests
```

`Build` checks all four band roles for each NPC with `BandSkinChecks.Run`.
It also checks that the left limbs map to negative X and that neutral elbows bend
forward, matching the authored venue's humanoid pose. This guards against the
reversed left/right mapping that initially bent the imported arms backward.
`BAND_SKIN_PASS` proves pose transfer and retained controllers/props; it does not
prove natural elbows, hand grips or visual quality. Check live playback as well.
`Editor.BandSkinChecks.RunCast` checks the three exported bundles together when
`YARG_NPC_ROOT` and `YARG_BAND_VENUE` are set. It exercises turns, translations,
reachable wrist targets, fixed arm lengths and repeated copies without drift.
The original NPC groups have no finger or facial animation data.
