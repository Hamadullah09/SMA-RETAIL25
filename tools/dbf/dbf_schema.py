import sys, os, struct, glob, re

# Prints the structure of every DBF in a folder (plus CDX tag expressions and FPT block size).
folder = sys.argv[1]
VERSIONS = {0x03: 'dBASE III', 0x30: 'Visual FoxPro', 0x31: 'VFP autoinc', 0x83: 'dBASE III + DBT memo',
            0xF5: 'FoxPro 2.x + FPT memo', 0x8B: 'dBASE IV + memo'}

for path in sorted(glob.glob(os.path.join(folder, '*.[dD][bB][fF]'))):
    d = open(path, 'rb').read()
    ver, yy, mm, dd, nrec, hdrlen, reclen = struct.unpack('<BBBBIHH', d[:12])
    flags, cp = d[28], d[29]
    print(f'## {os.path.basename(path)}  version=0x{ver:02X} ({VERSIONS.get(ver, "?")})  records={nrec}  '
          f'reclen={reclen}  hdrlen={hdrlen}  lastupdate={1900 + yy}-{mm:02}-{dd:02}  tableflags=0x{flags:02X}  codepage=0x{cp:02X}')
    off = 32
    while d[off] != 0x0D:
        name = d[off:off + 11].split(b'\0')[0].decode('latin-1')
        typ = chr(d[off + 11])
        disp = struct.unpack('<I', d[off + 12:off + 16])[0]
        ln, dec, fflags = d[off + 16], d[off + 17], d[off + 18]
        if typ == 'C' and dec:
            ln, dec = ln + dec * 256, 0  # Clipper/VO: long C fields use the decimals byte
        print(f'  {name:<11} {typ} {ln:>4} {dec:>2}  @{disp}' + (f'  fieldflags=0x{fflags:02X}' if fflags else ''))
        off += 32
    base = os.path.splitext(path)[0]
    for ext in ('.CDX', '.cdx'):
        if os.path.exists(base + ext):
            c = open(base + ext, 'rb').read()
            exprs = sorted(set(m.group().decode('latin-1') for m in re.finditer(rb'[A-Za-z_][A-Za-z0-9_+()\-,. "\']{2,80}(?=\x00)', c)))
            print('  CDX strings:', exprs)
            break
    for ext in ('.FPT', '.fpt'):
        if os.path.exists(base + ext):
            p = open(base + ext, 'rb').read(8)
            print('  FPT next-free-block', struct.unpack('>I', p[:4])[0], 'blocksize', struct.unpack('>H', p[6:8])[0])
            break
    print()
