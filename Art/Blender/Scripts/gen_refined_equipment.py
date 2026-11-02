"""Editable Blender hard-surface sources and three FBX LODs for portable equipment.

Run with Blender 5.2 --background --python this_file.py. Never opens or changes the
supplied office or vehicle sources. Inputs below use Unity metres (X, up Y, front Z).
"""
from pathlib import Path
import bpy, bmesh, math, json, sys
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[3]
SOURCES = ROOT/'Art/Blender/Models/RefinedEquipment'
OUTPUT = ROOT/'Assets/_Project/Art/BlenderEquipment/Models'
PREVIEWS = ROOT/'Documentation'
for folder in (SOURCES, OUTPUT, PREVIEWS): folder.mkdir(parents=True, exist_ok=True)
IDS = ['toolbox','case','parts','bin','fuel','grease','absorbent','jack']
SIZES = dict(zip(IDS,[(.46,.32,.33),(.55,.30,.34),(.52,.48,.52),(.52,.32,.40),(.32,.48,.32),(.25,.23,.25),(.38,.42,.38),(.66,.41,1.03)]))
COLORS = {'BrushedAlloy':((.49,.52,.51),.78,.31),'CastGraphite':((.080,.095,.095),.57,.47),
 'Rubber':((.032,.037,.036),0,.72),'PressedRed':((.40,.075,.046),.22,.38),
 'ServiceOlive':((.25,.29,.20),.16,.42),'MouldedGraphite':((.11,.14,.145),.04,.53),
 'BucketIvory':((.67,.64,.53),0,.43),'SlateBlue':((.13,.20,.235),.06,.49),
 'BrassFitting':((.46,.33,.14),.74,.32)}
materials={}; source=[]
def B(v): return Vector((v[0],v[2],v[1]))
def activate(obj):
    bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True); bpy.context.view_layer.objects.active=obj
def material(name):
    if name in materials: return materials[name]
    color,metal,rough=COLORS[name];m=bpy.data.materials.new(name);m.use_nodes=True
    p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*color,1);p.inputs['Metallic'].default_value=metal;p.inputs['Roughness'].default_value=rough
    m.diffuse_color=(*color,1);materials[name]=m;return m
def finish(obj,name,mat,bevel=0,segments=8):
    obj.name=name;obj.data.materials.clear();obj.data.materials.append(material(mat))
    for p in obj.data.polygons:p.use_smooth=True
    if bevel:
        mod=obj.modifiers.new('Machined edge radii','BEVEL');mod.width=bevel;mod.segments=segments;mod.limit_method='ANGLE';mod.angle_limit=math.radians(28);mod.harden_normals=True
    n=obj.modifiers.new('Weighted corner normals','WEIGHTED_NORMAL');n.keep_sharp=True;n.weight=70
    source.append(obj);return obj
def box(name,c,size,mat,bevel=.004,segments=8):
    bpy.ops.mesh.primitive_cube_add(size=1,location=B(c));o=bpy.context.object;o.dimensions=B(size);activate(o);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    return finish(o,name,mat,bevel,segments)
def cylinder(name,c,r,length,mat,axis=(0,1,0),segments=96,bevel=.0015):
    bpy.ops.mesh.primitive_cylinder_add(vertices=segments,radius=r,depth=length,location=B(c));o=bpy.context.object;o.rotation_mode='QUATERNION';o.rotation_quaternion=B(axis).normalized().to_track_quat('Z','Y');activate(o);bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    return finish(o,name,mat,bevel,6)
def rod(name,a,b,r,mat,segments=64):
    a,b=Vector(a),Vector(b);return cylinder(name,(a+b)*.5,r,(b-a).length,mat,b-a,segments,min(r*.18,.001))
def torus(name,c,r,t,mat,axis=(0,1,0),segments=80):
    bpy.ops.mesh.primitive_torus_add(major_segments=segments,minor_segments=16,location=B(c),major_radius=r,minor_radius=t);o=bpy.context.object;o.rotation_mode='QUATERNION';o.rotation_quaternion=B(axis).normalized().to_track_quat('Z','Y');activate(o);bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    return finish(o,name,mat)
