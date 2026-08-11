import bpy
import sys
from collections import defaultdict, deque


if "--" in sys.argv:
    source_path = sys.argv[sys.argv.index("--") + 1]
    bpy.ops.import_scene.gltf(filepath=source_path)

print("DV_NORMAL_AUDIT_BEGIN")
for obj in (item for item in bpy.data.objects if item.type == "MESH"):
    mesh = obj.data
    edge_faces = defaultdict(list)
    face_edges = []
    for polygon in mesh.polygons:
        edges = []
        vertices = polygon.vertices
        for index, vertex in enumerate(vertices):
            edge = tuple(sorted((vertex, vertices[(index + 1) % len(vertices)])))
            edge_faces[edge].append(polygon.index)
            edges.append(edge)
        face_edges.append(edges)

    neighbors = defaultdict(set)
    for faces in edge_faces.values():
        for face in faces:
            neighbors[face].update(other for other in faces if other != face)

    remaining = set(range(len(mesh.polygons)))
    components = []
    while remaining:
        seed = remaining.pop()
        component = {seed}
        queue = deque([seed])
        while queue:
            face = queue.popleft()
            for neighbor in neighbors[face]:
                if neighbor in remaining:
                    remaining.remove(neighbor)
                    component.add(neighbor)
                    queue.append(neighbor)

        volume = 0.0
        boundary_edges = 0
        component_edges = set()
        for face in component:
            polygon = mesh.polygons[face]
            v0 = mesh.vertices[polygon.vertices[0]].co
            for index in range(1, len(polygon.vertices) - 1):
                v1 = mesh.vertices[polygon.vertices[index]].co
                v2 = mesh.vertices[polygon.vertices[index + 1]].co
                volume += v0.dot(v1.cross(v2)) / 6.0
            component_edges.update(face_edges[face])
        for edge in component_edges:
            if len(edge_faces[edge]) == 1:
                boundary_edges += 1
        components.append((len(component), boundary_edges, volume))

    negative_closed = sum(1 for _, boundary, volume in components if boundary == 0 and volume < -1e-6)
    open_components = sum(1 for _, boundary, _ in components if boundary > 0)
    summary = sorted(components, reverse=True)[:12]
    print(obj.name, "components=", len(components), "negative_closed=", negative_closed,
          "open=", open_components, "largest=", summary)
    if obj.name == "Spire_Cloth":
        remaining = set(range(len(mesh.polygons)))
        while remaining:
            seed = remaining.pop()
            component = {seed}
            queue = deque([seed])
            while queue:
                face = queue.popleft()
                for neighbor in neighbors[face]:
                    if neighbor in remaining:
                        remaining.remove(neighbor)
                        component.add(neighbor)
                        queue.append(neighbor)
            radial_score = 0.0
            for face in component:
                polygon = mesh.polygons[face]
                radial = polygon.center.copy()
                radial.z = 0.0
                radial_score += polygon.normal.dot(radial.normalized())
            print("CLOTH_COMPONENT", len(component), "mean_outward_radial=", radial_score / len(component))
print("DV_NORMAL_AUDIT_END")
