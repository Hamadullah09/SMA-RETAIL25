"""Independent integrity check of a table's production CDX against its records.

For every tag: walks the B-tree from the root, decodes each compact leaf, recomputes the expected key of every
record from the tag expression, and reports missing/extra/duplicate keys, wrong key bytes, ordering errors and
broken sibling links. Supports the expression forms Retail Plus uses: FIELD, UPPER(...), DESCEND(...), and '+'.

Usage: python cdx_check.py <table.dbf>
"""
import os
import re
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from dbfread import Table  # noqa: E402

PAGE = 512


def num_key(value):
    """FoxPro numeric/date key: big-endian IEEE double, sign bit flipped (positive) or all bits inverted (negative)."""
    b = bytearray(struct.pack('>d', value))
    if value >= 0:
        b[0] ^= 0x80
    else:
        b = bytearray(x ^ 0xFF for x in b)
    return bytes(b)


def julian(yyyymmdd):
    s = yyyymmdd.decode('latin-1').strip()
    if not s:
        return 0
    y, m, d = int(s[:4]), int(s[4:6]), int(s[6:8])
    a = (14 - m) // 12
    y2, m2 = y + 4800 - a, m + 12 * a - 3
    return d + (153 * m2 + 2) // 5 + 365 * y2 + y2 // 4 - y2 // 100 + y2 // 400 - 32045


DESCEND_DATE_BASE = 5231808   # Clipper/VO: DESCEND(date) = 5231808 - julian day (empty date = 0)


def evaluate(expr, table, rec):
    """Returns the key bytes of an expression for one record."""
    kind, value = value_of(expr, table, rec)
    if kind == 'C':
        return 'C', value
    return 'N', num_key(float(value))


def value_of(expr, table, rec):
    """Returns (kind, value): ('C', bytes), ('N', float) or ('D', julian day)."""
    expr = expr.strip()
    parts = split_plus(expr)
    if len(parts) > 1:
        return 'C', b''.join(value_of(p, table, rec)[1] for p in parts)
    m = re.fullmatch(r'(?i)(UPPER|DESCEND)\((.*)\)', expr)
    if m:
        kind, val = value_of(m.group(2), table, rec)
        if m.group(1).upper() == 'UPPER':
            return kind, val.upper()
        if kind == 'C':
            return 'C', bytes((256 - c) & 0xFF for c in val)
        if kind == 'D':
            return 'N', DESCEND_DATE_BASE - val
        return 'N', -val
    name, typ, ln, dec, pos = table.by_name[expr.upper()]
    raw = rec[pos:pos + ln]
    if typ in ('C', 'L'):
        return 'C', raw
    if typ == 'N':
        txt = raw.decode('latin-1').strip()
        return 'N', float(txt) if txt else 0.0
    if typ == 'D':
        return 'D', julian(raw)
    raise ValueError(f'unsupported field type {typ} in key')


def split_plus(expr):
    parts, depth, cur = [], 0, ''
    for ch in expr:
        if ch == '(':
            depth += 1
        elif ch == ')':
            depth -= 1
        if ch == '+' and depth == 0:
            parts.append(cur)
            cur = ''
        else:
            cur += ch
    parts.append(cur)
    return parts


def leaf_entries(d, page, keylen, trail_byte):
    nkeys = struct.unpack('<H', d[page + 2:page + 4])[0]
    recmask = struct.unpack('<I', d[page + 14:page + 18])[0]
    recbits, dupbits, trailbits, bpk = d[page + 20], d[page + 21], d[page + 22], d[page + 23]
    out, prev, end = [], b'', page + PAGE
    for i in range(nkeys):
        info = int.from_bytes(d[page + 24 + i * bpk: page + 24 + (i + 1) * bpk], 'little')
        rec = info & recmask
        dup = (info >> recbits) & ((1 << dupbits) - 1)
        trail = (info >> (recbits + dupbits)) & ((1 << trailbits) - 1)
        n = keylen - dup - trail
        end -= n
        key = prev[:dup] + d[end:end + n] + bytes([trail_byte]) * trail
        prev = key
        out.append((key, rec))
    return out


