# Reference evaluator (MMD coordinates): VMD sampling + FK + append + IK variants. Research probe only.
import struct, math, collections
import numpy as np

# ---------- quaternions (x,y,z,w) ----------
def qmul(a,b):
    ax,ay,az,aw=a; bx,by,bz,bw=b
    return np.array([aw*bx+ax*bw+ay*bz-az*by, aw*by-ax*bz+ay*bw+az*bx, aw*bz+ax*by-ay*bx+az*bw, aw*bw-ax*bx-ay*by-az*bz])
def qconj(a): return np.array([-a[0],-a[1],-a[2],a[3]])
def qrot(q,v):
    u=q[:3]; w=q[3]
    return v+2*np.cross(u,np.cross(u,v)+w*v)
def qnorm(q):
    n=np.linalg.norm(q); return q/n if n>0 else np.array([0,0,0,1.0])
def qaxis(axis,ang):
    axis=np.asarray(axis,float); n=np.linalg.norm(axis)
    if n<1e-12: return np.array([0,0,0,1.0])
    s=math.sin(ang/2)/n
    return np.array([axis[0]*s,axis[1]*s,axis[2]*s,math.cos(ang/2)])
def qslerp(a,b,t):
    d=float(np.dot(a,b))
    if d<0: b=-b; d=-d
    if d>0.9995: return qnorm(a+(b-a)*t)
    th=math.acos(d); s=math.sin(th)
    return a*(math.sin((1-t)*th)/s)+b*(math.sin(t*th)/s)
def qnlerp(a,b,t):
    if np.dot(a,b)<0: b=-b
    return qnorm(a+(b-a)*t)
def qpow(q,r):
    if q[3]<0: q=-q
    s=np.linalg.norm(q[:3])
    if s<1e-9: return np.array([0,0,0,1.0])
    ang=2*math.atan2(s,q[3])
    return qaxis(q[:3]/s,ang*r)
QI=np.array([0,0,0,1.0])
def qangle(a,b):
    d=abs(float(np.dot(a,b))); return math.degrees(2*math.acos(min(1.0,d)))

# ---------- bezier ----------
def bez(x1,y1,x2,y2,x,iters=40):
    if x1==y1 and x2==y2: return x
    lo,hi=0.0,1.0
    for _ in range(iters):
        t=(lo+hi)/2
        fx=3*(1-t)**2*t*x1+3*(1-t)*t*t*x2+t**3
        if fx<x: lo=t
        else: hi=t
    t=(lo+hi)/2
    return 3*(1-t)**2*t*y1+3*(1-t)*t*t*y2+t**3

# ---------- VMD ----------
def nm(raw): return raw.split(b'\0',1)[0].decode('cp932','replace')
def load_vmd(path):
    d=open(path,'rb').read(); nl=20 if d.startswith(b'Vocaloid Motion Data 0002') else 10
    o=30+nl; n,=struct.unpack_from('<I',d,o); o+=4
    tr=collections.defaultdict(dict)
    for i in range(n):
        rec=d[o:o+111]; o+=111
        fr,px,py,pz,qx,qy,qz,qw=struct.unpack_from('<I3f4f',rec,15); ip=rec[47:]
        cur=[tuple(ip[b+k]/127.0 for k in (0,4,8,12)) for b in (0,16,32,48)]
        tr[nm(rec[:15])][fr]=(fr,np.array([px,py,pz]),qnorm(np.array([qx,qy,qz,qw])),cur)
    return {k:[v[f] for f in sorted(v)] for k,v in tr.items()}
