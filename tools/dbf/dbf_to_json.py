"""Test oracle: writes a DBF table (and its FPT memos) as canonical JSON.

The C# reader in RetailPlus.Migration produces the same JSON for the same file, so the two implementations check
each other. The canonical form is defined here and documented in tools/dbf/README.md:

- C   decoded text, not trimmed. CP1252, except the 5 bytes CP1252 leaves undefined (0x81 0x8D 0x8F 0x90 0x9D),
      which become the C1 control characters U+0081 etc.
- N F the number as a minimal decimal string ("12.5", "0", "-3"); null when blank; {"invalid": text} otherwise.
      Accepted forms: optional sign, digits, optional decimal point and digits.
- D   "YYYY-MM-DD"; null when blank or all zeros; {"invalid": text} for anything else.
- L   true for T/t/Y/y, false for F/f/N/n, null for space or "?", {"invalid": text} otherwise.
- M   null when the block pointer is blank or 0; {"type": n, "text": decoded} for a readable memo;
      {"invalid": reason} when the pointer is bad, the block is past the end of the FPT, or there is no FPT.
- other field types  {"unsupported": type}

Usage: python dbf_to_json.py <table.dbf> [output.json]     (prints to stdout without an output path)
"""
import codecs
import json
import os
import re
import struct
import sys
from datetime import date
from decimal import Decimal

NUMBER = re.compile(r'^[+-]?([0-9]+\.?[0-9]*|\.[0-9]+)$')


def ascii_digits(t):
    return t != '' and all('0' <= ch <= '9' for ch in t)


def _c1_fallback(error):
    return ''.join(chr(b) for b in error.object[error.start:error.end]), error.end


codecs.register_error('retailplus_c1', _c1_fallback)


def text(b):
    return b.decode('cp1252', errors='retailplus_c1')


def number(raw):
    t = text(raw).strip(' \0')
    if not t:
        return None
    if not NUMBER.match(t):
        return {'invalid': text(raw)}
    d = Decimal(t)
    if d == 0:
        return '0'
    s = format(d, 'f')
    if '.' in s:
        s = s.rstrip('0').rstrip('.')
    return s


def date_value(raw):
    t = text(raw)
    if t.strip(' \0') == '' or t == '00000000':
        return None
    if len(t) != 8 or not ascii_digits(t):
        return {'invalid': t}
    try:
        return date(int(t[:4]), int(t[4:6]), int(t[6:8])).isoformat()
    except ValueError:
        return {'invalid': t}


def logical(raw):
    t = text(raw)
    if t in ('T', 't', 'Y', 'y'):
        return True
    if t in ('F', 'f', 'N', 'n'):
        return False
    if t in (' ', '?', '\0'):
        return None
    return {'invalid': t}


def memo(raw, fpt, blocksize):
    if len(raw) == 4:
        block = struct.unpack('<I', raw)[0]
    else:
        ref = text(raw).strip(' \0')
        if ref == '':
            return None
        if not ascii_digits(ref):
            return {'invalid': 'bad block pointer ' + ref}
        block = int(ref)
    if block == 0:
        return None
    if fpt is None:
        return {'invalid': 'no memo file'}
    offset = block * blocksize
    if blocksize == 0 or offset + 8 > len(fpt):
        return {'invalid': 'block %d past end of memo file' % block}
    kind, length = struct.unpack('>II', fpt[offset:offset + 8])
    if offset + 8 + length > len(fpt):
        return {'invalid': 'memo in block %d runs past end of memo file' % block}
    return {'type': kind, 'text': text(fpt[offset + 8:offset + 8 + length])}


def export(path):
    d = open(path, 'rb').read()
    version, yy, mm, dd = d[0], d[1], d[2], d[3]
    nrec, hdrlen, reclen = struct.unpack('<IHH', d[4:12])
    fields, off, pos = [], 32, 1
    while off < hdrlen and d[off] != 0x0D:
        name = d[off:off + 11].split(b'\0')[0].decode('ascii', errors='replace')
        typ, ln, dec = chr(d[off + 11]), d[off + 16], d[off + 17]
        if typ == 'C' and dec:
            ln, dec = ln + dec * 256, 0
        fields.append((name, typ, ln, dec, pos))
        pos += ln
        off += 32

    fpt, blocksize = None, 0
    base = os.path.splitext(path)[0]
    for ext in ('.FPT', '.fpt', '.Fpt'):
        if os.path.exists(base + ext):
            fpt = open(base + ext, 'rb').read()
            blocksize = struct.unpack('>H', fpt[6:8])[0] if len(fpt) >= 8 else 0
            break

    present = min(nrec, max(0, (len(d) - hdrlen) // reclen)) if reclen else 0
    records = []
    for n in range(1, present + 1):
        rec = d[hdrlen + (n - 1) * reclen: hdrlen + n * reclen]
        values = {}
        for name, typ, ln, dec, p in fields:
            raw = rec[p:p + ln]
            if typ == 'C':
                values[name] = text(raw)
            elif typ in ('N', 'F'):
                values[name] = number(raw)
            elif typ == 'D':
                values[name] = date_value(raw)
            elif typ == 'L':
                values[name] = logical(raw)
            elif typ == 'M':
                values[name] = memo(raw, fpt, blocksize)
            else:
                values[name] = {'unsupported': typ}
        records.append({'n': n, 'deleted': rec[0:1] == b'*', 'values': values})

    return {
        'file': os.path.basename(path),
        'header': {'version': version, 'last_update': [yy, mm, dd], 'record_count': nrec,
                   'header_length': hdrlen, 'record_length': reclen, 'table_flags': d[28], 'code_page': d[29]},
        'fields': [{'name': n, 'type': t, 'length': l, 'decimals': dc} for n, t, l, dc, _ in fields],
        'memo_block_size': blocksize if fpt is not None else None,
        'records_present': present,
        'truncated': present < nrec,
        'records': records,
    }


if __name__ == '__main__':
    out = json.dumps(export(sys.argv[1]), ensure_ascii=True, indent=1, sort_keys=False)
    if len(sys.argv) > 2:
        with open(sys.argv[2], 'w', encoding='ascii', newline='\n') as f:
            f.write(out + '\n')
    else:
        sys.stdout.write(out + '\n')
