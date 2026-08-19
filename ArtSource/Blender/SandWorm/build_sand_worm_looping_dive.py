import math
import os

import bpy
import bmesh
from mathutils import Matrix, Vector


OUTPUT_BLEND = r"C:\Dune Vector URP\ArtSource\Blender\SandWorm\SandWorm_LoopingDive.blend"
ACTION_NAME = "Worm_LoopingDive"

ARMATURE_NAME = "Armature"
MESH_NAME = "model_0"

# These are the weighted segment centers in the imported worm's rest pose,
# ordered from anatomical head to tail. Bone is an unweighted import root and
# remains untouched so the action stays compatible with the source skeleton.
# The final value is path lag: zero makes the toothed head lead the motion.
SEGMENTS = (
    ("Bone.001", Vector((0.093, -18.550, -0.020)), 0.0),
    ("Bone.002", Vector((-0.216, -8.936, -0.234)), 9.5),
    ("Bone.003", Vector((-0.067, 0.964, -0.483)), 19.0),
    ("Bone.004", Vector((-0.163, 9.781, -0.806)), 28.5),
    ("Bone.005", Vector((0.006, 18.533, -1.119)), 38.0),
)

# Motion path: rise vertically, follow a broad half-loop, then dive vertically.
RISE_HEIGHT = 18.0
LOOP_RADIUS = 28.0
ARC_LENGTH = math.pi * LOOP_RADIUS
PATH_END = RISE_HEIGHT + ARC_LENGTH + RISE_HEIGHT


def path_sample(distance):
    """Return a point and tangent angle for arc-length-like path distance."""
    if distance < RISE_HEIGHT:
        return Vector((0.0, 0.0, distance)), math.radians(90.0)

    if distance <= RISE_HEIGHT + ARC_LENGTH:
        u = (distance - RISE_HEIGHT) / LOOP_RADIUS
        y = LOOP_RADIUS - LOOP_RADIUS * math.cos(u)
        z = RISE_HEIGHT + LOOP_RADIUS * math.sin(u)
        angle = math.atan2(math.cos(u), math.sin(u))
        return Vector((0.0, y, z)), angle

    dive = distance - (RISE_HEIGHT + ARC_LENGTH)
    return Vector((0.0, LOOP_RADIUS * 2.0, RISE_HEIGHT - dive)), math.radians(-90.0)


def progress_at_frame(frame):
    if frame <= 36:
        return -50.0 + (frame - 1.0) * (45.0 / 35.0)
    if frame <= 192:
        return -5.0 + (frame - 36.0) * (175.0 / 156.0)
    return 170.0 + (frame - 192.0) * (25.0 / 23.0)


def clear_old_animation(armature):
    armature.animation_data_clear()
    old_action = bpy.data.actions.get(ACTION_NAME)
    if old_action:
        bpy.data.actions.remove(old_action)


def repair_mesh_and_weights(mesh_object):
    """Weld imported seam duplicates, then build continuous two-bone weights."""
    mesh = mesh_object.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    verts_before = len(bm.verts)
    boundary_before = sum(1 for edge in bm.edges if edge.is_boundary)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=0.00001)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()

    weighted_names = [entry[0] for entry in SEGMENTS]
    groups = [mesh_object.vertex_groups.get(name) for name in weighted_names]
    all_indices = list(range(len(mesh.vertices)))
    for group in groups:
        if group:
            group.remove(all_indices)

    centers = [entry[1].y for entry in SEGMENTS]
    for vertex in mesh.vertices:
        y = vertex.co.y
        if y <= centers[0]:
            groups[0].add([vertex.index], 1.0, "REPLACE")
            continue
        if y >= centers[-1]:
            groups[-1].add([vertex.index], 1.0, "REPLACE")
            continue
        for index in range(len(centers) - 1):
            if centers[index] <= y <= centers[index + 1]:
                blend = (y - centers[index]) / (centers[index + 1] - centers[index])
                # Smoothstep removes visible slope changes at segment centers.
                blend = blend * blend * (3.0 - 2.0 * blend)
                groups[index].add([vertex.index], 1.0 - blend, "REPLACE")
                groups[index + 1].add([vertex.index], blend, "REPLACE")
                break

    bm = bmesh.new()
    bm.from_mesh(mesh)
    topology = {
        "verts_before": verts_before,
        "verts_after": len(bm.verts),
        "welded": verts_before - len(bm.verts),
        "boundary_before": boundary_before,
        "boundary_after": sum(1 for edge in bm.edges if edge.is_boundary),
        "nonmanifold_after": sum(1 for edge in bm.edges if not edge.is_manifold),
    }
    bm.free()
    return topology


