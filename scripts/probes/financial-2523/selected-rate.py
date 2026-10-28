# Reproduce the stopping rule requested by the final #2523 supervisor comment.
from rate import RateModel
from probe import *

with localcontext() as context:
    context.prec = 100
    for precision in [64, 113]:
        for row in parse():
            if row[0] != 'rate':
                continue
            args = row[1]
            n, payment, pv = args[:3]
            fv = args[3] if len(args) > 3 else F(0)
            timing = args[4] if len(args) > 4 else F(0)
            x = args[5] if len(args) > 5 else F(.1)
            y = x / 2
            model = RateModel(193, precision)
            result = 'ERR5'
            iterations = 0
            if n:
                for iterations in range(40):
                    a = model.residual(x, n, payment, pv, fv, timing)
                    b = model.residual(y, n, payment, pv, fv, timing)
                    if abs(b) < F(1e-7):
                        result = bits(y)
                        break
                    if a == b:
                        break
                    z = model.node('iterate', model.sub(y, model.div(
                        model.mul(b, model.sub(y, x)), model.sub(b, a))))
                    x, y = y, z
            expected = row[3] or 'ERR' + str(row[2])
            print(precision, row[4], result, 'iterations', iterations,
                  'OK' if result == expected else 'MISS')
            assert result == expected
