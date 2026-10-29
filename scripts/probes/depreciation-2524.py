"""Precision investigation for #2524; oracle targets come from that issue.

Integer powers use exact fractions; the fractional power uses Decimal at 100
(and 150) digits, both comfortably above the requested 64-bit significand.
Masks spill rate, base, power, book value and amount to Double respectively.
No VB6 or twinBASIC compiler is called.
"""
from decimal import Decimal, localcontext
from fractions import Fraction as F
from struct import pack


def bits(value):
    return pack('>d', float(value)).hex().upper()


rows = [
    (3650, 1, 2, 300, '3FF50A8542A150A8'),
    (120, 1, 2, 300, '4044000000000000'),
    (10, 1, 2, 300, '407E000000000000'),
    (10, 2, 1.5, 300, '4073200000000000'),
    (10, 10, 2, 300, '40361F5F40D83797'),
    (10, 2.5, 2, 300, '4075775C544FF263'),
    (10, 1, 2, 3000, 'C082C00000000000'),
    (10, 11, 2, 300, 'Err 5'),
    (10, 0, 2, 300, 'Err 5'),
    (0, 1, 2, 300, 'Err 5'),
]


def ddb(row, mask, precision):
    life, period, factor, salvage, _ = row
    if life == 0 or period < 1 or period > life:
        return 'Err 5'
    with localcontext() as context:
        context.prec = precision
        fractional = period != int(period)
        number = Decimal if fractional else F
        spill = lambda value, bit: number(float(value)) if mask & bit else value
        rate = spill(number(factor) / life, 1)
        base = spill(1 - rate, 2)
        power = spill(base ** (number(period) - 1), 4)
        book = spill(2400 * power, 8)
        amount = spill(book * rate, 16)
        return bits(min(amount, book - salvage))


for precision in (100, 150):
    results = [[ddb(row, mask, precision) for row in rows] for mask in range(32)]
    best = max(sum(value == row[-1] for value, row in zip(result, rows)) for result in results)
    print(f'DDB {precision} decimal digits: best {best}/10 measured rows')
    print('  period 10:', ', '.join(sorted({result[4] for result in results})))
    print('  no-spill:', ', '.join(results[0]))
    print('  base-spill:', ', '.join(results[2]))

for life, expected in ((10, '40A1940000000000'), (0, 'Err 5'), (-2, 'C0C5F90000000000')):
    actual = 'Err 5' if life == 0 else bits(F(30000 - 7500, life))
    assert actual == expected, (life, actual, expected)
    print('SLN', life, actual)
for period, expected in ((1, '40AFF5D1745D1746'), (10, '407991745D1745D1'),
                         (11, 'Err 5'), (0, 'Err 5'), (2.5, '40AB2A8BA2E8BA2F')):
    actual = ('Err 5' if not 1 <= period <= 10
              else bits(F(22500) * (11 - F(period)) * 2 / 110))
    assert actual == expected, (period, actual, expected)
    print('SYD', period, actual)