def sample(keys,f,mode='mmd'):
    if not keys: return np.zeros(3),QI
    if f<=keys[0][0]: return keys[0][1],keys[0][2]
    if f>=keys[-1][0]: return keys[-1][1],keys[-1][2]
    lo,hi=0,len(keys)-1
    while hi-lo>1:
        m=(lo+hi)//2
        if keys[m][0]<=f: lo=m
        else: hi=m
    a,b=keys[lo],keys[hi]
    s=(f-a[0])/float(b[0]-a[0])
    cur=b[3] if mode=='mmd' else a[3]
    p=np.array([a[1][i]+(b[1][i]-a[1][i])*bez(cur[i][0],cur[i][1],cur[i][2],cur[i][3],s) for i in range(3)])
    y=bez(cur[3][0],cur[3][1],cur[3][2],cur[3][3],s)
    q=qslerp(a[2],b[2],y) if mode=='mmd' else qnlerp(a[2],b[2],y)
    return p,q

# ---------- PMX bones ----------
def load_pmx(path):
    d=open(path,'rb').read(); o=[4]
    def rd(fmt):
        v=struct.unpack_from(fmt,d,o[0]); o[0]+=struct.calcsize(fmt); return v
    rd('<f'); n,=rd('<B'); g=list(d[o[0]:o[0]+n]); o[0]+=n
    enc,auv,vis,tis,mis,bis,mois,ris=g[:8]
    def txt():
        l,=rd('<i'); s=d[o[0]:o[0]+l]; o[0]+=l; return s.decode('utf-16le' if enc==0 else 'utf-8')
    def idx(sz): return rd('<'+{1:'b',2:'h',4:'i'}[sz])[0]
    for _ in range(4): txt()
    nv,=rd('<i')
    for _ in range(nv):
        o[0]+=32+16*auv; w,=rd('<B'); o[0]+={0:bis,1:2*bis+4,2:4*bis+16,3:2*bis+40,4:4*bis+16}[w]+4
    nf,=rd('<i'); o[0]+=nf*vis
    nt,=rd('<i')
    for _ in range(nt): txt()
    nmat,=rd('<i')
    for _ in range(nmat):
        txt();txt(); o[0]+=16+12+4+12+1+16+4+tis*2+1; sh,=rd('<B'); o[0]+=1 if sh==1 else tis; txt(); o[0]+=4
    nb,=rd('<i'); bones=[]
    for i in range(nb):
        b=dict(i=i,name=txt()); txt(); b['pos']=np.array(rd('<3f')); b['par']=idx(bis); b['lvl'],=rd('<i'); fl,=rd('<H'); b['fl']=fl
        if fl&1: idx(bis)
        else: rd('<3f')
        if fl&0x300: b['app']=(idx(bis),rd('<f')[0])
        if fl&0x400: rd('<3f')
        if fl&0x800: rd('<6f')
        if fl&0x2000: rd('<i')
        if fl&0x20:
            tgt=idx(bis); loop,=rd('<i'); lim,=rd('<f'); ln,=rd('<i'); links=[]
            for _ in range(ln):
                li=idx(bis); hl,=rd('<B'); lo=hi=None
                if hl: lo=np.array(rd('<3f')); hi=np.array(rd('<3f'))
                links.append((li,hl,lo,hi))
            b['ik']=(tgt,loop,lim,links)
        bones.append(b)
    return bones

