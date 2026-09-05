#!/usr/bin/env python3
"""CS2 first-person viewmodel rig: skeleton, channels and absolute matrices.

Reads one exported viewmodel animation (a binary DMX under
``08_first_person/decompiled/animation/anims/viewmodel/``) and exposes the same
maths the mod's renderer uses, so anything measured here is what the mod would
draw. Convention matches ``rigprobe.py`` and ``CsmcKnifeRig``: row vectors,
``v' = v @ M``, ``local = R(q) @ T(p)``, ``absolute = local @ parent``.

An exported clip DMX carries the merged tree already: the 56-bone viewmodel
skeleton and the weapon's own bones sit side by side as two roots of the
DmeModel. That holds for the knives too - knife_m9's idle carries 59 bones, of
which the same 44 arm bones the AK's 64 carry. The weapon root is re-parented onto the viewmodel's attach bone
(``wpn``, per ``viewmodel.vnmskel``'s m_secondarySkeletons) to make one tree.
"""

from __future__ import annotations

import re
from dataclasses import dataclass, field
from pathlib import Path

import numpy as np

import cs2_dmx

_EXPORTS = Path.home() / "workspaces/CSMCReverse/local_cs2_analysis/all_weapons"

# Two export trees, same conventions. The guns came first, in 08_first_person;
# the 22 knives arrived on 2026-09-05 in 09_knives. They share one primary
# skeleton - every one of the knives' 317 clips names
# animation/skeletons/characters/viewmodel.vnmskel, which only 08_first_person
# carries - so ANALYSIS stays the guns' tree and only the clip lookup spans both.
ROOTS = [_EXPORTS / "08_first_person", _EXPORTS / "09_knives"]

ANALYSIS = ROOTS[0]
ANIM = ANALYSIS / "decompiled/animation"
CLIPS = ANIM / "anims/viewmodel"


def clip_roots():
    """Every decompiled/animation/anims/viewmodel that exists."""
    return [r / "decompiled/animation/anims/viewmodel" for r in ROOTS
            if (r / "decompiled/animation/anims/viewmodel").is_dir()]


def clip_path(folder: str, stem: str) -> Path:
    """<folder>/<stem>.dmx from whichever export tree has it.

    Raises rather than returning a missing path: a silently absent clip used to
    surface much later as an empty rig.
    """
    tried = []
    for root in clip_roots():
        candidate = root / folder / (stem + ".dmx")
        tried.append(candidate)
        if candidate.is_file():
            return candidate
    raise FileNotFoundError("no clip %s/%s.dmx in any export tree:\n  %s"
                            % (folder, stem, "\n  ".join(str(t) for t in tried)))


def clip_stems(folder: str):
    """Every clip stem in a folder, from whichever tree has it."""
    for root in clip_roots():
        d = root / folder
        if d.is_dir():
            return sorted(p.stem for p in d.glob("*.dmx"))
    return []


def relative_to_root(path) -> str:
    """A path as <tree>/... so SourceFile says which export it came from."""
    path = Path(path)
    for root in ROOTS:
        try:
            return "%s/%s" % (root.name, path.relative_to(root))
        except ValueError:
            continue
    return str(path)

# Bone the weapon skeletons hang off. viewmodel.vnmskel lists it explicitly for
# 13 of the 35 weapons and names "wpn" every time; the rest (AWP included) carry
# no entry there, so "wpn" is the assumed attach and is checked by
# cs2_rig_selftest against the CS:MC muzzle distance.
DEFAULT_ATTACH_BONE = "wpn"


def attach_bones() -> dict:
    """Weapon skeleton stem -> attach bone id, from viewmodel.vnmskel."""
    text = (ANIM / "skeletons/characters/viewmodel.vnmskel").read_text()
    pairs = re.findall(
        r'm_attachToBoneID\s*=\s*"([^"]+)"\s*\n\s*m_skeleton\s*=\s*resource:"([^"]+)"',
        text)
    return {Path(res).stem: bone for bone, res in pairs}


def translation(t) -> np.ndarray:
    m = np.eye(4)
    m[3, :3] = t
    return m


def from_quat(q) -> np.ndarray:
    """Row-vector rotation matrix for an (x, y, z, w) quaternion."""
    x, y, z, w = q
    return np.array([
        [1 - 2 * (y * y + z * z), 2 * (x * y + w * z), 2 * (x * z - w * y), 0],
        [2 * (x * y - w * z), 1 - 2 * (x * x + z * z), 2 * (y * z + w * x), 0],
        [2 * (x * z + w * y), 2 * (y * z - w * x), 1 - 2 * (x * x + y * y), 0],
        [0, 0, 0, 1]], float)


def sample(times, values, t):
    """Linear (nlerp for quaternions) sample of one DmeLogLayer."""
    if not times:
        return None
    if t <= times[0]:
        return np.array(values[0], float)
    if t >= times[-1]:
        return np.array(values[-1], float)
    hi = int(np.searchsorted(np.asarray(times), t, side="left"))
    hi = max(1, min(hi, len(times) - 1))
    lo = hi - 1
    f = (t - times[lo]) / max(1e-9, times[hi] - times[lo])
    a = np.array(values[lo], float)
    b = np.array(values[hi], float)
    if a.size == 4:
        if a @ b < 0:
            b = -b
        q = a + (b - a) * f
        n = np.linalg.norm(q)
        return q / n if n > 1e-9 else a
    return a + (b - a) * f


