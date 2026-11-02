"""Original, deterministic PBR tiles for the garage; no source model is edited."""
from pathlib import Path
import numpy as np
from PIL import Image

OUT=Path(__file__).resolve().parents[3]/'Assets/_Project/Art/Garage/Textures'
OUT.mkdir(parents=True,exist_ok=True)
SIZE=1024
rng=np.random.default_rng(6047)

def noise(grid):
    data=rng.random((grid,grid)).astype(np.float32)
    # Periodic bilinear sampling makes every texture seamless.
    p=np.arange(SIZE)*grid/SIZE
    i=np.floor(p).astype(int); f=p-i
    a=data[i[:,None]%grid,i[None,:]%grid]
    b=data[i[:,None]%grid,(i[None,:]+1)%grid]
    c=data[(i[:,None]+1)%grid,i[None,:]%grid]
    d=data[(i[:,None]+1)%grid,(i[None,:]+1)%grid]
    return (a*(1-f[None,:])+b*f[None,:])*(1-f[:,None])+(c*(1-f[None,:])+d*f[None,:])*f[:,None]

def save(name,data):
    Image.fromarray(np.uint8(np.clip(data,0,1)*255+.5)).save(OUT/(name+'.png'))

def normal(name,height,strength):
    dx=(np.roll(height,-1,axis=1)-np.roll(height,1,axis=1))*strength
    dy=(np.roll(height,-1,axis=0)-np.roll(height,1,axis=0))*strength
    n=np.stack((-dx,-dy,np.ones_like(dx)),axis=-1)
    n/=np.linalg.norm(n,axis=-1,keepdims=True)
    save(name,n*.5+.5)

large=noise(5)-.5; medium=noise(48)-.5; grain=noise(384)-.5
fleck=rng.random((SIZE,SIZE))
epoxy=.87+large*.08+medium*.035+grain*.09
epoxy-=np.where(fleck>.995,.10,0)
save('Epoxy_Albedo',np.repeat(epoxy[...,None],3,axis=-1))
height=medium*.08+grain*.20+np.where(fleck>.995,-.04,0)
normal('Epoxy_Normal',height,1.6)
mask=np.zeros((SIZE,SIZE,4)); mask[...,:3]=.02; mask[...,3]=.35+large*.12+medium*.08
save('Epoxy_MetalSmooth',mask)

plaster=.91+large*.055+medium*.04+grain*.075
save('Plaster_Albedo',np.repeat(plaster[...,None],3,axis=-1))
normal('Plaster_Normal',medium*.12+grain*.22,1.5)

steel=.94+noise(96)*.035
save('Paint_Albedo',np.repeat(steel[...,None],3,axis=-1))
normal('Paint_Normal',noise(256)*.10,.8)
print('Generated 7 seamless PBR textures:',OUT)
