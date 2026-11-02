"""Original synthesized interface and workshop cues; no third-party samples."""
import math, random, struct, wave
from pathlib import Path

out = Path(r'C:\Users\uur\oyunnn\Assets\_Project\Art\Audio')
out.mkdir(parents=True, exist_ok=True)
rate = 44100

def write(name, seconds, fn):
    frames = bytearray()
    for i in range(int(rate*seconds)):
        t=i/rate
        sample=max(-1,min(1,fn(t,seconds)))
        frames.extend(struct.pack('<h',int(sample*20000)))
    with wave.open(str(out/name),'wb') as f:
        f.setnchannels(1); f.setsampwidth(2); f.setframerate(rate); f.writeframes(frames)

write('ui_click.wav', .11, lambda t,d: math.sin(2*math.pi*740*t)*math.exp(-42*t)*.43)
write('notification.wav', .39, lambda t,d: (math.sin(2*math.pi*587*t) + .47*math.sin(2*math.pi*784*t))*min(1,t*35)*math.exp(-7*t)*.29)
random.seed(238)
noise=[random.uniform(-1,1) for _ in range(int(rate*.35))]
write('metal_drop.wav', .35, lambda t,d: (noise[min(int(t*rate),len(noise)-1)]*.28 + math.sin(2*math.pi*175*t)*.55)*math.exp(-18*t))
write('garage_motor.wav', 2.15, lambda t,d: (math.sin(2*math.pi*(88+8*math.sin(t*5))*t) + .18*math.sin(2*math.pi*242*t)) * min(1,t*8)*min(1,(d-t)*8)*.22)
