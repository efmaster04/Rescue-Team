"""Blender sources for the portable winch and centrifugal pump, in Unity metres.

Uses the equipment pass's modelling helpers; supplied office/vehicle files are
never opened. Run: blender --background --python this_file.py [-- towing water].
"""
from pathlib import Path
import sys, math, json
sys.path.insert(0, str(Path(__file__).resolve().parent))
import gen_refined_equipment as k
import bpy, bmesh
from mathutils import Vector

ROOT = k.ROOT
SOURCES = ROOT/'Art/Blender/Models/RefinedModules'
OUTPUT = ROOT/'Assets/_Project/Art/BlenderModules/Models'
IDS = ('towing', 'water')
for folder in (SOURCES, OUTPUT): folder.mkdir(parents=True, exist_ok=True)
k.COLORS = {
 'SatinSteel': ((.48,.51,.50),.80,.50),
 'MachinedRecess': ((.075,.092,.095),.45,.71),
 'GraphiteEnamel': ((.080,.102,.104),.23,.62),
 'Rubber': ((.043,.050,.049),0,.82),
 'WireRope': ((.24,.28,.29),.62,.61),
 'EquipmentEnamel': ((.23,.29,.255),.15,.71),
 'CastAlloy': ((.23,.26,.25),.67,.76),
 'SafetyOrange': ((.60,.19,.048),.10,.66),
 'ValveBrass': ((.48,.35,.16),.73,.60),
 'CastGraphite': ((.080,.095,.095),.57,.47), # Preview floor only.
}
box,cylinder,rod,torus,tube,lathe = k.box,k.cylinder,k.rod,k.torus,k.tube,k.lathe

def bolt(name,c,axis=(0,1,0),r=.003):
    c=Vector(c);axis=Vector(axis)
    cylinder(name+' seated washer',c,r*1.4,.0016,'SatinSteel',axis,48,.00035)
    cylinder(name+' hex head',c+axis*.0023,r,.0038,'MachinedRecess',axis,6,.00045)

def pipe(name,c,r,length,axis,mat='SatinSteel',wall=.004):
    return lathe(name,c,[(-length/2,r-wall),(-length/2,r),(-length/2+.002,r+.001),
        (length/2-.002,r+.001),(length/2,r),(length/2,r-wall)],mat,axis,96)

def ring_bolts(name,x,y,z,r,count,axis=(1,0,0)):
    for i in range(count):
        a=2*math.pi*i/count
        if abs(axis[0])>.5:c=(x,y+math.cos(a)*r,z+math.sin(a)*r)
        else:c=(x+math.cos(a)*r,y,z+math.sin(a)*r)
        bolt(name,c,axis,.0028)

def aperture(name,c,w,h,d,iw,ih,mat):
    shell=box(name,c,(w,h,d),mat,min(.010,h*.20),12)
    cutter=box(name+' opening',c,(iw,ih,d*4),mat,min(ih*.4,.014),12)
    k.difference(shell,cutter)

def hollow_elbow():
    # Swept annular section, including the inside bend. No front cap or black disk.
    points=[Vector((.153,.189,-.021)),Vector((.173,.189,-.021))]
    for i in range(1,49):
        a=i*math.pi/96;points.append(Vector((.173+.040*math.sin(a),.189,.019-.040*math.cos(a))))
    points += [Vector((.213,.189,.060)),Vector((.213,.189,.083))]
    vertices=[];faces=[];n=64;count=len(points)
    for i,p in enumerate(points):
        tangent=(points[min(i+1,count-1)]-points[max(0,i-1)]).normalized()
        normal=Vector((0,1,0));second=tangent.cross(normal).normalized()
        for r in (.026,.020):
            for j in range(n):
                a=j*2*math.pi/n;vertices.append(k.B(p+r*(normal*math.cos(a)+second*math.sin(a))))
    for i in range(count-1):
        for j in range(n):
            nj=(j+1)%n
            for offset in (0,n):
                a=i*n*2+offset; b=(i+1)*n*2+offset
                faces.append((a+j,a+nj,b+nj,b+j))
    for end in (0,count-1):
        a=end*n*2
        for j in range(n):nj=(j+1)%n;faces.append((a+j,a+nj,a+n+nj,a+n+j))
    mesh=bpy.data.meshes.new('Hollow intake sweep');mesh.from_pydata(vertices,[],faces);mesh.update()
    bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bm.to_mesh(mesh);bm.free()
    obj=bpy.data.objects.new('Long radius hollow inlet elbow',mesh);bpy.context.collection.objects.link(obj);k.finish(obj,obj.name,'SatinSteel')

