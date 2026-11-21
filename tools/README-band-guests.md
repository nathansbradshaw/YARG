# Additional band characters

Keep **Shuffle Custom Band** enabled; the existing loader mixes compatible
`.yargchar` files between songs. The active roster has 13 band characters.
Sonic, Shadow, Knuckles and Tails are kept separately as extras because their
large hands do not suit the instruments. Their bundles belong in
`custom/characters/extras`; crowd placement is not implemented yet.

The expanded build profiles add **Wise Old Man, Nieve, Vannaka, Duradel,
Krystilia, Sandwich Lady, Shadow, Knuckles, Tails and Wind-Up Cloud**.
Sandwich Lady uses her original baguette as a vocal microphone. That custom prop
requires the matching player update; other new bundles use the existing loader.

Sandwich Lady is **vocals only**. Wind-Up Cloud is **instruments only** because
that model has a rigid head. Embedded `BandModelOptions` enforce these restrictions
both during shuffled selection and direct attachment. Extras-only bundles are
rejected for every band role, even if copied into the band folder accidentally.

Sora's existing lower-mouth control follows actual vocal note onsets and lengths,
closes during rests, and seeks with the song clock. This is basic mouth movement,
not phoneme recognition. Models without facial controls cannot gain lip sync just
from an ordinary body rig. Named jaw/lip controls and common mouth blendshapes are
supported for compatible future guests.

Fingered humanoids retain authored finger animation, with minimum grip poses for
microphones, drumsticks, picks and instrument necks. The singer's free hand keeps
its gestures. Importing uses Unity's canonical human bone names: finger names
contain spaces and cannot be supplied as raw `HumanBodyBones` enum names. Every
requested bone mapping is checked after avatar creation.

## Build inputs

Generated assets and game geometry stay in ignored `Assets/LocalCharacterBuild`
and `.local` folders. They must not be committed or redistributed with this fork.

