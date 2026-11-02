"""Short original office/garage foley, deterministic and generated locally."""
import math
import random
import struct
import wave
from pathlib import Path

folder=Path(__file__).resolve().parents[3]/'Assets/_Project/Art/Audio'
rate=44100
random.seed(982)

def write(name,duration,sound):
    samples=bytearray()
    low=0.0
    for index in range(int(duration*rate)):
        t=index/rate
        low=low*.82+random.uniform(-1,1)*.18
        value=max(-1,min(1,sound(t,low)))
        samples.extend(struct.pack('<h',int(value*28000)))
    with wave.open(str(folder/(name+'.wav')),'wb') as file:
        file.setparams((1,2,rate,0,'NONE','not compressed'))
        file.writeframes(samples)

write('concrete_step',.22,lambda t,n:
    (math.sin(2*math.pi*92*t)*.20+n*.85)*math.exp(-24*t)*min(1,t*260))
write('door_open',.36,lambda t,n:
    (n*.45+math.sin(2*math.pi*(280+70*t)*t)*.08)*math.exp(-13*t)*min(1,t*180))
write('door_close',.29,lambda t,n:
    (n*.4+math.sin(2*math.pi*105*t)*.25)*math.exp(-21*t)*min(1,t*210))
