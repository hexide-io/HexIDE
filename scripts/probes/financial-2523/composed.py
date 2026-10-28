from probe import *
class Composed(Model):
    sites=['base','power','add','sub','mul','div','log','payment','balance','interest']
    def node(self,name,x):
        return rnd(x,53 if self.mask>>self.sites.index(name)&1 else self.p) if (self.mask>>self.sites.index(name)&1 or self.p) else x
    def pmt(self,*args): return self.node('payment',super().pmt(*args))
    def fv(self,*args): return self.node('balance',super().fv(*args))
    def ipmt(self,*args): return self.node('interest',super().ipmt(*args))
with localcontext() as ctx:
 ctx.prec=100
 for name in ['ipmt','ppmt']:
    rows=[r for r in parse() if r[0]==name]
    for p in [0,64]:
        best=-1; winners=[]; near=[]
        for mask in range(1<<len(Composed.sites)):
            outputs=[]
            for _,args,err,target,_ in rows:
                try:
                    v=getattr(Composed(mask,p),name)(*args)
                    out='8000000000000000' if name=='ipmt' and not args[0] else bits(v)
                except (ValueError,ZeroDivisionError): out='ERR5'
                outputs.append(out)
            score=sum(o==(r[3] or 'ERR'+str(r[2])) for r,o in zip(rows,outputs))
            if score>best: best=score; near=[(mask,outputs)]
            if score==len(rows): winners.append(mask)
        print(name,p,best,'winners',winners,flush=True)
        for mask,outputs in near:
            print('sites',[s for i,s in enumerate(Composed.sites) if mask>>i&1],flush=True)
            for r,o in zip(rows,outputs): print(r[4],r[3] or 'ERR'+str(r[2]),o,flush=True)

# Equivalent beginning-period recurrence; avoids dividing a rounded-base FV by 1+r.
with localcontext() as ctx:
 ctx.prec=100
 for row in parse():
  if row[0]!='ipmt': continue
  r,per,n,pv,*rest=row[1]; fv,t=(rest+[F(0),F(0)])[:2]
  try:
   if per<1 or per>n: raise ValueError(5)
   payment=F(float(Model(1,0).pmt(r,n,pv,fv,t)))
   if t:
    value=F(0) if per==1 else (Model(1,0).fv(r,per-2,payment,pv,t)-payment)*r
   else: value=Model(1,0).fv(r,per-1,payment,pv,t)*r
   result='8000000000000000' if not r and not t else bits(value)
  except ValueError: result='ERR5'
  print('recurrence',row[4],row[3] or 'ERR'+str(row[2]),result)
