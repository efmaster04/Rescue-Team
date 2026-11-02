"""Deterministic, seamless material detail; dimensions assume a 2 m UV tile."""
from pathlib import Path
import numpy as np
from PIL import Image

OUT = Path(__file__).resolve().parents[3] / 'Assets/_Project/Art/Garage/Textures'
OUT.mkdir(parents=True, exist_ok=True)
N = 1024
rng = np.random.default_rng(17109)
y, x = np.mgrid[:N, :N] / N

def save(name, data):
    Image.fromarray(np.uint8(np.clip(data, 0, 1) * 255 + .5)).save(OUT / (name + '.png'))

def rgb(data):
    return np.repeat(data[..., None], 3, axis=-1)

def normal(name, height, strength):
    dx = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) * strength
    dy = (np.roll(height, -1, 0) - np.roll(height, 1, 0)) * strength
    n = np.stack((-dx, -dy, np.ones_like(dx)), -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    save(name + '_Normal', n * .5 + .5)

def mask(name, metallic, smooth):
    data = np.ones((N, N, 4))
    data[..., :3] = metallic
    data[..., 3] = smooth
    save(name + '_MetalSmooth', data)

# Fine brushed stainless, directionally consistent, with sparse hairline wear.
grain = rng.random((N, 1)) * .5 + rng.random((N, N)) * .12
brush = .91 + grain * .11
height = grain * .025
for _ in range(44):
    row = rng.integers(N)
    start = rng.integers(N)
    length = rng.integers(25, 240)
    ids = (start + np.arange(length)) % N
    brush[row, ids] -= rng.uniform(.025, .075)
    height[row, ids] -= .006
save('BrushedSteel_Albedo', rgb(brush))
normal('BrushedSteel', height, 1.2)
mask('BrushedSteel', .86, .53 + grain * .10)

# Powder-coated sheet: tiny orange peel, no broad dirty/noisy blotches.
peel = rng.random((N, N))
save('PowderCoat_Albedo', rgb(.96 + peel * .035))
normal('PowderCoat', peel * .035, .9)
mask('PowderCoat', .16, .35 + peel * .10)

# 25 mm perforation pitch, 5 mm bore, shaded bore and raised cut edge.
pitch = 80
dx = ((x * pitch + .5) % 1 - .5)
dy = ((y * pitch + .5) % 1 - .5)
radius = np.sqrt(dx * dx + dy * dy)
inside = np.clip((.105 - radius) * 90, 0, 1)
edge = np.exp(-((radius - .115) / .022) ** 2)
board = .88 - inside * .79 + edge * .09
save('Pegboard_Albedo', rgb(board))
normal('Pegboard', -inside * .20 + edge * .04, 1.5)
mask('Pegboard', .22, .28 + edge * .15)

# Ribbed anti-fatigue rubber, 20 mm ribs with a shallow crosshatch.
rib = np.sin(x * np.pi * 2 * 100) ** 12
cross = np.sin(y * np.pi * 2 * 50) ** 18
rubber = .87 + rib * .065 + rng.random((N, N)) * .035
save('RibbedRubber_Albedo', rgb(rubber))
normal('RibbedRubber', rib * .08 + cross * .015, 1.2)
mask('RibbedRubber', 0, .13 + rib * .04)

# Equipment plastics and tire/handle rubber use fine grain without mat ribs.
for name, base, amplitude, strength, smooth in [
        ('MouldedPlastic', .965, .025, .8, .28),
        ('GripRubber', .93, .045, 1.1, .12)]:
    grain = rng.random((N, N))
    save(name + '_Albedo', rgb(base + grain * amplitude))
    normal(name, grain * .055, strength)
    mask(name, 0, smooth + grain * .035)

print('Generated six material sets:', OUT)