def tube(name,points,r,mat,bezier=True,sides=5):
    data=bpy.data.curves.new(name,'CURVE');data.dimensions='3D';data.resolution_u=16;data.bevel_depth=r;data.bevel_resolution=sides;data.use_fill_caps=True
    spline=data.splines.new('BEZIER' if bezier else 'POLY')
    if bezier:
        spline.bezier_points.add(len(points)-1)
        for p,co in zip(spline.bezier_points,points):p.co=B(co);p.handle_left_type='AUTO';p.handle_right_type='AUTO'
    else:
        spline.points.add(len(points)-1)
        for p,co in zip(spline.points,points):p.co=(*B(co),1)
    o=bpy.data.objects.new(name,data);bpy.context.collection.objects.link(o);activate(o);bpy.ops.object.convert(target='MESH');return finish(bpy.context.object,name,mat)
def profile(name,coords,depth,mat,plane='xy',c=(0,0,0),bevel=.004):
    verts=[]
    for side in (-.5,.5):
        for a,b in coords:
            p=(a,b,side*depth) if plane=='xy' else (side*depth,b,a)
            verts.append(B(Vector(p)+Vector(c)))
    count=len(coords);faces=[tuple(range(count-1,-1,-1)),tuple(range(count,count*2))]
    faces += [(i,(i+1)%count,(i+1)%count+count,i+count) for i in range(count)]
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update();bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bm.to_mesh(mesh);bm.free()
    o=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(o);return finish(o,name,mat,bevel)
def lathe(name,c,section,mat,axis=(0,1,0),segments=128):
    verts=[]
    rotation=B(axis).normalized().to_track_quat('Z','Y')
    for h,r in section:
        for j in range(segments):
            a=2*math.pi*j/segments;verts.append(B(c)+rotation@Vector((r*math.cos(a),r*math.sin(a),h)))
    faces=[]
    for i in range(len(section)):
        ni=(i+1)%len(section)
        for j in range(segments):nj=(j+1)%segments;faces.append((i*segments+j,i*segments+nj,ni*segments+nj,ni*segments+j))
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update();bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.remove_doubles(bm,verts=bm.verts,dist=1e-7);bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bm.to_mesh(mesh);bm.free()
    o=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(o);return finish(o,name,mat,.0008,4)
def difference(target,cutter):
    activate(cutter)
    for m in list(cutter.modifiers):bpy.ops.object.modifier_apply(modifier=m.name)
    activate(target)
    # Bake the broad outer fillets first. Otherwise tiny pocket edges globally
    # clamp the large shell bevel, leaving a misleadingly sharp main silhouette.
    for existing in list(target.modifiers):bpy.ops.object.modifier_apply(modifier=existing.name)
    m=target.modifiers.new('Formed pocket','BOOLEAN');m.operation='DIFFERENCE';m.solver='EXACT';m.object=cutter
    bpy.ops.object.modifier_apply(modifier=m.name);source.remove(cutter);bpy.data.objects.remove(cutter,do_unlink=True)
    edge=target.modifiers.new('Pocket edge radius','BEVEL');edge.width=.0012;edge.segments=5;edge.limit_method='ANGLE';edge.angle_limit=math.radians(35);edge.harden_normals=True
    normal=target.modifiers.new('Pocket weighted normals','WEIGHTED_NORMAL');normal.keep_sharp=True;normal.weight=70
def handle_ring(name,c,w,h,d,mat,plane='xy'):
    outer=box(name,c,(w,h,d) if plane=='xy' else (d,h,w),mat,min(.006,d*.36),10)
    cutter=box(name+' bore',c,(w-.027,h-.023,d*4) if plane=='xy' else (d*4,h-.023,w-.027),mat,min(.010,(h-.023)*.4),10)
    difference(outer,cutter);return outer
def bolt(name,c,axis=(0,0,1),r=.003):
    cylinder(name+' washer',c,r*1.35,.0015,'BrushedAlloy',axis,48,.0004)
    cylinder(name+' head',Vector(c)+Vector(axis)*.002,r,.0035,'CastGraphite',axis,6,.00035)
