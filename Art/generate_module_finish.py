"""Small deterministic PBR maps for the equipment castings, independent of the van."""
from pathlib import Path
import numpy as np
from PIL import Image

out = Path(__file__).resolve().parent.parent / 'Assets/_Project/Art/ModuleEquipment/Textures'
out.mkdir(parents=True, exist_ok=True)
rng = np.random.default_rng(90318)
n = 512
grain = rng.random((n, n))
# Fine, irregular casting grain. Gentle broad variation avoids a perfectly uniform toy surface.
coarse = Image.fromarray(np.uint8(rng.random((24, 24))*255)).resize((n,n), Image.Resampling.BICUBIC)
cloud = np.asarray(coarse)/255
color = np.clip(.88 + .08*cloud + .035*grain, 0, 1)
Image.fromarray(np.uint8(np.repeat(color[:,:,None],3,2)*255)).save(out/'Cast_Albedo.png')
height = (grain-.5)*.30 + (cloud-.5)*.08
dx = (np.roll(height,-1,1)-np.roll(height,1,1))*.5
dy = (np.roll(height,-1,0)-np.roll(height,1,0))*.5
normal = np.stack((-dx,-dy,np.ones_like(dx)),axis=2)
normal /= np.linalg.norm(normal,axis=2,keepdims=True)
Image.fromarray(np.uint8((normal*.5+.5)*255)).save(out/'Cast_Normal.png')
packed = np.zeros((n,n,4))
packed[:,:,0] = .70
packed[:,:,3] = .44 + .12*cloud + .06*grain
Image.fromarray(np.uint8(packed*255)).save(out/'Cast_MetalSmooth.png')
print('Created three 512 px equipment casting maps.')
