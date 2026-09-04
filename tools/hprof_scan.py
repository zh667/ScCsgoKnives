"""Scan a Java heap dump (HPROF, JDK 9+) for byte[] arrays holding text of interest --
shader sources, CSMCMDL6 headers, anything matching the markers -- without loading the
whole file. Compact strings keep Latin-1 text as a plain byte[] (the String's value
field), so a shader source handed to glShaderSource shows up as one byte[] here.

    python3 tools/hprof_scan.py dump.hprof out_dir [marker ...]
"""
import sys, struct, os, re

path, out = sys.argv[1], sys.argv[2]
markers = [m.encode() for m in (sys.argv[3:] or ['#version', 'void main', 'gecko', 'CSMCMDL', 'uniform ', 'layout('])]
os.makedirs(out, exist_ok=True)
f = open(path, 'rb')
hdr = bytearray()
while True:
    c = f.read(1)
    if c == b'\x00' or not c:
        break
    hdr += c
idsz = struct.unpack('>I', f.read(4))[0]
f.read(8)
print('format', hdr.decode(errors='replace'), 'id size', idsz)
BASIC = {2: idsz, 4: 1, 5: 2, 6: 4, 7: 8, 8: 1, 9: 2, 10: 4, 11: 8}


def rid(): return f.read(idsz)
def u1(): return f.read(1)[0]
def u2(): return struct.unpack('>H', f.read(2))[0]
def u4(): return struct.unpack('>I', f.read(4))[0]


found = 0
arrays = 0


def scan_segment(end):
    global found, arrays
    while f.tell() < end:
        tag = u1()
        if tag == 0xFF: rid()
        elif tag == 0x01: rid(); rid()
        elif tag in (0x02, 0x03): rid(); u4(); u4()
        elif tag == 0x04: rid(); u4()
        elif tag == 0x05: rid()
        elif tag == 0x06: rid(); u4()
        elif tag == 0x07: rid()
        elif tag == 0x08: rid(); u4(); u4()
        elif tag == 0x20:  # class dump
            rid(); u4(); rid(); rid(); rid(); rid(); rid(); rid(); u4()
            for _ in range(u2()): f.read(2); f.read(BASIC[u1()])
            for _ in range(u2()): rid(); f.read(BASIC[u1()])
            for _ in range(u2()): rid(); u1()
        elif tag == 0x21: rid(); u4(); rid(); n = u4(); f.seek(n, 1)
        elif tag == 0x22: rid(); u4(); n = u4(); rid(); f.seek(n * idsz, 1)
        elif tag == 0x23:
            rid(); u4(); n = u4(); t = u1(); size = BASIC[t]
            if t == 8 and n >= 16:
                data = f.read(n)
                arrays += 1
                if any(m in data for m in markers):
                    found += 1
                    name = os.path.join(out, f'bytes_{arrays:08d}_{n}.bin')
                    open(name, 'wb').write(data)
                    head = re.sub(rb'[^ -~]', b'.', data[:80]).decode()
                    print(f'  hit {found}: {n} bytes -> {name}  | {head}')
            else:
                f.seek(n * size, 1)
        else:
            raise SystemExit(f'unknown sub-record tag 0x{tag:02x} at {f.tell()}')


size = os.path.getsize(path)
while f.tell() < size:
    tag = u1()
    f.read(4)
    length = u4()
    start = f.tell()
    if tag in (0x0C, 0x1C):
        scan_segment(start + length)
    f.seek(start + length)
print(f'done: {arrays} byte arrays scanned, {found} hits')
