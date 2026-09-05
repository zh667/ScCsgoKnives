#!/usr/bin/env python3
"""Reader for Valve binary DMX files (encoding "binary" 9, format "model" 22).

Written for the CS2 first-person viewmodel animations under
``local_cs2_analysis/all_weapons/08_first_person/decompiled/animation``.
Layout was reversed from the files themselves; the parse is self-checking:
:func:`load` refuses any file it cannot consume down to the last byte.

Only reading is implemented. No Valve or GPL code is used or vendored here,
and nothing in this module ships inside the mod - it is an offline tool.
"""

from __future__ import annotations

import struct
from dataclasses import dataclass, field
from pathlib import Path


# Attribute type ids. Scalars are 1..14; bit 0x20 marks the array form of the
# same scalar (element array is 0x21, vector3 array 0x2a, quaternion 0x2d), which
# is how encoding 9 differs from the older "first array type" numbering.
(AT_ELEMENT, AT_INT, AT_FLOAT, AT_BOOL, AT_STRING, AT_BINARY, AT_TIME,
 AT_COLOR, AT_VECTOR2, AT_VECTOR3, AT_VECTOR4, AT_QANGLE, AT_QUATERNION,
 AT_MATRIX) = range(1, 15)
AT_ARRAY_FLAG = 0x20

_SCALAR_NAMES = {
    AT_ELEMENT: "element", AT_INT: "int", AT_FLOAT: "float", AT_BOOL: "bool",
    AT_STRING: "string", AT_BINARY: "binary", AT_TIME: "time",
    AT_COLOR: "color", AT_VECTOR2: "vector2", AT_VECTOR3: "vector3",
    AT_VECTOR4: "vector4", AT_QANGLE: "qangle", AT_QUATERNION: "quaternion",
    AT_MATRIX: "matrix",
}

# Fixed-width float payloads: type id -> component count.
_FLOAT_WIDTH = {AT_VECTOR2: 2, AT_VECTOR3: 3, AT_VECTOR4: 4, AT_QANGLE: 3,
                AT_QUATERNION: 4, AT_MATRIX: 16}


class DmxError(Exception):
    pass


@dataclass
class Element:
    """One DMX element. ``attrs`` maps attribute name to a decoded value.

    Element-valued attributes hold an :class:`Element` (or ``None``); element
    arrays hold a list of those. External element references - which the model
    format uses for cross-file links - become an :class:`ExternalRef`.
    """

    type: str
    name: str
    guid: bytes
    index: int
    attrs: dict = field(default_factory=dict)

    def get(self, key, default=None):
        return self.attrs.get(key, default)

    def __repr__(self) -> str:  # keep debugging output readable
        return f"<Element #{self.index} {self.type} {self.name!r} {len(self.attrs)} attrs>"


@dataclass
class ExternalRef:
    guid: bytes
    path: str


@dataclass
class Datamodel:
    encoding: str
    encoding_version: int
    format: str
    format_version: int
    elements: list
    strings: list
    prefix_elements: list

    @property
    def root(self):
        return self.elements[0] if self.elements else None

    def by_type(self, type_name: str):
        return [e for e in self.elements if e.type == type_name]


class _Reader:
    def __init__(self, data: bytes, encoding_version: int, strings: list):
        self.d = data
        self.o = 0
        self.ver = encoding_version
        self.strings = strings

    # --- primitives -------------------------------------------------
    def i32(self) -> int:
        (v,) = struct.unpack_from("<i", self.d, self.o)
        self.o += 4
        return v

    def u8(self) -> int:
        v = self.d[self.o]
        self.o += 1
        return v

    def f32n(self, n: int):
        v = struct.unpack_from("<%df" % n, self.d, self.o)
        self.o += 4 * n
        return list(v)

    def raw(self, n: int) -> bytes:
        v = self.d[self.o:self.o + n]
        if len(v) != n:
            raise DmxError("truncated read of %d bytes at %d" % (n, self.o))
        self.o += n
        return v

    def cstr(self) -> str:
        e = self.d.index(b"\0", self.o)
        v = self.d[self.o:e].decode("utf-8", "replace")
        self.o = e + 1
        return v

    def dict_str(self) -> str:
        # Encoding 5 and up index the string table with a 32-bit integer.
        idx = self.i32() if self.ver >= 5 else struct.unpack_from("<h", self.d, self.o)[0]
        if self.ver < 5:
            self.o += 2
        if not 0 <= idx < len(self.strings):
            raise DmxError("string index %d out of range at %d" % (idx, self.o))
        return self.strings[idx]


