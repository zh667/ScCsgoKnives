"""CS2 world jump / in-air clips for the CT and T actors (agent-followup-140 F3; played once and held since
video-feedback-20260929 R1).

Shared by build_companion_assets.py (full regeneration) and the incremental mode below, which appends the clips to
an already derived dense actor GLB without touching any existing node, accessor, byte or clip. Source clips come from
the preserved CS2 exports (ctm_sas / tm_phoenix, Source2Viewer); CS2 has no separate landing clip, so landing is a
blend back to the ground gait at runtime, not a fabricated clip. Channels follow the gait-clip rule exactly: every
node present in the actor, except root_motion translation (the body is moved by the game) and morph weights.

Incremental usage: python actor_air_clips.py <dense.glb> <cs2-export.glb> <out.glb> <report.json>
"""
import copy, hashlib, json, struct, sys
from pathlib import Path

# alias -> source clip. Unarmed gait uses the knife set, armed gait the rifle set (as for idle/walk/run/aim).
AIR_CLIPS = {
    "jump": "animation/anims/world/knife/_default_knife/jump_stand_knife",
    "jumprun": "animation/anims/world/knife/_default_knife/jump_n_knife",
    "air": "animation/anims/world/knife/_default_knife/inair_stand_knife",
    "airrun": "animation/anims/world/knife/_default_knife/inair_n_knife",
    "crouchjump": "animation/anims/world/knife/_default_knife/jump_crouch_stand_knife",
    "crouchair": "animation/anims/world/knife/_default_knife/inair_crouch_stand_knife",
    "aimjump": "animation/anims/world/rifle/_default_rifle/jump_stand_rifle",
    "aimjumprun": "animation/anims/world/rifle/_default_rifle/jump_n_rifle",
    "aimair": "animation/anims/world/rifle/_default_rifle/inair_stand_rifle",
    "aimairrun": "animation/anims/world/rifle/_default_rifle/inair_n_rifle",
    "aimcrouchjump": "animation/anims/world/rifle/_default_rifle/jump_crouch_stand_rifle",
    "aimcrouchair": "animation/anims/world/rifle/_default_rifle/inair_crouch_stand_rifle",
}
# Ground crouch (r2-c4-completion-20260929). The CT/T actors had no crouched gait: the Crouch parameter only chose
# air clips, so a crouching NPC (planting a bomb) stood upright and a crouching CT/T player was only squashed. The
# CS2 crouch idle is a one-frame pose; the crouch walks loop. Same channel rule as the gait: legs and pelvis included.
CROUCH_CLIPS = {
    "crouch": "animation/anims/world/knife/_default_knife/idle_crouch_knife",
    "crouchwalk": "animation/anims/world/knife/_default_knife/crouch_n_knife",
    "aimcrouch": "animation/anims/world/rifle/_default_rifle/idle_crouch_rifle",
    "aimcrouchwalk": "animation/anims/world/rifle/_default_rifle/crouch_n_rifle",
}
# Every air clip plays once and then keeps its end pose (the engine's preservePose). The CS2 in-air clips are short
# transitions whose first and last frames differ by up to 71 degrees in the legs: looped, they snap three times a
# second in a long fall (video-feedback-20260929 R1). The jump clips end exactly on the first in-air frame.
# HOLD_PHASE: fraction of the clip that is played before holding. The standing/moving in-air clips reach a steady
# legs-down pose at 0.7-0.9 of their length and then jump about 50 degrees in their very last frame (measured on the
# baked clips and seen in the r1-01 pose sheet); they are held on the steady pose, just before that frame.
HOLD_PHASE = {"air": 0.9, "airrun": 0.9, "aimair": 0.9, "aimairrun": 0.9}

