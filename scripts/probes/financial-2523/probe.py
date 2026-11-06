from fractions import Fraction as F
from decimal import Decimal, localcontext
import re, struct, sys

def bits(x): return struct.pack('>d',float(x)).hex().upper()
def dec(x):
    if isinstance(x,Decimal): return x
    return Decimal(x.numerator)/Decimal(x.denominator)
def rnd(x,p):
    if not x: return x
    if p==53: return F(float(x))
    x=F(x); sign=-1 if x<0 else 1; x=abs(x)
    e=x.numerator.bit_length()-x.denominator.bit_length()
    if x < (F(2)**e): e-=1
    unit=F(2)**(e-p+1); z=x/unit; q,r=divmod(z.numerator,z.denominator)
    if 2*r>z.denominator or (2*r==z.denominator and q%2): q+=1
    return sign*q*unit
class Model:
    def __init__(self,mask=0,p=0): self.mask=mask; self.p=p; self.i=0; self.names=[]
    def node(self,name,x):
        i=self.i; self.i+=1; self.names.append(name)
        return rnd(x,53 if self.mask>>i&1 else self.p) if (self.mask>>i&1 or self.p) else x
    def power(self,b,n):
        if n.denominator==1: return self.node('power',b**int(n))
        return self.node('power',F(dec(b)**dec(n)))
    def log(self,x): return self.node('log',F(dec(x).ln()))
    def add(self,a,b,name='add'): return self.node(name,a+b)
    def sub(self,a,b): return self.node('sub',a-b)
    def mul(self,a,b): return self.node('mul',a*b)
    def div(self,a,b): return self.node('div',a/b)
    def pmt(self,r,n,pv,fv=F(0),t=F(0)):
        if not n: raise ValueError(5)
        if not r: return -self.div(self.add(pv,fv),n)
        b=self.add(F(1),r,'base'); q=self.power(b,n)
        return -self.div(self.mul(r,self.add(self.mul(pv,q),fv)),self.mul(self.sub(q,F(1)), b if t else F(1)))
    def fv(self,r,n,pmt,pv=F(0),t=F(0)):
        if not r: return -self.add(pv,self.mul(pmt,n))
        b=self.add(F(1),r,'base'); q=self.power(b,n)
        return -self.add(self.mul(pv,q),self.div(self.mul(self.mul(pmt,b if t else F(1)),self.sub(q,F(1))),r))
    def pv(self,r,n,pmt,fv=F(0),t=F(0)):
        if not r: return -self.add(self.mul(pmt,n),fv)
        b=self.add(F(1),r,'base'); q=self.power(b,n)
        return -self.div(self.add(fv,self.div(self.mul(self.mul(pmt,b if t else F(1)),self.sub(q,F(1))),r)),q)
    def nper(self,r,pmt,pv,fv=F(0),t=F(0)):
        if not r:
            if not pmt: raise ValueError(5)
            return -self.div(self.add(pv,fv),pmt)
        b=self.add(F(1),r,'base'); a=self.mul(pmt,b if t else F(1))
        ratio=self.div(self.sub(a,self.mul(fv,r)),self.add(a,self.mul(pv,r)))
        return self.div(F(self.log(ratio)),F(self.log(b)))
    def ipmt(self,r,per,n,pv,fv=F(0),t=F(0)):
        if per<1 or per>n: raise ValueError(5)
        if per==1 and t: return F(0)
        payment=self.pmt(r,n,pv,fv,t)
        balance=self.fv(r,per-F(1),payment,pv,t)
        interest=self.mul(balance,r)
        return self.div(interest,self.add(F(1),r,'base')) if t else interest
    def ppmt(self,r,per,n,pv,fv=F(0),t=F(0)):
        return self.sub(self.pmt(r,n,pv,fv,t),self.ipmt(r,per,n,pv,fv,t))

def parse():
    rows=[]
    for line in open(sys.argv[1], encoding='utf-8'):
        m=re.match(r'\| `([A-Za-z]+)\((.*?)\)` \|.*?\| ([0-9]+) \|\s*(?:`([0-9A-F]{16})`)?\s*\|',line)
        if not m: continue
        name,args,err,target=m.groups(); vals=[]
        for arg in args.split(','):
            arg=arg.strip().strip('"')
            vals.append(None if arg=='Null' else F(float(arg.split('/')[0]) / float(arg.split('/')[1])) if '/' in arg else F(float(arg)))
        rows.append((name.lower(),vals,int(err),target,m.group(0).split(' |')[0][3:]))
    return rows

def evaluate(name,args,mask,p):
    m=Model(mask,p)
    if None in args: return 'ERR94',m
    try:
        v=getattr(m,name)(*args)
        # Fraction has no signed zero; the measured IPmt zero-rate path is negated zero.
        return ('8000000000000000' if name=='ipmt' and not args[0] else bits(v)),m
    except (ValueError,ZeroDivisionError,OverflowError): return 'ERR5',m

if __name__=='__main__':
 with localcontext() as ctx:
    ctx.prec=100
    rows=parse()
    for name in ['pmt','fv','pv','nper','ipmt','ppmt']:
        group=[r for r in rows if r[0]==name]
        print(name,len(group),flush=True)
        # Each site independently spills or retains its chosen arithmetic precision.
        numeric=[r for r in group if r[3]]
        maxnodes=max(evaluate(name,r[1],0,0)[1].i for r in numeric)
        # Shared site masks across paths; IPmt/PPmt use independent call-site nodes.
        if maxnodes>18: print('large graph',maxnodes,flush=True); continue
        for p in [0,64]:
            candidates=[]; best=-1; near=[]
            for mask in range(1<<maxnodes):
                score=0; outputs=[]
                for _,args,err,target,_ in group:
                    out,m=evaluate(name,args,mask,p)
                    outputs.append(out)
                    if out==(target or 'ERR'+str(err)): score+=1
                if score>best: best=score; near=[(mask,outputs)]
                elif score==best and len(near)<3: near.append((mask,outputs))
                if score==len(group): candidates.append(mask)
            print('precision',p or 'exact','nodes',maxnodes,'best',best,'matching masks',candidates[:20], 'count',len(candidates),flush=True)
            for mask,outputs in near[:1]:
                print('mask',hex(mask),flush=True)
                for r,out in zip(group,outputs): print(r[4],r[3] or 'ERR'+str(r[2]),out,'OK' if out==(r[3] or 'ERR'+str(r[2])) else 'MISS',flush=True)
