import sys, os, struct

# Dumps records of a DBF, resolving FPT memo blocks; non-empty fields only.
path = sys.argv[1]
only = set(sys.argv[2].split(',')) if len(sys.argv) > 2 and sys.argv[2] else None
limit = int(sys.argv[3]) if len(sys.argv) > 3 else 999999
d = open(path, 'rb').read()
nrec, hdrlen, reclen = struct.unpack('<IHH', d[4:12])
fields, off, pos = [], 32, 1
while d[off] != 0x0D:
    name = d[off:off + 11].split(b'\0')[0].decode('latin-1')
    typ = chr(d[off + 11])
    ln = d[off + 16] + (d[off + 17] * 256 if typ == 'C' else 0)  # Clipper/VO: long C fields use the decimals byte
    fields.append((name, typ, ln, pos))
    pos += ln
    off += 32
fpt = None
for ext in ('.FPT', '.fpt'):
    if os.path.exists(os.path.splitext(path)[0] + ext):
        fpt = open(os.path.splitext(path)[0] + ext, 'rb').read()
        bs = struct.unpack('>H', fpt[6:8])[0]


def memo(ref):
    ref = ref.strip()
    if not ref or not ref.strip(b' \0'):
        return None
    blk = int(ref) if ref.isdigit() else struct.unpack('<I', ref[:4])[0]
    if blk == 0:
        return None
    o = blk * bs
    typ, ln = struct.unpack('>II', fpt[o:o + 8])
    return typ, fpt[o + 8:o + 8 + ln]


for r in range(min(nrec, limit)):
    rec = d[hdrlen + r * reclen: hdrlen + (r + 1) * reclen]
    print(f'--- rec {r + 1} deleted={rec[0:1] == b"*"}')
    for name, typ, ln, p in fields:
        if only and name not in only:
            continue
        raw = rec[p:p + ln]
        if typ == 'M' and fpt:
            m = memo(raw)
            if m:
                print(f'  {name} [memo type {m[0]}, {len(m[1])} bytes] {m[1][:400]!r}')
        else:
            v = raw.decode('latin-1')
            if v.strip():
                print(f'  {name} = {v!r}')
