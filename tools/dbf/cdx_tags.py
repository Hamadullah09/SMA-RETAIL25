import sys, struct, glob, os

# Lists the tags of FoxPro-compatible compound (CDX) indexes: name, key expression, FOR condition, order.


def leaf_keys(d, page, keylen):
    attr, nkeys = struct.unpack('<HH', d[page:page + 4])
    recmask = struct.unpack('<I', d[page + 14:page + 18])[0]
    recbits, dupbits, trailbits, bpk = d[page + 20], d[page + 21], d[page + 22], d[page + 23]
    keys, prev, end = [], b'', page + 512
    for i in range(nkeys):
        info = int.from_bytes(d[page + 24 + i * bpk: page + 24 + (i + 1) * bpk], 'little')
        rec = info & recmask
        dup = (info >> recbits) & ((1 << dupbits) - 1)
        trail = (info >> (recbits + dupbits)) & ((1 << trailbits) - 1)
        n = keylen - dup - trail
        end -= n
        key = prev[:dup] + d[end:end + n]
        prev = key
        keys.append((key, rec))
    return attr, keys


def first_leaf(d, root, keylen):
    page = root
    while not (struct.unpack('<H', d[page:page + 2])[0] & 2):  # branch: follow first child pointer
        page = int.from_bytes(d[page + 12 + keylen + 4: page + 12 + keylen + 8], 'big')
    return page


def tags(path):
    d = open(path, 'rb').read()
    root, keylen = struct.unpack('<I', d[0:4])[0], struct.unpack('<H', d[12:14])[0]
    page, out = first_leaf(d, root, keylen), []
    while page not in (0, 0xFFFFFFFF):
        _, keys = leaf_keys(d, page, keylen)
        for name, hdr in keys:
            klen = struct.unpack('<H', d[hdr + 12:hdr + 14])[0]
            opts = d[hdr + 14]
            desc = struct.unpack('<H', d[hdr + 502:hdr + 504])[0]
            forlen, exprlen = struct.unpack('<H', d[hdr + 506:hdr + 508])[0], struct.unpack('<H', d[hdr + 510:hdr + 512])[0]
            pool = d[hdr + 512:hdr + 1024]
            expr = pool[:exprlen].split(b'\0')[0].decode('latin-1')
            cond = pool[exprlen:exprlen + forlen].split(b'\0')[0].decode('latin-1')
            out.append((name.rstrip(b'\0 ').decode('latin-1'), expr, cond, 'DESC' if desc else 'ASC', klen, opts))
        page = struct.unpack('<I', d[page + 8:page + 12])[0]
    return out


for p in sys.argv[1:]:
    for path in sorted(glob.glob(p)):
        print(os.path.basename(path))
        for t in tags(path):
            print(f'  tag {t[0]:<10} key {t[1]!r:<32} for {t[2]!r:<12} {t[3]} keylen={t[4]} opts=0x{t[5]:02X}')