@dataclass
class Bone:
    name: str
    parent: int
    rest_position: np.ndarray
    rest_orientation: np.ndarray
    position: tuple = None      # (times, values) or None when not animated
    orientation: tuple = None


@dataclass
class Clip:
    """One viewmodel animation with its merged skeleton."""

    name: str
    path: Path
    bones: list
    frame_rate: float
    duration: float
    weapon_root: str = None
    attach_bone: str = None
    _cache: dict = field(default_factory=dict, repr=False)

    @property
    def frame_count(self) -> int:
        """Frames on the clip's own timeline, endpoints included."""
        if self.duration <= 0:
            return 1
        return int(round(self.duration * self.frame_rate)) + 1

    @property
    def names(self) -> list:
        return [b.name for b in self.bones]

    def index(self, name: str) -> int:
        return self.names.index(name)

    def frame_time(self, frame: int) -> float:
        return frame / self.frame_rate if self.frame_rate > 0 else 0.0

    def local(self, i: int, t: float) -> np.ndarray:
        b = self.bones[i]
        p = sample(*b.position, t) if b.position else b.rest_position
        q = sample(*b.orientation, t) if b.orientation else b.rest_orientation
        return from_quat(q) @ translation(p)

    def absolute(self, t: float) -> dict:
        """Bone name -> absolute matrix at time ``t`` (inches, Source axes)."""
        key = round(t, 7)
        hit = self._cache.get(key)
        if hit is not None:
            return hit
        local = [self.local(i, t) for i in range(len(self.bones))]
        out = [None] * len(self.bones)

        def calc(i):
            if out[i] is None:
                p = self.bones[i].parent
                out[i] = local[i] @ calc(p) if p >= 0 else local[i]
            return out[i]

        for i in range(len(self.bones)):
            calc(i)
        result = {b.name: out[i] for i, b in enumerate(self.bones)}
        self._cache[key] = result
        return result

    def animated(self) -> list:
        return [b.name for b in self.bones if b.position or b.orientation]


def load_clip(path, attach_bone: str = DEFAULT_ATTACH_BONE) -> Clip:
    path = Path(path)
    dm = cs2_dmx.load(path)
    model = dm.by_type("DmeModel")[0]

    bones = []
    transform_index = {}

    def walk(joint, parent):
        i = len(bones)
        xf = joint.attrs["transform"]
        transform_index[id(xf)] = i
        bones.append(Bone(joint.name, parent,
                          np.array(xf.attrs["position"], float),
                          np.array(xf.attrs["orientation"], float)))
        for child in joint.attrs.get("children") or []:
            walk(child, i)

    roots = list(model.attrs.get("children") or [])
    for root in roots:
        walk(root, -1)

    clips = dm.by_type("DmeChannelsClip")
    if len(clips) != 1:
        raise cs2_dmx.DmxError("expected one DmeChannelsClip, got %d" % len(clips))
    channels_clip = clips[0]
    for channel in channels_clip.attrs["channels"]:
        target = channel.attrs["toElement"]
        i = transform_index.get(id(target))
        if i is None:
            continue
        layer = channel.attrs["log"].attrs["layers"][0]
        curve = (layer.attrs["times"], layer.attrs["values"])
        if channel.attrs["toAttribute"] == "position":
            bones[i].position = curve
        elif channel.attrs["toAttribute"] == "orientation":
            bones[i].orientation = curve

    # Second root (when present) is the weapon's own skeleton; hang it off the
    # viewmodel attach bone so one tree covers hands and weapon parts.
    weapon_root = None
    if len(roots) > 1:
        weapon_root = roots[-1].name
        names = [b.name for b in bones]
        bones[names.index(weapon_root)].parent = names.index(attach_bone)

    time_frame = dm.by_type("DmeTimeFrame")[0]
    return Clip(path.stem, path, bones,
                float(channels_clip.attrs["frameRate"]),
                float(time_frame.attrs["duration"]),
                weapon_root, attach_bone if weapon_root else None)


def rotation_of(m) -> np.ndarray:
    """Orthonormal 3x3 of a 4x4, with any scale (CS:MC carries 0.0254) removed."""
    a = np.array(m, float)[:3, :3]
    u, _, vt = np.linalg.svd(a)
    r = u @ vt
    if np.linalg.det(r) < 0:
        u[:, -1] *= -1
        r = u @ vt
    return r


def angle_between(ra, rb) -> float:
    """Degrees of the rotation carrying ``ra`` to ``rb``."""
    return float(np.degrees(np.arccos(np.clip((np.trace(ra.T @ rb) - 1) / 2, -1, 1))))


def umeyama(x, y):
    """Similarity transform (scale, rotation, offset) with ``y ~= s * x @ R + t``."""
    x = np.asarray(x, float)
    y = np.asarray(y, float)
    mx, my = x.mean(0), y.mean(0)
    xc, yc = x - mx, y - my
    u, s, vt = np.linalg.svd(xc.T @ yc / len(x))
    d = np.sign(np.linalg.det(u @ vt))
    r = u @ np.diag([1, 1, d]) @ vt
    scale = (s * np.array([1, 1, d])).sum() * len(x) / (xc ** 2).sum()
    return scale, r, my - scale * mx @ r


if __name__ == "__main__":
    import sys

    for arg in sys.argv[1:]:
        c = load_clip(arg)
        print("%s: %.4f fps, %.4f s, %d frames, %d bones (%d animated)"
              % (c.name, c.frame_rate, c.duration, c.frame_count,
                 len(c.bones), len(c.animated())))
