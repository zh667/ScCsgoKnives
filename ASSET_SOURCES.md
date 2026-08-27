# Asset sources

All adapted files below originate from `[TaCZ X LR] CS2 Knifes Packet v1.0.1`, CurseForge file `6635636`, by `White_Food`. Their use in this Survivalcraft port is based on the permission recorded in `THIRD_PARTY_NOTICES.md`.

| ScCsgoKnives asset | Upstream asset |
|---|---|
| `Models/ScCsgoKnives/karambit.obj` | `geo_models/melee/karambit_geo.json`, subtree `karambit` |
| `Models/ScCsgoKnives/m9.obj` | `geo_models/melee/m9_geo.json`, subtree `m9` |
| `Models/ScCsgoKnives/butterfly.obj` | `geo_models/melee/butterfly_geo.json`, subtree `butterfly` |
| `Textures/ScCsgoKnives/karambit.png` | `textures/melee/karambit_uv.png` |
| `Textures/ScCsgoKnives/m9.png` | `textures/melee/m9_uv.png` |
| `Textures/ScCsgoKnives/butterfly.png` | `textures/melee/butterfly_uv.png` |
| `Textures/ScCsgoKnives/karambit_slot.png` | `textures/melee/slot/karambit.png` |
| `Textures/ScCsgoKnives/m9_slot.png` | `textures/melee/slot/m9.png` |
| `Textures/ScCsgoKnives/butterfly_slot.png` | `textures/melee/slot/butterfly.png` |
| `Audio/ScCsgoKnives/knife_deploy.ogg` | `tacz_sounds/melee/knife/knife_deploy1.ogg` |
| `Audio/ScCsgoKnives/knife_slash.ogg` | `tacz_sounds/melee/knife/knife_slash1.ogg` |
| `Audio/ScCsgoKnives/butterfly_draw.ogg` | `tacz_sounds/melee/knife/bknife_draw01.ogg` |
| `Audio/ScCsgoKnives/butterfly_inspect.ogg` | `tacz_sounds/melee/knife/bknife_look01_ab.ogg` |

The OBJ files are mechanical geometry/UV conversions produced by `tools/bedrock_to_obj.py`. Inventory and hotbar rendering follows TaCZ's GUI path and uses the upstream `slot_texture` assets instead of flattening the animated Bedrock model. The first-person animation controller is a Survivalcraft C# implementation using the upstream action categories and durations; the Minecraft Lua runtime is not bundled.
