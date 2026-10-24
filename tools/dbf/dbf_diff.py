"""Byte-level comparison of two copies of a DBF table (and its FPT memos).

Reports header differences (including reserved bytes), field-descriptor differences, and per-record changes
(deletion flag, each field's raw bytes, memo contents). Memo block numbers that move are reported separately
from memo text that changes.

Usage: python dbf_diff.py <before.dbf> <after.dbf>
"""
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from dbfread import Table  # noqa: E402

HEADER_FIELDS = [('version', 0, 1), ('last update YMD', 1, 3), ('record count', 4, 4), ('header length', 8, 2),
                 ('record length', 10, 2), ('reserved 12-27', 12, 16), ('table flags', 28, 1), ('code page', 29, 1),
                 ('reserved 30-31', 30, 2)]


def diff(a_path, b_path):
    a, b = Table(a_path), Table(b_path)
    print(f'{os.path.basename(a_path)}: {a.nrec} -> {b.nrec} records')
    for label, off, ln in HEADER_FIELDS:
        va, vb = a.raw[off:off + ln], b.raw[off:off + ln]
        if va != vb:
            print(f'  header {label}: {va.hex(" ")} -> {vb.hex(" ")}')
    if a.raw[32:a.hdrlen] != b.raw[32:b.hdrlen]:
        print('  field descriptors / header tail differ')
    for label, t in (('before', a), ('after', b)):
        end = t.hdrlen + t.nrec * t.reclen
        tail = t.raw[end:]
        if tail not in (b'\x1a', b''):
            print(f'  {label}: unexpected bytes after last record: {tail[:16].hex(" ")}')
        if tail == b'':
            print(f'  {label}: no EOF marker (0x1A) after last record')
    if a.fpt and b.fpt:
        fa, fb = struct.unpack('>I', a.fpt[:4])[0], struct.unpack('>I', b.fpt[:4])[0]
        print(f'  FPT next free block {fa} -> {fb}, block size {a.blocksize} -> {b.blocksize}, size {len(a.fpt)} -> {len(b.fpt)}')
    memo_fields = {f[0] for f in a.fields if f[1] == 'M'}
    changed = 0
    for n in range(1, max(a.nrec, b.nrec) + 1):
        if n > a.nrec:
            print(f'  rec {n}: appended')
            for name, typ, ln, dec, pos in b.fields:
                v = b.memo(b.record(n), name) if name in memo_fields else b.field_bytes(b.record(n), name)
                if v and (v.strip() if isinstance(v, bytes) else v):
                    print(f'      {name} = {v[:60]!r}')
            changed += 1
            continue
        if n > b.nrec:
            print(f'  rec {n}: missing after')
            changed += 1
            continue
        ra, rb = a.record(n), b.record(n)
        if ra == rb:
            continue
        changed += 1
        notes = []
        if ra[0:1] != rb[0:1]:
            notes.append(f'deleted flag {ra[0:1]!r} -> {rb[0:1]!r}')
        for name, typ, ln, dec, pos in a.fields:
            fa_, fb_ = ra[pos:pos + ln], rb[pos:pos + ln]
            if name in memo_fields:
                ma, mb = a.memo(ra, name), b.memo(rb, name)
                if ma != mb:
                    notes.append(f'{name} memo text changed ({len(ma or b"")} -> {len(mb or b"")} bytes)')
                elif fa_ != fb_:
                    notes.append(f'{name} memo moved block {fa_.strip()!r} -> {fb_.strip()!r} (same text)')
            elif fa_ != fb_:
                notes.append(f'{name}: {fa_.decode("latin-1")!r} -> {fb_.decode("latin-1")!r}')
        print(f'  rec {n}: ' + '; '.join(notes))
    print(f'  {changed} record(s) differ')


if __name__ == '__main__':
    diff(sys.argv[1], sys.argv[2])
