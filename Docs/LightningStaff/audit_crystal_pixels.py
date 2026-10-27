import numpy as np
from PIL import Image,ImageDraw
from pathlib import Path
d=np.load('Temp/crystal-uv-analysis.npz'); v,t,l=d['v'],d['t'],d['labels']
root=Path('Assets/Weapons/Lightning Weapon/LightningStaff/NewStaff')
a=Image.open(root/'LightningStaff_Albedo.png').convert('RGB'); e=Image.open(root/'LightningStaff_Emission_Adjusted.png').convert('RGB')
canvas=Image.new('RGB',(1200,1200)); draw=ImageDraw.Draw(canvas)
for row,label in enumerate([295,325,166]):
 pts=v[t[l==label].ravel(),3:]*[4096,-4096]+[0,4096]
 box=(int(pts[:,0].min())-4,int(pts[:,1].min())-4,int(pts[:,0].max())+5,int(pts[:,1].max())+5)
 for col,im in enumerate([a,e]):
  crop=im.crop(box); crop.thumbnail((390,370)); canvas.paste(crop.resize((390,370)),(col*400,row*400+25))
 mask=Image.new('L',(4096,4096)); md=ImageDraw.Draw(mask)
 for tri in t[l==label]: md.polygon([tuple(q) for q in v[tri,3:]*[4096,-4096]+[0,4096]],fill=255)
 canvas.paste(mask.crop(box).resize((390,370)),(800,row*400+25))
 draw.text((5,row*400+4),f'UV island {label}: albedo | emission | actual mesh UV coverage',fill='white')
 print(label,'pixel bounds top-left origin',box)
canvas.save('Docs/LightningStaff/UV-crystal-audit.png')