class Model(object):
    def __init__(self,bones):
        self.b=bones; n=len(bones); self.n=n
        self.idx={}
        for b in bones: self.idx.setdefault(b['name'],b['i'])
        self.off=[b['pos']-(bones[b['par']]['pos'] if 0<=b['par']<n else 0) for b in bones]
        self.children=[[] for _ in range(n)]
        for b in bones:
            if 0<=b['par']<n: self.children[b['par']].append(b['i'])
        # parent-first order
        self.topo=[]
        def walk(i):
            self.topo.append(i)
            for c in self.children[i]: walk(c)
        for b in bones:
            if not (0<=b['par']<n): walk(b['i'])
        self.sub=[None]*n
        def subt(i):
            out=[i]
            for c in self.children[i]: out+=subt(c)
            self.sub[i]=out; return out
        for b in bones:
            if not (0<=b['par']<n): subt(b['i'])
        self.order=sorted(range(n),key=lambda i:(1 if bones[i]['fl']&0x1000 else 0,bones[i]['lvl'],i))
    def evaluate(self,tracks,f,ik='saba',ikoff=(),mode='mmd',stats=None):
        n=self.n; b=self.b
        self.at=[None]*n; self.ar=[None]*n; self.ikr=[QI]*n; self.apr=[QI]*n; self.apt=[np.zeros(3)]*n
        self.wr=[QI]*n; self.wp=[np.zeros(3)]*n
        for i in range(n):
            p,q=sample(tracks.get(b[i]['name']),f,mode)
            self.at[i]=p; self.ar[i]=q
        self.upd(self.topo)
        for i in self.order:
            bi=b[i]
            if 'app' in bi and 0<=bi['app'][0]<n:
                src,ratio=bi['app']
                if bi['fl']&0x100:
                    full=qmul(qmul(self.ikr[src],self.ar[src]),self.apr[src])   # user * IK link * received grant
                    self.apr[i]=qpow(full,ratio)
                if bi['fl']&0x200:
                    self.apt[i]=(self.at[src]+self.apt[src])*ratio
                self.upd(self.sub[i])
            if 'ik' in bi and bi['name'] not in ikoff:
                if ik=='saba': self.ik_saba(i,stats)
                elif ik=='libmmd': self.ik_libmmd(i,stats)
                elif ik=='analytic': self.ik_analytic(i,stats)
        self.upd(self.topo)
    def local_rot(self,i): return qmul(qmul(self.ikr[i],self.ar[i]),self.apr[i])
    def upd(self,lst):
        b=self.b
        for i in lst:
            p=b[i]['par']; lr=self.local_rot(i); lt=self.off[i]+self.at[i]+self.apt[i]
            if 0<=p<self.n:
                self.wr[i]=qmul(self.wr[p],lr); self.wp[i]=self.wp[p]+qrot(self.wr[p],lt)
            else:
                self.wr[i]=lr; self.wp[i]=lt
    # ---- saba style ----
    def ik_saba(self,i,stats):
        tgt,loop,lim,links=self.b[i]['ik']
        plane=[0.0]*len(links); prev=[np.zeros(3)]*len(links)
        for (li,hl,lo,hi) in links: self.ikr[li]=QI
        for (li,hl,lo,hi) in links[::-1]: self.upd(self.sub[li])
        best=float('inf'); save=None; it_used=0
        for it in range(loop):
            it_used=it+1
            ikpos=self.wp[i]
            for ci,(li,hl,lo,hi) in enumerate(links):
                if li==tgt: continue
                tpos=self.wp[tgt]
                inv=qconj(self.wr[li])
                cik=qrot(inv,ikpos-self.wp[li]); ctg=qrot(inv,tpos-self.wp[li])
                nik=np.linalg.norm(cik); ntg=np.linalg.norm(ctg)
                if nik<1e-12 or ntg<1e-12: continue
                cik/=nik; ctg/=ntg
                dot=max(-1.0,min(1.0,float(np.dot(ctg,cik)))); ang=math.acos(dot)
                ax=None
                if hl:
                    def only(a):
                        o=[k for k in range(3) if k!=a]
                        return (lo[a]!=0 or hi[a]!=0) and all(lo[k]==0 or hi[k]==0 for k in o)
                    for a in range(3):
                        if only(a): ax=a; break
                if ax is not None:
                    ang=min(ang,lim); axis=np.eye(3)[ax]
                    d1=float(np.dot(qrot(qaxis(axis,ang),ctg),cik)); d2=float(np.dot(qrot(qaxis(axis,-ang),ctg),cik))
                    new=plane[ci]+(ang if d1>d2 else -ang)
                    if it==0:
                        if new<lo[ax] or new>hi[ax]:
                            if -new>lo[ax] and -new<hi[ax]: new=-new
                            else:
                                half=(lo[ax]+hi[ax])*0.5
                                if abs(half-new)>abs(half+new): new=-new
                    new=max(lo[ax],min(hi[ax],new)); dlt=new-plane[ci]; plane[ci]=new
                    self.ikr[li]=qmul(self.ikr[li],qaxis(axis,dlt))
                else:
                    if math.degrees(ang)<1e-3: continue
                    ang=min(ang,lim)
                    cr=np.cross(ctg,cik); cn=np.linalg.norm(cr)
                    if cn<1e-12: continue
                    rot=qaxis(cr/cn,ang)
                    chain=qmul(qmul(self.ikr[li],self.ar[li]),rot)
                    # (general Euler limit branch omitted: no such link in the leg chains used here)
                    self.ikr[li]=qmul(chain,qconj(self.ar[li]))
                self.upd(self.sub[li])
            dist=np.linalg.norm(self.wp[tgt]-self.wp[i])
            if dist<best:
                best=dist; save=[self.ikr[l[0]] for l in links]
            else:
                for l,s in zip(links,save): self.ikr[l[0]]=s
                for l in links[::-1]: self.upd(self.sub[l[0]])
                break
        if stats is not None: stats.setdefault(self.b[i]['name'],[]).append((it_used,best))
    # ---- libmmd (mmd2timeline Poser.cs) style ----
    def ik_libmmd(self,i,stats):
        EPS=1e-7
        tgt,loop,lim,links=self.b[i]['ik']; loop=min(loop,256)
        fix=[]; order=[]; pre=[]
        for (li,hl,lo,hi) in links:
            self.ikr[li]=QI; ft=None; od='YZX'
            if hl:
                mn=np.minimum(lo,hi); mx=np.maximum(lo,hi)
                if mn[0]>-math.pi/2 and mx[0]<math.pi/2: od='ZXY'
                elif mn[1]>-math.pi/2 and mx[1]<math.pi/2: od='XYZ'
                z=[abs(mn[k])<EPS and abs(mx[k])<EPS for k in range(3)]
                if all(z): ft='ALL'
                elif z[1] and z[2]: ft=0
                elif z[0] and z[2]: ft=1
                elif z[0] and z[1]: ft=2
            fix.append(ft); order.append(od)
        for l in links[::-1]: self.upd(self.sub[l[0]])
        ikpos=self.wp[i]
        if np.dot(ikpos-self.wp[tgt],ikpos-self.wp[tgt])<EPS:
            if stats is not None: stats.setdefault(self.b[i]['name'],[]).append((0,0.0))
            return
        ikt=loop//2; used=0
        for it in range(loop):
            used=it+1
            for j,(li,hl,lo,hi) in enumerate(links):
                if fix[j]=='ALL': continue
                tpos=self.wp[tgt]; lp=self.wp[li]
                td=lp-tpos; idr=lp-ikpos
                td=td/max(np.linalg.norm(td),1e-20); idr=idr/max(np.linalg.norm(idr),1e-20)
                axis=np.cross(td,idr)
                axis=np.where(np.abs(axis)<EPS,EPS,axis)
                p=self.b[li]['par']; pr=self.wr[p] if 0<=p<self.n else QI
                if hl and fix[j] is not None and it<ikt:
                    e=np.eye(3)[fix[j]]
                    sgn=1.0 if np.dot(axis,qrot(pr,e))>0 else -1.0
                    axis=e*sgn
                else:
                    axis=qrot(qconj(pr),axis); axis=axis/np.linalg.norm(axis)
                ang=min(math.acos(max(-1.0,min(1.0,float(np.dot(td,idr))))),lim*(j+1))
                self.ikr[li]=qmul(qaxis(axis,ang),self.ikr[li])
                if hl:
                    mn=np.minimum(lo,hi); mx=np.maximum(lo,hi)
                    loc=qmul(self.ikr[li],self.ar[li])
                    x,y,z,w=loc
                    if order[j]=='XYZ':
                        e=np.array([math.atan2(2*(w*x-y*z),1-2*(x*x+y*y)), math.asin(max(-1,min(1,2*(w*y+x*z)))), math.atan2(2*(w*z-x*y),1-2*(y*y+z*z))])
                    else:
                        raise NotImplementedError(order[j])
                    for k in range(3):
                        if e[k]<mn[k]:
                            tf=2*mn[k]-e[k]; e[k]=tf if (tf<=mx[k] and it<ikt) else mn[k]
                        if e[k]>mx[k]:
                            tf=2*mx[k]-e[k]; e[k]=tf if (tf>=mn[k] and it<ikt) else mx[k]
                    loc=qmul(qmul(qaxis([1,0,0],e[0]),qaxis([0,1,0],e[1])),qaxis([0,0,1],e[2]))
                    self.ikr[li]=qmul(loc,qconj(self.ar[li]))
                self.upd(self.sub[links[j][0]])
                for k in range(j):  # parents of link j were not changed; subtree update above covers children
                    pass
            err=ikpos-self.wp[tgt]
            if np.dot(err,err)<EPS: break
        if stats is not None: stats.setdefault(self.b[i]['name'],[]).append((used,float(np.linalg.norm(ikpos-self.wp[tgt]))))
    # ---- analytic two-bone (knee hinge about local X) / one-bone aim ----
    def ik_analytic(self,i,stats):
        tgt,loop,lim,links=self.b[i]['ik']
        for (li,hl,lo,hi) in links: self.ikr[li]=QI
        for l in links[::-1]: self.upd(self.sub[l[0]])
        T=self.wp[i]
        if len(links)==2 and links[0][1]:
            knee,_,lo,hi=links[0]; hip=links[1][0]
            # effector offset from knee in knee-local frame at keyed knee rotation; hinge about local X in parent(thigh) frame
            k=self.off[knee]                       # hip->knee in thigh frame (thigh local = model axes at rest)
            # chain knee->effector, expressed in knee frame, with FK rotations of intermediates
            s=qrot(qconj(self.wr[knee]),self.wp[tgt]-self.wp[knee])
            s=qrot(self.ar[knee],s)                # in thigh frame before the hinge rotation (keyed knee rot applied)
            d=np.linalg.norm(T-self.wp[hip])
            A=k[0]*s[0]; B=k[1]*s[1]+k[2]*s[2]; C=k[2]*s[1]-k[1]*s[2]
            D=(d*d-np.dot(k,k)-np.dot(s,s))/2-A
            R=math.hypot(B,C); c=max(-1.0,min(1.0,D/R)); base=math.atan2(C,B)
            cands=[base+math.acos(c),base-math.acos(c)]
            cands=[((a+math.pi)%(2*math.pi))-math.pi for a in cands]
            ok=[a for a in cands if lo[0]-1e-9<=a<=hi[0]+1e-9]
            th=ok[0] if ok else max(lo[0],min(hi[0],min(cands,key=lambda a:min(abs(a-lo[0]),abs(a-hi[0])))))
            self.ikr[knee]=qaxis([1,0,0],th)       # ikr*ar : hinge applied in thigh frame, on top of keyed knee rot
            self.upd(self.sub[knee])
            base_link=hip
        else:
            base_link=links[-1][0]
        # minimal swing of the base link so that the effector direction hits the target direction
        p=self.b[base_link]['par']; pr=self.wr[p] if 0<=p<self.n else QI
        a=qrot(qconj(pr),self.wp[tgt]-self.wp[base_link]); t=qrot(qconj(pr),T-self.wp[base_link])
        na=np.linalg.norm(a); nt=np.linalg.norm(t)
        if na>1e-9 and nt>1e-9:
            a/=na; t/=nt
            ax=np.cross(a,t); sn=np.linalg.norm(ax); cs=float(np.dot(a,t))
            if sn>1e-9:
                self.ikr[base_link]=qmul(qaxis(ax/sn,math.atan2(sn,cs)),self.ikr[base_link])
                self.upd(self.sub[base_link])
        if stats is not None: stats.setdefault(self.b[i]['name'],[]).append((1,float(np.linalg.norm(T-self.wp[tgt]))))
