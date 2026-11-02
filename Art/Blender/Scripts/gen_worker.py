"""Original 1.78 m service worker mesh for the player avatar."""
import sys
import bpy
sys.path.insert(0,r'C:\Users\uur\oyunnn\Art\Blender\Scripts')
import firma_kit as k
k.UNITY_MODELS=r'C:\Users\uur\oyunnn\Assets\_Project\Art\Models'
k.reset_scene()
blue=k.mat('F Worker navy canvas',(.12,.22,.28),.88,0)
denim=k.mat('F Worker faded denim',(.23,.31,.32),.90,0)
vest=k.mat('F Worker ochre vest',(.82,.54,.15),.73,0)
skin=k.mat('F Worker skin',(.59,.40,.28),.85,0)
dark=k.mat('F Worker boots and gloves',(.065,.075,.075),.91,0)
steel=k.mat('F Worker reflective trim',(.71,.75,.70),.45,.23)
r=k.root('Character_ServiceWorker')
k.box('Torso work jacket',(.58,.31,.69),(0,0,1.25),blue,bevel=.11)
k.box('Safety vest front',(.49,.055,.50),(0,-.18,1.27),vest,bevel=.05)
k.box('Safety vest rear',(.49,.055,.50),(0,.18,1.27),vest,bevel=.05)
for x in (-.18,.18):
    k.box('Reflective vest strip',(.075,.043,.49),(x,-.215,1.25),steel,bevel=.01)
k.box('Tool belt',(.63,.36,.13),(0,0,.91),dark,bevel=.03)
for x in (-.43,.43):
    k.cylinder('Sleeve',.11,.53,(x,0,1.25),blue,verts=14)
    k.cylinder('Forearm',.09,.35,(x,0,.83),denim,verts=14)
    k.uv_sphere('Glove',.10,(x,0,.62),dark,seg=14,rings=8)
for x in (-.17,.17):
    k.cylinder('Trouser leg',.145,.70,(x,0,.49),denim,verts=14)
    k.box('Boot',(.24,.39,.19),(x,-.07,.105),dark,bevel=.04)
k.cylinder('Neck',.095,.12,(0,0,1.64),skin,verts=14)
k.uv_sphere('Head',.19,(0,0,1.77),skin,seg=18,rings=12,scale=(1,.93,1.1))
k.cylinder('Hard hat crown',.205,.14,(0,0,1.94),vest,verts=20)
k.cylinder('Hard hat brim',.255,.035,(0,0,1.87),vest,verts=20)
k.box('Hat lamp',(.10,.06,.07),(0,-.24,1.94),steel,bevel=.015)
k.parent_all(r,list(bpy.data.objects))
k.export_fbx(r,'Characters/Character_ServiceWorker')
k.save_blend('Characters/Character_ServiceWorker')