def latch(x,y,z,scale=1):
    box('Latch recessed seat',(x,y,z),(.031*scale,.047*scale,.008*scale),'CastGraphite',.003)
    box('Bent stainless latch',(x,y-.003*scale,z+.005*scale),(.024*scale,.033*scale,.006*scale),'BrushedAlloy',.002,10)
    cylinder('Latch hinge',(x,y+.017*scale,z+.007*scale),.0038*scale,.027*scale,'BrushedAlloy',(1,0,0),64,.0008)
    box('Latch grip',(x,y-.008*scale,z+.009*scale),(.014*scale,.014*scale,.003*scale),'Rubber',.001)
def carry_handle(c,w=.17,rise=.022):
    c=Vector(c)
    for sign in (-1,1):box('Handle pivot',c+Vector((sign*w*.5,0,0)),(.027,.013,.034),'CastGraphite',.004)
    bend=min(.016,rise*.65);points=[c+Vector((-w*.5,0,0))]
    for i in range(25):
        a=i*math.pi/48;points.append(c+Vector((-w*.5+bend-bend*math.cos(a),rise-bend+bend*math.sin(a),0)))
    for i in range(25):
        a=i*math.pi/48;points.append(c+Vector((w*.5-bend+bend*math.sin(a),rise-bend+bend*math.cos(a),0)))
    points.append(c+Vector((w*.5,0,0)));tube('Bent carrying bail',points,.005,'BrushedAlloy',False,6)
    cylinder('Moulded carrying grip',c+Vector((0,rise+.001,0)),.009,w*.66,'Rubber',(1,0,0),96,.002)
def toolbox():
    body=box('Pressed steel lower shell',(0,.103,0),(.434,.176,.288),'PressedRed',.012,12)
    difference(body,box('Front shallow pressing',(0,.107,.150),(.370,.112,.022),'PressedRed',.009,10))
    box('Folded lower perimeter',(0,.022,0),(.443,.026,.297),'CastGraphite',.009,10)
    box('Continuous lid seal',(0,.192,0),(.442,.006,.298),'Rubber',.003)
    coords=[(-.148,.197),(.148,.197),(.148,.221)]+[(.148*math.cos(a),.221+.050*math.sin(a)) for a in [math.pi*i/48 for i in range(1,49)]]
    profile('Rolled curved steel lid',coords,.442,'PressedRed','yz',bevel=.004)
    for x in (-.140,.140):
        latch(x,.188,.151)
        cylinder('Rolled lid hinge',(x,.195,-.151),.004,.062,'BrushedAlloy',(1,0,0),64,.0008)
        for xx in (x-.020,x+.020):bolt('Hinge rivet',(xx,.176,-.146),(0,0,-1),.0022)
    for sign in (-1,1):
        handle_ring('Recessed end grip',(sign*.222,.129,0),.116,.042,.007,'CastGraphite','yz')
        for z in (-.115,.115):box('Rubber standing pad',(sign*.177,.009,z),(.039,.018,.039),'Rubber',.005)
    carry_handle((0,.276,0),.165,.019)
def case():
    body=box('Moulded lower case',(0,.111,0),(.515,.204,.306),'MouldedGraphite',.021,12)
    for side in (-1,1):difference(body,box('Side carrying recess',(side*.258,.142,0),(.026,.054,.135),'MouldedGraphite',.010,10))
    box('Elastomer case gasket',(0,.216,0),(.524,.008,.312),'Rubber',.012,10)
    lid=box('Domed impact lid',(0,.245,0),(.530,.049,.318),'MouldedGraphite',.017,12)
    difference(lid,box('Lid inset',(0,.273,0),(.432,.017,.229),'MouldedGraphite',.008,8))
    for x in (-.205,-.108,.108,.205):
        box('Moulded face rib',(x,.116,.154),(.017,.151,.012),'MouldedGraphite',.005,10)
        box('Lid stacking rib',(x,.271,0),(.018,.010,.229),'MouldedGraphite',.004)
    for x in (-.159,.159):
        latch(x,.217,.158,1.08);cylinder('Case hinge knuckle',(x,.218,-.162),.0055,.072,'CastGraphite',(1,0,0),64,.001)
    for side in (-1,1):
        for z in (-.119,.119):
            box('Lower impact bumper',(side*.246,.058,z),(.043,.097,.052),'Rubber',.010,12)
            box('Lid corner bumper',(side*.246,.246,z),(.044,.042,.050),'Rubber',.008,10)
    for x in (-.074,.074):box('Folded handle attachment',(x,.277,0),(.026,.013,.032),'CastGraphite',.004)
    box('Flush carrying handle',(0,.288,0),(.167,.017,.028),'Rubber',.008,12)
    cylinder('Pressure release rim',(.214,.154,.158),.011,.007,'CastGraphite',(0,0,1),80,.0015)
    cylinder('Pressure release centre',(.214,.154,.163),.007,.003,'Rubber',(0,0,1),64,.0005)
