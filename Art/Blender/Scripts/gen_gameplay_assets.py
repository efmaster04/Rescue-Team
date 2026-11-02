"""Stylized mid-poly gameplay models. Run with Blender 5.2 in background mode."""
import math
import sys
import bpy

sys.path.insert(0, r"C:\Users\uur\oyunnn\Art\Blender\Scripts")
import firma_kit as k
k.UNITY_MODELS = r"C:\Users\uur\oyunnn\Assets\_Project\Art\Models"
k.UNITY_TEXTURES = r"C:\Users\uur\oyunnn\Assets\_Project\Art\Textures"

def materials():
    return {
        'ivory': k.mat('F Aged ivory enamel', (.68,.65,.56), .68,.15),
        'blue': k.mat('F Service blue', (.10,.27,.35), .61,.18),
        'dark': k.mat('F Charcoal steel', (.07,.09,.10), .55,.45),
        'steel': k.mat('F Brushed steel', (.46,.50,.50), .42,.70),
        'rubber': k.mat('F Tire rubber', (.035,.04,.04), .92,0),
        'yellow': k.mat('F Safety ochre', (.83,.51,.10), .56,.2),
        'red': k.mat('F Fire red', (.53,.09,.07), .52,.21),
        'glass': k.mat('F Smoky glazing', (.10,.19,.21), .25,.13),
        'wood': k.mat('F Workshop timber', (.39,.24,.13), .75,0),
        'white': k.mat('F Warm white', (.81,.80,.73), .67,0),
        'orange': k.mat('F Amber lens', (.93,.38,.045), .25,0),
    }

def b(name, size, loc, mat, bevel=.035):
    return k.box(name, size, loc, mat, bevel=bevel)
def c(name, radius, depth, loc, mat, axis='Z', verts=20):
    return k.cylinder(name, radius, depth, loc, mat, axis=axis, verts=verts)
def finish(root, path):
    k.parent_all(root, list(bpy.data.objects))
    k.export_fbx(root, path)
    k.save_blend(path)

def truck():
    k.reset_scene(); m=materials(); r=k.root('Vehicle_ServiceTruck')
    b('Chassis left rail',(.15,5.4,.25),(-.76,-.35,.81),m['dark'])
    b('Chassis right rail',(.15,5.4,.25),(.76,-.35,.81),m['dark'])
    for y in (-3,-1.4,.4,1.7): b('Chassis crossmember',(1.75,.13,.20),(0,y,.78),m['steel'])
    b('Cab lower body',(2.16,2.24,1.30),(0,2.16,1.39),m['ivory'],.13)
    b('Cab roof',(2.10,2.02,.27),(0,2.24,2.45),m['ivory'],.12)
    b('Cab windshield',(1.83,.055,.73),(0,3.24,2.08),m['glass'],.02)
    for x in (-1.08,1.08):
        b('Cab side glazing',(.05,1.05,.55),(x,2.34,2.03),m['glass'],.02)
        b('Cab door blue service stripe',(.055,1.55,.14),(x*1.02,2.19,1.48),m['blue'],.01)
        b('Step board',(.22,1.18,.08),(x*1.05,2.08,.67),m['dark'])
        b('Side mirror arm',(.28,.05,.05),(x*1.08,3.02,2.04),m['steel'])
        b('Side mirror',(.07,.32,.26),(x*1.26,3.02,2.07),m['dark'])
    b('Front grille',(1.38,.08,.48),(0,3.35,1.25),m['dark'])
    for x in (-.82,.82):
        c('Headlamp bezel',.18,.07,(x,3.38,1.35),m['steel'],'Y')
        c('Headlamp lens',.135,.07,(x,3.43,1.35),m['white'],'Y')
        b('Rear lamp',(.27,.07,.23),(x,-3.38,.94),m['red'])
    b('Front bumper',(2.22,.23,.19),(0,3.45,.70),m['steel'])
    b('Rear bumper',(2.30,.23,.19),(0,-3.39,.74),m['steel'])
    for x in (-1.00,1.00):
        for y in (-2.25,2.34):
            c('Wheel rubber',.44,.25,(x,y,.47),m['rubber'],'X',24)
            c('Wheel hub',.24,.265,(x,y,.47),m['steel'],'X',20)
            c('Hub cap',.08,.28,(x,y,.47),m['dark'],'X',12)
    b('Mount deck',(2.03,4.00,.11),(0,-1.25,.99),m['steel'])
    for x in (-.84,.84):
        for y in (-2.94,.45):
            c('Twist lock housing',.085,.09,(x,y,1.07),m['yellow'])
    k.empty('SOCKET_ModuleMount',(0,-1.25,1.08))
    k.empty('SOCKET_Driver',(-.45,2.2,1.18))
    k.empty('SOCKET_Connector_Hydraulic',(.43,.89,1.33))
    k.empty('SOCKET_Connector_Electric',(-.43,.89,1.33))
    k.collider('Cab',(2.2,2.3,2.2),(0,2.1,1.35))
    k.collider('Deck',(2.3,4.0,.32),(0,-1.25,.95))
    finish(r,'Vehicles/Vehicle_ServiceTruck')

