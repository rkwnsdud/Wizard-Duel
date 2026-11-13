"""무기 FBX를 목표 삼각형 수로 줄여 같은 구조로 다시 내보낸다 (이슈 #105).

blender -b --factory-startup --python Tools/WeaponMesh/decimate_weapon.py -- <in.fbx> <out.fbx> <target_tris> <render_dir> <label>

- Decimate(Collapse)로 줄인 뒤 원본 표면의 커스텀 노멀을 Data Transfer로 옮긴다.
- 오브젝트·메시·머티리얼 이름을 원본과 같게 두므로 Unity fileID가 원본과 같다.
  새 FBX의 .meta는 원본 .meta를 복사해 guid만 바꾸고, 프리팹에서는 guid만 바꾸면 된다.
- 텍스처는 넣지 않는다. Unity 머티리얼 리맵(externalObjects)으로 기존 .mat에 연결된다.
- <render_dir>에 원본(위)·감량본(아래) 비교 이미지 <label>_compare.png를 남긴다.

2026-11-13 Blender 5.2로 생성:
  BasicWand_v2.fbx (243,156) -> BasicWand_Low.fbx (10,000)
  LightningStaff_Fixed.fbx (298,036) -> LightningStaff_Low.fbx (9,998)
"""
import bpy, sys, os, math
import mathutils
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:]
in_path, out_path, target, render_dir, label = argv[0], argv[1], int(argv[2]), argv[3], argv[4]
os.makedirs(render_dir, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=in_path)
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
assert len(meshes) == 1, f"mesh objects: {[o.name for o in meshes]}"
obj = meshes[0]
for o in list(bpy.data.objects):
    if o is not obj:
        bpy.data.objects.remove(o, do_unlink=True)

obj_name, mesh_name = obj.name, obj.data.name
mat_names = [m.name for m in obj.data.materials]


def tri_count(o):
    o.data.calc_loop_triangles()
    return len(o.data.loop_triangles)


def apply_modifier(o, mod):
    bpy.context.view_layer.objects.active = o
    for x in bpy.context.view_layer.objects:
        x.select_set(x is o)
    bpy.ops.object.modifier_apply(modifier=mod.name)


src_tris = tri_count(obj)
print(f"[{label}] source tris={src_tris} verts={len(obj.data.vertices)}")

# 노멀을 옮겨 받을 원본 사본
src = obj.copy()
src.data = obj.data.copy()
src.name = "__source"
bpy.context.scene.collection.objects.link(src)

dec = obj.modifiers.new("Decimate", 'DECIMATE')
dec.decimate_type = 'COLLAPSE'
dec.ratio = target / src_tris
dec.use_collapse_triangulate = True
apply_modifier(obj, dec)
print(f"[{label}] decimated tris={tri_count(obj)} verts={len(obj.data.vertices)} ratio={target / src_tris:.5f}")

# 원본 표면의 커스텀 노멀을 감량본에 옮긴다
dt = obj.modifiers.new("Normals", 'DATA_TRANSFER')
dt.object = src
dt.use_loop_data = True
dt.data_types_loops = {'CUSTOM_NORMAL'}
dt.loop_mapping = 'POLYINTERP_NEAREST'
apply_modifier(obj, dt)

# 이름은 원본과 같게 둔다 (Unity fileID가 이름에서 나온다)
obj.name, obj.data.name = obj_name, mesh_name
assert [m.name for m in obj.data.materials] == mat_names, [m.name for m in obj.data.materials]
print(f"[{label}] names obj={obj.name!r} mesh={obj.data.name!r} mats={mat_names} faces/mat="
      f"{np.bincount([p.material_index for p in obj.data.polygons]).tolist()}")


# ---- 전후 비교 렌더 (Workbench, 텍스처 색) ----
scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'
scene.display.shading.color_type = 'TEXTURE'
scene.display.shading.show_cavity = False
scene.render.resolution_x = 900
scene.render.resolution_y = 900
scene.render.film_transparent = False
scene.world = bpy.data.worlds.new("w") if scene.world is None else scene.world