def rounded_loop(w,d,r,n=10):
    points=[]
    for corner in range(4):
        cx=(1 if corner in (0,3) else -1)*(w*.5-r);cz=(1 if corner<2 else -1)*(d*.5-r)
        for i in range(n):a=(corner*90+i*90/(n-1))*math.pi/180;points.append((cx+math.cos(a)*r,cz+math.sin(a)*r))
    return points
def loft(name,levels,mat,r=.020,bevel=.001):
    verts=[];n=40
    for w,y,d in levels:verts += [B((x,y,z)) for x,z in rounded_loop(w,d,r)]
    faces=[tuple(range(n-1,-1,-1)),tuple(range((len(levels)-1)*n,len(levels)*n))]
    for k in range(len(levels)-1):
        for i in range(n):j=(i+1)%n;faces.append((k*n+i,k*n+j,(k+1)*n+j,(k+1)*n+i))
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update();bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bm.to_mesh(mesh);bm.free()
    o=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(o);return finish(o,name,mat,bevel)
def parts():
    box('Crate reinforced floor',(0,.023,0),(.480,.035,.480),'MouldedGraphite',.009)
    for side in (-1,1):
        for a in (-.218,-.109,0,.109,.218):
            box('Vertical front rib',(a,.217,side*.235),(.025,.351,.020),'SlateBlue',.005)
            box('Vertical end rib',(side*.235,.217,a),(.020,.351,.025),'SlateBlue',.005)
        for y in (.079,.186,.293):
            box('Horizontal front rib',(0,y,side*.236),(.471,.022,.022),'SlateBlue',.005)
            box('Horizontal side rib',(side*.236,y,0),(.022,.022,.471),'SlateBlue',.005)
        handle_ring('Front hand aperture',(0,.392,side*.238),.483,.079,.017,'MouldedGraphite')
        handle_ring('End hand aperture',(side*.238,.392,0),.452,.079,.017,'MouldedGraphite','yz')
        for z in (-.229,.229):
            box('Moulded corner collar',(side*.229,.392,z),(.036,.082,.036),'MouldedGraphite',.006)
            box('Crate standing pad',(side*.211,.010,z*.92),(.055,.020,.055),'Rubber',.005)
    for x in (-.112,.096):
        lathe('Spare machined hub',(x,.122,-.035),[(-.067,0),(-.067,.047),(-.060,.059),(-.015,.059),(-.010,.044),(.010,.044),(.012,.025),(.069,.025),(.074,.018),(.074,0)],'BrushedAlloy')
        torus('Hub sealing ring',(x,.143,-.035),.043,.003,'Rubber')
        for i in range(4):a=i*math.pi/2;bolt('Hub flange bolt',(x+math.cos(a)*.047,.115,-.035+math.sin(a)*.047),(0,1,0),.003)
    torus('Replacement drive belt',(-.025,.221,-.035),.079,.008,'Rubber')
    box('Paper wrapped spare',(0,.090,.132),(.267,.088,.099),'BucketIvory',.007)
    box('Packing band',(0,.136,.132),(.035,.004,.100),'CastGraphite',.001)
