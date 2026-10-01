# Run inside Blender: Scripting > Open this script > Run Script.
# Creates a native .blend and a Unity-friendly FBX next to this script.
import bpy, bmesh, json, pathlib, math
base=pathlib.Path(__file__).resolve().parent
parts=json.loads((base/'mesh_data.json').read_text())
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
colors={'BlackWood':(.025,.021,.019,1),'Bronze':(.38,.25,.12,1),'CrystalBlue':(.015,.42,.9,1),'CrystalLight':(.08,.72,1,1)}
mats={}
for name,color in colors.items():
 m=bpy.data.materials.new(name);m.diffuse_color=color;m.use_nodes=True;p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=color;p.inputs['Roughness'].default_value=.7
 if 'Crystal' in name:
  if 'Emission Color' in p.inputs:p.inputs['Emission Color'].default_value=color;p.inputs['Emission Strength'].default_value=1.6
 mats[name]=m
root=bpy.data.objects.new('LightningStaff',None);bpy.context.collection.objects.link(root)
for name,vertices,faces,mat in parts:
 # Y-up source to Blender Z-up; right-handed rotation.
 mesh=bpy.data.meshes.new(name);mesh.from_pydata([(x,-z,y) for x,y,z in vertices],[],faces);mesh.update()
 obj=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(obj);obj.parent=root;obj.data.materials.append(mats[mat])
 bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
 bpy.context.view_layer.objects.active=obj
 # triangulated, flat shaded for the game asset
 mod=obj.modifiers.new('Triangulate','TRIANGULATE');bpy.ops.object.modifier_apply(modifier=mod.name)
for obj in bpy.context.scene.objects:obj.select_set(True)
bpy.context.scene.unit_settings.system='METRIC'
bpy.ops.wm.save_as_mainfile(filepath=str(base/'LightningStaff.blend'))
bpy.ops.export_scene.fbx(filepath=str(base/'LightningStaff.fbx'),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',add_leaf_bones=False,apply_unit_scale=True,bake_anim=False)
print('Saved LightningStaff.blend and LightningStaff.fbx')