def winch():
    y,z=.210,-.025
    # Compact three-stage planetary reducer and separate finned electric motor.
    lathe('Cast planetary housing',(-.202,y,z),[(-.067,0),(-.067,.051),(-.064,.071),(-.057,.084),(-.046,.089),
        (.033,.089),(.043,.078),(.054,.071),(.057,.051),(.057,0)],'CastAlloy',(1,0,0))
    lathe('Planetary end cover',(-.272,y,z),[(-.008,0),(-.008,.052),(-.006,.069),(.003,.074),(.008,.066),(.008,0)],'GraphiteEnamel',(1,0,0))
    torus('Reducer gasket',(-.258,y,z),.079,.0015,'Rubber',(1,0,0),112)
    ring_bolts('Reducer cover fixing',-.278,y,z,.061,8,(-1,0,0))
    for i in range(8):
        a=2*math.pi*i/8
        rod('Reducer reinforcing rib',(-.246,y+math.cos(a)*.087,z+math.sin(a)*.087),
            (-.182,y+math.cos(a)*.087,z+math.sin(a)*.087),.0027,'CastAlloy',48)
    lathe('Motor body',(.203,y,z),[(-.056,0),(-.056,.054),(-.049,.065),(.048,.065),(.061,.056),(.064,.042),(.064,0)],'GraphiteEnamel',(1,0,0))
    for i in range(16):
        a=i*2*math.pi/16
        rod('Extruded motor cooling fin',(.164,y+math.cos(a)*.064,z+math.sin(a)*.064),
            (.244,y+math.cos(a)*.064,z+math.sin(a)*.064),.0023,'CastAlloy',32)
    for x in (.151,.251):torus('Motor end seal',(x,y,z),.062,.0016,'Rubber',(1,0,0),96)
    ring_bolts('Motor cover fixing',.271,y,z,.045,4)
    # Recessed drum cheeks, shaft bearings and a real continuous helical cable.
    cylinder('Drum barrel',(0,y,z),.047,.272,'MachinedRecess',(1,0,0),128,.002)
    for x in (-.133,.133):
        lathe('Rolled drum cheek',(x,y,z),[(-.006,.030),(-.006,.077),(-.003,.083),(.003,.083),(.006,.077),(.006,.030)],'GraphiteEnamel',(1,0,0))
        cylinder('Sealed drum bearing',(x*1.04,y,z),.031,.020,'CastAlloy',(1,0,0),96,.002)
    pts=[]
    turns=31
    for i in range(turns*64+1):
        t=i/(turns*64);a=t*turns*2*math.pi
        pts.append((-.122+t*.244,y+math.cos(a)*.059,z+math.sin(a)*.059))
    tube('Continuous steel wire winding',pts,.0038,'WireRope',False,3)
    for x in (-.202,.202):
        for zz in (-.082,.032):
            k.profile('Cast mounting foot',[(-.036,.081),(.036,.081),(.032,.108),(.019,.145),(-.019,.145),(-.032,.108)],.034,'CastAlloy','xy',(x,0,zz),.003)
            bolt('Mounting foot bolt',(x,.087,zz+.021),(0,1,0),.004)
    for yy,zz in ((.286,.018),(.142,-.081)):
        rod('Tie rod',(-.160,yy,zz),(.158,yy,zz),.0045,'SatinSteel')
    # Raised sealed solenoid enclosure with moulded cable glands.
    for x in (-.091,.091):box('Relay support bracket',(x,.302,-.056),(.023,.044,.028),'CastAlloy',.004)
    box('Rounded sealed relay enclosure',(0,.333,-.040),(.222,.053,.091),'Rubber',.014,12)
    box('Solenoid lid seam',(0,.359,-.040),(.201,.004,.073),'GraphiteEnamel',.008,10)
    for x in (-.079,.079):bolt('Relay captive screw',(x,.363,-.040),r=.0024)
    for x in (-.065,.065):
        cylinder('Cable gland',(x,.314,-.091),.009,.020,'MachinedRecess',(0,0,1),64,.002)
        tube('Motor power lead',[(x,.314,-.100),(x,.283,-.119),(.217 if x>0 else .181,.275,-.115),(.220,.243,-.073)],.004,'Rubber',sides=4)
    cylinder('Clutch spindle',(-.216,.312,-.025),.010,.027,'SatinSteel',segments=80,bevel=.002)
    box('Clutch selector lever',(-.184,.326,-.025),(.080,.012,.018),'CastAlloy',.006,10)
    box('Clutch rubber thumb pad',(-.147,.326,-.025),(.025,.017,.028),'SafetyOrange',.007,10)
    # Chamfered hawse plate, real opening and steel lip; no opaque black insert.
    aperture('Rounded mounting fairlead plate',(0,.174,.139),.314,.120,.016,.237,.043,'GraphiteEnamel')
    aperture('Polished hawse fairlead',(0,.184,.155),.268,.073,.025,.220,.040,'SatinSteel')
    for x in (-.140,.140):bolt('Fairlead mounting bolt',(x,.156,.151),(0,0,1),.004)
    for x in (-.130,.130):
        k.profile('Triangular fairlead support',[(-.080,.081),(.148,.081),(.148,.224),(.125,.242)],.012,'GraphiteEnamel','yz',(x,0,0),.0025)
    tube('Free cable and hook eye',[(.012,.181,.046),(.012,.177,.140),(.024,.171,.183),(.043,.168,.198)],.0038,'WireRope',sides=4)
    torus('Forged hook eye',(.044,.159,.198),.009,.0035,'SatinSteel',(0,0,1),96)
    hook=[(.044,.151,.201),(.050,.140,.201),(.058,.126,.201),(.056,.111,.201),(.046,.103,.201),(.032,.103,.201),(.023,.113,.201),(.022,.123,.201)]
    tube('Continuous forged recovery hook',hook,.0058,'SafetyOrange',sides=6)
    rod('Spring loaded hook safety latch',(.023,.123,.202),(.045,.143,.202),.0017,'SatinSteel',48)
    cylinder('Hook latch pivot',(.044,.141,.205),.003,.010,'MachinedRecess',(0,0,1),48,.0006)

