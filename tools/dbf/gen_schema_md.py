import sys, os, struct, glob, subprocess

# Writes a Markdown appendix of every table structure and index tag under a Retail Plus data folder.
root, out, cdxtool = sys.argv[1], sys.argv[2], sys.argv[3]
WHERE = {'': 'data root', 'POS': '`<LOC>\\` folder (sample location POS)', os.path.join('POS', 'PO'): '`<LOC>\\PO\\` folder'}
lines = []


def fields(path):
    d = open(path, 'rb').read(65536)
    ver, nrec, hdrlen, reclen = d[0], *struct.unpack('<IHH', d[4:12])
    res, off = [], 32
    while d[off] != 0x0D:
        name = d[off:off + 11].split(b'\0')[0].decode('latin-1')
        typ, ln, dec = chr(d[off + 11]), d[off + 16], d[off + 17]
        if typ == 'C' and dec:
            ln, dec = ln + dec * 256, 0
        res.append((name, typ, ln, dec))
        off += 32
    return ver, reclen, d[28], res


def tags(cdx):
    r = subprocess.run([sys.executable, '-I', cdxtool, cdx], capture_output=True, text=True).stdout.splitlines()[1:]
    return [l.strip() for l in r]


for sub in ('', 'POS', os.path.join('POS', 'PO')):
    folder = os.path.join(root, sub)
    for path in sorted(glob.glob(os.path.join(folder, '*')), key=lambda p: os.path.basename(p).upper()):
        name = os.path.basename(path)
        if not (name.upper().endswith('.DBF') or name.upper().endswith('.POS')) or not os.path.isfile(path):
            continue
        ver, reclen, flags, fl = fields(path)
        base = os.path.splitext(path)[0]
        memo = any(t == 'M' for _, t, _, _ in fl)
        cdx = next((base + e for e in ('.CDX', '.cdx') if os.path.exists(base + e)), None)
        lines.append(f'### {name.upper()}')
        lines.append('')
        kind = {0x03: 'dBASE III (0x03), no memo', 0xF5: 'FoxPro 2.x with FPT memo (0xF5)'}.get(ver, hex(ver))
        lines.append(f'- Location: {WHERE[sub]}')
        lines.append(f'- Header: {kind}; record length {reclen}; structural CDX flag {"set" if flags & 1 else "not set"}')
        if cdx:
            lines.append('- Index tags (' + os.path.basename(cdx).upper() + '):')
            for t in tags(cdx):
                parts = t.split()
                tag, key = parts[1], t.split("key '")[1].split("'")[0]
                lines.append(f'  - `{tag}` on `{key}`')
        lines.append('')
        lines.append('| Field | Type | Len | Dec |')
        lines.append('|---|---|---|---|')
        for n, t, l, dc in fl:
            lines.append(f'| {n} | {t} | {l} | {dc} |')
        lines.append('')

open(out, 'w', encoding='utf-8').write('\n'.join(lines))
print(len(lines), 'lines')
