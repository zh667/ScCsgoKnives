#!/usr/bin/env python3
"""Transfer legacy artwork to the existing HD atlas; never change runtime geometry.

Requires scipy, trimesh and rtree in addition to the usual numpy/Pillow tools.
Both meshes come from the same CS2 GLB bind pose. Correspondence is restricted
to the same named weapon bone, so a magazine cannot borrow receiver artwork.
This is a geometric approximation, not Valve's paint compositor.
"""
from pathlib import Path
import hashlib

import numpy as np
import trimesh
from scipy.ndimage import distance_transform_edt, map_coordinates

from cs2_glb import Glb


def body(glb, suffix):
    index, mesh = next((i, m) for i, m in enumerate(glb.meshes())
                       if m.name.endswith("body_" + suffix))
    prim = mesh.primitives[0]  # exclude separate glass/sticker materials
    a = prim.attributes
    joints = glb.mesh_skin(index)["joints"]
    owner = a["JOINTS_0"][np.arange(len(a["POSITION"])), a["WEIGHTS_0"].argmax(1)]
    faces = prim.indices.reshape(-1, 3)
    tri_owners = owner[faces]
    majority = np.where(tri_owners[:, 1] == tri_owners[:, 2], tri_owners[:, 1], tri_owners[:, 0])
    return {"positions": a["POSITION"].astype(float), "uv": a["TEXCOORD_0"].astype(float),
            "faces": faces, "bones": np.array(joints)[majority]}


def raster_surface(mesh, size):
    """Pixel-centred atlas samples; the exported GLB and runtime UVs are Y-down."""
    positions = np.zeros((size, size, 3), np.float32)
    face_ids = np.full((size, size), -1, np.int32)
    for fi, tri in enumerate(mesh["faces"]):
        uv = mesh["uv"][tri] * size - 0.5
        low = np.maximum(np.ceil(uv.min(0)).astype(int), 0)
        high = np.minimum(np.floor(uv.max(0)).astype(int), size - 1)
        if (low > high).any():
            continue
        a, b, c = uv
        det = (b[0]-a[0])*(c[1]-a[1]) - (b[1]-a[1])*(c[0]-a[0])
        if abs(det) < 1e-9:
            continue
        xx, yy = np.meshgrid(np.arange(low[0], high[0]+1), np.arange(low[1], high[1]+1))
        q = np.stack([xx, yy], -1)
        w1 = ((q[..., 0]-a[0])*(c[1]-a[1]) - (q[..., 1]-a[1])*(c[0]-a[0])) / det
        w2 = ((b[0]-a[0])*(q[..., 1]-a[1]) - (b[1]-a[1])*(q[..., 0]-a[0])) / det
        w0 = 1-w1-w2
        inside = (w0 >= -1e-7) & (w1 >= -1e-7) & (w2 >= -1e-7)
        y, x = yy[inside], xx[inside]
        positions[y, x] = np.stack([w0[inside], w1[inside], w2[inside]], -1) @ mesh["positions"][tri]
        face_ids[y, x] = fi
    return positions, face_ids


def correspondence(glb_path: Path, size: int, cache: Path):
    digest = hashlib.sha256(glb_path.read_bytes()).hexdigest()
    cache.mkdir(parents=True, exist_ok=True)
    path = cache / f"{glb_path.stem}-{digest[:16]}-{size}-v2.npz"
    if path.exists():
        with np.load(path) as data:
            return {k: data[k] for k in data.files}
    glb = Glb(glb_path)
    legacy, hd = body(glb, "legacy"), body(glb, "hd")
    pos, ids = raster_surface(hd, size)
    uv = np.zeros((size, size, 2), np.float32)
    distance = np.zeros((size, size), np.float32)
    valid = ids >= 0
    for bone in np.unique(hd["bones"]):
        selected = np.flatnonzero(legacy["bones"] == bone)
        target = valid & (hd["bones"][np.maximum(ids, 0)] == bone)
        if not len(selected):
            raise ValueError(f"{glb_path.name}: missing legacy bone {bone}")
        source = trimesh.Trimesh(vertices=legacy["positions"], faces=legacy["faces"][selected], process=False)
        yy, xx = np.where(target)
        print(f"  {bone}: {len(xx)} atlas samples", flush=True)
        for start in range(0, len(xx), 4096):
            y, x = yy[start:start+4096], xx[start:start+4096]
            closest, dist, face = trimesh.proximity.closest_point(source, pos[y, x])
            bary = trimesh.triangles.points_to_barycentric(source.triangles[face], closest)
            uv[y, x] = np.einsum("ni,nij->nj", bary, legacy["uv"][source.faces[face]])
            distance[y, x] = dist
    # Extend only into unused texels. This prevents dark atlas backgrounds bleeding
    # across island edges when the game filters or generates mipmaps.
    nearest = distance_transform_edt(~valid, return_distances=False, return_indices=True)
    uv = uv[tuple(nearest)]
    result = {"uv": uv, "valid": valid, "distance": distance, "position": pos}
    np.savez_compressed(path, **result)
    return result


def lookup(image, uv):
    h, w = image.shape[:2]
    coords = [uv[..., 1]*h-0.5, uv[..., 0]*w-0.5]
    if image.ndim == 2:
        return map_coordinates(image, coords, order=1, mode="nearest")
    return np.stack([map_coordinates(image[..., c], coords, order=1, mode="nearest")
                     for c in range(image.shape[-1])], -1)
