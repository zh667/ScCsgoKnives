"""CS2 world pull-pin / throw clips for the CT and T actors (video-feedback-20260929 R2).

Appends the clips to an already derived dense actor GLB (after the air clips) without touching any existing node,
accessor, byte or clip. Same sources and rules as the draw/reload clips of build_companion_assets.py and
derive_world_props.py: actor channels only under spine_0/spine_1 (the legs stay with the gait), the weapon mount
following the source wpn socket relative to spine_2, the prop bones from the source clip where it animates them.

CS2 has one world grenade set (_default_grenade) for HE, flashbang, smoke, decoy and incendiary, and molotov-specific
draw/idle/pull-pin clips only; the molotov is thrown with the default throw clips. Those do not animate the molotov's
own hand bones, so its bottle bone (weapon_hand_r) keeps, relative to the actor's right hand, the offset it has on the
last frame of the molotov pull-pin clip. Nothing is fitted by eye.

The arm keeps the source's own keys (30 a second). The source keys its weapon socket (wpn) on its own, not under the
hand: between two keys the hand swings on an arc (up to 0.47 m in one key of the throw) while the socket cuts the
chord, and at 60 frames a second the grenade is drawn centimetres off the hand. So the mount and the prop bones are
written DENSE times as often. At a source key the socket is the source's, exactly. Between two keys it keeps, relative
to the right hand, the offset interpolated between the offsets it has at those two keys, with the hand taken from the
arm interpolated the way the game interpolates it.

Usage: python actor_throw_clips.py <dense.glb> <cs2-export.glb> <out.glb> <report.json>
"""
import copy, hashlib, json, struct, sys
from pathlib import Path
import numpy as np

GRENADE = "animation/anims/world/grenade/_default_grenade/"
MOLOTOV = "animation/anims/world/grenade/grenade_molotov/"
C4 = "animation/anims/world/equipment/c4/"
# alias -> (source clip, world prop asset, source weapon skeleton)
THROW_CLIPS = {
    "pullpin_grenade": (GRENADE + "pullpin_grenade", "grenade", "hegrenade"),
    "throwhigh_grenade": (GRENADE + "throw_overhand_grenade", "grenade", "hegrenade"),
    "throwlow_grenade": (GRENADE + "throw_underhand_grenade", "grenade", "hegrenade"),
    "pullpin_molotov": (MOLOTOV + "pullpin_molotov", "molotov", "molotov"),
    "throwhigh_molotov": (GRENADE + "throw_overhand_grenade", "molotov", "molotov"),
    "throwlow_molotov": (GRENADE + "throw_underhand_grenade", "molotov", "molotov"),
    # r2-c4-completion-20260929: CS2's crouched pull-pin and throws (far = the overhand, near = the underhand one) ...
    "pullpin_crouch_grenade": (GRENADE + "pullpin_crouch_grenade", "grenade", "hegrenade"),
    "throwhigh_crouch_grenade": (GRENADE + "crouch_throw_far_grenade", "grenade", "hegrenade"),
    "throwlow_crouch_grenade": (GRENADE + "crouch_throw_near_grenade", "grenade", "hegrenade"),
    "pullpin_crouch_molotov": (MOLOTOV + "pullpin_crouch_molotov", "molotov", "molotov"),
    "throwhigh_crouch_molotov": (GRENADE + "crouch_throw_far_grenade", "molotov", "molotov"),
    "throwlow_crouch_molotov": (GRENADE + "crouch_throw_near_grenade", "molotov", "molotov"),
    # ... and planting the bomb, standing and crouched (3.3 s in CS2; the game commits at 3.2 s).
    "plant_c4": (C4 + "planting", "c4", "c4"),
    "plant_crouch_c4": (C4 + "planting_crouch", "c4", "c4"),
}
# Clips whose source does not animate the prop's hand bone: (bone kept in the hand, actor bone, reference alias).
DENSE = 4
HAND_TRACKED = {"throwhigh_molotov": ("weapon_hand_r", "hand_R", "pullpin_molotov"),
                "throwlow_molotov": ("weapon_hand_r", "hand_R", "pullpin_molotov"),
                "throwhigh_crouch_molotov": ("weapon_hand_r", "hand_R", "pullpin_crouch_molotov"),
                "throwlow_crouch_molotov": ("weapon_hand_r", "hand_R", "pullpin_crouch_molotov")}

def _matrix(node):
    from scipy.spatial.transform import Rotation
    if "matrix" in node: return np.array(node["matrix"], float).reshape(4, 4)
    m = np.eye(4)
    m[:3, :3] = np.diag(node.get("scale", [1, 1, 1])) @ Rotation.from_quat(node.get("rotation", [0, 0, 0, 1])).as_matrix().T
    m[3, :3] = node.get("translation", [0, 0, 0])
    return m

