"""Deterministic, seamless PBR finish maps. No photographs or source-model edits."""
from pathlib import Path
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / 'Assets/_Project/Art/WorkshopFinish/Textures'
OUT.mkdir(parents=True, exist_ok=True)
SIZE = 512
rng = np.random.default_rng(52819)

def smooth_noise(cells):
    values = rng.random((cells, cells)).astype(np.float32)
    # Periodic bilinear interpolation avoids a seam on repeated finish maps.
    p = np.arange(SIZE) * cells / SIZE
    lo = p.astype(int); f = p-lo; f=f*f*(3-2*f)
    a=values[lo[:,None]%cells,lo[None,:]%cells]
    b=values[lo[:,None]%cells,(lo[None,:]+1)%cells]
    c=values[(lo[:,None]+1)%cells,lo[None,:]%cells]
    d=values[(lo[:,None]+1)%cells,(lo[None,:]+1)%cells]
    return (a*(1-f[None,:])+b*f[None,:])*(1-f[:,None])+(c*(1-f[None,:])+d*f[None,:])*f[:,None]

def write(name, color, height, metal, gloss):
    color=np.asarray(color)
    if color.ndim==2: color=np.repeat(color[:,:,None],3,2)
    Image.fromarray(np.uint8(np.clip(color,0,1)*255)).save(OUT/f'{name}_Albedo.png')
    dx=(np.roll(height,-1,1)-np.roll(height,1,1))*.5
    dy=(np.roll(height,-1,0)-np.roll(height,1,0))*.5
    normal=np.stack((-dx,-dy,np.ones_like(dx)),2)
    normal/=np.linalg.norm(normal,axis=2,keepdims=True)
    Image.fromarray(np.uint8(np.clip(normal*.5+.5,0,1)*255)).save(OUT/f'{name}_Normal.png')
    packed=np.zeros((SIZE,SIZE,4),dtype=np.float32); packed[:,:,0]=metal; packed[:,:,3]=gloss
    Image.fromarray(np.uint8(np.clip(packed,0,1)*255)).save(OUT/f'{name}_MetalSmooth.png')

cloud=smooth_noise(4); medium=smooth_noise(32); grain=smooth_noise(144)
y,x=np.mgrid[0:SIZE,0:SIZE]/SIZE
write('Epoxy', .88+.085*cloud+.025*medium, (grain-.5)*.11, .025, .32+.075*cloud+.025*grain)
write('Wall', .91+.06*cloud+.025*medium, (grain-.5)*.13, 0, .17+.025*medium)
write('Enamel', .94+.035*cloud+.018*grain, (grain-.5)*.045, .23, .46+.06*medium)
# Body UV V is vehicle height / 3, with dirt confined to the lower returns.
low=np.clip((.31-y)/.31,0,1)**1.5
paint=.97+.022*cloud+.008*grain-low*(.04+.06*medium)
write('VanPaint', paint, (grain-.5)*.035, .24, .49+.045*medium-low*.09)
write('Rubber', .87+.055*medium+.055*grain, (grain-.5)*.19, 0, .105+.035*medium)
brushed=smooth_noise(128)*.014+np.sin(x*2*np.pi*180)*.008
write('Steel', .92+.04*medium+brushed, brushed*2, .73, .43+.07*medium)
write('Glass', .89+.08*y+.012*cloud, np.zeros_like(x), .10, .82+.055*medium)
flutes=(.5+.5*np.cos(x*2*np.pi*64))**7
write('Lens', .91+.065*flutes+.015*medium, flutes*.11, .025, .74+.08*flutes)

# One restrained, soft overlay inside the existing service bay, at floor height.
wx=2.41+x*4.48; wz=-4.25+y*7.66
tracks=np.exp(-((wx-(4.65-.94))/.145)**2)+np.exp(-((wx-(4.65+.94))/.145)**2)
travel=np.clip((wz+4.25)/.6,0,1)*np.clip((2.65-wz)/.6,0,1)
alpha=tracks*travel*(.055+.045*medium)
for cx,cz in [(3.71,-2.14),(5.59,-2.14),(3.71,1.60),(5.59,1.60)]:
    alpha+=.045*np.exp(-((wx-cx)/.35)**2-((wz-cz)/.55)**2)*medium
rgba=np.empty((SIZE,SIZE,4)); rgba[:,:,:3]=(.13,.145,.14); rgba[:,:,3]=np.clip(alpha,0,.14)
Image.fromarray(np.uint8(rgba*255)).save(OUT/'ServiceBayWear.png')
print(f'Generated {len(list(OUT.glob("*.png")))} finish maps in {OUT}')