cam_data = bpy.data.cameras.new("cam")
cam_data.type = 'ORTHO'
cam = bpy.data.objects.new("cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam

bb = [obj.matrix_world @ mathutils.Vector(c) for c in obj.bound_box]
mn = mathutils.Vector([min(v[i] for v in bb) for i in range(3)])
mx = mathutils.Vector([max(v[i] for v in bb) for i in range(3)])
size = mx - mn
long_axis = max(range(3), key=lambda i: size[i])


def look(direction, center, ortho):
    direction = mathutils.Vector(direction).normalized()
    cam.location = center - direction * 5.0
    cam.rotation_euler = direction.to_track_quat('-Z', 'Y' if long_axis != 1 else 'Z').to_euler()
    cam_data.ortho_scale = ortho
    cam_data.clip_end = 20


views = []
center = (mn + mx) / 2
side_dirs = [d for d in ([0, 1, 0], [1, 0, 0], [0, 0, -1]) if d[long_axis] == 0][:2]
views.append(("full_a", side_dirs[0], center, size[long_axis] * 1.05))
views.append(("full_b", side_dirs[1], center, size[long_axis] * 1.05))
for end, sign in (("end_hi", 1), ("end_lo", -1)):
    c = center.copy()
    c[long_axis] += sign * size[long_axis] * 0.38
    views.append((end, side_dirs[0], c, size[long_axis] * 0.26))
    views.append((end + "_b", [(a + b) / math.sqrt(2) for a, b in zip(side_dirs[0], side_dirs[1])], c, size[long_axis] * 0.26))


def render_set(o, tag):
    paths = []
    for name, d, c, ortho in views:
        look(d, c, ortho)
        p = os.path.join(render_dir, f"{label}_{tag}_{name}.png")
        scene.render.filepath = p
        bpy.ops.render.render(write_still=True)
        paths.append(p)
    return paths


def hide_only(show):
    for o in (obj, src):
        o.hide_render = o is not show


hide_only(src)
before = render_set(src, "before")
hide_only(obj)
after = render_set(obj, "after")

# 전(위)·후(아래)를 한 장으로 붙인다
rows = []
for pb, pa in zip(before, after):
    ib, ia = bpy.data.images.load(pb), bpy.data.images.load(pa)
    w, h = ib.size
    ab = np.array(ib.pixels[:]).reshape(h, w, 4)
    aa = np.array(ia.pixels[:]).reshape(h, w, 4)
    rows.append((ab, aa))
top = np.concatenate([r[0] for r in rows], axis=1)  # 이미지 좌표는 아래에서 위로
bottom = np.concatenate([r[1] for r in rows], axis=1)
sheet = np.concatenate([bottom, top], axis=0)
hh, ww = sheet.shape[:2]
img = bpy.data.images.new(f"{label}_compare", ww, hh)
img.pixels = sheet.ravel().tolist()
img.filepath_raw = os.path.join(render_dir, f"{label}_compare.png")
img.file_format = 'PNG'
img.save()
print(f"[{label}] compare sheet {img.filepath_raw} ({ww}x{hh}) views={[v[0] for v in views]}")

# ---- 내보내기 ----
bpy.data.objects.remove(src, do_unlink=True)
bpy.data.objects.remove(cam, do_unlink=True)
# 텍스처는 Unity에서 머티리얼 리맵으로 연결하므로 FBX에 넣지 않는다
for m in obj.data.materials:
    if m and m.node_tree:
        for n in [n for n in m.node_tree.nodes if n.type == 'TEX_IMAGE']:
            m.node_tree.nodes.remove(n)

bpy.context.view_layer.update()
for x in bpy.data.objects:
    x.select_set(x is obj)
bpy.context.view_layer.objects.active = obj
bpy.ops.export_scene.fbx(
    filepath=out_path,
    use_selection=True,
    object_types={'MESH'},
    axis_forward='-Z', axis_up='Y',
    global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE',
    bake_space_transform=False,
    use_mesh_modifiers=True,
    mesh_smooth_type='OFF',
    use_tspace=False,
    use_custom_props=False,
    add_leaf_bones=False,
    bake_anim=False,
    path_mode='STRIP', embed_textures=False,
)
print(f"[{label}] exported {out_path} size={os.path.getsize(out_path)}")