def _read_value(r: _Reader, atype: int, elements: list, in_array: bool):
    if atype == AT_ELEMENT:
        idx = r.i32()
        if idx == -1:
            return None
        if idx == -2:
            # External reference: 16-byte guid follows as a string.
            return ExternalRef(b"", r.cstr())
        if not 0 <= idx < len(elements):
            raise DmxError("element index %d out of range" % idx)
        return elements[idx]
    if atype == AT_INT:
        return r.i32()
    if atype == AT_FLOAT:
        return r.f32n(1)[0]
    if atype == AT_BOOL:
        return bool(r.u8())
    if atype == AT_STRING:
        # Attribute strings come from the dictionary, array entries are inline.
        return r.cstr() if (r.ver < 4 or in_array) else r.dict_str()
    if atype == AT_BINARY:
        return r.raw(r.i32())
    if atype == AT_TIME:
        return r.i32() / 10000.0
    if atype == AT_COLOR:
        return list(r.raw(4))
    width = _FLOAT_WIDTH.get(atype)
    if width:
        return r.f32n(width)
    raise DmxError("unknown attribute type %d" % atype)


def load(path) -> Datamodel:
    data = Path(path).read_bytes()
    end = data.index(b"-->") + 3
    header = data[:end].decode("ascii")
    parts = header.split()
    if len(parts) < 8 or parts[1] != "dmx":
        raise DmxError("not a DMX file: %r" % header)
    encoding, encoding_version = parts[3], int(parts[4])
    fmt, format_version = parts[6], int(parts[7])
    if encoding != "binary":
        raise DmxError("only the binary encoding is supported, got %r" % encoding)
    if encoding_version < 9:
        raise DmxError("only encoding 9 and up verified, got %d" % encoding_version)

    o = end
    while o < len(data) and data[o] in (0x0A, 0x0D):
        o += 1
    if data[o] != 0:
        raise DmxError("expected NUL after header at %d" % o)
    o += 1

    r = _Reader(data[o:], encoding_version, [])

    prefix_count = r.i32()
    prefix_elements = []

    string_count = r.i32()
    strings = [r.cstr() for _ in range(string_count)]
    r.strings = strings

    element_count = r.i32()
    elements = []
    for i in range(element_count):
        etype = r.dict_str()
        ename = r.dict_str()
        guid = r.raw(16)
        elements.append(Element(etype, ename, guid, i))

    for el in elements:
        for _ in range(r.i32()):
            aname = r.dict_str()
            atype = r.u8()
            if atype & AT_ARRAY_FLAG:
                scalar = atype & ~AT_ARRAY_FLAG
                count = r.i32()
                el.attrs[aname] = [_read_value(r, scalar, elements, True)
                                   for _ in range(count)]
            else:
                el.attrs[aname] = _read_value(r, atype, elements, False)

    if r.o != len(r.d):
        raise DmxError("parse left %d of %d bytes unread"
                       % (len(r.d) - r.o, len(r.d)))
    if prefix_count:
        raise DmxError("prefix elements are present (%d) but unhandled" % prefix_count)

    return Datamodel(encoding, encoding_version, fmt, format_version,
                     elements, strings, prefix_elements)


def type_name(atype: int) -> str:
    if atype & AT_ARRAY_FLAG:
        return _SCALAR_NAMES.get(atype & ~AT_ARRAY_FLAG, "?") + "_array"
    return _SCALAR_NAMES.get(atype, "?")


if __name__ == "__main__":
    import sys
    from collections import Counter

    for arg in sys.argv[1:]:
        dm = load(arg)
        print(arg)
        print("  format %s %d, encoding %s %d, %d elements, %d strings"
              % (dm.format, dm.format_version, dm.encoding,
                 dm.encoding_version, len(dm.elements), len(dm.strings)))
        for t, c in Counter(e.type for e in dm.elements).most_common():
            print("    %-28s %4d" % (t, c))
