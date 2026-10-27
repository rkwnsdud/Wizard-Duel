"""Deterministic, conservative repair. Run from project root after uvdump/audit.
UV island IDs come from inspect_crystal_uv.py (rounded UV topology).
Only three visually audited crystal islands are in scope; other islands are untouched.
"""
from pathlib import Path
import hashlib,json
import numpy as np
from PIL import Image,ImageDraw
from scipy.ndimage import distance_transform_edt, gaussian_filter

root=Path('Assets/Weapons/Lightning Weapon/LightningStaff/NewStaff')
out=root/'LightningStaff_Emission_CrystalFilled.png'
if out.exists(): raise RuntimeError('Refusing to overwrite an existing result')
d=np.load('Temp/crystal-uv-analysis.npz'); v,t,labels=d['v'],d['t'],d['labels']
source=root/'LightningStaff_Emission_Adjusted.png'
e=np.array(Image.open(source).convert('RGBA')); a=np.array(Image.open(root/'LightningStaff_Albedo.png').convert('RGB'))
assert e.shape[:2]==a.shape[:2]==(4096,4096)
selected=np.isin(labels,[295,325,166])
xyz=v[t[selected].ravel(),:3]
assert np.all(np.abs(xyz[:,0])<.0005) and np.all(np.abs(xyz[:,1])<.0004) and np.all(xyz[:,2]>.007)
inside=Image.new('L',(4096,4096)); other=Image.new('L',(4096,4096))
draws=[ImageDraw.Draw(other),ImageDraw.Draw(inside)]
uv=v[:,3:]*[4096,-4096]+[0,4096]
for tri,sel in zip(t,selected): draws[int(sel)].polygon([tuple(q) for q in uv[tri]],fill=255)
mask=np.asarray(inside)>0; overlap=np.asarray(other)>0
distance=distance_transform_edt(mask)
rgb=a.astype(np.float32)
blue=(rgb[:,:,2]>100)&(rgb[:,:,1]>55)&(rgb[:,:,2]>rgb[:,:,0]*1.7)&(rgb[:,:,1]>rgb[:,:,0]*1.35)
black=e[:,:,:3].max(2)==0
candidate=mask & (distance>8) & ~overlap & blue & black
# Preserve every original non-black texel, including authored lightning lines.
existing=(e[:,:,:3].max(2)>20)&mask
_,nearest=distance_transform_edt(~existing,return_indices=True)
sample=e[nearest[0],nearest[1],:3].astype(np.float32)
# Slight albedo-driven variation preserves dimensionality without boosting existing highlights.
detail=np.clip(gaussian_filter(rgb[:,:,1],1.5)/150,0.80,1.0)
edge=np.clip((distance-8)/6,0,1)
graded=np.rint(sample*detail[:,:,None]*edge[:,:,None]).astype(np.uint8)
result=e.copy(); result[candidate,:3]=graded[candidate]
changed=np.any(result!=e,axis=2)
assert not np.any(changed & ~candidate)
assert np.array_equal(result[~black],e[~black])
assert not np.any(changed & ((distance<=8)|overlap|~mask))
Image.fromarray(result).save(out)
Image.fromarray((changed*255).astype(np.uint8)).save('Docs/LightningStaff/Crystal-changed-pixels.png')
y,x=np.where(changed)
report={'source':str(source),'output':str(out),'audited_uv_islands':[295,325,166],
 'changed_pixels':int(changed.sum()),'bounds_xy_inclusive':[int(x.min()),int(y.min()),int(x.max()),int(y.max())],
 'preserved_uv_border_pixels':8,'outside_audited_islands_changed':0,'existing_nonblack_texels_changed':0,
 'overlapping_uv_pixels_changed':0,'source_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),
 'output_sha256':hashlib.sha256(out.read_bytes()).hexdigest()}
Path('Docs/LightningStaff/Crystal-pixel-audit.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps(report,indent=2))