def bin():
    body=loft('Tapered injection moulded body',[(.433,.009,.321),(.443,.040,.330),(.497,.261,.376)],'SlateBlue',.025)
    for side in (-1,1):difference(body,box('Recessed lifting pocket',(side*.247,.226,0),(.031,.051,.143),'SlateBlue',.010))
    box('Lid rim',(0,.264,0),(.507,.014,.383),'MouldedGraphite',.010)
    lid=box('Stacking lid',(0,.284,0),(.512,.031,.390),'MouldedGraphite',.013,12)
    difference(lid,box('Top recessed field',(0,.303,0),(.447,.013,.313),'MouldedGraphite',.006))
    for x in (-.18,-.09,0,.09,.18):box('Lid reinforcement',(x,.303,0),(.031,.011,.302),'MouldedGraphite',.005)
    for side in (-1,1):
        for x in (-.17,-.085,0,.085,.17):rod('Wall moulding',(x,.065,side*.169),(x,.250,side*.188),.0045,'SlateBlue',48)
        box('Over-centre lid catch',(side*.244,.282,0),(.021,.023,.109),'ServiceOlive',.006)
def fuel():
    tank=profile('Rounded pressed jerrycan shell',[(-.139,.020),(.139,.020),(.148,.051),(.148,.334),(.105,.391),(-.109,.391),(-.148,.334),(-.148,.051)],.216,'ServiceOlive',bevel=.010)
    for side in (-1,1):
        box('Pressed reinforcement field',(0,.202,side*.107),(.245,.260,.008),'ServiceOlive',.010)
        for angle in (-37,37):
            o=box('Embossed diagonal pressing',(0,.202,side*.113),(.014,.274,.007),'ServiceOlive',.0035,10);o.rotation_mode='QUATERNION';o.rotation_quaternion=Vector((0,1,0)).rotation_difference(Vector((0,1,0)))
            # Unity XY rotation becomes Blender rotation about its depth/Y axis.
            o.rotation_mode='XYZ';o.rotation_euler[1]=-math.radians(angle)
        box('Pressing intersection',(0,.202,side*.115),(.045,.051,.008),'ServiceOlive',.009)
    box('Folded base seam',(0,.020,0),(.302,.025,.227),'ServiceOlive',.007,10)
    for z in (-.058,0,.058):tube('Continuous welded carrying rail',[(-.035,.388,z),(-.035,.433,z),(-.020,.450,z),(.101,.450,z),(.117,.432,z),(.117,.388,z)],.010,'ServiceOlive',sides=6)
    axis=(-.20,.98,0);c=(-.101,.413,0)
    lathe('Filler neck',c,[(-.019,0),(-.019,.022),(.007,.027),(.012,.030),(.020,.030),(.024,.024),(.024,0)],'CastGraphite',axis)
    box('Hinged cap locking bridge',(-.103,.448,0),(.053,.010,.020),'BrushedAlloy',.003)
    for x in (-.121,-.085):bolt('Cap locking pin',(x,.454,0),(0,1,0),.0025)
    box('Retaining chain anchor',(-.120,.383,.030),(.012,.025,.014),'ServiceOlive',.004)
    torus('Chain anchor eye',(-.120,.399,.030),.004,.001,'BrushedAlloy',(1,0,0),40)
    for i in range(8):torus('Cap retaining chain',(-.122,.433-i*.0045,.022+i*.001),.0032,.0008,'BrushedAlloy',(1,0,0) if i%2 else (0,0,1),32)
def grease():
    lathe('Rolled tin body',(0,.110,0),[(-.104,0),(-.104,.101),(-.100,.108),(-.091,.110),(-.087,.106),(.084,.106),(.090,.111),(.100,.111),(.103,.103),(.103,0)],'BucketIvory')
    lathe('Printed stock band',(0,.107,0),[(-.045,.106),(-.045,.1072),(.045,.1072),(.045,.106)],'SlateBlue')
    for y in (.014,.206):torus('Rolled metal edge',(0,y,0),.108,.0028,'BrushedAlloy')
    cylinder('Pressed metal lid',(0,.215,0),.101,.005,'BrushedAlloy',segments=128,bevel=.001)
    torus('Lid circular pressing',(0,.219,0),.085,.0013,'CastGraphite')
    tab=handle_ring('Rounded pull tab',(0,.220,.010),.052,.027,.003,'CastGraphite');tab.rotation_euler[0]=math.pi/2