def camlock(c,axis,r=.027):
    c=Vector(c);axis=Vector(axis)
    lathe('Hollow camlock receiver',c,[(-.022,r-.006),(-.022,r),(-.005,r),(-.002,r+.005),
        (.013,r+.005),(.019,r),(.019,r-.005)],'SatinSteel',axis,128)
    torus('Camlock sealing gasket',c+axis*.013,r-.003,.002,'Rubber',axis,96)
    side=Vector((1,0,0))
    if abs(axis.x)>.5:side=Vector((0,1,0))
    for sign in (-1,1):
        p=c+side*sign*(r+.003)
        cylinder('Cam arm pivot',p,.004,.014,'ValveBrass',side,48,.0006)
        tube('Curved cam locking arm',[p,p+side*sign*.010-axis*.008,p+side*sign*.013-axis*.026,p+side*sign*.008-axis*.035],.003,'CastAlloy',sides=4)
        torus('Cam arm retaining ring',p+side*sign*.007-axis*.027,.006,.001,'SatinSteel',(0,0,1),48)

def pump():
    y,z=.189,-.021
    # Cast motor, curved aluminium fan cover and integral axial cooling fins.
    lathe('Electric motor cast body',(-.118,y,z),[(-.105,0),(-.105,.050),(-.095,.061),(.080,.061),(.092,.052),(.096,.041),(.096,0)],'EquipmentEnamel',(1,0,0))
    for i in range(20):
        a=i*math.pi/10
        rod('Integral curved cooling fin',(-.200,y+math.cos(a)*.063,z+math.sin(a)*.063),(-.045,y+math.cos(a)*.063,z+math.sin(a)*.063),.0026,'CastAlloy',32)
    lathe('Ventilated fan cowl',(-.233,y,z),[(-.014,0),(-.014,.041),(-.012,.052),(-.004,.060),(.010,.060),(.015,.053),(.015,0)],'GraphiteEnamel',(1,0,0))
    for i in range(12):
        a=i*math.pi/6
        rod('Fan cowl radial vent',(-.248,y+math.cos(a)*.021,z+math.sin(a)*.021),(-.248,y+math.cos(a)*.046,z+math.sin(a)*.046),.002,'MachinedRecess',24)
    for x in (-.206,-.035):torus('Motor end casting seam',(x,y,z),.060,.0015,'MachinedRecess',(1,0,0),96)
    for i in range(4):
        a=(i+.5)*math.pi/2
        rod('Motor through bolt',(-.213,y+math.cos(a)*.054,z+math.sin(a)*.054),(-.022,y+math.cos(a)*.054,z+math.sin(a)*.054),.002,'SatinSteel',32)
    cylinder('Shaft coupling guard',(.016,y,z),.034,.085,'GraphiteEnamel',(1,0,0),96,.004)
    for x in (-.021,.052):torus('Coupling guard clamp',(x,y,z),.034,.002,'SatinSteel',(1,0,0),96)
    # Smooth volute casing with machined split flange and seated fasteners.
    lathe('Centrifugal volute casting',(.111,y,z),[(-.057,0),(-.057,.040),(-.047,.065),(-.029,.084),(-.010,.091),
        (.016,.091),(.035,.077),(.046,.052),(.049,.026),(.049,0)],'CastAlloy',(1,0,0),128)
    torus('Machined volute split flange',(.132,y,z),.084,.003,'SatinSteel',(1,0,0),128)
    ring_bolts('Pump casing bolt',.145,y,z,.074,8)
    for i in range(6):
        a=i*math.pi/3
        rod('Cast radial strengthening rib',(.082,y+math.cos(a)*.080,z+math.sin(a)*.080),(.130,y+math.cos(a)*.075,z+math.sin(a)*.075),.003,'CastAlloy',48)
    # Hollow long-radius intake elbow. The inner sweep is real geometry.
    # A lathed annulus at the visible end joins the short continuous metal elbow.
    hollow_elbow()
    pipe('Open intake throat',(.213,y,.091),.026,.048,(0,0,1),wall=.005)
    camlock((.213,y,.128),(0,0,1),.027)
    pipe('Tangential outlet neck',(.110,.290,-.021),.025,.060,(0,1,0),wall=.005)
    camlock((.110,.325,-.021),(0,1,0),.026)
    cylinder('Priming plug seat',(.119,.273,.025),.013,.009,'CastAlloy',segments=64,bevel=.002)
    cylinder('Brass priming plug',(.119,.282,.025),.009,.013,'ValveBrass',segments=6,bevel=.001)
    cylinder('Volute drain plug',(.160,.129,-.021),.006,.012,'ValveBrass',(1,0,0),6,.001)
    # Sealed motor terminal box, real glands and tied-down power cable.
    box('Sealed terminal box',(-.114,.274,-.021),(.093,.043,.079),'EquipmentEnamel',.010,12)
    box('Terminal box lid seam',(-.114,.296,-.021),(.084,.005,.071),'GraphiteEnamel',.007,10)
    for x in (-.142,-.086):bolt('Terminal lid screw',(x,.301,-.021),r=.0025)
    cylinder('Power cable gland',(-.143,.270,-.066),.008,.023,'MachinedRecess',(0,0,1),64,.002)
    tube('Tied motor power cable',[(-.143,.270,-.078),(-.165,.269,-.108),(-.225,.245,-.143),(-.257,.271,-.102),(-.266,.270,.150)],.004,'Rubber',sides=4)
    # Motor and pump feet seat directly on the unchanged common tray.
    for x in (-.140,.110):
        box('Cast mounting shoe',(x,.088,-.021),(.078,.018,.146),'GraphiteEnamel',.004)
        for zz in (-.071,.029):
            k.profile('Angled mounting pedestal',[(-.028,.091),(.028,.091),(.021,.142),(-.021,.142)],.023,'CastAlloy','xy',(x,0,zz),.004)
            bolt('Mounting shoe bolt',(x,.100,zz),r=.0035)
    # Continuous bent tube frame, with horizontal attached rubber grips.
    for x in (-.266,.266):
        pts=[(x,.087,-.129),(x,.308,-.129)]
        for i in range(25):
            a=i*math.pi/48;pts.append((x,.308+.025*math.sin(a),-.104-.025*math.cos(a)))
        pts.append((x,.333,.104))
        for i in range(25):
            a=i*math.pi/48;pts.append((x,.308+.025*math.cos(a),.104+.025*math.sin(a)))
        pts.append((x,.087,.129));tube('Continuous mandrel bent frame',pts,.0085,'GraphiteEnamel',False,6)
        cylinder('Bonded carrying sleeve',(x,.333,0),.012,.123,'Rubber',(0,0,1),96,.004)
        for zz in (-.059,.059):torus('Grip retaining collar',(x,.333,zz),.011,.0015,'Rubber',(0,0,1),80)
    for zz in (-.129,.129):rod('Frame cross rail',(-.266,.086,zz),(.266,.086,zz),.0085,'GraphiteEnamel')