def base_module(name, accent):
    k.reset_scene(); m=materials(); r=k.root(name)
    for x in (-.94,.94): b('Load frame longitudinal',(.13,3.82,.17),(x,0,.11),m['dark'])
    for y in (-1.78,1.78): b('Load frame cross rail',(2.08,.15,.17),(0,y,.11),m['steel'])
    for x in (-.84,.84):
        for y in (-1.69,1.69):
            c('Twist-lock receiver',.08,.13,(x,y,.15),m['steel'])
            k.empty('SOCKET_LiftPoint', (x,y,1.9))
    b('Hydraulic and electric coupling block',(.56,.14,.28),(0,1.91,.30),m['yellow'])
    return r,m,accent

def cargo_sockets(n, y0=-1.2, ystep=.75):
    for i in range(n):
        x=(-.51 if i%2==0 else .51)
        y=y0+(i//2)*ystep
        k.empty(f'SOCKET_Cargo_{i+1:02d}',(x,y,.52))

def module_towing():
    r,m,a=base_module('Module_Towing','yellow')
    b('Reinforced recovery platform',(2.16,3.75,.20),(0,0,.32),m[a])
    for x in (-.94,.94):
        b('Side equipment locker',(.34,1.75,.76),(x,.35,.78),m['dark'])
        for y in (-.32,.35,.99): b('Locker latch',(.03,.13,.08),(x*1.19,y,.80),m['steel'])
    b('Winch pedestal',(.92,.73,.58),(0,.98,.83),m['steel'])
    c('Winch spool',.31,.91,(0,.87,1.16),m['dark'],'X')
    b('Recovery boom',(1.00,2.75,.25),(0,-.58,1.32),m[a])
    b('Boom reinforcement',(.52,1.93,.20),(0,-.86,1.51),m['dark'])
    c('Tow hook',.12,.09,(0,-2.02,1.16),m['steel'],'X')
    for y in (-1.55,1.45): b('Safety chevron',(2.06,.09,.05),(0,y,.45),m['dark'])
    cargo_sockets(4); k.collider('Body',(2.2,3.8,1.3),(0,0,.82)); finish(r,'Modules/Module_Towing')

def module_fire():
    r,m,a=base_module('Module_Fire','red')
    b('Water tank shell',(1.78,2.10,1.18),(0,.18,1.01),m[a],.18)
    b('Tank top',(1.47,1.72,.25),(0,.18,1.68),m['steel'],.10)
    for x in (-.99,.99):
        b('Pump side cabinet',(.22,2.52,.89),(x,0,.88),m[a])
        for y in (-.84,-.24,.37,.97): b('Shutter ridge',(.035,.04,.69),(x*1.13,y,.88),m['steel'],.005)
    c('Hose reel',.37,.28,(0,-1.36,1.02),m['dark'],'Y')
    c('Reel hub',.17,.30,(0,-1.36,1.02),m[a],'Y')
    for x in (-.57,.57): c('Gauge',.105,.045,(x,-1.54,.92),m['white'],'Y')
    b('Roof ladder',(1.0,2.75,.10),(0,.28,1.89),m['steel'])
    cargo_sockets(5); k.collider('Tank',(2.2,3.6,1.9),(0,0,1.03)); finish(r,'Modules/Module_Fire')

def module_cargo():
    r,m,a=base_module('Module_Cargo','wood')
    b('Timber cargo floor',(2.13,3.75,.16),(0,0,.30),m[a])
    for x in (-1.06,1.06):
        b('Folded side gate',(.09,3.75,.51),(x,0,.64),m['steel'])
        for y in (-1.4,-.7,0,.7,1.4): c('Tie down ring',.065,.035,(x,y,.94),m['yellow'],'X',12)
    b('Cab guard',(2.13,.13,1.22),(0,1.83,.86),m['dark'])
    b('Rear flap',(2.13,.10,.49),(0,-1.89,.64),m['steel'])
    cargo_sockets(8); k.collider('Bed',(2.2,3.8,.35),(0,0,.35)); finish(r,'Modules/Module_Cargo')

def module_tech():
    r,m,a=base_module('Module_TechService','blue')
    b('Service body',(2.15,3.69,1.62),(0,0,1.02),m[a],.12)
    b('Roof skin',(2.09,3.63,.12),(0,0,1.91),m['ivory'])
    for x in (-1.09,1.09):
        for y in (-.87,.20,1.26):
            b('Tool locker shutter',(.05,.89,.97),(x,y,1.13),m['steel'])
            b('Locker pull',(.075,.24,.045),(x*1.03,y,1.05),m['dark'])
    b('Rear work door',(1.27,.05,1.48),(0,-1.86,1.05),m['ivory'])
    b('Generator exhaust',(.27,.27,.33),(.63,.99,2.09),m['dark'])
    cargo_sockets(6); k.collider('Body',(2.2,3.7,2.0),(0,0,1.08)); finish(r,'Modules/Module_TechService')

def module_water():
    r,m,a=base_module('Module_WaterPump','blue')
    b('Pump skid',(1.74,2.95,.19),(0,0,.34),m['steel'])
    b('Diesel pump engine',(1.10,1.02,.85),(0,.68,.87),m['dark'],.10)
    b('Engine cover',(.96,.63,.26),(0,.78,1.41),m[a])
    for x in (-.78,.78):
        c('Hose reel flange',.54,.12,(x,-.68,1.05),m[a],'X')
        c('Hose coil',.38,.16,(x*1.06,-.68,1.05),m['dark'],'X')
    b('IBC tank',(1.50,1.12,1.19),(0,-1.31,1.11),m['white'],.08)
    for z in (.70,1.14,1.56): b('Tank cage',(1.63,1.20,.045),(0,-1.31,z),m['steel'],.005)
    c('Intake pipe',.16,.85,(0,1.49,.76),m['steel'],'Y')
    cargo_sockets(4); k.collider('Body',(2.1,3.8,1.85),(0,0,1.03)); finish(r,'Modules/Module_WaterPump')

def station():
    k.reset_scene(); m=materials(); r=k.root('Station_ModuleGantry')
    for x in (-2.52,2.52):
        for y in (-3.30,3.30):
            b('Gantry foot',(.57,.57,.19),(x,y,.11),m['dark'])
            b('Gantry column',(.20,.21,4.10),(x,y,2.15),m['yellow'])
        b('Travel rail',(.20,7.05,.28),(x,0,4.18),m['dark'])
    b('ANIM_Bridge',(5.28,.31,.39),(0,0,4.26),m['yellow'])
    b('ANIM_Trolley',(.82,.80,.34),(0,0,4.04),m['dark'])
    b('ANIM_Hook',(.35,.36,.53),(0,0,3.28),m['steel'])
    for x in (-.9,.9): b('Lift spreader',(.16,3.18,.14),(x,0,3.02),m['yellow'])
    for y in (-1.75,1.75): b('Lift spreader cross rail',(1.92,.13,.13),(0,y,3.02),m['yellow'])
    for x in (-.9,.9):
        for y in (-1.75,1.75): c('Lift hook',.08,.27,(x,y,2.86),m['steel'])
    b('Motor cover',(.54,.62,.46),(.64,0,4.22),m['blue'])
    b('Control cabinet',(.44,.26,1.15),(2.45,-2.55,1.25),m['blue'])
    b('Control face',(.35,.03,.43),(2.45,-2.70,1.39),m['dark'])
    for x, color in [(-.11,'red'),(.11,'yellow')]: c('Control button',.065,.05,(2.45+x,-2.75,1.44),m[color],'Y')
    k.empty('SOCKET_Hook',(0,0,2.72))
    k.collider('ControlPanel',(.55,.5,1.4),(2.45,-2.55,1.12))
    finish(r,'Station/Station_ModuleGantry')

for fn in (truck,module_towing,module_fire,module_cargo,module_tech,module_water,station):
    fn()
