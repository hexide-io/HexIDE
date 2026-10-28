from probe import *
# Secant candidates over the standard cash-flow equation. Only spill sites vary
# within each candidate; no expected result is used in the calculation.
class RateModel(Model):
 sites=['base','power','add','sub','mul','div','residual','iterate']
 def node(self,name,x):
    return rnd(x,53 if self.mask>>self.sites.index(name)&1 else self.p)
 def residual(self,r,n,pmt,pv,fv,t):
    if not r: return self.node('residual',self.add(self.add(pv,self.mul(pmt,n)),fv))
    b=self.add(F(1),r,'base'); q=self.power(b,n)
    return self.node('residual',self.add(self.add(self.mul(pv,q),self.div(self.mul(self.mul(pmt,b if t else F(1)),self.sub(q,F(1))),r)),fv))
 def trace(self,n,pmt,pv,fv=F(0),t=F(0),guess=F(.1),start='step'):
    if not n: return []
    x=guess
    y={'step':x+F(.00001),'half':x/F(2),'tenth':x*F(1.1),'double':x*2}[start]
    out=[]
    for _ in range(40):
        a=self.residual(x,n,pmt,pv,fv,t); b=self.residual(y,n,pmt,pv,fv,t)
        if a==b: break
        z=self.node('iterate',self.sub(y,self.div(self.mul(b,self.sub(y,x)),self.sub(b,a))))
        out.append(bits(z)); x,y=y,z
        if abs(y)>100: break
    return out
if __name__ == '__main__':
 with localcontext() as ctx:
  ctx.prec=100
  rows=[r for r in parse() if r[0]=='rate' and r[3]]
  for p in [64,113]:
   for start in ['step','half','tenth','double']:
     best=-1; near=[]; matches=[]
     for mask in range(256):
       traces=[]
       try:
        for _,args,err,target,_ in rows: traces.append(RateModel(mask,p).trace(*args,start=start))
       except (ValueError,ZeroDivisionError,OverflowError): continue
       score=sum(r[3] in trace for r,trace in zip(rows,traces))
       if score>best: best=score; near=[(mask,traces)]
       if score==len(rows): matches.append(mask)
     print('p',p,'start',start,'best',best,'matches',matches,flush=True)
     for mask,traces in near:
       print('spills',[s for i,s in enumerate(RateModel.sites) if mask>>i&1],flush=True)
       for r,trace in zip(rows,traces):
        target=int(r[3],16); nearest=min(trace,key=lambda b:abs(int(b,16)-target)) if trace else 'none'
        print(r[4],'target',r[3],'nearest',nearest,'iteration',trace.index(nearest)+1 if trace else 0,flush=True)

 # Error rows: inspect termination separately. A match anywhere in a trace is not
 # evidence for a stopping criterion, and is not enough to implement Rate.
 with localcontext() as ctx:
  ctx.prec=100
  for row in parse():
   if row[0]!='rate': continue
   trace=RateModel(193,64).trace(*row[1],start='half')
   print('termination',row[4],row[3] or 'ERR'+str(row[2]),len(trace),trace[-3:])
