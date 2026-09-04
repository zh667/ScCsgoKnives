#!/usr/bin/env python3
"""Minimal glTF 2.0 / GLB reader for the CS2 exports.

Enough of the format for what stages 2 and 4 need: accessors (including the
sparse-free strided case), node hierarchy, meshes with their primitives, and
skins with inverse bind matrices. No writing, no PBR material evaluation.

Matrices come back in the row-vector convention the rest of this repo uses:
glTF stores column-major, so reading a float[16] straight into a 4x4 numpy
array already gives the transpose, which is what ``v @ M`` wants.
"""

from __future__ import annotations

import base64
import json
import struct
from dataclasses import dataclass
from pathlib import Path

import numpy as np

_COMPONENT = {5120: "b", 5121: "B", 5122: "h", 5123: "H", 5125: "I", 5126: "f"}
_COUNT = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT2": 4, "MAT3": 9, "MAT4": 16}
# Integer component types that carry normalized values when accessor.normalized is set.
_NORMALIZE = {5120: 127.0, 5121: 255.0, 5122: 32767.0, 5123: 65535.0}


class GlbError(Exception):
    pass


@dataclass
class Primitive:
    attributes: dict
    indices: np.ndarray
    material: str
    mode: int


@dataclass
class Mesh:
    name: str
    primitives: list


class Glb:
    def __init__(self, path):
        self.path = Path(path)
        data = self.path.read_bytes()
        if data[:4] == b"glTF":
            magic, version, _ = struct.unpack_from("<III", data, 0)
            if version != 2:
                raise GlbError("only glTF 2 is supported, got %d" % version)
            offset = 12
            self.json = None
            self.bin = b""
            while offset < len(data):
                length, kind = struct.unpack_from("<II", data, offset)
                chunk = data[offset + 8:offset + 8 + length]
                if kind == 0x4E4F534A:
                    self.json = json.loads(chunk.rstrip(b" \t\r\n\0"))
                elif kind == 0x004E4942:
                    self.bin = chunk
                offset += 8 + length + (-length % 4)
            if self.json is None:
                raise GlbError("no JSON chunk in %s" % path)
        else:
            self.json = json.loads(data)
            self.bin = b""
        self._buffers = {}

    # --- buffers -----------------------------------------------------
    def buffer(self, index: int) -> bytes:
        if index in self._buffers:
            return self._buffers[index]
        spec = self.json["buffers"][index]
        uri = spec.get("uri")
        if uri is None:
            blob = self.bin
        elif uri.startswith("data:"):
            blob = base64.b64decode(uri.split(",", 1)[1])
        else:
            blob = (self.path.parent / uri).read_bytes()
        self._buffers[index] = blob
        return blob

    def accessor(self, index: int) -> np.ndarray:
        spec = self.json["accessors"][index]
        count = spec["count"]
        components = _COUNT[spec["type"]]
        fmt = _COMPONENT[spec["componentType"]]
        if "bufferView" not in spec:
            return np.zeros((count, components), float)
        view = self.json["bufferViews"][spec["bufferView"]]
        blob = self.buffer(view.get("buffer", 0))
        base = view.get("byteOffset", 0) + spec.get("byteOffset", 0)
        item = struct.calcsize("<" + fmt) * components
        stride = view.get("byteStride") or item
        if stride == item:
            out = np.frombuffer(blob, np.dtype("<" + fmt), count * components, base)
            out = out.reshape(count, components)
        else:
            out = np.empty((count, components), np.dtype("<" + fmt))
            for i in range(count):
                out[i] = struct.unpack_from("<" + fmt * components, blob, base + i * stride)
        out = np.array(out)
        if spec.get("normalized") and spec["componentType"] in _NORMALIZE:
            out = out.astype(float) / _NORMALIZE[spec["componentType"]]
        if spec.get("sparse"):
            raise GlbError("sparse accessors are not implemented (accessor %d)" % index)
        return out

    # --- scene -------------------------------------------------------
    @property
    def nodes(self) -> list:
        return self.json.get("nodes", [])

    def node_name(self, index: int) -> str:
        return self.nodes[index].get("name") or "node%d" % index

    def node_matrix(self, index: int) -> np.ndarray:
        """Local transform of one node, row-vector convention."""
        node = self.nodes[index]
        if "matrix" in node:
            return np.array(node["matrix"], float).reshape(4, 4)
        m = np.eye(4)
        if "scale" in node:
            m = m @ np.diag(list(node["scale"]) + [1.0])
        if "rotation" in node:
            x, y, z, w = node["rotation"]
            r = np.array([
                [1 - 2 * (y * y + z * z), 2 * (x * y + w * z), 2 * (x * z - w * y), 0],
                [2 * (x * y - w * z), 1 - 2 * (x * x + z * z), 2 * (y * z + w * x), 0],
                [2 * (x * z + w * y), 2 * (y * z - w * x), 1 - 2 * (x * x + y * y), 0],
                [0, 0, 0, 1]], float)
            m = m @ r
        if "translation" in node:
            t = np.eye(4)
            t[3, :3] = node["translation"]
            m = m @ t
        return m

    def parents(self) -> dict:
        out = {}
        for i, node in enumerate(self.nodes):
            for child in node.get("children") or []:
                out[child] = i
        return out

    def world_matrix(self, index: int) -> np.ndarray:
        parents = self.parents()
        m = np.eye(4)
        cur = index
        while cur is not None:
            m = m @ self.node_matrix(cur)
            cur = parents.get(cur)
        return m

    def meshes(self) -> list:
        materials = self.json.get("materials", [])
        out = []
        for mesh in self.json.get("meshes", []):
            prims = []
            for prim in mesh["primitives"]:
                attrs = {k: self.accessor(v) for k, v in prim["attributes"].items()}
                idx = self.accessor(prim["indices"]).reshape(-1) if "indices" in prim else None
                name = (materials[prim["material"]].get("name")
                        if "material" in prim and prim["material"] < len(materials) else "")
                prims.append(Primitive(attrs, idx, name, prim.get("mode", 4)))
            out.append(Mesh(mesh.get("name", ""), prims))
        return out

    def skin(self, index: int) -> dict:
        spec = self.json["skins"][index]
        joints = [self.node_name(j) for j in spec["joints"]]
        ibm = (self.accessor(spec["inverseBindMatrices"]).astype(float).reshape(-1, 4, 4)
               if "inverseBindMatrices" in spec else
               np.tile(np.eye(4), (len(joints), 1, 1)))
        return {"joints": joints, "joint_nodes": spec["joints"], "inverse_bind": ibm}

    def mesh_skin(self, mesh_index: int):
        """The skin bound to the node that instantiates this mesh, if any."""
        for i, node in enumerate(self.nodes):
            if node.get("mesh") == mesh_index and "skin" in node:
                return self.skin(node["skin"])
        return None


if __name__ == "__main__":
    import sys

    for arg in sys.argv[1:]:
        g = Glb(arg)
        print(Path(arg).name)
        for i, mesh in enumerate(g.meshes()):
            skin = g.mesh_skin(i)
            for j, prim in enumerate(mesh.primitives):
                pos = prim.attributes["POSITION"]
                print("  %-58s prim%d %6d verts %6d tris  mat=%s%s"
                      % (mesh.name.split(".")[-1], j, len(pos),
                         0 if prim.indices is None else len(prim.indices) // 3,
                         prim.material,
                         "" if skin is None else "  joints=%d" % len(skin["joints"])))
