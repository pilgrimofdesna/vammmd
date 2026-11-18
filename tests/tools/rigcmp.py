# Compares the C# rig (79 fixed bones on the built-in reference body) with an independent
# evaluator (mmdref.py) of the whole PMX the reference body was measured from.
#
#   mcs -langversion:6 -out:Dump.exe ../../Custom/Scripts/Usermade/VamMmd/src/Core/*.cs Dump.cs
#   mono Dump.exe rig DANCE.vmd 23 > dump.tsv
#   python3 rigcmp.py DANCE.vmd 23 dump.tsv BONES.json
#
# BONES.json is the bone table of the reference model (index, name, pos, parent, level, flags,
# append, ik), written by tools/pmx_bones.py from the PMX. The model itself is not in this repo.
# Expected: every bone within 0.0001 units and 0.001 degrees, except the right thumb (the model
# is not mirror-symmetric there, the built-in table is).
import sys, os, json, math, collections
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np
import mmdref
def fold(s):
    return ''.join(chr(ord(c)-0xFEE0) if 0xFF01 <= ord(c) <= 0xFF5E else c for c in s)
J = json.load(open(sys.argv[4], encoding='utf-8'))
bones = []
for x in J['bones']:
    b = dict(i=x['i'], name=fold(x['name']), pos=np.array(x['pos'], float), par=x['parent'], lvl=x['level'], fl=x['flags'])
    if 'append' in x: b['app'] = (x['append'][0], x['append'][1])
    if 'ik' in x:
        links = []
        for l in x['ik']['links']:
            hl = 1 if 'min' in l else 0
            links.append((l['bone'], hl, np.array(l['min'], float) if hl else None, np.array(l['max'], float) if hl else None))
        b['ik'] = (x['ik']['target'], x['ik']['loops'], x['ik']['limit'], links)
    bones.append(b)
M = mmdref.Model(bones)
tr = {fold(k): v for k, v in mmdref.load_vmd(sys.argv[1]).items()}
step = float(sys.argv[2])
cs = collections.defaultdict(dict)
for line in open(sys.argv[3], encoding='utf-8'):
    t = line.rstrip('\n').split('\t')
    cs[float(t[1])][t[0]] = np.array([float(v) for v in t[2:]])
F = 0.8; CX = 0.00274
worst = collections.defaultdict(lambda: [0.0, 0.0, 0, 0.0, 0.0])
frames = sorted(cs)
ikoff = ()
for fr in frames:
    M.evaluate(tr, fr, ik='saba', ikoff=ikoff)
    for name, v in cs[fr].items():
        if name not in M.idx: continue
        i = M.idx[name]
        dp = np.linalg.norm(M.wp[i] - (v[:3] + np.array([CX, F, 0])))
        da = mmdref.qangle(M.wr[i], v[3:])
        w = worst[name]; w[0] = max(w[0], dp); w[1] = max(w[1], da); w[2] += 1; w[3] += dp; w[4] += da
missing = [n for n in cs[frames[0]] if n not in M.idx]
print('frames', len(frames), 'bones compared', len(worst), 'not in the model:', missing)
allp = max(w[0] for w in worst.values()); alla = max(w[1] for w in worst.values())
print('worst position diff %.5f units, worst rotation diff %.4f deg' % (allp, alla))
for name, w in sorted(worst.items(), key=lambda kv: -kv[1][0])[:12]:
    print('  %-10s max %.5f mean %.5f units | max %.4f mean %.4f deg' % (name, w[0], w[3]/w[2], w[1], w[4]/w[2]))
