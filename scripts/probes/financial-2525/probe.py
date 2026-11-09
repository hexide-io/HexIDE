"""Candidate arithmetic models against #2525; no compiler inspection.
Run: python3 -B scripts/probes/financial-2525/probe.py
"""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'financial-2523'))
from probe import F, rnd, bits, dec, localcontext

A = list(map(F, [-10000, 3000, 4200, 6800, 1000]))
B = list(map(F, [-70000, 12000, 15000, 18000, 21000, 26000]))
CS = list(map(F, [100, 200, 300]))

class Cash:
    sites = ['base', 'power', 'term', 'sum', 'residual', 'iterate']
    def __init__(self, mask, precision=64, form='power'):
        self.mask, self.precision, self.form = mask, precision, form
    def spill(self, site, x):
        return rnd(x, 53 if self.mask >> self.sites.index(site) & 1 else self.precision)
    def npv(self, r, flows):
        base = self.spill('base', 1+r)
        total, power = F(0), F(1)
        if self.form == 'horner':
            for value in reversed(flows):
                total = self.spill('term', self.spill('sum', total+value)/base)
            return total
        for i, value in enumerate(flows):
            power = self.spill('power', base**(i+1) if self.form == 'power' else power*base)
            total = self.spill('sum', total+self.spill('term', value/power))
        return total
    def irr(self, flows, guess=F(.1)):
        x, y = guess, guess/2
        for _ in range(40):
            a = self.spill('residual', self.npv(x, flows))
            b = self.spill('residual', self.npv(y, flows))
            if abs(b) < F(1e-7): return y
            if a == b: raise ValueError('no convergence')
            x, y = y, self.spill('iterate', y-b*(y-x)/(b-a))
        raise ValueError('no convergence')

if __name__ == '__main__':
    for name, rows in [
        ('npv', [(F(.1), A, '409C45757D31FA2E'), (F(0), A, '40B3880000000000'), (F(.1), CS, '407E197C0E9E19C5')]),
        ('irr', [(F(.1), A, '3FC875EFF170437B'), (F(.2), A, '3FC875EFF1704153'), (F(.1), B, '3FB62D72209FBD49')])]:
        for precision in [64, 113]:
            for form in ['power', 'multiply', 'horner']:
                best, nearest, matches = -1, None, []
                for mask in range(64):
                    model = Cash(mask, precision, form)
                    try:
                        outputs = [bits(model.npv(r, f) if name == 'npv' else model.irr(f, r)) for r, f, _ in rows]
                    except (ValueError, ZeroDivisionError, OverflowError): continue
                    score = sum(out == row[2] for out, row in zip(outputs, rows))
                    distance = sum(abs(int(out,16)-int(row[2],16)) for out,row in zip(outputs,rows))
                    if score > best or score == best and distance < nearest[0]: best, nearest = score, (distance, mask, outputs)
                    if score == len(rows): matches.append(mask)
                print(name, precision, form, 'matches', matches, 'best', best, 'nearest', nearest, flush=True)
    # MIRR: present negative flows and terminal positive flows; vary every
    # grouped spill in the compounded-NPV ratio.
    with localcontext() as ctx:
        ctx.prec = 100
        targets = ['3FB94260ED237997', '3FC477233C8903B7']
        for precision in [64, 113]:
            best, nearest, matches = -1, None, []
            for form in ['power', 'multiply', 'horner']:
                for mask in range(256):
                    outputs = []
                    for flows in [B, A]:
                        m = Cash(mask & 15, precision, form)
                        n = len(flows)
                        neg = m.npv(F(.1), [min(v,0) for v in flows])
                        pos = m.npv(F(.12), [max(v,0) for v in flows])
                        spill = lambda bit, x: rnd(x, 53 if mask >> bit & 1 else precision)
                        terminal = spill(4, F(dec(1+F(.12)) ** n))
                        denominator = spill(5, -neg*(1+F(.1)))
                        ratio = spill(6, pos*terminal/denominator)
                        root = spill(7, F(dec(ratio) ** (1/dec(F(n-1)))))
                        outputs.append(bits(root-1))
                    score = sum(x==y for x,y in zip(outputs,targets))
                    distance = sum(abs(int(x,16)-int(y,16)) for x,y in zip(outputs,targets))
                    if score>best or score==best and distance<nearest[0]: best,nearest=score,(distance,form,mask,outputs)
                    if score==2: matches.append((form,mask))
            print('mirr',precision,'matches',matches,'best',best,'nearest',nearest,flush=True)
