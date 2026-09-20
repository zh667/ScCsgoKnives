"""Attach the matching CS2 world prop tracks to the derived actor's upper body.

Source weapon nodes are independent skeletons in Source axes/metres. Their glTF
axis-conversion root is removed before attaching to the actor's wpn socket. No
viewmodel animation is mixed into these clips. Source files remain untouched.
"""
import copy
import numpy as np
from scipy.spatial.transform import Rotation
from cs2_knife_render import key


def matrix(node):
    if 'matrix' in node:
        return np.array(node['matrix']).reshape(4, 4)
    result = np.eye(4)
    result[:3, :3] = np.diag(node.get('scale', [1, 1, 1])) @ Rotation.from_quat(node.get('rotation', [0, 0, 0, 1])).as_matrix().T
    result[3, :3] = node.get('translation', [0, 0, 0])
    return result


def derive(source, doc, raw, wanted):
    nodes = source.j['nodes']
    parents = {c: i for i, n in enumerate(nodes) for c in n.get('children', [])}
    # A common upper-body parent preserves the source socket between keyframes.
    # Parenting to a rapidly spinning knife wrist makes the inverse wrist/socket
    # transform interpolate along a different arc and visibly breaks contact.
    anchor = next(i for i, n in enumerate(nodes) if n.get('name') == 'spine_2')
    socket = next(i for i, n in enumerate(nodes) if n.get('name') == 'wpn')
    dest_anchor = next(i for i, n in enumerate(doc['nodes']) if n.get('name') == 'spine_2')
    mount = len(doc['nodes'])
    doc['nodes'].append({'name': 'cs_weapon_mount', 'children': []})
    doc['nodes'][dest_anchor].setdefault('children', []).append(mount)
    skeletons = {n['name'].split('/')[-1][:-8]: i for i, n in enumerate(nodes) if n.get('name', '').endswith('.vnmskel')}
    aliases = {'m4a1s': 'm4a1_silencer', 'galilar': 'galil', 'grenade': 'hegrenade'}
    mappings = {}
    def descendants(i):
        yield i
        for child in nodes[i].get('children', []):
            yield from descendants(child)
    def array(i):
        a = source.j['accessors'][i]
        width = {'SCALAR': 1, 'VEC3': 3, 'VEC4': 4}[a['type']]
        view = source.j['bufferViews'][a['bufferView']]
        return np.ndarray((a['count'], width), dtype='<f4', buffer=source.view(a['bufferView']),
                          offset=a.get('byteOffset', 0), strides=(view.get('byteStride', width * 4), 4)).copy()
    def add(out, node, prop, times, values):
        values = np.array(values, dtype='<f4')
        times = np.array(times, dtype='<f4').reshape(-1, 1)
        if np.max(np.abs(values - values[0])) < 1e-7:
            values, times = values[[0, -1]], times[[0, -1]]
        indices = []
        for data, kind in ((times, 'SCALAR'), (values, 'VEC4' if prop == 'rotation' else 'VEC3')):
            indices.append(len(doc['accessors']))
            doc['accessors'].append({'bufferView': raw(data.tobytes()), 'componentType': 5126, 'count': len(data), 'type': kind})
        out['channels'].append({'sampler': len(out['samplers']), 'target': {'node': node, 'path': prop}})
        out['samplers'].append({'input': indices[0], 'output': indices[1], 'interpolation': 'LINEAR'})
    for out in list(doc['animations']):
        alias = out['name']
        if not alias.startswith(('draw_', 'reload_', 'reloadEmpty_')):
            continue
        asset = alias.split('_', 1)[1]
        skeleton = aliases.get(asset, asset)
        if skeleton not in skeletons:
            skeleton = 'knife_' + skeleton
        assert skeleton in skeletons, alias
        if asset not in mappings:
            wrapper = skeletons[skeleton]
            roots = nodes[wrapper]['children']
            selected = [i for root in roots for i in descendants(root) if 'mesh' not in nodes[i]]
            remap = {i: len(doc['nodes']) + k for k, i in enumerate(selected)}
            for i in selected:
                node = {k: copy.deepcopy(v) for k, v in nodes[i].items() if k in ('translation', 'rotation', 'scale')}
                node['name'] = 'cswp_' + asset + '/' + nodes[i]['name']
                node['children'] = [remap[c] for c in nodes[i].get('children', []) if c in remap]
                if not node['children']:
                    node.pop('children')
                if i in roots:
                    node.pop('rotation', None)  # axis conversion belongs to the actor, once
                doc['nodes'].append(node)
            doc['nodes'][mount]['children'].extend(remap[i] for i in roots if i in remap)
            mappings[asset] = (remap, roots)
        remap, roots = mappings[asset]
        animation = next(a for a in source.j['animations'] if a['name'] == wanted[alias])
        curves = {}
        for c in animation['channels']:
            s = animation['samplers'][c['sampler']]
            curves[(c['target']['node'], c['target']['path'])] = {'Times': array(s['input']).ravel().tolist(), 'Values': array(s['output']).tolist()}
        duration = max(v['Times'][-1] for v in curves.values())
        # Retain source frames, just as the actor channels do.
        source_times = np.array(max(curves.values(), key=lambda c: len(c['Times']))['Times'])
        times = source_times
        mount_values = []
        for t in times:
            cache = {}
            def absolute(i):
                if i not in cache:
                    n = dict(nodes[i])
                    for prop in ('translation', 'rotation', 'scale'):
                        if (i, prop) in curves:
                            n[prop] = key(curves[i, prop], float(t))
                    cache[i] = matrix(n) @ (absolute(parents[i]) if i in parents else np.eye(4))
                return cache[i]
            mount_values.append(absolute(socket) @ np.linalg.inv(absolute(anchor)))
        add(out, mount, 'translation', times, [m[3, :3] for m in mount_values])
        rotations = Rotation.from_matrix(np.array([m[:3, :3].T for m in mount_values])).as_quat()
        for i in range(1, len(rotations)):
            if rotations[i] @ rotations[i - 1] < 0:
                rotations[i] *= -1
        add(out, mount, 'rotation', times, rotations)
        for old, new in remap.items():
            for prop, fallback in [('translation', [0, 0, 0]), ('rotation', [0, 0, 0, 1]), ('scale', [1, 1, 1])]:
                # Root axis correction is static; prop motion starts at weapon_offset.
                values = [key(curves[old, prop], float(t)) for t in times] if (old, prop) in curves and old not in roots else [doc['nodes'][new].get(prop, fallback)] * len(times)
                add(out, new, prop, times, values)
        if alias.startswith('draw_'):
            # idle_<weapon> in this export is additive. The final full draw frame is
            # a resolved, weapon-specific hold, including the fingers and prop pose.
            hold = {'name': 'hold_' + asset, 'channels': [], 'samplers': []}
            # Read generated buffer data via the body sampler's original source for
            # actor channels, and the already resolved mount/prop values for extras.
            for c in out['channels']:
                node, prop = c['target']['node'], c['target']['path']
                if node < mount:
                    old = next(i for i, n in enumerate(nodes) if n.get('name') == doc['nodes'][node]['name'])
                    value = key(curves[old, prop], duration)
                elif node == mount:
                    value = mount_values[-1][3, :3] if prop == 'translation' else rotations[-1]
                else:
                    old = next(i for i, new in remap.items() if new == node)
                    fallback = {'translation': [0, 0, 0], 'rotation': [0, 0, 0, 1], 'scale': [1, 1, 1]}[prop]
                    value = key(curves[old, prop], duration) if (old, prop) in curves and old not in roots else doc['nodes'][node].get(prop, fallback)
                add(hold, node, prop, [0, .01], [value, value])
            doc['animations'].append(hold)