def air_rules(prefix):
    """Airborne variants in priority order; the last one always matches inside its group. AirRising and AirMoving
    are latched/hysteretic booleans from TacticalAirState, not raw speeds, so a threshold cannot restart a clip."""
    return [{"condition": "[Crouch] && [AirRising]", "animation": prefix + "crouchjump"},
            {"condition": "[Crouch]", "animation": prefix + "crouchair"},
            {"condition": "[AirRising] && [AirMoving]", "animation": prefix + "jumprun"},
            {"condition": "[AirRising]", "animation": prefix + "jump"},
            {"condition": "[AirMoving]", "animation": prefix + "airrun"},
            {"condition": "true", "animation": prefix + "air"}]

def crouch_rules():
    """Grounded crouch in priority order, one [Crouch] group after the airborne one: armed variants first, moving before
    standing still. The walk threshold matches the standing gait's."""
    return {"condition": "[Crouch]", "rules": [
        {"condition": "[Armed] && [SpeedAbs] > 0.15", "animation": "aimcrouchwalk"},
        {"condition": "[Armed]", "animation": "aimcrouch"},
        {"condition": "[SpeedAbs] > 0.15", "animation": "crouchwalk"},
        {"condition": "true", "animation": "crouch"}]}

def crouch_config(config):
    """Adds the ground crouch clips and their group right after the [Airborne] group (idempotent input check)."""
    config = copy.deepcopy(config)
    assert not any(a in config["animations"] for a in CROUCH_CLIPS), "crouch clips already configured"
    for alias in CROUCH_CLIPS:
        config["animations"][alias] = {"source": alias, "speed": 1, "loop": True, "blendDuration": 0.18}
    rules = config["states"]["gait"]["rules"]
    air = next(i for i, r in enumerate(rules) if r.get("condition") == "[Airborne]")
    config["states"]["gait"]["rules"] = rules[:air + 1] + [crouch_rules()] + rules[air + 1:]
    return config

def air_config(config):
    """CT/T animation config with airborne states before the ground gait. Parameters come from TacticalAirState
    (NPC model and player pose): Airborne (debounced not-on-ground), Crouch, AirRising, AirMoving. Landing blends
    back to the ground gait. The variants sit in one nested [Airborne] group (armed ones in an inner [Armed] group):
    the engine re-evaluates rules whenever a parameter changes, and a failed group condition skips the whole group,
    so a grounded actor pays for one condition instead of twelve."""
    config = copy.deepcopy(config)
    for alias in AIR_CLIPS:
        entry = {"source": alias, "speed": 1, "loop": False, "preservePose": True, "blendDuration": 0.12}
        if alias in HOLD_PHASE: entry["endPhase"] = HOLD_PHASE[alias]
        config["animations"][alias] = entry
    rules = config["states"]["gait"]["rules"]
    assert rules[0]["condition"] == "IsDead"
    air = {"condition": "[Airborne]", "rules": [{"condition": "[Armed]", "rules": air_rules("aim")}] + air_rules("")}
    config["states"]["gait"]["rules"] = rules[:1] + [air] + rules[1:]
    return crouch_config(config)

def derive_air(source, doc, names, accessor):
    """Append AIR_CLIPS to doc['animations']. `source` has .j (glTF JSON); `names` maps actor node name -> index;
    `accessor(source, i)` copies one source accessor (and its data) into doc and returns the new index."""
    existing = {a.get("name") for a in doc["animations"]}
    added = []
    for alias, path in {**AIR_CLIPS, **CROUCH_CLIPS}.items():
        assert alias not in existing, "air/crouch clip already present: " + alias
        a = next(x for x in source.j["animations"] if x["name"] == path)
        out = {"name": alias, "channels": [], "samplers": []}
        for c in a["channels"]:
            target = source.j["nodes"][c["target"]["node"]].get("name"); prop = c["target"]["path"]
            if target not in names or (target == "root_motion" and prop == "translation") or prop == "weights":
                continue
            s = copy.deepcopy(a["samplers"][c["sampler"]]); s["input"] = accessor(source, s["input"]); s["output"] = accessor(source, s["output"])
            out["channels"].append({"sampler": len(out["samplers"]), "target": {"node": names[target], "path": prop}}); out["samplers"].append(s)
        assert out["channels"], "no usable channels in " + path
        doc["animations"].append(out); added.append({"alias": alias, "source": path, "channels": len(out["channels"])})
    return added

