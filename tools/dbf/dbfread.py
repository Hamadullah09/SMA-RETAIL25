"""Minimal DBF/FPT reader shared by the checking tools (read-only, standard library only)."""
import os
import struct


class Table:
    def __init__(self, path):
        self.path = path
        self.raw = open(path, 'rb').read()
        d = self.raw
        self.version = d[0]
        self.nrec, self.hdrlen, self.reclen = struct.unpack('<IHH', d[4:12])
        self.fields = []          # (name, type, length, decimals, offset in record)
        off, pos = 32, 1
        while d[off] != 0x0D:
            name = d[off:off + 11].split(b'\0')[0].decode('latin-1')
            typ, ln, dec = chr(d[off + 11]), d[off + 16], d[off + 17]
            if typ == 'C' and dec:          # Clipper/VO long character field
                ln, dec = ln + dec * 256, 0
            self.fields.append((name, typ, ln, dec, pos))
            pos += ln
            off += 32
        self.by_name = {f[0].upper(): f for f in self.fields}
        self.fpt = None
        base = os.path.splitext(path)[0]
        for ext in ('.FPT', '.fpt'):
            if os.path.exists(base + ext):
                self.fpt = open(base + ext, 'rb').read()
                self.blocksize = struct.unpack('>H', self.fpt[6:8])[0]

    def record(self, n):
        """Raw bytes of 1-based record n (byte 0 is the deletion flag)."""
        start = self.hdrlen + (n - 1) * self.reclen
        return self.raw[start:start + self.reclen]

    def field_bytes(self, rec, name):
        _, _, ln, _, pos = self.by_name[name.upper()]
        return rec[pos:pos + ln]

    def memo(self, rec, name):
        ref = self.field_bytes(rec, name).strip()
        if not ref or not self.fpt:
            return None
        blk = int(ref) if ref.isdigit() else struct.unpack('<I', ref[:4])[0]
        if blk == 0:
            return None
        o = blk * self.blocksize
        _, ln = struct.unpack('>II', self.fpt[o:o + 8])
        return self.fpt[o + 8:o + 8 + ln]