def absorbent():
    lathe('Tapered pail wall',(0,.185,0),[(-.178,0),(-.178,.135),(-.173,.143),(-.157,.145),(.127,.167),(.138,.174),(.169,.174),(.174,.165),(.174,0)],'BucketIvory')
    for y in (.319,.339,.356):torus('Injection moulded rim',(0,y,0),.172,.003,'BucketIvory')
    cylinder('Sealed lid',(0,.367,0),.177,.022,'ServiceOlive',segments=128,bevel=.004)
    cylinder('Lid inner pressing',(0,.380,0),.160,.005,'ServiceOlive',segments=128,bevel=.002)
    torus('Lid gasket',(0,.378,0),.173,.0024,'CastGraphite')
    for sign in (-1,1):
        box('Bail pivot boss',(sign*.168,.283,0),(.016,.035,.035),'BucketIvory',.006)
        bolt('Bail hinge',(sign*.179,.283,0),(sign,0,0),.004)
    def bail_point(a):return (-.178*math.cos(a),.283-.050*math.sin(a),.164*math.sin(a))
    tube('Wire carrying bail',[bail_point(i*math.pi/128) for i in range(129)],.0027,'BrushedAlloy',False,6)
    start=math.acos(.055/.178);end=math.pi-start
    tube('Bail comfort sleeve',[bail_point(start+(end-start)*i/40) for i in range(41)],.0065,'Rubber',False,6)
def jack():
    coords=[(-.430,.056),(.430,.056),(.430,.105),(.346,.145),(.090,.156),(-.085,.178),(-.342,.129),(-.430,.105)]
    for x in (-.213,.213):
        profile('Formed curved side chassis',[(-z,y) for z,y in coords],.024,'PressedRed','yz',(x,0,0),.005)
        for z in (-.375,.334):bolt('Chassis axle retainer',(x+math.copysign(.016,x),.100,z),(math.copysign(1,x),0,0),.006)
    box('Lower hydraulic tray',(0,.068,-.110),(.422,.018,.575),'CastGraphite',.006)
    rod('Front through axle',(-.268,.074,.334),(.268,.074,.334),.014,'BrushedAlloy')
    for sign in (-1,1):
        lathe('Radiused front roller',(sign*.265,.074,.334),[(-.041,.012),(-.041,.056),(-.036,.067),(-.027,.073),(.027,.073),(.036,.067),(.041,.056),(.041,.012)],'CastGraphite',(1,0,0))
        cylinder('Axle bearing cap',(sign*.309,.074,.334),.030,.007,'BrushedAlloy',(1,0,0),96,.002)
        bolt('Axle fixing',(sign*.315,.074,.334),(sign,0,0),.006)
        cylinder('Swivel bearing',(sign*.233,.103,-.366),.026,.015,'BrushedAlloy',segments=96,bevel=.002)
        for off in (-.025,.025):box('Rear caster fork',(sign*.233+off,.064,-.383),(.010,.060,.055),'CastGraphite',.004)
        lathe('Round rear caster tyre',(sign*.233,.046,-.392),[(-.020,.012),(-.020,.035),(-.014,.045),(.014,.045),(.020,.035),(.020,.012)],'Rubber',(1,0,0),96)
        cylinder('Caster hub',(sign*.233,.046,-.392),.016,.060,'BrushedAlloy',(1,0,0),64,.002)
    rod('Lift arm pivot shaft',(-.232,.156,-.086),(.232,.156,-.086),.017,'BrushedAlloy')
    arm=[(-.302,.201),(-.259,.157),(.086,.127),(.109,.155),(.065,.183),(-.227,.245),(-.302,.245)]
    for x in (-.080,.080):
        profile('Cast lifting link',[(-z,y) for z,y in arm],.034,'PressedRed','yz',(x,0,0),.006)
        for z,y in ((-.075,.156),(.260,.218)):cylinder('Machined link boss',(x,y,z),.023,.043,'BrushedAlloy',(1,0,0),80,.002)
    rod('Saddle cross pin',(-.103,.218,.263),(.103,.218,.263),.014,'BrushedAlloy')
    cylinder('Machined lifting saddle',(0,.254,.263),.082,.027,'BrushedAlloy',segments=128,bevel=.004)
    lathe('Concentrically grooved rubber saddle',(0,.274,.263),[(-.008,0),(-.008,.079),(-.006,.085),(-.002,.088),(.005,.088),(.008,.082),(.008,.067),(.004,.064),(.004,.061),(.008,.058),(.008,.037),(.004,.034),(.004,.030),(.008,.027),(.008,0)],'Rubber')
    start=Vector((0,.112,-.295));end=Vector((0,.192,.130));d=end-start
    rod('Hydraulic cylinder',start,start+d*.58,.029,'CastGraphite',96);rod('Chrome piston',start+d*.54,end,.013,'BrushedAlloy',96)
    torus('Piston gland',start+d*.58,.028,.003,'BrushedAlloy',d,96)
    for x in (-.123,.123):
        pts=[]
        for i in range(385):t=i/384;a=t*14*math.pi*2;pts.append((x+math.cos(a)*.009,.137+t*.044+math.sin(a)*.009,-.230+t*.277))
        tube('Return spring',pts,.0019,'BrushedAlloy',False,3)
        rod('Spring arm fixing',(math.copysign(.080,x),.181,.047),(math.copysign(.136,x),.181,.047),.004,'BrushedAlloy',48)
    box('Rear spring crossmember',(0,.129,-.230),(.404,.022,.024),'PressedRed',.005)
    cylinder('Rounded hydraulic pump head',(0,.160,-.326),.046,.100,'PressedRed',segments=96,bevel=.006)
    lathe('Operating handle socket',(0,.213,-.326),[(-.015,.017),(-.015,.033),(.016,.033),(.020,.027),(.020,.017)],'CastGraphite')
    rod('Stowed two piece handle',(.157,.185,-.426),(.157,.185,.164),.011,'BrushedAlloy',96)
    rod('Stowed handle sleeve',(.157,.185,-.426),(.157,.185,-.301),.016,'Rubber',96)
    for z in (-.246,.117):
        box('Handle clip mounting bracket',(.186,.154 if z<0 else .174,z),(.085,.025,.028),'CastGraphite',.005)
        box('Rubber handle stowage clip',(.157,.175,z),(.039,.025,.025),'CastGraphite',.005)