| Character | Existing model and rig |
| --- | --- |
| Sonic | [Apoc Hedgie model mirror](https://github.com/sarisman84/Project-Going-Indie-2024/tree/552ddbdb0a4fe54970ab20e20c4171bb245fa8c3/graphics/models/testing/sonic/generations), retaining `Credits.txt` |
| Sora | [Soraiko KH2 P_EX100 model](https://github.com/Soraiko/SrkOpenGLBasicSample/tree/48c7faea462133c770556144669b9510d1f69aaf/KHDebug_files/debug_files/P_EX100) |
| Druid / Duke Horacio | Read-only local cache NPCs 3258 / 815, using the [existing OSRS rig workflow](README-existing-osrs-rig.md) |
| Expanded OSRS cast | Read-only local cache NPCs 2108, 6797, 403, 13441, 7663 and 5510; existing male/female OSRS armatures |
| Shadow / Knuckles / Tails | Skinned `Skeletons` FBXs and diffuse DDS textures from [ring-engine](https://github.com/lurodsil/ring-engine/tree/12ad78d4ce57cdff65051119777768d64bc5031c/Assets/Static%20Assets/Characters); static `Models` FBXs are unsuitable |
| Wind-Up Cloud | Existing FFXIV minion rig from [Gen1Recomp Character Selector](https://github.com/randyadr/Gen1Recomp-Character-Selector/tree/019e10c4be57c98d3b86742bf2be5ba61348fa8b/source/cloud); this is the chibi variant, not the full-size FF7 model |

Place the DAEs and PNGs in `Assets/LocalCharacterBuild/Guests/Sonic` and `Sora`.
The Sonic mirror textures retain their `Sonic_` filename prefix. The editor uses
the original skin weights, maps the existing joints to Humanoid, repairs Sora's
image-space V orientation, and opens Sonic's authored upper eyelids.

Run Unity batch mode with `-executeMethod Editor.GuestCharacterBuilder.Build`,
`YARG_GUEST_OUTPUT=/absolute/local/output`, and `YARG_BAND_VENUE=/path/to/venue.yarground`.
Each bundle must pass every allowed `BandSkinChecks` role before installation;
forbidden roles must reject attachment. Extras must reject all band roles.

Set `YARG_GUEST_NAMES='Sora|Cloud'` for the current band guests, or
`YARG_GUEST_NAMES='Sonic|Shadow|Knuckles|Tails'` for the extras.
Their explicit JSON profiles in `tools/character-profiles` name source files,
existing joints, axis corrections and textures. Original skin weights are retained.
Cloud's source sword triangles are omitted from the band copy by their original
weapon-bone weights; the downloaded source remains intact. Never run scripts
bundled with downloaded model archives.
Profiles can set `roles`, `extrasOnly`, `mouthLower`, `mouthUpper` and `mouthTravel`.
The mouth arrays name existing source transforms; they do not create new bones.

For additional OSRS NPCs, set `YARG_NPC_NAMES='Druid-3258|Duke_Horacio-815'` with
`YARG_NPC_ROOT`, `YARG_BAND_VENUE`, and `YARG_OSRS_WAIST_CHECK=1`, then execute
`Editor.ExistingOsrsRig.Build`. Unknown source mesh groups still fail explicitly.
Neck/back region labels use the supplied rig's existing anatomical joints.

To validate bundles without rebuilding them, execute `Editor.BandSkinChecks.RunModels`
with pipe-separated `YARG_BAND_MODELS` and a venue path. Optional `YARG_BAND_PREVIEWS`
writes starting-pose renders. These are editor checks, not proof of live song playback.
Preview rendering isolates the requested hierarchy so a build's source model
cannot accidentally appear over the tested skin. Batch-rendered skins may lag
behind CPU pose changes; use live playback to judge hand/prop appearance.

## Verification and limits

All 17 supported characters passed both installed venues' pose transfer, palm
reach, fixed arm length, prop attachment, root-motion restoration and repeated-pose
drift checks. All 11 OSRS bodies also passed the original waist limits (1.6 maximum
edge stretch and 4 mm duplicated-vertex seam gap). Those OSRS-specific coordinate
limits are not applied to the differently proportioned game guests.
Static source previews were inspected after texture/eyelid corrections.
Sandwich Lady's prop passes mesh/material identity, palm attachment, head aim
and vocals-only visibility checks. Her source model has no facial rig for lip sync.
Updated checks cover restricted casting, extras exclusion, note/rest/seek mouth
timing, Sora's visible mouth vertices, closed-pose restoration and repeated-update
drift. Finger checks verify that fingertips move toward the held-object palms.
Sora passed those checks in all four roles in both venues after its finger-map
correction. Exact finger-to-instrument surface contact still needs visual review.

Guard 3010 was rejected after its authored equipment/arm pose distorted under this
rig. The unrigged Sonic Adventure candidate was also rejected. Neither is installed.
The updated portable Mac player loaded Tails, Cook, Father Aereck and Sora in
Festival on “Quevedo: Bzrp Music Sessions, Vol. 52”. A controlled one-song cast
then loaded Sandwich Lady in all four roles in Backyard Bash on “Hardware Store”.
The user confirmed her baguette replaces the microphone and stays in her hand
while she moves. The complete 17-character shuffle was restored immediately,
with all bundle hashes unchanged. Other new guests still need individual live
visual acceptance; editor checks alone do not establish their appearance.
Multi-game crowds are not implemented; the venue crowd controller needs separate
placement and animation integration.

The candidate named Riku in `abarabone/ecs_motion` was visually rejected: it is
not Kingdom Hearts Riku. The Kingdom Hearts Riku download remains unavailable
through the tested source. Roxas needs an explicit bone map and material review.
Konar, Commander Zilyana, Zanik and Eluned are not approved for installation:
their source groups or deformation checks need further work. Profiles for rejected
automatic mappings must not be included as supported characters.
