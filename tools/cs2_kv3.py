#!/usr/bin/env python3
"""Reader for Valve's KV3 text format, as the CS2 exports use it.

Enough for .vpcf, .vmat, .vnmclip and .vnmskel: nested objects and arrays,
quoted and multi-line strings, numbers, booleans, null, and the typed prefixes
these files carry (resource:"...", resource_name:"...", subclass:"...").

Multi-line strings are triple-quoted. Matching them on a doubled quote instead
made every ordinary empty string - and .vpcf is full of `m_NamedValue = ""` - open
a multi-line string that ran to the next empty string, swallowing whole blocks:
weapon_tracers_assrifle.vpcf parsed with no m_Operators and no m_Renderers at all.

    doc = cs2_kv3.load(path)      # -> dict

Trailing commas are allowed, comments are not stripped inside strings, and the
leading `<!-- kv3 ... -->` header is skipped.
"""

from __future__ import annotations

import re
from pathlib import Path

_TOKEN = re.compile(r"""
    (?P<ws>\s+)
  | (?P<comment>//[^\n]*)
  | (?P<mstring>\"\"\"(?:.|\n)*?\"\"\")
  | (?P<string>"(?:\\.|[^"\\])*")
  | (?P<prefix>[A-Za-z_][A-Za-z_0-9]*:(?="))
  | (?P<number>[+-]?(?:\d+\.\d*|\.\d+|\d+)(?:[eE][+-]?\d+)?)
  | (?P<name>[A-Za-z_][A-Za-z_0-9]*)
  | (?P<punct>[{}\[\],=])
""", re.VERBOSE)


class Kv3Error(Exception):
    pass


def _tokenize(text: str):
    pos = 0
    n = len(text)
    while pos < n:
        m = _TOKEN.match(text, pos)
        if not m:
            raise Kv3Error("cannot tokenize at %d: %r" % (pos, text[pos:pos + 40]))
        pos = m.end()
        kind = m.lastgroup
        if kind in ("ws", "comment"):
            continue
        yield kind, m.group()
    yield "eof", ""


class _Parser:
    def __init__(self, text):
        self.tokens = list(_tokenize(text))
        self.i = 0

    def peek(self):
        return self.tokens[self.i]

    def next(self):
        t = self.tokens[self.i]
        self.i += 1
        return t

    def expect(self, value):
        kind, text = self.next()
        if text != value:
            raise Kv3Error("expected %r, got %r" % (value, text))

    def value(self):
        kind, text = self.next()
        if text == "{":
            return self.obj()
        if text == "[":
            return self.array()
        if kind == "prefix":
            # resource:"path" and friends: keep the payload, drop the tag.
            return self.value()
        if kind == "mstring":
            return text[3:-3]
        if kind == "string":
            return text[1:-1].encode().decode("unicode_escape", "replace")
        if kind == "number":
            return float(text) if any(c in text for c in ".eE") else int(text)
        if kind == "name":
            if text == "true":
                return True
            if text == "false":
                return False
            if text == "null":
                return None
            return text
        raise Kv3Error("unexpected token %r" % text)

    def array(self):
        out = []
        while True:
            kind, text = self.peek()
            if text == "]":
                self.next()
                return out
            out.append(self.value())
            if self.peek()[1] == ",":
                self.next()

    def obj(self):
        out = {}
        while True:
            kind, text = self.peek()
            if text == "}":
                self.next()
                return out
            self.next()
            key = text[1:-1] if kind == "string" else text
            self.expect("=")
            out[key] = self.value()
            if self.peek()[1] == ",":
                self.next()


def loads(text: str):
    body = text
    if body.lstrip().startswith("<!--"):
        body = body[body.index("-->") + 3:]
    parser = _Parser(body)
    kind, first = parser.peek()
    if first == "{":
        parser.next()
        return parser.obj()
    return parser.value()


def load(path):
    return loads(Path(path).read_text("utf-8", "replace"))


def walk(node, cls=None):
    """Yield every dict in the tree, optionally only those with a given _class."""
    if isinstance(node, dict):
        if cls is None or node.get("_class") == cls:
            yield node
        for v in node.values():
            yield from walk(v, cls)
    elif isinstance(node, list):
        for v in node:
            yield from walk(v, cls)


if __name__ == "__main__":
    import json
    import sys

    for arg in sys.argv[1:]:
        print(arg)
        print(json.dumps(load(arg), indent=1)[:2000])