def run(id):
    bpy.ops.wm.read_factory_settings(use_empty=True);k.source=[];k.materials={}
    bpy.context.scene.unit_settings.system='METRIC'
    {'towing':winch,'water':pump}[id]();bpy.context.view_layer.update()
    bounds=[o.matrix_world@Vector(p) for o in k.source for p in o.bound_box]
    low=[min(p[i] for p in bounds) for i in range(3)];high=[max(p[i] for p in bounds) for i in range(3)]
    if low[0]<-.322 or high[0]>.322 or low[1]<-.222 or high[1]>.231 or low[2]<.072 or high[2]>.441:
        raise RuntimeError(f'{id} outside existing device envelope: {low}..{high}')
    lods=k.make_lods(id)
    # Deterministic FBX surface parity: bake triangulation and remove zero-area
    # Boolean slivers before export, rather than relying on importer heuristics.
    for obj in lods:
        bm=bmesh.new();bm.from_mesh(obj.data);bmesh.ops.triangulate(bm,faces=list(bm.faces))
        degenerate=[f for f in bm.faces if f.calc_area()<1e-14]
        if degenerate:bmesh.ops.delete(bm,geom=degenerate,context='FACES_ONLY')
        unused=[v for v in bm.verts if not v.link_faces]
        if unused:bmesh.ops.delete(bm,geom=unused,context='VERTS')
        bm.to_mesh(obj.data);bm.free();obj.data.update()
    k.activate(lods[0]);bpy.ops.object.select_all(action='DESELECT')
    for o in lods:o.select_set(True)
    bpy.context.view_layer.objects.active=lods[0]
    bpy.ops.export_scene.fbx(filepath=str(OUTPUT/f'{id}.fbx'),use_selection=True,object_types={'MESH'},axis_forward='Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',use_space_transform=True,bake_space_transform=True,use_mesh_modifiers=True,mesh_smooth_type='FACE',use_triangles=True,add_leaf_bones=False,bake_anim=False,path_mode='AUTO')
    info={'id':id,'lods':[{'name':o.name,'triangles':k.tri_count(o),'materials':[m.name for m in o.data.materials]} for o in lods],
        'blend':f'Art/Blender/Models/RefinedModules/{id}.blend','bounds_blender':{'min':low,'max':high}}
    for o in lods:o.hide_render=True;o.hide_set(True)
    k.studio(id);bpy.context.scene.render.filepath=str(ROOT/'Documentation'/f'BlenderModuleStudio_{id}.png')
    k.activate(k.source[0]);bpy.ops.wm.save_as_mainfile(filepath=str(SOURCES/f'{id}.blend'))
    print('MODULE_RESULT '+json.dumps(info),flush=True);return info

if __name__=='__main__':
    chosen=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else IDS
    manifest=OUTPUT.parent/'module_manifest.json'
    existing=json.loads(manifest.read_text()) if manifest.exists() else {'version':1,'blender':'5.2','models':[]}
    lookup={m['id']:m for m in existing['models']}
    for id in chosen:
        lookup[id]=run(id);existing['models']=[lookup[key] for key in IDS if key in lookup]
        manifest.write_text(json.dumps(existing,indent=2)+'\n',encoding='utf-8')
