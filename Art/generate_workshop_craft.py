"""Small, repeatable workshop material maps; no source model/texture edits."""
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

ROOT=Path(__file__).resolve().parent.parent
OUT=ROOT/'Assets/_Project/Art/WorkshopCraft/Textures'
OUT.mkdir(parents=True,exist_ok=True)
N=512
rng=np.random.default_rng(90312)
y,x=np.mgrid[0:N,0:N]/N
grain=rng.random((N,N))-.5

def write(name,color,height,metal,gloss):
    if color.ndim==2: color=np.repeat(color[:,:,None],3,2)
    Image.fromarray(np.uint8(np.clip(color,0,1)*255)).save(OUT/f'{name}_Albedo.png')
    dx=(np.roll(height,-1,1)-np.roll(height,1,1))*.5
    dy=(np.roll(height,-1,0)-np.roll(height,1,0))*.5
    normal=np.stack((-dx,-dy,np.ones_like(dx)),2)
    normal/=np.linalg.norm(normal,axis=2,keepdims=True)
    Image.fromarray(np.uint8((normal*.5+.5)*255)).save(OUT/f'{name}_Normal.png')
    packed=np.zeros((N,N,4)); packed[:,:,0]=metal; packed[:,:,3]=gloss
    Image.fromarray(np.uint8(np.clip(packed,0,1)*255)).save(OUT/f'{name}_MetalSmooth.png')

tread=np.clip((.32-np.abs(y-.5))/.035,0,1)
phase=(x*58+np.abs(y-.5)*1.8)%1
block=np.clip((phase-.14)/.09,0,1)*np.clip((.95-phase)/.09,0,1)
grooves=np.clip(np.abs(y-.36)/.012,0,1)*np.clip(np.abs(y-.64)/.012,0,1)
height=tread*block*grooves*1.6+grain*.028
write('Tyre',.84+.10*block*tread+grain*.035,height,0,.12+.05*block*tread)

weave=(np.cos(x*2*np.pi*128)*np.cos(y*2*np.pi*128))*.5+.5
fold=(np.cos(y*2*np.pi*3)*.5+.5)*.04
write('Cloth',.87+.08*weave+grain*.04-fold,weave*.35,0,.075+.025*weave)
fibres=rng.random((N,N))*.12+np.sin(y*2*np.pi*110)*.018
write('Cardboard',.84+fibres,grain*.16,0,.14+fibres*.18)

# Thin overlapping scratches and occasional dark hand marks, confined to the worktop.
wear=Image.new('RGBA',(1024,256),(0,0,0,0)); draw=ImageDraw.Draw(wear)
for _ in range(240):
    cx=int(rng.uniform(0,1024)); cy=int(rng.uniform(24,232)); length=int(rng.uniform(3,55))
    draw.line((cx,cy,cx+length,cy+int(rng.uniform(-6,7))),fill=(48,48,43,int(rng.uniform(10,35))),width=1)
for _ in range(90):
    cx=int(rng.uniform(0,1024)); cy=int(rng.uniform(100,225)); r=int(rng.uniform(1,5))
    draw.ellipse((cx-r,cy-r,cx+r,cy+r),fill=(48,46,40,int(rng.uniform(8,27))))
wear.save(OUT/'CounterUse.png')
print(f'Generated {len(list(OUT.glob("*.png")))} craft maps.')
