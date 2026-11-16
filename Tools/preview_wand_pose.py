"""Render the serialized Unity grip mesh with its actual skin weights in Blender.

Usage: blender -b --python Tools/preview_wand_pose.py -- CLIP OUTPUT [TIME]
This is an offline mesh preview, not a Unity runtime capture.
"""
import bpy, re, struct, zlib, sys
from pathlib import Path
from mathutils import Matrix, Vector, Quaternion

ROOT=Path(__file__).resolve().parents[1]
n={'__file__':str(ROOT/'Tools/aim_wand_offline.py')}
exec((ROOT/'Tools/aim_wand_offline.py').read_text().split('for filename, local in ((')[0],n)
args=sys.argv[sys.argv.index('--')+1:]
clip=Path(args[0]).read_text(encoding='utf-8-sig')
output=Path(args[1]).resolve(); output.parent.mkdir(parents=True,exist_ok=True)
t=float(args[2]) if len(args)>2 else .2
view=args[3] if len(args)>3 else 'back'
r=n['data_of'](clip,'  m_RotationCurves:\n','  m_CompressedRotationCurves:')
p=n['data_of'](clip,'  m_PositionCurves:\n','  m_ScaleCurves:')
world={}
for path in sorted(set(r)|set(p),key=lambda s:s.count('/')):
    q=n['sample'](r[path],t,n['quat']) if path in r else Quaternion()
    v=n['sample'](p[path],t,n['vector']) if path in p else Vector()
    world[path]=world.get(path.rsplit('/',1)[0],Matrix.Identity(4)) @ Matrix.LocRotScale(v,q,Vector((1,1,1)))
mesh=(ROOT/'Assets/Game/Weapons/BasicWand/Art/Grip/Wizard_BasicWandGrip.asset').read_text()
hash_bytes=bytes.fromhex(re.search(r'm_BoneNameHashes: (\w+)',mesh)[1])
hashes=struct.unpack('<'+'I'*(len(hash_bytes)//4),hash_bytes)
names=[]
for h in hashes:
    names.append(next(path for path in world if any(zlib.crc32('/'.join(path.split('/')[i:]).encode())==h for i in range(len(path.split('/'))))))
bind_data=mesh.split('  m_BindPose:\n')[1].split('  m_BoneNameHashes:')[0]
bind_numbers=[float(v) for v in re.findall(r'e\d\d: (\S+)',bind_data)]
bind=[Matrix([bind_numbers[i+j:i+j+4] for j in (0,4,8,12)]) for i in range(0,len(bind_numbers),16)]
skin=[world[path] @ b for path,b in zip(names,bind)]
raw=bytes.fromhex(re.search(r'_typelessdata: (\w+)',mesh)[1])
count=int(re.search(r'm_VertexCount: (\d+)',mesh)[1])
index_bytes=bytes.fromhex(re.search(r'm_IndexBuffer: (\w+)',mesh)[1])
indices=struct.unpack('<'+'H'*(len(index_bytes)//2),index_bytes)
def xyz(v): return (v.x,-v.z,v.y)
vertices=[]
for i in range(count):
    v=Vector(struct.unpack_from('<3f',raw,i*40))
    weights=struct.unpack_from('<4f',raw,count*48+i*32)
    bones=struct.unpack_from('<4I',raw,count*48+i*32+16)
    out=Vector()
    for weight,bone in zip(weights,bones):
        if weight: out+=(skin[bone] @ v)*weight
    vertices.append(xyz(out))
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
data=bpy.data.meshes.new('Unity baked skin')
data.from_pydata(vertices,[],[indices[i:i+3] for i in range(0,len(indices),3)])
obj=bpy.data.objects.new('Wizard',data); bpy.context.collection.objects.link(obj)
uv=data.uv_layers.new()
for face in data.polygons:
    for loop in face.loop_indices:
        uv.data[loop].uv=struct.unpack_from('<2f',raw,count*40+data.loops[loop].vertex_index*8)
material=bpy.data.materials.new('Wizard texture'); material.use_nodes=True
texture=material.node_tree.nodes.new('ShaderNodeTexImage')
texture.image=bpy.data.images.load(str(ROOT/'Assets/Game/Player/Art/Models/WizardAnimated/Textures/Meshy_AI_Wizard_Navy_Rigged_biped_texture_0.png'))
material.node_tree.links.new(texture.outputs['Color'],material.node_tree.nodes.get('Principled BSDF').inputs['Base Color'])
material.node_tree.nodes.get('Principled BSDF').inputs['Roughness'].default_value=.8
data.materials.append(material)
hand=world[n['HAND']]
wand_path=n['HAND']+'/WeaponSocket/BasicWand'
wand_q=Quaternion((-.43970728,.11589537,.8612196,.2269948))
wand_p=Vector((-.0354,.1328,.0304))
if wand_path in r: wand_q=n['sample'](r[wand_path],t,n['quat'])
if wand_path in p: wand_p=n['sample'](p[wand_path],t,n['vector'])
start=hand @ wand_p; direction=hand.to_quaternion() @ wand_q @ Vector((0,0,1))
mid=start+direction*.22
bpy.ops.mesh.primitive_cone_add(vertices=12,radius1=.009,radius2=.003,depth=.44,location=xyz(mid))
rod=bpy.context.object; rod.rotation_mode='QUATERNION'; rod.rotation_quaternion=Vector((0,0,1)).rotation_difference(Vector(xyz(direction)))
wood=bpy.data.materials.new('Wand proxy'); wood.diffuse_color=(.12,.045,.015,1); rod.data.materials.append(wood)
scene=bpy.context.scene; scene.render.engine='CYCLES'; scene.cycles.samples=24
scene.world.color=(.35,.35,.35)
focus=Vector(xyz(hand.translation))
for i,pos in enumerate((focus+Vector((1,-1,2)),focus+Vector((-1,1,1)))):
    bpy.ops.object.light_add(type='AREA',location=pos); light=bpy.context.object; light.data.energy=100; light.data.shape='DISK'; light.data.size=2
    light.rotation_euler=(focus-light.location).to_track_quat('-Z','Y').to_euler()
if view == 'wide': focus=(focus+Vector(xyz(world[n['ARM']].translation)))*.5
offset=Vector((-.8,-.5,.3)) if view=='palm' else Vector((1.1,-.1,.5))
bpy.ops.object.camera_add(location=focus+offset)
camera=bpy.context.object; camera.rotation_euler=(focus-camera.location).to_track_quat('-Z','Y').to_euler(); camera.data.type='ORTHO'; camera.data.ortho_scale=.9
if view=='wide': camera.data.ortho_scale=1.25
scene.camera=camera; scene.render.resolution_x=1000; scene.render.resolution_y=800; scene.render.resolution_percentage=100
scene.render.filepath=str(output); bpy.ops.render.render(write_still=True)
print('OFFLINE_SKIN_PREVIEW',output,'hand',tuple(hand.translation))