def key_segment_pose(pose_bone, rest_center, target, angle, frame):
    # The mesh's rest +Y axis runs from head toward tail, opposite travel.
    angle += math.pi
    deform = (
        Matrix.Translation(target)
        @ Matrix.Rotation(angle, 4, "X")
        @ Matrix.Translation(-rest_center)
    )
    pose_bone.matrix = deform @ pose_bone.bone.matrix_local
    # Blender 5's layered Action evaluation can otherwise leave a child's
    # matrix_basis relative to the previous sampled parent pose at endpoints.
    bpy.context.view_layer.update()
    pose_bone.keyframe_insert("location", frame=frame, group=pose_bone.name)
    pose_bone.keyframe_insert("rotation_euler", frame=frame, group=pose_bone.name)
    pose_bone.keyframe_insert("scale", frame=frame, group=pose_bone.name)


def build_animation():
    scene = bpy.context.scene
    armature = bpy.data.objects[ARMATURE_NAME]
    mesh = bpy.data.objects[MESH_NAME]
    topology = repair_mesh_and_weights(mesh)

    scene.render.fps = 60
    scene.render.fps_base = 1.0
    scene.frame_start = 1
    scene.frame_end = 215
    scene.use_preview_range = True
    scene.frame_preview_start = 1
    scene.frame_preview_end = 215

    clear_old_animation(armature)
    armature.animation_data_create()
    armature.animation_data.action = bpy.data.actions.new(ACTION_NAME)

    for pose_bone in armature.pose.bones:
        # The animation is planar. Euler channels avoid quaternion sign flips
        # between sampled keys (the visible bad in-between pose at frame 94).
        pose_bone.rotation_mode = "XYZ"
        pose_bone.location = (0.0, 0.0, 0.0)
        pose_bone.rotation_euler = (0.0, 0.0, 0.0)
        pose_bone.scale = (1.0, 1.0, 1.0)
    bpy.context.view_layer.update()

    keyed_frames = sorted(set([1, 215] + list(range(36, 193, 4)) + [48, 72, 94, 96, 120, 144, 168, 192]))
    for frame in keyed_frames:
        scene.frame_set(frame)
        head_progress = progress_at_frame(frame)
        for bone_name, rest_center, lag in SEGMENTS:
            target, angle = path_sample(head_progress - lag)
            key_segment_pose(armature.pose.bones[bone_name], rest_center, target, angle, frame)

    action = armature.animation_data.action
    action["description"] = "Head-led rise, broad looping arc, and tail-following dive matched to the supplied reference."
    action["reference_fps"] = 60
    action["reference_frames"] = 215
    armature["animation_action"] = ACTION_NAME
    armature["animation_reference"] = "firefox_6890 loop-dive MP4"

    scene.timeline_markers.clear()
    for name, frame in (
        ("Emergence", 36),
        ("Loop Apex", 96),
        ("Dive", 144),
        ("Tail Clear", 192),
    ):
        scene.timeline_markers.new(name, frame=frame)

    # Keep only the authored scene data; the reference movie was loaded only
    # for analysis and should not become an external dependency of the asset.
    for clip in list(bpy.data.movieclips):
        bpy.data.movieclips.remove(clip)

    mesh.hide_set(False)
    armature.hide_set(False)
    scene.frame_set(72)
    bpy.context.view_layer.objects.active = armature
    armature.select_set(True)
    mesh.select_set(False)

    os.makedirs(os.path.dirname(OUTPUT_BLEND), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=OUTPUT_BLEND)
    print(
        {
            "saved": bpy.data.filepath,
            "action": action.name,
            "frame_range": [scene.frame_start, scene.frame_end],
            "fps": scene.render.fps,
            "keyed_frames": len(keyed_frames),
            "action_slots": len(action.slots),
            "segments": [entry[0] for entry in SEGMENTS],
            "path_end": round(PATH_END, 3),
            "topology": topology,
        }
    )


build_animation()
