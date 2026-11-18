"""Match database connections to one rail centerline. No synthetic gap filling.

Usage: python build-track-paths.py connections.json railways.geojson paths.json
Outputs paths.json, paths.report.json and paths.geojson for visual review.
"""
import heapq
import json
import math
import re
import sys
from pathlib import Path

SCALE_X = 111320 * math.cos(math.radians(35.75))
SCALE_Y = 111320

# Shared interchange coordinates can lie on another line's platform. Keep the
# default strict and permit only this measured Line 4 / Line 5 interchange offset.
def snap_limit(line, station_id):
    return 400 if (line, station_id) == (4, 'eram-e-sabz') else 300


def xy(p):
    return p[0] * SCALE_X, p[1] * SCALE_Y


def distance(a, b):
    return math.dist(xy(a), xy(b))


def line_number(properties):
    for key in ('name:en', 'name:fa', 'name'):
        match = re.search(r'(?:line|خط)\s*([\d]+)', properties.get(key, ''), re.I)
        if match:
            return int(match[1])
    return None


class Graph:
    def __init__(self, features, line):
        self.points, self.adj, self.edges = [], [], []
        grid, edges = {}, set()

        def node(p):
            x, y = xy(p)
            key = (math.floor(x / 2), math.floor(y / 2))
            for dx in (-1, 0, 1):
                for dy in (-1, 0, 1):
                    for i in grid.get((key[0] + dx, key[1] + dy), []):
                        if distance(p, self.points[i]) <= 1:
                            return i
            i = len(self.points)
            self.points.append(p)
            self.adj.append([])
            grid.setdefault(key, []).append(i)
            return i

        for f in features:
            properties = f['properties']
            kind = properties.get('kind_detail')
            if kind not in ('subway', 'rail', 'light_rail', 'narrow_gauge'):
                continue
            number = line_number(properties)
            # The app models the airport service under line 1; OSM labels it line 8.
            if number is not None and number != line and not (line == 1 and number == 8):
                continue
            # Line 5 runs on surface rail; underground lines must not use mainline railways.
            if line != 5 and kind == 'rail':
                continue
            geometry = f['geometry']
            parts = [geometry['coordinates']] if geometry['type'] == 'LineString' else geometry['coordinates']
            for part in parts:
                for a, b in zip(part, part[1:]):
                    u, v = node(a), node(b)
                    key = tuple(sorted((u, v)))
                    if u == v or key in edges:
                        continue
                    edges.add(key)
                    length = distance(self.points[u], self.points[v])
                    self.adj[u].append((v, length))
                    self.adj[v].append((u, length))
                    self.edges.append((u, v, length))

        # Buffered tiles clip the same line at different positions. Split at
        # coincident vertices so an overlapping fragment remains traversable.
        edge_grid = {}
        splits = {i: [(0, u), (1, v)] for i, (u, v, _) in enumerate(self.edges)}
        for i, (u, v, _) in enumerate(self.edges):
            ax, ay = xy(self.points[u])
            bx, by = xy(self.points[v])
            for gx in range(math.floor((min(ax,bx)-2)/100), math.floor((max(ax,bx)+2)/100)+1):
                for gy in range(math.floor((min(ay,by)-2)/100), math.floor((max(ay,by)+2)/100)+1):
                    edge_grid.setdefault((gx,gy), []).append(i)
        for node_id, p in enumerate(self.points):
            px, py = xy(p)
            for i in edge_grid.get((math.floor(px/100), math.floor(py/100)), []):
                u, v, _ = self.edges[i]
                if node_id in (u,v):
                    continue
                ax, ay = xy(self.points[u])
                bx, by = xy(self.points[v])
                t = ((px-ax)*(bx-ax)+(py-ay)*(by-ay))/((bx-ax)**2+(by-ay)**2)
                if 0 < t < 1 and math.hypot(px-(ax+t*(bx-ax)), py-(ay+t*(by-ay))) <= 1:
                    splits[i].append((t,node_id))
        self.adj = [[] for _ in self.points]
        self.edges = []
        seen = set()
        for split in splits.values():
            split.sort()
            for (_,u), (_,v) in zip(split, split[1:]):
                key = tuple(sorted((u,v)))
                if u == v or key in seen:
                    continue
                seen.add(key)
                length = distance(self.points[u],self.points[v])
                self.edges.append((u,v,length))
                self.adj[u].append((v,length))
                self.adj[v].append((u,length))

    def candidates(self, point, limit=12, radius=300):
        px, py = xy(point)
        found = []
        for i, (u, v, length) in enumerate(self.edges):
            ax, ay = xy(self.points[u])
            bx, by = xy(self.points[v])
            fraction = max(0, min(1, ((px-ax)*(bx-ax)+(py-ay)*(by-ay)) / ((bx-ax)**2+(by-ay)**2)))
            snapped = [(ax + fraction*(bx-ax))/SCALE_X, (ay + fraction*(by-ay))/SCALE_Y]
            gap = distance(point, snapped)
            if gap <= radius:
                found.append((gap, i, fraction, snapped))
        return sorted(found)[:limit]

    def path(self, start, end, start_radius=300, end_radius=300, start_limit=12, end_limit=12):
        starts = self.candidates(start, limit=start_limit, radius=start_radius)
        ends = self.candidates(end, limit=end_limit, radius=end_radius)
        if not starts or not ends:
            return None, f'station farther than allowed snap distance ({start_radius}m / {end_radius}m) from eligible track'
        best = None
        max_length = max(distance(start, end) * 2, distance(start, end) + 500)
        # Solve separately for each candidate so the output always belongs to one path.
        for sgap, edge, fraction, snapped in starts:
            u, v, length = self.edges[edge]
            dist = {u: fraction*length, v: (1-fraction)*length}
            previous = {u: None, v: None}
            queue = [(dist[u], u), (dist[v], v)]
            heapq.heapify(queue)
            while queue:
                cost, node = heapq.heappop(queue)
                if cost != dist[node] or cost > max_length:
                    continue
                for target, weight in self.adj[node]:
                    new = cost + weight
                    if new < dist.get(target, math.inf) and new <= max_length:
                        dist[target], previous[target] = new, node
                        heapq.heappush(queue, (new, target))
            for egap, eedge, efraction, esnapped in ends:
                eu, ev, elength = self.edges[eedge]
                choices = [(dist.get(eu, math.inf) + efraction*elength, eu),
                           (dist.get(ev, math.inf) + (1-efraction)*elength, ev)]
                if edge == eedge:
                    choices.append((abs(fraction-efraction)*length, None))
                route_length, last = min(choices, key=lambda item: item[0])
                if route_length > max_length or route_length < 1:
                    continue
                score = route_length + 5*(sgap+egap)
                if best is not None and score >= best[0]:
                    continue
                middle = []
                while last is not None:
                    middle.append(self.points[last])
                    last = previous[last]
                coords = [snapped] + list(reversed(middle)) + [esnapped]
                coords = [p for i, p in enumerate(coords) if i == 0 or distance(p, coords[i-1]) > 0.05]
                best = score, coords, {'lengthMeters': round(route_length), 'startSnapMeters': round(sgap), 'endSnapMeters': round(egap)}
        return (best[1:], None) if best else (None, 'disconnected track or excessive detour')


