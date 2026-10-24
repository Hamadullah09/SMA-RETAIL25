"""Generates small synthetic DBF/FPT files that exercise the edge cases the importer must handle.

They contain no Retail Plus data, so unlike the vendor sample files they can be committed. Each fixture comes with a
golden JSON file written by dbf_to_json.py, which the C# reader tests compare against.

Usage: python make_fixtures.py <output folder>
"""
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dbf_to_json  # noqa: E402


def cp(s):
    return s.encode('cp1252')


def write_dbf(path, version, fields, records, last_update=(26, 10, 24), header_count=None, code_page=0x03,
              eof=True, table_flags=0, truncate_to=None):
    """fields: (name, type, length, decimals); C fields longer than 255 use the Clipper/VO decimals-byte encoding.
    records: (deleted, [raw bytes per field])."""
    descriptors = b''
    reclen = 1
    for name, typ, length, dec in fields:
        stored_len, stored_dec = (length % 256, length // 256) if typ == 'C' and length > 255 else (length, dec)
        descriptors += name.encode('ascii').ljust(11, b'\0') + typ.encode('ascii') + b'\0' * 4 + \
            bytes([stored_len, stored_dec]) + b'\0' * 14
        reclen += length
    hdrlen = 32 + len(descriptors) + 1
    count = len(records) if header_count is None else header_count
    header = bytes([version, *last_update]) + struct.pack('<IHH', count, hdrlen, reclen) + b'\0' * 16 + \
        bytes([table_flags, code_page]) + b'\0\0'
    body = b''
    for deleted, values in records:
        rec = b'*' if deleted else b' '
        for (name, typ, length, dec), raw in zip(fields, values):
            assert len(raw) == length, (path, name, raw)
            rec += raw
        body += rec
    data = header + descriptors + b'\r' + body + (b'\x1a' if eof else b'')
    if truncate_to is not None:
        data = data[:truncate_to]
    with open(path, 'wb') as f:
        f.write(data)


class Fpt:
    """Builds an FPT memo file; add() returns the block number to put in the DBF field."""

    def __init__(self, blocksize):
        self.blocksize = blocksize
        header_blocks = (512 + blocksize - 1) // blocksize
        self.data = bytearray(header_blocks * blocksize)

    def add(self, payload, kind=1):
        block = len(self.data) // self.blocksize
        chunk = struct.pack('>II', kind, len(payload)) + payload
        chunk += b'\0' * (-len(chunk) % self.blocksize)
        self.data += chunk
        return block

    def save(self, path):
        next_free = len(self.data) // self.blocksize
        self.data[0:4] = struct.pack('>I', next_free)
        self.data[6:8] = struct.pack('>H', self.blocksize)
        with open(path, 'wb') as f:
            f.write(bytes(self.data))


def ref(block):
    return str(block).rjust(10).encode('ascii')


def c(s, n):
    b = s if isinstance(s, bytes) else cp(s)
    return b.ljust(n, b' ')[:n]


def num(s, n):
    return s.encode('ascii').rjust(n)


def make(out):
    os.makedirs(out, exist_ok=True)

    # 1. dBASE III table without memo: every scalar type and the awkward values found in real files.
    fields = [('NAME', 'C', 12, 0), ('PRICE', 'N', 9, 2), ('QTY', 'N', 6, 0), ('RATE', 'F', 8, 3),
              ('SOLD', 'D', 8, 0), ('TAXABLE', 'L', 1, 0)]
    rows = [
        (False, [c('Plain item', 12), num('12.50', 9), num('3', 6), num('0.125', 8), b'20261024', b'T']),
        (False, [c('Café €“q”', 12), num('-4.05', 9), num('-17', 6), num('-0.500', 8), b'20240229', b'F']),
        (False, [c('', 12), b' ' * 9, b' ' * 6, b' ' * 8, b' ' * 8, b' ']),
        (False, [c('Zeros', 12), num('0.00', 9), num('0', 6), num('.5', 8), b'00000000', b'?']),
        (False, [c('Bad values', 12), num('12a', 9), num('1 2', 6), num('1e5', 8), b'20261340', b'X']),
        (False, [c(b'Und\x81ef\x9d', 12), num('+7.10', 9), num('007', 6), num('3.', 8), b'2026 10 ', b'y']),
        (True, [c('Deleted row', 12), num('1.00', 9), num('1', 6), num('1.000', 8), b'20200101', b'n']),
        (False, [c(b'Nul\x00inside', 12), num('99999.99', 9), num('999999', 6), num('9999.999', 8), b'19991231', b'Y']),
    ]
    write_dbf(os.path.join(out, 'scalars.dbf'), 0x03, fields, rows)

    # 2. FoxPro 2.x table with FPT (block size 64) and a Clipper/VO long character field, like POSINV.
    fpt = Fpt(64)
    long_text = ','.join(['1.00'] * 54)
    fields = [('PLU', 'C', 16, 0), ('WEEKLY', 'C', 600, 0), ('NOTES', 'M', 10, 0), ('MATRIX', 'M', 10, 0)]
    rows = [
        (False, [c('T100', 16), c(long_text, 600),
                 ref(fpt.add(cp('Soft cotton shirt.\r\nSecond line.\r\n'))),
                 ref(fpt.add(cp('Colour,Size,Fit,|Navy,M,Slim,4.00|Grey,L,Classic,6.00')))]),
        (False, [c('T200', 16), c('', 600), ref(fpt.add(cp('x' * 200 + '\r\n' + 'y' * 150))), b' ' * 10]),
        (False, [c('T300', 16), c(b'0.00,1.00\x00\x00\x00,2.00', 600), ref(0), ref(fpt.add(cp('Café €'), kind=0))]),
        (False, [c('BAD1', 16), c('', 600), ref(9999), b'   12x    ']),
        (True, [c('DEL', 16), c('deleted', 600), ref(fpt.add(cp('memo of a deleted record'))), b' ' * 10]),
        (False, [c('EMPTY', 16), c('', 600), ref(fpt.add(b'')), b' ' * 10]),
    ]
    write_dbf(os.path.join(out, 'memo64.dbf'), 0xF5, fields, rows, table_flags=1)
    fpt.save(os.path.join(out, 'memo64.fpt'))

    # 3. Same idea with the older 32-byte memo blocks, and a lowercase file name like category.dbf.
    fpt = Fpt(32)
    fields = [('CODE', 'C', 4, 0), ('SUPLIST', 'M', 10, 0)]
    rows = [
        (False, [c('A1', 4), ref(fpt.add(cp('|ACME OUTDOOR SUPPLY,AX100,12.500,6.00|')))]),
        (False, [c('A2', 4), ref(fpt.add(cp('NORTHWIND TRADERS,NW-77,8.250,1|' * 3)))]),
    ]
    write_dbf(os.path.join(out, 'memo32.dbf'), 0xF5, fields, rows, last_update=(4, 10, 30))
    fpt.save(os.path.join(out, 'memo32.fpt'))

    # 4. Header claims more records than the file holds; the last record is cut off half way.
    fields = [('ID', 'N', 4, 0), ('NAME', 'C', 10, 0)]
    rows = [(False, [num(str(i), 4), c('row %d' % i, 10)]) for i in range(1, 4)]
    header_len = 32 + 2 * 32 + 1
    write_dbf(os.path.join(out, 'truncated.dbf'), 0x03, fields, rows, header_count=5, eof=False,
              truncate_to=header_len + 15 * 2 + 7)

    # 5. Memo field but the FPT is missing (someone copied only the DBF); old two-digit year; code page byte 0.
    fields = [('NAME', 'C', 6, 0), ('NOTES', 'M', 10, 0)]
    rows = [(False, [c('lost', 6), ref(8)]), (False, [c('none', 6), b' ' * 10])]
    write_dbf(os.path.join(out, 'nofpt.dbf'), 0xF5, fields, rows, last_update=(11, 7, 20), code_page=0)

    # 6. Empty table, as Retail Plus creates them before first use.
    write_dbf(os.path.join(out, 'empty.dbf'), 0x03, [('DEPT', 'C', 15, 0)], [])

    for name in sorted(os.listdir(out)):
        if name.lower().endswith('.dbf'):
            path = os.path.join(out, name)
            golden = os.path.splitext(path)[0] + '.json'
            data = dbf_to_json.export(path)
            import json
            with open(golden, 'w', encoding='ascii', newline='\n') as f:
                f.write(json.dumps(data, ensure_ascii=True, indent=1) + '\n')
            print('wrote', name, '+', os.path.basename(golden), '-', data['records_present'], 'records')


if __name__ == '__main__':
    make(sys.argv[1])