def _key(curve, t):
    ts, vs = curve["Times"], curve["Values"]
    if len(ts) == 1 or t <= ts[0]: return np.array(vs[0], float)
    if t >= ts[-1]: return np.array(vs[-1], float)
    hi = int(np.searchsorted(ts, t)); lo = hi - 1
    f = (t - ts[lo]) / max(1e-6, ts[hi] - ts[lo])
    a, b = np.array(vs[lo], float), np.array(vs[hi], float)
    if len(a) == 4:
        if np.dot(a, b) < 0: b = -b
        q = a + (b - a) * f
        return q / np.linalg.norm(q)
    return a + (b - a) * f

def append_throw(dense_path, source_path, out_path):
    from scipy.spatial.transform import Rotation
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    from actor_air_clips import _Source
    data = Path(dense_path).read_bytes()
    assert data[:4] == b"glTF"
    jlen = struct.unpack_from("<I", data, 12)[0]; doc = json.loads(data[20:20 + jlen])
    boff = 20 + jlen + (-jlen % 4); blen, kind = struct.unpack_from("<II", data, boff); assert kind == 0x004E4942
    blob = bytearray(data[boff + 8:boff + 8 + blen]); original_blob = bytes(blob)
    before = copy.deepcopy(doc)
    source = _Source(source_path); nodes = source.j["nodes"]
    parents = {c: i for i, n in enumerate(nodes) for c in n.get("children", [])}
    source_names = {}
    for i, n in enumerate(nodes): source_names.setdefault(n.get("name"), i)
    names = {n.get("name"): i for i, n in enumerate(doc["nodes"])}
    doc_parents = {c: i for i, n in enumerate(doc["nodes"]) for c in n.get("children", [])}
    mount = names["cs_weapon_mount"]; anchor = source_names["spine_2"]; socket = source_names["wpn"]
    skeletons = {n["name"].split("/")[-1][:-8]: i for i, n in enumerate(nodes) if n.get("name", "").endswith(".vnmskel")}

    def raw(b):
        blob.extend(b"\0" * (-len(blob) % 4)); index = len(doc["bufferViews"])
        doc["bufferViews"].append({"buffer": 0, "byteOffset": len(blob), "byteLength": len(b)}); blob.extend(b); return index
    def array(i):
        a = source.j["accessors"][i]; width = {"SCALAR": 1, "VEC3": 3, "VEC4": 4}[a["type"]]; view = source.j["bufferViews"][a["bufferView"]]
        return np.ndarray((a["count"], width), dtype="<f4", buffer=source.view(a["bufferView"]), offset=a.get("byteOffset", 0),
                          strides=(view.get("byteStride", width * 4), 4)).copy()
    def add(out, node, prop, times, values):
        values = np.array(values, dtype="<f4"); times = np.array(times, dtype="<f4").reshape(-1, 1)
        if np.max(np.abs(values - values[0])) < 1e-7: values, times = values[[0, -1]], times[[0, -1]]
        indices = []
        for payload, kind in ((times, "SCALAR"), (values, "VEC4" if prop == "rotation" else "VEC3")):
            indices.append(len(doc["accessors"]))
            doc["accessors"].append({"bufferView": raw(payload.tobytes()), "componentType": 5126, "count": len(payload), "type": kind})
        out["channels"].append({"sampler": len(out["samplers"]), "target": {"node": node, "path": prop}})
        out["samplers"].append({"input": indices[0], "output": indices[1], "interpolation": "LINEAR"})
    def pelvis_down(node):
        while node in doc_parents and doc["nodes"][node].get("name") != "pelvis": node = doc_parents[node]
        return doc["nodes"][node].get("name") == "pelvis"
    def upper(node):
        while node in doc_parents and doc["nodes"][node].get("name") not in ("spine_0", "spine_1"): node = doc_parents[node]
        return doc["nodes"][node].get("name") in ("spine_0", "spine_1")
    def curves_of(path):
        animation = next(a for a in source.j["animations"] if a["name"] == path); curves = {}
        for c in animation["channels"]:
            s = animation["samplers"][c["sampler"]]
            curves[(c["target"]["node"], c["target"]["path"])] = {"Times": array(s["input"]).ravel().tolist(), "Values": array(s["output"]).tolist()}
        return curves
    def absolute(curves, t):
        cache = {}
        def of(i):
            if i not in cache:
                n = dict(nodes[i])
                for prop in ("translation", "rotation", "scale"):
                    if (i, prop) in curves: n[prop] = _key(curves[i, prop], float(t))
                cache[i] = _matrix(n) @ (of(parents[i]) if i in parents else np.eye(4))
            return cache[i]
        return of
    def prop_nodes(asset, skeleton):
        """Source prop node -> existing dense node, by name, and the prop's roots (static, axis correction removed)."""
        roots = nodes[skeletons[skeleton]]["children"]; mapping = {}
        def walk(i):
            if "mesh" not in nodes[i] and "cswp_" + asset + "/" + nodes[i]["name"] in names: mapping[i] = names["cswp_" + asset + "/" + nodes[i]["name"]]
            for c in nodes[i].get("children", []): walk(c)
        for r in roots: walk(r)
        assert mapping, asset
        return mapping, roots
    def local_of(node_index, curves, old, t, roots):
        n = dict(doc["nodes"][node_index])
        if old not in roots:
            for prop in ("translation", "rotation", "scale"):
                if (old, prop) in curves: n[prop] = _key(curves[old, prop], float(t))
        return _matrix(n)

    existing = {a.get("name") for a in doc["animations"]}; added = []; cache = {}
    for alias, (path, asset, skeleton) in THROW_CLIPS.items():
        assert alias not in existing, "throw clip already present: " + alias
        curves = cache.setdefault(path, curves_of(path)); mapping, roots = prop_nodes(asset, skeleton)
        keys = np.array(max(curves.values(), key=lambda c: len(c["Times"]))["Times"])
        times = np.unique(np.concatenate([keys] + [keys[:-1] + (keys[1:] - keys[:-1]) * k / DENSE for k in range(1, DENSE)]).astype("<f4"))
        assert len(times) == (len(keys) - 1) * DENSE + 1 and all(np.any(np.abs(times - k) < 1e-6) for k in keys), alias
        out = {"name": alias, "channels": [], "samplers": []}; actor = 0
        # Throws replace the upper body only (the legs keep the gait); planting kneels, so its clips also carry the pelvis
        # and legs (r2-c4-completion-20260929 c03: upper body alone over the crouch gait left the bomb 0.2 m up at the
        # commit). Never the root motion: the entity keeps its own position and heading.
        whole_body = alias.startswith("plant")
        for (old, prop), curve in curves.items():
            target = nodes[old].get("name")
            if target not in names or old in mapping or (target == "root_motion" and (prop == "translation" or whole_body)) or prop == "weights": continue
            if source_names.get(target) != old or not (upper(names[target]) or whole_body and pelvis_down(names[target]) and not target.startswith("cswp_")): continue
            add(out, names[target], prop, curve["Times"], curve["Values"]); actor += 1
        hand_r = source_names["hand_R"]; at_key = []
        for k in keys:
            of = absolute(curves, k); at_key.append(of(socket) @ np.linalg.inv(of(hand_r)))
        sockets = []; mounts = []; off_chord = 0.0
        for t in times:
            of = absolute(curves, t); hi = int(np.searchsorted(keys, t)); exact = hi < len(keys) and abs(float(keys[hi]) - float(t)) < 1e-6
            if exact or hi == 0 or hi >= len(keys): held = of(socket)
            else:
                f = (float(t) - float(keys[hi - 1])) / max(1e-6, float(keys[hi]) - float(keys[hi - 1])); a, b = at_key[hi - 1], at_key[hi]
                qa, qb = Rotation.from_matrix(a[:3, :3].T).as_quat(), Rotation.from_matrix(b[:3, :3].T).as_quat()
                if qa @ qb < 0: qb = -qb
                q = qa + (qb - qa) * f; offset = np.eye(4); offset[:3, :3] = Rotation.from_quat(q / np.linalg.norm(q)).as_matrix().T
                offset[3, :3] = a[3, :3] + (b[3, :3] - a[3, :3]) * f
                held = offset @ of(hand_r); off_chord = max(off_chord, float(np.linalg.norm(held[3, :3] - of(socket)[3, :3])))
            sockets.append(held); mounts.append(held @ np.linalg.inv(of(anchor)))
        add(out, mount, "translation", times, [m[3, :3] for m in mounts])
        rotations = Rotation.from_matrix(np.array([m[:3, :3].T for m in mounts])).as_quat()
        for i in range(1, len(rotations)):
            if rotations[i] @ rotations[i - 1] < 0: rotations[i] *= -1
        add(out, mount, "rotation", times, rotations)
        tracked = HAND_TRACKED.get(alias); tracked_values = None
        if tracked:
            bone, hand, reference = tracked; ref_path = THROW_CLIPS[reference][0]; ref = cache.setdefault(ref_path, curves_of(ref_path))
            ref_time = max(c["Times"][-1] for c in ref.values()); ref_of = absolute(ref, ref_time)
            old_bone = next(o for o, new in mapping.items() if doc["nodes"][new]["name"] == "cswp_" + asset + "/" + bone)
            chain = []; walk = old_bone
            while walk not in roots: chain.append(walk); walk = parents[walk]
            root_new = mapping[walk]
            def bone_abs(curve_set, t, socket_abs):
                m = np.eye(4)
                for o in chain: m = m @ local_of(mapping[o], curve_set, o, t, roots)
                return m @ _matrix(doc["nodes"][root_new]) @ socket_abs
            offset = bone_abs(ref, ref_time, ref_of(socket)) @ np.linalg.inv(ref_of(source_names[hand]))
            tracked_values = []
            for t, held in zip(times, sockets):
                of = absolute(curves, t); desired = offset @ of(source_names[hand])
                parent = np.eye(4)
                for o in chain[1:]: parent = parent @ local_of(mapping[o], curves, o, t, roots)
                parent = parent @ _matrix(doc["nodes"][root_new]) @ held
                tracked_values.append(desired @ np.linalg.inv(parent))
        for old, new in mapping.items():
            tracked_here = tracked and doc["nodes"][new]["name"] == "cswp_" + asset + "/" + tracked[0]
            for prop, fallback in (("translation", [0, 0, 0]), ("rotation", [0, 0, 0, 1]), ("scale", [1, 1, 1])):
                if tracked_here and prop == "translation": values = [m[3, :3] for m in tracked_values]
                elif tracked_here and prop == "rotation":
                    values = Rotation.from_matrix(np.array([m[:3, :3].T for m in tracked_values])).as_quat()
                    for i in range(1, len(values)):
                        if values[i] @ values[i - 1] < 0: values[i] *= -1
                elif (old, prop) in curves and old not in roots: values = [_key(curves[old, prop], float(t)) for t in times]
                else: values = [doc["nodes"][new].get(prop, fallback)] * len(times)
                add(out, new, prop, times, values)
        # Release moment of a throw: the right hand's greatest speed in the actor's own space (the clip has no event).
        release = None
        if alias.startswith("throw"):
            hands = np.array([absolute(curves, t)(source_names["hand_R"])[3, :3] - absolute(curves, t)(source_names["pelvis"])[3, :3] for t in keys])
            speed = np.linalg.norm(np.diff(hands, axis=0), axis=1) / np.maximum(1e-6, np.diff(keys)); release = float(keys[int(np.argmax(speed)) + 1])
        # Planting: where the bomb is at each source key, relative to the right hand and above the feet (the clip has no
        # event for the moment it is set down), so the runtime maps the gameplay commit onto the clip from data.
        plant = None
        if alias.startswith("plant_"):
            ankles = [source_names[b] for b in ("ankle_L", "ankle_R") if b in source_names]
            plant = []
            for t in keys:
                of = absolute(curves, t); held = of(socket)[3, :3]; hand = of(source_names["hand_R"])[3, :3]
                floor = min(of(b)[3, 1] for b in ankles) if ankles else 0.0
                plant.append({"t": round(float(t), 4), "socketToHand": round(float(np.linalg.norm(held - hand)), 4), "socketAboveAnkles": round(float(held[1] - floor), 4)})
        doc["animations"].append(out)
        added.append({"alias": alias, "source": path, "prop": asset, "duration": float(times[-1]), "frames": len(keys), "propFrames": len(times), "actorChannels": actor,
                      "propBones": len(mapping), "handTracked": bool(tracked), "fastestRightHandAt": release,
                      "socketOffTheChordBetweenKeys": off_chord, "plantTrack": plant})
    for key in before:
        if key in ("accessors", "bufferViews", "animations", "buffers"): continue
        assert doc[key] == before[key], key
    for key in ("accessors", "bufferViews", "animations"):
        assert doc[key][:len(before[key])] == before[key], key
    assert bytes(blob[:len(original_blob)]) == original_blob
    blob.extend(b"\0" * (-len(blob) % 4)); doc["buffers"][0]["byteLength"] = len(blob)
    js = json.dumps(doc, separators=(",", ":")).encode(); js += b" " * (-len(js) % 4)
    out_bytes = struct.pack("<4sII", b"glTF", 2, 28 + len(js) + len(blob)) + struct.pack("<II", len(js), 0x4E4F534A) + js + struct.pack("<II", len(blob), 0x004E4942) + bytes(blob)
    Path(out_path).write_bytes(out_bytes)
    return {"dense": str(dense_path), "denseSha256": hashlib.sha256(data).hexdigest(), "source": str(source_path), "out": str(out_path),
            "outSha256": hashlib.sha256(out_bytes).hexdigest(), "clipsBefore": len(before["animations"]), "clipsAfter": len(doc["animations"]), "added": added}

if __name__ == "__main__":
    report = append_throw(sys.argv[1], sys.argv[2], sys.argv[3])
    Path(sys.argv[4]).write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", "utf-8"); print(json.dumps(report, ensure_ascii=False)[:3000])