BUILDERS=dict(zip(IDS,[toolbox,case,parts,bin,fuel,grease,absorbent,jack]))
def mesh_object(obj):
    deps=bpy.context.evaluated_depsgraph_get();mesh=bpy.data.meshes.new_from_object(obj.evaluated_get(deps),preserve_all_data_layers=True,depsgraph=deps)
    result=bpy.data.objects.new(obj.name+'_export',mesh);bpy.context.collection.objects.link(result);result.matrix_world=obj.matrix_world.copy();return result
def tri_count(obj):
    obj.data.calc_loop_triangles()
    # Unity discards zero-area cap slivers during FBX import; count actual surfaces.
    return sum(t.area>=1e-14 for t in obj.data.loop_triangles)
def make_lods(id):
    parts=[mesh_object(o) for o in source];bpy.ops.object.select_all(action='DESELECT')
    for o in parts:o.select_set(True)
    bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.join();high=bpy.context.object;high.name=id+'_LOD0';activate(high);bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(angle_limit=1.15,island_margin=.006);bpy.ops.object.mode_set(mode='OBJECT')
    lods=[high]
    for level,ratio in ((1,.36),(2,.10)):
        o=high.copy();o.data=high.data.copy();bpy.context.collection.objects.link(o);o.name=id+'_LOD'+str(level);activate(o)
        dec=o.modifiers.new('Distance detail reduction','DECIMATE');dec.ratio=ratio;dec.use_collapse_triangulate=True;bpy.ops.object.modifier_apply(modifier=dec.name)
        normal=o.modifiers.new('LOD weighted normals','WEIGHTED_NORMAL');normal.keep_sharp=True;bpy.ops.object.modifier_apply(modifier=normal.name);lods.append(o)
    return lods
