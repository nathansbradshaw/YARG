# Custom band bodies

Enable **Settings → Experimental → Shuffle Custom Band**. Put native-platform
`.yargchar` bundles in `custom/characters/band` under YARG's data folder.
Each bundle needs `assets/_character.prefab`, an Animator with a valid humanoid
Avatar, and a skinned body. VRM metadata is optional. Models are shuffled among
roles on each song; with fewer models than roles, models repeat within the band.
A selected Custom Vocals Character keeps priority for the singer.

The authored band stays in charge of chart notes, tempo, rests, instrument props,
IK, cameras and animations. Imported bodies copy its humanoid pose after animation.
This does not rig arbitrary meshes. Hands depend on model proportions; finger and
facial animation need the corresponding rig data. Native instrument meshes must
be MeshRenderers: the adapter hides the original SkinnedMeshRenderers.

For Hans, Cook and Father Aereck using an existing skeleton and original skin
groups, use the [existing OSRS rig workflow](README-existing-osrs-rig.md).

## Saved OSRS player prototype

The converter contains a hand-authored skinning recipe for the saved armoured
player's XPS ASCII mesh. It removes the bow, makes a T-pose and writes an editable
22-bone rig description. It is not a universal OSRS NPC rigger.

```sh
rustc tools/osrs_rig.rs -o /tmp/osrs-rig
/tmp/osrs-rig /path/to/osrs-player.mesh.ascii /path/to/generated
YARG_OSRS_INPUT=/path/to/generated/mesh.json \
YARG_OSRS_TEXTURES=/path/to/player-textures \
YARG_OSRS_OUTPUT=/path/to/bundle-output \
Unity -batchmode -quit -projectPath "$PWD" \
  -executeMethod Editor.OsrsCharacterBuilder.Build -logFile /tmp/osrs-build.log
```

The editor builds a Mac bundle and local editable assets in
`Assets/LocalCharacterBuild`. Keep that folder and game assets out of Git.
The player has no finger bones or face blendshapes. Distinct NPCs need their own
local model files and valid humanoid rigs before export; no NPC is bundled here.

## Verification

```sh
YARG_BAND_MODEL=/path/to/model.yargchar \
YARG_BAND_VENUE=/path/to/full-band.yarground \
Unity -batchmode -quit -projectPath "$PWD" \
  -executeMethod Editor.BandSkinChecks.Run -logFile /tmp/band-skin.log
```

`BAND_SKIN_PASS` requires four roles, skin deformation, reasonable world bounds,
unchanged authored controllers, active chart actors and retained instrument props.
Live playback is still required to judge appearance and instrument grips.
