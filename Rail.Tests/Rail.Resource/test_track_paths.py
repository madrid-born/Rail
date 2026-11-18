import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('paths',
    Path(__file__).resolve().parents[2] / 'Rail.Resource' / 'Scripts' / 'build-track-paths.py')
paths = importlib.util.module_from_spec(spec)
spec.loader.exec_module(paths)


def feature(coords, line=1):
    return {'properties': {'kind_detail': 'subway', 'name:en': f'Line {line}'},
            'geometry': {'type': 'LineString', 'coordinates': coords}}


class TrackPathsTests(unittest.TestCase):
    def test_shared_interchange_snap_allowance_is_specific_to_its_line(self):
        self.assertEqual(paths.snap_limit(4, 'eram-e-sabz'), 400)
        self.assertEqual(paths.snap_limit(5, 'eram-e-sabz'), 300)
        graph = paths.Graph([feature([[51.4,35.7],[51.42,35.7]],line=4)], 4)
        start, end = [51.4,35.7031], [51.42,35.7]
        self.assertIsNone(graph.path(start,end)[0])
        result, reason = graph.path(start,end,start_radius=400)
        self.assertIsNone(reason)
        self.assertGreater(result[1]['startSnapMeters'], 300)

    def test_overlapping_tile_fragments_join_and_keep_the_curve(self):
        graph = paths.Graph([
            feature([[51.4,35.7],[51.405,35.702],[51.41,35.7]]),
            feature([[51.408,35.7008],[51.41,35.7],[51.415,35.703]])], 1)
        result, reason = graph.path([51.4,35.7], [51.415,35.703])
        self.assertIsNone(reason)
        self.assertGreaterEqual(len(result[0]), 4)

    def test_parallel_tracks_produce_one_path(self):
        graph = paths.Graph([
            feature([[51.4,35.7],[51.41,35.7]]),
            feature([[51.4,35.70005],[51.41,35.70005]])], 1)
        result, _ = graph.path([51.401,35.7],[51.409,35.7])
        self.assertTrue(all(abs(p[1]-35.7) < 0.000001 for p in result[0]))

    def test_different_lines_and_disconnected_gaps_are_not_joined(self):
        graph = paths.Graph([
            feature([[51.4,35.7],[51.402,35.7]]),
            feature([[51.408,35.7],[51.41,35.7]]),
            feature([[51.402,35.7],[51.408,35.7]], line=2)], 1)
        result, reason = graph.path([51.4,35.7],[51.41,35.7])
        self.assertIsNone(result)
        self.assertIsNotNone(reason)


if __name__ == '__main__':
    unittest.main()
