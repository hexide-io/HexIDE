"""Probe mechanics: synthetic geometry and the supplied measurement corpus."""
from dataclasses import replace
from pathlib import Path
import tempfile
import unittest

import compare


class ComparisonTests(unittest.TestCase):
    def test_reads_five_oracle_and_seventeen_issue_grids(self):
        original = compare.read_cases(compare.ROOT / 'docs/vb6-fidelity-oracle/circles-arcs-sectors-and-thick-pens-pixel-by-pixel.md')
        additional = compare.read_cases(compare.HERE / 'issue-4771-grids.md')
        self.assertEqual(len(original), 5)
        self.assertEqual(len(additional), 17)
        self.assertEqual(len({c.name for c in original + additional}), 22)
        self.assertEqual(sum(c.filled for c in additional), 2)

    def test_parser_rejects_wrong_grid_width(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'bad.md'
            path.write_text('**arc bad**: `Circle (40, 40), 10, , 0, 1`.\n```text\ny030 .#\n```')
            with self.assertRaisesRegex(ValueError, 'Bad grid row'):
                compare.read_cases(path)

    def test_difference_counts_extra_missing_and_changed_colour_once(self):
        self.assertEqual(compare.difference({(1, 1): 'R', (2, 2): '#'},
                                            {(1, 1): 'G', (3, 3): '#'}), 3)
        self.assertEqual(compare.difference({(1, 1): 'R'}, {(1, 1): 'R'}), 0)

    def test_integer_line_excludes_terminal_and_obeys_directional_bias(self):
        self.assertEqual(list(compare.line((0, 0), (4, 2))), [(0, 0), (1, 0), (2, 1), (3, 1)])
        self.assertEqual(list(compare.line((4, 2), (0, 0))), [(4, 2), (3, 1), (2, 1), (1, 0)])
        self.assertEqual(list(compare.line((0, 0), (0, 0))), [])

    def test_fill_is_inside_polygon(self):
        pixels = compare.fill_polygon([(20, 20), (24, 20), (24, 24), (20, 24)])
        self.assertIn((22, 22), pixels)
        self.assertNotIn((20, 22), pixels)
        self.assertNotIn((24, 22), pixels)
        self.assertNotIn((22, 24), pixels)

    def test_candidates_ignore_expected_pixels_and_arcs_never_fill(self):
        case = compare.Case('synthetic', 8, 0.2, 1.4, 1, False, {})
        for name, options in compare.candidates():
            with self.subTest(candidate=name):
                actual = compare.draw(case, **options)
                self.assertTrue(actual)
                self.assertEqual(actual, compare.draw(replace(case, expected={(40, 40): 'G'}), **options))
                filled = compare.draw(replace(case, filled=True), **options)
                self.assertEqual(set(actual), set(filled))
                self.assertNotIn('G', filled.values())


if __name__ == '__main__':
    unittest.main()
