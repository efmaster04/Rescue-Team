"""Short original drawer rail cue, deterministic and quiet."""
from pathlib import Path
import wave
import numpy as np

rate=44100
rng=np.random.default_rng(927)
t=np.arange(int(rate*.36))/rate
noise=rng.normal(0,1,len(t))
filtered=np.convolve(noise,np.ones(13)/13,mode='same')
envelope=np.sin(np.pi*t/.36)**1.3
rail=(filtered*.16+np.sin(2*np.pi*117*t)*.023+np.sin(2*np.pi*243*t)*.012)*envelope
rail+=np.exp(-t*140)*np.sin(2*np.pi*1250*t)*.10
path=Path(__file__).resolve().parents[3]/'Assets/_Project/Art/Audio/drawer_slide.wav'
with wave.open(str(path),'wb') as audio:
    audio.setnchannels(1); audio.setsampwidth(2); audio.setframerate(rate)
    audio.writeframes(np.int16(np.clip(rail,-1,1)*32767).tobytes())
print(path)
