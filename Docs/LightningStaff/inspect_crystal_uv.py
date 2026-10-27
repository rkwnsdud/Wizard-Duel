from pathlib import Path
import numpy as np
from PIL import Image
import struct
p=Path('Docs/LightningStaff/mesh-uv-audit.bin').read_bytes()
n=struct.unpack_from('<i',p)[0]
v=np.frombuffer(p,dtype='<f4',count=n*5,offset=4).reshape(-1,5)
nt=struct.unpack_from('<i',p,4+n*20)[0]
t=np.frombuffer(p,dtype='<i4',count=nt,offset=8+n*20).reshape(-1,3)
root=Path('Assets/Weapons/Lightning Weapon/LightningStaff/NewStaff')
a=np.asarray(Image.open(root/'LightningStaff_Albedo.png').convert('RGB'))
e=np.asarray(Image.open(root/'LightningStaff_Emission_Adjusted.png').convert('RGB'))
c=v[t].mean(axis=1); uv=c[:,3:]; xy=np.clip((uv*[4096,-4096]+[0,4096]).astype(int),0,4095)
col=a[xy[:,1],xy[:,0]]; em=e[xy[:,1],xy[:,0]]
lit=em.max(axis=1)>20
print('lit spatial percentiles',np.percentile(c[lit,:3],[0,5,50,95,100],axis=0))
for z0,z1 in [(0.005,0.006),(0.006,0.007),(0.007,0.008),(0.008,0.0095)]:
 sel=(c[:,2]>z0)&(c[:,2]<z1)
 print(z0,z1,'tris',sel.sum(),'lit',lit[sel].sum())
# UV islands associated with existing emission, identify disconnected triangle topology in UV space.
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import connected_components
_,ids=np.unique(np.round(v[:,3:],6),axis=0,return_inverse=True)
edges=ids[t]; rows=edges[:,[0,1,2]].ravel(); cols=edges[:,[1,2,0]].ravel()
_,labels=connected_components(coo_matrix((np.ones(len(rows)),(rows,cols)),shape=(ids.max()+1,)*2),directed=False)
tl=labels[ids[t[:,0]]]
for label in np.unique(tl[lit]):
 s=tl==label
 print('island',label,'tris',s.sum(),'lit',lit[s].sum(),'xyz',c[s,:3].min(0),c[s,:3].max(0),'uv',c[s,3:].min(0),c[s,3:].max(0))
np.savez('Temp/crystal-uv-analysis.npz',v=v,t=t,labels=tl)
