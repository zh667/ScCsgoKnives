# Third-party notices

Current weapon models, materials, skeletal animations and real hand/glove meshes are converted from the user's existing Counter-Strike 2 exports. Counter-Strike 2 and its assets are by Valve. The local source inventory and conversion history are recorded in `ASSET_SOURCES.md` and the CS2 acquisition reports under `docs/`.

Version 0.20.4 removes the retired CS:MC weapon models and animation payloads. The historical attributions below remain for shared resources and conversion history; removal of a weapon mesh does not remove attribution for retained icons, sounds or lighting resources.

This project contains adapted assets from:

- CSMC client weapon resources: M9 bayonet, karambit, and butterfly knife `.meshbin`, `basecolor.webp`, and `.animbin` files
- Local conversion source: `reference/CSMCClient20260822-selected`
- The ScCsgoKnives repository maintainer has represented that permission was obtained to adapt and publicly redistribute these CSMC weapon resources as part of this Survivalcraft port.

- `[TaCZ X LR] CS2 Knifes Packet v1.0.1`
- Author/uploader: `White_Food`
- Source: <https://www.curseforge.com/minecraft/customization/tacz-x-lr-cs2-knifes-packet/files/6635636>
- Original file SHA-256: `5CAB4AF53CB80FB016EF14FBB1BC27CD21D3B2D234415301CF4A9A4238A10C10`

The ScCsgoKnives repository maintainer has represented that permission was obtained from the rights holder to modify and publicly redistribute the referenced inventory icons and sounds as part of this Survivalcraft port. Keep the original author and source attribution when redistributing this project.

Architecture references:

- [MCModderAnchor/TACZ](https://github.com/MCModderAnchor/TACZ), GPL-3.0 code.
- [LesRaisins-Studios/LesRaisins-Tactical-Equipements](https://github.com/LesRaisins-Studios/LesRaisins-Tactical-Equipements), GPL-3.0 code.

Battlefield 1 feedback audio:

- `bf1_kill_confirm.ogg`: original `Sound/UI/UI_KillMessage_Wave` from the user-provided local Battlefield 1 installation (EA / DICE), converted from PCM to Ogg Vorbis.
- Game audio remains the property of its respective rights holders and is not covered by the project code license. Provenance: `docs/bf1-feedback-source.json`.
- Extraction tools: NicknineTheEagle/Frostbite-Scripts; vgmstream r2117. Tools themselves are not bundled in the mod.

- 0.28.1 `bf1_kill_ding.wav`: edited metallic confirmation layer from EA / DICE Battlefield 1 `Sound/UI/UI_KillMessage_HeadShotAdd_Wave`. Source and conversion details: `docs/bf1-ding-0281-source.json`.