def studio(id):
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=48;scene.cycles.use_denoising=True;scene.cycles.max_bounces=4;scene.cycles.adaptive_threshold=.05
    scene.render.resolution_x=1400;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
    if scene.world is None:scene.world=bpy.data.worlds.new('Equipment studio world')
    scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.17,.19,.20,1);scene.world.node_tree.nodes['Background'].inputs[1].default_value=.35
    bounds=[o.matrix_world@Vector(p) for o in source for p in o.bound_box];low=Vector(tuple(min(p[i] for p in bounds) for i in range(3)));high=Vector(tuple(max(p[i] for p in bounds) for i in range(3)));center=(low+high)*.5
    floor=box('Preview floor',(0,-.016,0),(200,.02,200),'CastGraphite',0);source.remove(floor)
    bpy.ops.object.camera_add(location=center+Vector((.95,1.45,.78)).normalized()*2.8);cam=bpy.context.object;cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=max(high.x-low.x,(high.y-low.y)*.9,high.z-low.z)*1.65;scene.camera=cam
    for loc,energy,size in (((-1.1,1.0,2.0),110,1.4),((1.5,.7,1.1),45,1.2),((.2,-1.5,1.9),85,1.1)):
        data=bpy.data.lights.new('Studio softbox','AREA');data.energy=energy;data.shape='DISK';data.size=size;o=bpy.data.objects.new('Studio softbox',data);scene.collection.objects.link(o);o.location=Vector(loc);o.rotation_euler=(center-o.location).to_track_quat('-Z','Y').to_euler()
    scene.view_settings.view_transform='AgX'
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':area.spaces.active.region_3d.view_distance=1.5;area.spaces.active.region_3d.view_location=center;area.spaces.active.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
    scene.render.filepath=str(PREVIEWS/f'BlenderEquipmentStudio_{id}.png')
def run(id):
    global source,materials
    bpy.ops.wm.read_factory_settings(use_empty=True);source=[];materials={};bpy.context.scene.unit_settings.system='METRIC';BUILDERS[id]();bpy.context.view_layer.update()
    bounds=[o.matrix_world@Vector(p) for o in source for p in o.bound_box]
    low=[min(p[i] for p in bounds) for i in range(3)];high=[max(p[i] for p in bounds) for i in range(3)];sx,sy,sz=SIZES[id]
    if low[0]<-sx*.5-.003 or high[0]>sx*.5+.003 or low[1]<-sz*.5-.003 or high[1]>sz*.5+.003 or low[2]<-.003 or high[2]>sy+.003:raise RuntimeError(f'{id} outside original physical envelope: {low}..{high}')
    lods=make_lods(id);activate(lods[0]);bpy.ops.object.select_all(action='DESELECT')
    for o in lods:o.select_set(True)
    bpy.context.view_layer.objects.active=lods[0]
    bpy.ops.export_scene.fbx(filepath=str(OUTPUT/f'{id}.fbx'),use_selection=True,object_types={'MESH'},axis_forward='Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',use_space_transform=True,bake_space_transform=True,use_mesh_modifiers=True,mesh_smooth_type='FACE',use_triangles=True,add_leaf_bones=False,bake_anim=False,path_mode='AUTO')
    info={'id':id,'lods':[{'name':o.name,'triangles':tri_count(o),'materials':[m.name for m in o.data.materials]} for o in lods],'blend':f'Art/Blender/Models/RefinedEquipment/{id}.blend','bounds_blender':{'min':low,'max':high}}
    for o in lods:o.hide_render=True;o.hide_set(True)
    studio(id);activate(source[0]);bpy.ops.wm.save_as_mainfile(filepath=str(SOURCES/f'{id}.blend'))
    if id in ('fuel','jack'):bpy.ops.render.render(write_still=True)
    print('EQUIPMENT_RESULT '+json.dumps(info),flush=True);return info

if __name__=='__main__':
    chosen=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else IDS
    manifest=OUTPUT.parent/'equipment_manifest.json'
    existing=json.loads(manifest.read_text()) if manifest.exists() else {'version':1,'blender':'5.2','models':[]}
    lookup={m['id']:m for m in existing['models']}
    for id in chosen:
        lookup[id]=run(id);existing['models']=[lookup[key] for key in IDS if key in lookup]
        manifest.write_text(json.dumps(existing,indent=2)+'\n',encoding='utf-8')
    print('Saved Blender equipment manifest: '+str(manifest),flush=True)
