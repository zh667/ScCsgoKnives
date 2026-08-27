#!/usr/bin/env python3
"""Convert a named Bedrock bone subtree to a normalized textured OBJ."""

import argparse
import json
import math
from pathlib import Path


def rotate(point, rotation, pivot):
    x, y, z = (point[i] - pivot[i] for i in range(3))
    rx, ry, rz = (math.radians(v) for v in rotation)
    cy, sy = math.cos(rx), math.sin(rx)
    y, z = y * cy - z * sy, y * sy + z * cy
    cy, sy = math.cos(ry), math.sin(ry)
    x, z = x * cy + z * sy, -x * sy + z * cy
    cy, sy = math.cos(rz), math.sin(rz)
    x, y = x * cy - y * sy, x * sy + y * cy
    return [x + pivot[0], y + pivot[1], z + pivot[2]]


def transform_part(point, position, rotation):
    """Apply the same local transform order as TaCZ BedrockPart."""
    point = rotate(point, rotation, [0.0, 0.0, 0.0])
    return [point[i] + position[i] for i in range(3)]


def bone_position(bone, bones):
    """Convert an absolute Bedrock pivot to TaCZ's parent-local ModelPart position."""
    pivot = bone.get("pivot", [0.0, 0.0, 0.0])
    parent = bones.get(bone.get("parent"))
    if parent is None:
        return [pivot[0], 24.0 - pivot[1], pivot[2]]
    parent_pivot = parent.get("pivot", [0.0, 0.0, 0.0])
    return [
        pivot[0] - parent_pivot[0],
        parent_pivot[1] - pivot[1],
        pivot[2] - parent_pivot[2],
    ]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("target", type=Path)
    parser.add_argument("--bone", required=True)
    parser.add_argument("--size", type=float, default=2.0)
    args = parser.parse_args()

    doc = json.loads(args.source.read_text(encoding="utf-8"))
    geometry = doc["minecraft:geometry"][0]
    width = geometry["description"]["texture_width"]
    height = geometry["description"]["texture_height"]
    bones = {bone["name"]: bone for bone in geometry["bones"]}

    selected = set()
    pending = [args.bone]
    while pending:
        name = pending.pop()
        if name in selected:
            continue
        selected.add(name)
        pending.extend(b["name"] for b in bones.values() if b.get("parent") == name)

    vertices = []
    texcoords = []
    normals = []
    faces = []
    face_corners = {
        "north": [1, 0, 3, 2], "south": [4, 5, 6, 7],
        "east": [5, 1, 2, 6], "west": [0, 4, 7, 3],
        "up": [3, 7, 6, 2], "down": [0, 1, 5, 4],
    }

    for bone_name in selected:
        bone = bones.get(bone_name, {})
        chain = []
        cursor = bone
        while cursor:
            chain.append(cursor)
            cursor = bones.get(cursor.get("parent"))
        for cube in bone.get("cubes", []):
            origin = cube["origin"]
            size = cube["size"]
            bone_pivot = bone.get("pivot", [0.0, 0.0, 0.0])
            cube_rotation = cube.get("rotation")
            cube_pivot = cube.get("pivot")
            if cube_rotation is not None and cube_pivot is not None:
                # TaCZ creates a child ModelPart at the cube pivot. Cubes are
                # expressed relative to that child, with Minecraft's inverted Y.
                x0 = origin[0] - cube_pivot[0]
                y0 = cube_pivot[1] - origin[1] - size[1]
                z0 = origin[2] - cube_pivot[2]
            else:
                # Unrotated cubes live directly in their owning bone's local space.
                x0 = origin[0] - bone_pivot[0]
                y0 = bone_pivot[1] - origin[1] - size[1]
                z0 = origin[2] - bone_pivot[2]
            inflate = float(cube.get("inflate", 0.0))
            x1, y1, z1 = x0 + size[0], y0 + size[1], z0 + size[2]
            x0, y0, z0 = x0 - inflate, y0 - inflate, z0 - inflate
            x1, y1, z1 = x1 + inflate, y1 + inflate, z1 + inflate
            corners = [
                [x0, y0, z0], [x1, y0, z0], [x1, y1, z0], [x0, y1, z0],
                [x0, y0, z1], [x1, y0, z1], [x1, y1, z1], [x0, y1, z1],
            ]
            if cube_rotation is not None and cube_pivot is not None:
                cube_position = [
                    cube_pivot[0] - bone_pivot[0],
                    bone_pivot[1] - cube_pivot[1],
                    cube_pivot[2] - bone_pivot[2],
                ]
                corners = [transform_part(p, cube_position, cube_rotation) for p in corners]
            for ancestor in chain:
                rotation = ancestor.get("rotation", [0, 0, 0])
                position = bone_position(ancestor, bones)
                corners = [transform_part(p, position, rotation) for p in corners]

            base = len(vertices) + 1
            vertices.extend(corners)
            uv_data = cube.get("uv", {})
            for face_name, indices in face_corners.items():
                face_uv = uv_data.get(face_name)
                if not isinstance(face_uv, dict):
                    continue
                u, v = face_uv["uv"]
                du, dv = face_uv["uv_size"]
                # Match TaCZ BedrockPolygon: first vertex gets (u2,v1), then
                # (u1,v1), (u1,v2), (u2,v2). SC samples V=0 at PNG top.
                coords = [(u + du, v), (u, v), (u, v + dv), (u + du, v + dv)]
                uv_base = len(texcoords) + 1
                # SCAPI uploads PNG rows to OpenGL without vertically flipping them,
                # therefore V=0 addresses the top of the source image.  Its OBJ
                # reader also forwards texture coordinates unchanged.
                texcoords.extend([(px / width, py / height) for px, py in coords])
                p0, p1, p2 = (corners[indices[i]] for i in range(3))
                a = [p1[i] - p0[i] for i in range(3)]
                b = [p2[i] - p0[i] for i in range(3)]
                normal = [
                    a[1] * b[2] - a[2] * b[1],
                    a[2] * b[0] - a[0] * b[2],
                    a[0] * b[1] - a[1] * b[0],
                ]
                length = math.sqrt(sum(v * v for v in normal)) or 1.0
                normals.append([v / length for v in normal])
                normal_index = len(normals)
                face = [(base + index, uv_base + i, normal_index) for i, index in enumerate(indices)]
                faces.extend([[face[0], face[1], face[2]], [face[0], face[2], face[3]]])

    if not vertices:
        raise SystemExit(f"No cubes found under bone {args.bone!r}")

    mins = [min(p[i] for p in vertices) for i in range(3)]
    maxs = [max(p[i] for p in vertices) for i in range(3)]
    center = [(mins[i] + maxs[i]) / 2 for i in range(3)]
    scale = args.size / max(maxs[i] - mins[i] for i in range(3))
    vertices = [[(p[i] - center[i]) * scale for i in range(3)] for p in vertices]

    lines = [f"# Converted from {args.source.name}; permitted Survivalcraft port", "o knife"]
    lines.extend(f"v {x:.7f} {y:.7f} {z:.7f}" for x, y, z in vertices)
    lines.extend(f"vt {u:.7f} {v:.7f}" for u, v in texcoords)
    lines.extend(f"vn {x:.7f} {y:.7f} {z:.7f}" for x, y, z in normals)
    lines.append("s off")
    lines.extend("f " + " ".join(f"{vi}/{ti}/{ni}" for vi, ti, ni in face) for face in faces)
    args.target.write_text("\n".join(lines) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