class _Source:
    """Lazy reader for multi-GB exports: JSON header plus random access to buffer views."""
    def __init__(self, path):
        self.path = Path(path); self.f = self.path.open("rb"); h = self.f.read(20)
        n = struct.unpack_from("<I", h, 12)[0]; self.j = json.loads(self.f.read(n)); self.start = 28 + n
    def view(self, i):
        v = self.j["bufferViews"][i]; self.f.seek(self.start + v.get("byteOffset", 0)); return self.f.read(v["byteLength"])

def append_to_dense(dense_path, source_path, out_path):
    data = Path(dense_path).read_bytes()
    assert data[:4] == b"glTF"
    jlen = struct.unpack_from("<I", data, 12)[0]; doc = json.loads(data[20:20 + jlen])
    boff = 20 + jlen + (-jlen % 4); blen, kind = struct.unpack_from("<II", data, boff); assert kind == 0x004E4942
    blob = bytearray(data[boff + 8:boff + 8 + blen]); original_blob = bytes(blob)
    assert len(doc["buffers"]) == 1 and "uri" not in doc["buffers"][0]
    before = copy.deepcopy(doc)
    source = _Source(source_path); views, accessors = {}, {}
    def raw(b, extra=None):
        blob.extend(b"\0" * (-len(blob) % 4)); idx = len(doc["bufferViews"]); v = dict(buffer=0, byteOffset=len(blob), byteLength=len(b)); v.update(extra or {})
        doc["bufferViews"].append(v); blob.extend(b); return idx
    def accessor(src, i):
        if i in accessors: return accessors[i]
        a = copy.deepcopy(src.j["accessors"][i]); assert "sparse" not in a
        vi = a["bufferView"]
        if vi not in views: views[vi] = raw(src.view(vi), {k: v for k, v in src.j["bufferViews"][vi].items() if k in ["byteStride", "target"]})
        a["bufferView"] = views[vi]; accessors[i] = len(doc["accessors"]); doc["accessors"].append(a); return accessors[i]
    names = {n.get("name"): i for i, n in enumerate(doc["nodes"])}
    added = derive_air(source, doc, names, accessor)
    # Nothing that existed before may change: nodes, meshes, skins, materials, images, prior accessors/views/clips, bytes.
    for key in before:
        if key in ("accessors", "bufferViews", "animations", "buffers"): continue
        assert doc[key] == before[key], key
    for key in ("accessors", "bufferViews", "animations"):
        assert doc[key][:len(before[key])] == before[key], key
    assert bytes(blob[:len(original_blob)]) == original_blob
    blob.extend(b"\0" * (-len(blob) % 4)); doc["buffers"][0]["byteLength"] = len(blob)
    js = json.dumps(doc, separators=(",", ":")).encode(); js += b" " * (-len(js) % 4)
    out = struct.pack("<4sII", b"glTF", 2, 28 + len(js) + len(blob)) + struct.pack("<II", len(js), 0x4E4F534A) + js + struct.pack("<II", len(blob), 0x004E4942) + bytes(blob)
    Path(out_path).write_bytes(out)
    return {"dense": str(dense_path), "denseSha256": hashlib.sha256(data).hexdigest(), "source": str(source_path),
            "out": str(out_path), "outSha256": hashlib.sha256(out).hexdigest(), "clipsBefore": len(before["animations"]),
            "clipsAfter": len(doc["animations"]), "added": added}

if __name__ == "__main__":
    report = append_to_dense(sys.argv[1], sys.argv[2], sys.argv[3])
    Path(sys.argv[4]).write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", "utf-8"); print(json.dumps(report, ensure_ascii=False)[:2000])
