"""Crop and magnify a PNG so screenshot detail can be inspected directly."""
import zlib, struct, sys

def read(path):
    d = open(path, 'rb').read()
    pos, idat, w = 8, b'', None
    while pos < len(d):
        ln = struct.unpack('>I', d[pos:pos+4])[0]
        typ = d[pos+4:pos+8]
        data = d[pos+8:pos+8+ln]
        if typ == b'IHDR':
            w, h, depth, color = struct.unpack('>IIBB', data[:10])
        elif typ == b'IDAT':
            idat += data
        pos += 12 + ln
    raw = zlib.decompress(idat)
    ch = {0: 1, 2: 3, 4: 2, 6: 4}[color]
    stride = w * ch
    out, prev = [], bytearray(stride)
    p = 0
    for _ in range(h):
        ft = raw[p]; p += 1
        line = bytearray(raw[p:p+stride]); p += stride
        for i in range(stride):
            a = line[i-ch] if i >= ch else 0
            b = prev[i]
            c = prev[i-ch] if i >= ch else 0
            if ft == 1: line[i] = (line[i]+a) & 255
            elif ft == 2: line[i] = (line[i]+b) & 255
            elif ft == 3: line[i] = (line[i]+(a+b)//2) & 255
            elif ft == 4:
                pp = a+b-c
                pa, pb, pc = abs(pp-a), abs(pp-b), abs(pp-c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[i] = (line[i]+pr) & 255
        out.append(bytes(line)); prev = line
    return w, h, ch, out

def write(path, w, h, rows):
    raw = b''.join(b'\x00'+r for r in rows)
    def chunk(t, d):
        c = t+d
        return struct.pack('>I', len(d))+c+struct.pack('>I', zlib.crc32(c) & 0xffffffff)
    open(path, 'wb').write(b'\x89PNG\r\n\x1a\n'
        + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 2, 0, 0, 0))
        + chunk(b'IDAT', zlib.compress(raw, 6)) + chunk(b'IEND', b''))

if __name__ == '__main__':
    src, dst, x0, y0, x1, y1, mag = sys.argv[1], sys.argv[2], *map(int, sys.argv[3:8])
    w, h, ch, rows = read(src)
    out = []
    for y in range(y0, min(y1, h)):
        line = rows[y]
        px = [line[x*ch:x*ch+3] for x in range(x0, min(x1, w))]
        r = b''.join(p*mag for p in px)
        for _ in range(mag):
            out.append(r)
    write(dst, (min(x1, w)-x0)*mag, len(out), out)
    print(dst, (min(x1, w)-x0)*mag, len(out))
