# Writes the bone table of a PMX 2.0/2.1 model as JSON (index, name, position, parent, level,
# flags, append source, IK block). Used to measure a reference body for Core/Fit.cs (RefBody)
# and as the model for tests/tools/rigcmp.py.
#
#   python3 pmx_bones.py MODEL.pmx BONES.json
import sys, struct, json
d = open(sys.argv[1], 'rb').read()
o = 0
def rd(fmt):
    global o
    v = struct.unpack_from('<' + fmt, d, o); o += struct.calcsize('<' + fmt); return v
assert d[:4] == b'PMX '
o = 4; ver, = rd('f'); n, = rd('B'); g = rd('%dB' % n)
enc = 'utf-16-le' if g[0] == 0 else 'utf-8'
addUV, vsz, tsz, msz, bsz, mosz, rsz = g[1], g[2], g[3], g[4], g[5], g[6], g[7]
def text():
    global o
    l, = rd('i'); s = d[o:o+l].decode(enc, 'replace'); o += l; return s
def idx(sz):
    return rd({1: 'b', 2: 'h', 4: 'i'}[sz])[0]
for _ in range(4): text()
nv, = rd('i')
for i in range(nv):
    o += 12 + 12 + 8 + 16 * addUV
    t, = rd('B')
    if t == 0: o += bsz
    elif t == 1: o += 2 * bsz + 4
    elif t == 2: o += 4 * bsz + 16
    elif t == 3: o += 2 * bsz + 4 + 36
    elif t == 4: o += 4 * bsz + 16
    o += 4
nf, = rd('i'); o += nf * vsz
nt, = rd('i')
for i in range(nt): text()
nm, = rd('i')
for i in range(nm):
    text(); text(); o += 16 + 12 + 4 + 12 + 1 + 16 + 4 + tsz * 2 + 1
    sh, = rd('B'); o += tsz if sh == 0 else 1
    text(); o += 4
nb, = rd('i'); bones = []
for i in range(nb):
    name = text(); text(); pos = rd('3f'); par = idx(bsz); lvl, = rd('i'); fl, = rd('H')
    b = dict(i=i, name=name, pos=[round(x, 5) for x in pos], parent=par, level=lvl, flags=fl)
    if fl & 1: b['tail_bone'] = idx(bsz)
    else: b['tail_off'] = [round(x, 5) for x in rd('3f')]
    if fl & 0x300:
        b['append'] = [idx(bsz), round(rd('f')[0], 4), bool(fl & 0x100), bool(fl & 0x200), bool(fl & 0x80)]
    if fl & 0x400: b['axis'] = [round(x, 5) for x in rd('3f')]
    if fl & 0x800: b['lx'] = [round(x, 5) for x in rd('3f')]; b['lz'] = [round(x, 5) for x in rd('3f')]
    if fl & 0x2000: rd('i')
    if fl & 0x20:
        tgt = idx(bsz); loops, = rd('i'); lim, = rd('f'); nl, = rd('i'); links = []
        for j in range(nl):
            lb = idx(bsz); hl, = rd('B'); L = dict(bone=lb)
            if hl: L['min'] = [round(x, 5) for x in rd('3f')]; L['max'] = [round(x, 5) for x in rd('3f')]
            links.append(L)
        b['ik'] = dict(target=tgt, loops=loops, limit=round(lim, 5), links=links)
    bones.append(b)
json.dump(dict(version=ver, bones=bones), open(sys.argv[2], 'w', encoding='utf-8'), ensure_ascii=False)
print(ver, nv, nb)
