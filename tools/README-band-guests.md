# Additional band characters

The portable cast now adds **Sonic, Sora, Druid and Duke Horacio** to Hans, Cook
and Father Aereck. Keep **Shuffle Custom Band** enabled; the existing loader mixes
all compatible `.yargchar` files between songs. No extra runtime build is needed.

## Build inputs

Generated assets and game geometry stay in ignored `Assets/LocalCharacterBuild`
and `.local` folders. They must not be committed or redistributed with this fork.

| Character | Existing model and rig |
| --- | --- |
| Sonic | [Apoc Hedgie model mirror](https://github.com/sarisman84/Project-Going-Indie-2024/tree/552ddbdb0a4fe54970ab20e20c4171bb245fa8c3/graphics/models/testing/sonic/generations), retaining `Credits.txt` |
| Sora | [Soraiko KH2 P_EX100 model](https://github.com/Soraiko/SrkOpenGLBasicSample/tree/48c7faea462133c770556144669b9510d1f69aaf/KHDebug_files/debug_files/P_EX100) |
| Druid / Duke Horacio | Read-only local cache NPCs 3258 / 815, using the [existing OSRS rig workflow](README-existing-osrs-rig.md) |

Place the DAEs and PNGs in `Assets/LocalCharacterBuild/Guests/Sonic` and `Sora`.
The Sonic mirror textures retain their `Sonic_` filename prefix. The editor uses
the original skin weights, maps the existing joints to Humanoid, repairs Sora's
image-space V orientation, and opens Sonic's authored upper eyelids.

Run Unity batch mode with `-executeMethod Editor.GuestCharacterBuilder.Build`,
`YARG_GUEST_OUTPUT=/absolute/local/output`, and `YARG_BAND_VENUE=/path/to/venue.yarground`.
Each bundle must pass all four `BandSkinChecks` roles before installation.

For additional OSRS NPCs, set `YARG_NPC_NAMES='Druid-3258|Duke_Horacio-815'` with
`YARG_NPC_ROOT`, `YARG_BAND_VENUE`, and `YARG_OSRS_WAIST_CHECK=1`, then execute
`Editor.ExistingOsrsRig.Build`. Unknown source mesh groups still fail explicitly.
Neck/back region labels use the supplied rig's existing anatomical joints.

To validate bundles without rebuilding them, execute `Editor.BandSkinChecks.RunModels`
with pipe-separated `YARG_BAND_MODELS` and a venue path. Optional `YARG_BAND_PREVIEWS`
writes starting-pose renders. These are editor checks, not proof of live song playback.

## Verification and limits

All four additions passed both installed venues' deformation, palm reach, fixed
arm length, prop attachment, root-motion restoration and repeated-pose drift checks.
The new OSRS waist deformation also passed the existing limits. Static rendered
previews were inspected after texture/eyelid corrections.

Guard 3010 was rejected after its authored equipment/arm pose distorted under this
rig. The unrigged Sonic Adventure candidate was also rejected. Neither is installed.
Live playback with the new cast is pending because computer control is unavailable.
Multi-game crowds are not implemented; the venue crowd controller needs separate
placement and animation integration.