def main():
    connections = json.loads(Path(sys.argv[1]).read_text(encoding='utf-8'))
    features = json.loads(Path(sys.argv[2]).read_text(encoding='utf-8'))['features']
    paths, report, graphs, cache = [], [], {}, {}
    for connection in connections:
        line = connection['lineId']
        if line not in graphs:
            graphs[line] = Graph(features, line)
        pair = (line, *sorted((connection['fromStationId'], connection['toStationId'])))
        reverse = connection['fromStationId'] != pair[1]
        if pair not in cache:
            start, end = (connection['to'], connection['from']) if reverse else (connection['from'], connection['to'])
            start_radius, end_radius = snap_limit(line, pair[1]), snap_limit(line, pair[2])
            # Use the same nearest track anchor for both sides of this interchange,
            # keeping adjacent connections attached to one platform position.
            cache[pair] = graphs[line].path(start, end, start_radius, end_radius,
                start_limit=1 if start_radius > 300 else 12,
                end_limit=1 if end_radius > 300 else 12)
        result, reason = cache[pair]
        entry = {key: connection[key] for key in ('id', 'sourceId', 'routeId', 'lineId', 'fromStationId', 'toStationId')}
        if result:
            coords, metrics = result
            if reverse:
                metrics = {**metrics, 'startSnapMeters': metrics['endSnapMeters'], 'endSnapMeters': metrics['startSnapMeters']}
            paths.append({**entry, 'coordinates': list(reversed(coords)) if reverse else coords})
            report.append({**entry, 'matched': True, **metrics})
        else:
            report.append({**entry, 'matched': False, 'reason': reason})
    output = Path(sys.argv[3])
    output.write_text(json.dumps(paths), encoding='utf-8')
    output.with_suffix('.report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    output.with_suffix('.geojson').write_text(json.dumps({'type': 'FeatureCollection', 'features': [
        {'type': 'Feature', 'properties': {k: v for k, v in p.items() if k != 'coordinates'},
         'geometry': {'type': 'LineString', 'coordinates': p['coordinates']}} for p in paths]}), encoding='utf-8')
    print(f'Matched {len(paths)}/{len(connections)} connections; see {output.with_suffix(".report.json")}')


if __name__ == '__main__':
    main()