def walk_tag(d, root, keylen, trail_byte, problems, tag):
    """Returns all (key, recno) of a tag in leaf order."""
    page = root
    while not (struct.unpack('<H', d[page:page + 2])[0] & 2):           # descend through branch pages
        page = int.from_bytes(d[page + 12 + keylen + 4: page + 12 + keylen + 8], 'big')
    entries, prev_page = [], 0xFFFFFFFF
    while page != 0xFFFFFFFF:
        left = struct.unpack('<I', d[page + 4:page + 8])[0]
        if left != prev_page:
            problems.append(f'{tag}: leaf {page} left link {left} != previous leaf {prev_page}')
        entries += leaf_entries(d, page, keylen, trail_byte)
        prev_page, page = page, struct.unpack('<I', d[page + 8:page + 12])[0]
    return entries


def check(dbf_path):
    table = Table(dbf_path)
    base = os.path.splitext(dbf_path)[0]
    cdx_path = next((base + e for e in ('.CDX', '.cdx') if os.path.exists(base + e)), None)
    if not cdx_path:
        print(f'{os.path.basename(dbf_path)}: no production CDX')
        return 0
    d = open(cdx_path, 'rb').read()
    bag_root, bag_keylen = struct.unpack('<I', d[0:4])[0], struct.unpack('<H', d[12:14])[0]
    bag_problems = []
    tags = walk_tag(d, bag_root, bag_keylen, 0, bag_problems, 'tag list')
    total_problems = len(bag_problems)
    for p in bag_problems:
        print('  ' + p)
    print(f'{os.path.basename(cdx_path)}: {len(tags)} tags, table has {table.nrec} records, size {len(d)} bytes')
    for name_bytes, hdr in sorted(tags, key=lambda t: t[1]):
        name = name_bytes.rstrip(b'\0 ').decode('latin-1')
        root, keylen = struct.unpack('<I', d[hdr:hdr + 4])[0], struct.unpack('<H', d[hdr + 12:hdr + 14])[0]
        forlen, exprlen = struct.unpack('<H', d[hdr + 506:hdr + 508])[0], struct.unpack('<H', d[hdr + 510:hdr + 512])[0]
        expr = d[hdr + 512:hdr + 512 + exprlen].split(b'\0')[0].decode('latin-1')
        expected = {}
        kind = 'C'
        for n in range(1, table.nrec + 1):
            kind, key = evaluate(expr, table, table.record(n))
            expected[n] = key[:keylen].ljust(keylen, b' ' if kind == 'C' else b'\0')
        problems = []
        entries = walk_tag(d, root, keylen, 0x20 if kind == 'C' else 0x00, problems, name)
        seen = {}
        for key, rec in entries:
            if rec in seen:
                problems.append(f'recno {rec} appears twice')
            seen[rec] = key
            if rec not in expected:
                problems.append(f'key for recno {rec} which does not exist')
            elif key != expected[rec]:
                problems.append(f'recno {rec}: key {key!r} != expected {expected[rec]!r}')
        for rec in expected:
            if rec not in seen:
                problems.append(f'recno {rec} missing from tag')
        for (k1, r1), (k2, r2) in zip(entries, entries[1:]):
            if (k1, r1) > (k2, r2):
                problems.append(f'order: ({k1!r},{r1}) before ({k2!r},{r2})')
        status = 'OK' if not problems else f'{len(problems)} PROBLEM(S)'
        print(f'  tag {name:<10} {expr:<28} keys={len(entries):<4} {status}')
        for p in problems[:8]:
            print('      ' + p)
        total_problems += len(problems)
    return total_problems


if __name__ == '__main__':
    bad = sum(check(p) for p in sys.argv[1:])
    sys.exit(1 if bad else 0)
