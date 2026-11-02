"""Deterministic fabric, birch lining and phenolic deck maps for the van only."""
from pathlib import Path
import numpy as np
from PIL import Image

OUT=Path(__file__).resolve().parent.parent/'Assets/_Project/Art/VehicleInterior/Textures'
OUT.mkdir(parents=True,exist_ok=True)
N=512
rng=np.random.default_rng(66327)
y,x=np.mgrid[0:N,0:N]/N
noise=rng.random((N,N))-.5

def save(name,albedo,height,smooth):
    if albedo.ndim==2: albedo=np.repeat(albedo[:,:,None],3,2)
    Image.fromarray(np.uint8(np.clip(albedo,0,1)*255)).save(OUT/f'{name}_Albedo.png')
    dx=(np.roll(height,-1,1)-np.roll(height,1,1))*.5
    dy=(np.roll(height,-1,0)-np.roll(height,1,0))*.5
    normal=np.stack((-dx,-dy,np.ones_like(x)),2);normal/=np.linalg.norm(normal,axis=2,keepdims=True)
    Image.fromarray(np.uint8((normal*.5+.5)*255)).save(OUT/f'{name}_Normal.png')
    packed=np.zeros((N,N,4));packed[:,:,3]=smooth
    Image.fromarray(np.uint8(np.clip(packed,0,1)*255)).save(OUT/f'{name}_MetalSmooth.png')

warp=.13*np.sin(y*2*np.pi*2)+.045*np.sin(y*2*np.pi*7)
grain=np.sin((x*72+warp)*2*np.pi)*.018+np.sin((x*19+warp*.7)*2*np.pi)*.016
colour=np.array([.93,.885,.77])[None,None,:]*(.96+grain[:,:,None]+noise[:,:,None]*.022)
save('Birch',colour,grain*.75,.22+noise*.018)
weave=.5+.5*np.cos((x+y*.35)*2*np.pi*96)*np.cos(y*2*np.pi*96)
save('SeatWeave',.84+.10*weave+noise*.035,weave*.22,.12+weave*.035)
# Small raised dots in staggered rows, used on sealed plywood loading decks.
row=np.floor(y*48)
u=(x*48+(row%2)*.5)%1-.5;v=(y*48)%1-.5
grip=np.clip((.29-np.sqrt(u*u+v*v))/.10,0,1)
save('DeckGrip',.86+.08*grip+noise*.02,grip*.45,.18+grip*.045)
print(f'Generated {len(list(OUT.glob("*.png")))} van interior maps.')
