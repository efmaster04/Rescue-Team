"""Deterministik prosedürel animasyon klipleri (RescueCrew / Patient ortak mixamorig iskeleti).
Blender 5.2, metre, Z yukarı / -Y ön. Yalnız Export/Animations/Procedural yazar.
Kullanım: blender -b --factory-startup --python build_procedural_anims.py --python-exit-code 1 -- [--no-render] [--only Ad1,Ad2]
"""
import argparse
import hashlib
import json
import math
import os
import re
from pathlib import Path
import sys
import tempfile
import traceback
sys.dont_write_bytecode = True
import bpy
import numpy as np
from mathutils import Vector, Matrix, Quaternion

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Export/Animations/Procedural'
CREW = ROOT / 'Export/RescueCrew/SK_RescueCrew_Blue.fbx'
PATIENT = ROOT / 'Export/Patient/SK_Patient_Tourist.fbx'
CIVILIAN = ROOT / 'Export/Patient/SK_Patient_Civilian.fbx'
PREFIX = 'mixamorig:'
FPS = 30
PI = math.pi
TAU = 2 * PI

# Ad, süre (sn), döngü, yerinde, kök hareketi, karakter
CLIPS = [
    ('Carry_Stretcher_Walk', 0.5, True, True, False, 'RescueCrew'),
    ('Carry_OneHand_Walk', 0.4, True, True, False, 'RescueCrew'),
    ('PickUp', 1.0, False, True, False, 'RescueCrew'),
    ('PutDown', 0.9, False, True, False, 'RescueCrew'),
    ('GetUp_FromBack', 1.6, False, False, True, 'RescueCrew'),
    ('Revive_Helper', 1.2, True, True, False, 'RescueCrew'),
    ('Jump', 0.8, False, True, False, 'RescueCrew'),
    ('Startled', 0.9, False, True, False, 'RescueCrew'),
    ('Lying_HelpWave', 1.4, True, True, False, 'Patient'),
    ('Lying_Groan', 2.0, True, True, False, 'Patient'),
    ('Celebrate', 1.5, False, True, False, 'RescueCrew'),
    # r3: minibüsten çıkarma, halatla iniş/çıkış, sırtta taşıma
    ('Patient_SitDazed', 2.0, True, True, False, 'Patient'),
    ('Patient_SitDazed_ArmSling', 2.0, True, True, False, 'Patient'),
    ('Patient_DraggedOut', 1.5, False, False, True, 'Patient'),
    ('Piggyback_Rider', 1.0, True, True, False, 'Patient'),
    ('Extract_Pull', 1.5, False, True, False, 'RescueCrew'),
    ('Rope_Descend', 1.0, True, True, False, 'RescueCrew'),
    ('Rope_Climb', 1.0, True, True, False, 'RescueCrew'),
    ('Rope_Hang_Idle', 1.5, True, True, False, 'RescueCrew'),
    ('Piggyback_PickUp', 1.5, False, True, False, 'RescueCrew'),
    ('Piggyback_Rope_Climb', 1.0, True, True, False, 'RescueCrew'),
    ('Piggyback_Walk', 0.8, True, True, False, 'RescueCrew'),
    ('Piggyback_PutDown', 1.2, False, True, False, 'RescueCrew'),
    ('Rope_Pull', 1.0, True, True, False, 'RescueCrew'),
    # r4: sedyenin ön ucunu taşıyan (sedye arkada)
    ('Carry_Stretcher_Front_Walk', 0.5, True, True, False, 'RescueCrew'),
    ('Cabinet_TakeCoil', 1.4, False, True, False, 'RescueCrew'),
    ('Carry_Coil_Idle', 1.5, True, True, False, 'RescueCrew'),
    ('Carry_Coil_Walk', 0.4, True, True, False, 'RescueCrew'),
    ('Rope_TieStake', 2.0, False, True, False, 'RescueCrew'),
    ('Rope_Throw', 1.4, False, True, False, 'RescueCrew'),
    ('Push_Shove', .6, False, True, False, 'crew'),
    ('Tackle_Dive', .9, False, True, False, 'crew'),
    ('Tackle_GetUp', 1.0, False, True, False, 'crew'),
    ('Throw_Aim', 1.0, True, True, False, 'crew'),
    ('Throw_Release', .5, False, True, False, 'crew'),
    ('Whistle_Blow', 1.2, False, True, False, 'crew'),
    ('HitReact', .5, False, True, False, 'crew'),
    ('Thief_Sneak_Walk', .6, True, True, False, 'thief'),
    ('Thief_Run_Carry', .4, True, True, False, 'thief'),
    ('Thief_Run', .4, True, True, False, 'thief'),
    ('Thief_Grab', .8, False, True, False, 'thief'),
    ('Thief_Punch', .8, False, True, False, 'thief'),
    ('Thief_Stunned', 1.2, True, True, False, 'thief'),
    ('Thief_ThrowInVan', .8, False, True, False, 'thief'),
]
# Taşıma döngüleri: oyundaki hedef hız (m/sn) ve yere basma oranı. Ayak yerdeyken yerel olarak tam bu hızla
# geriye kayar (stride = hız × basma oranı × süre); oynatma hızı 1.0 iken dünyada ayak kaymaz.
WALK_SPEEDS = {
    'Thief_Sneak_Walk': (1.6, .5),
    'Thief_Run_Carry': (3.6, .32),
    'Thief_Run': (3.6, .32),
    'Carry_Coil_Walk': (2.4, 0.4),
    'Carry_Stretcher_Walk': (1.4, 0.5),   # sedye ağır: çömelmiş, sendeleyen paytak adım
    'Carry_OneHand_Walk': (2.4, 0.4),     # normal yürüme hızı: uçuş fazlı telaşlı seğirtme
    'Piggyback_Walk': (1.2, 0.5),         # sırtta hasta: çömelmiş, öne eğik ağır yürüyüş
    'Carry_Stretcher_Front_Walk': (1.4, 0.5),   # arkadaki taşıyıcıyla aynı ritim ve adım
}
DESCRIPTIONS = {
    'Push_Shove': 'İki avuçla öne itiş; çömelip yüklenir, hızla toparlanır. Slapstick, temas koddan.',
    'Tackle_Dive': 'Koşu hazırlığından öne sıçrar; kollar açık yüzüstü yere iner. İleri hareket Unity kodundan.',
    'Tackle_GetUp': 'Tackle_Dive sonundan avuçlarla yerden iterek diz üstüne, ardından ayağa kalkar.',
    'Throw_Aim': 'Sağ elde yukarı-geride nesne, küçük nişan salınımı; fırlatışa hazır döngü.',
    'Throw_Release': 'Throw_Aim sınır pozundan omuz üstü fırlatış, kol öne takip eder ve açılır.',
    'Whistle_Blow': 'Sağ el ağızda düdük; nefes alır, kollar açılıp öne eğilir, komik titreyerek üfler.',
    'HitReact': 'Geri kaykılma, savrulan kollar, kısa sendeleme ve toparlanma; kök yerinde.',
    'Thief_Sneak_Walk': 'Çömelmiş, parmak ucunda sinsi yürüyüş; önde pati eller, abartılı yan bakışlar (1.6 m/sn).',
    'Thief_Run_Carry': 'Ganimeti göğüs önünde iki elle kavrayarak telaşlı kaçış koşusu (3.6 m/sn).',
    'Thief_Run': 'Eller boş, geniş kol salınımlı telaşlı kaçış koşusu (3.6 m/sn).',
    'Thief_Grab': 'Öne-aşağı uzanır, ganimeti kapıp göğse çeker; Thief_Run_Carry ilk pozunda biter.',
    'Thief_Punch': 'Büyük hazırlık dönüşü ve geniş yandan savurma; komik haymaker, kan/silah yok.',
    'Thief_Stunned': 'Dizler kırık, kollar sarkık, baş daire çizerken sersem gövde salınımı.',
    'Thief_ThrowInVan': 'Göğüste tuttuğu ganimeti iki elle ileri-yukarı kamyonet kasasına atar.',
    'Cabinet_TakeCoil': 'Açık dolaba uzanır, kangalı çeker; geri kaykılıp göğsüne bastırarak Carry_Coil pozunda biter.',
    'Carry_Coil_Idle': 'İki el kangalın yanlarında; göğüs önünde yükü sarar, nefes ve sabırsız kafa hareketi.',
    'Carry_Coil_Walk': 'İki elle kangalı göğüste tutarak telaşlı kısa adımlarla 2.4 m/sn yürür (yerinde).',
    'Rope_TieStake': 'Kangalı yere koyar, kazık yakasında üç abartılı çekişle düğüm atar, tekrar kucaklayıp doğrulur.',
    'Rope_Throw': 'Kangalı geriye çeker, ileri-aşağı fırlatır; kollar açık iki komik denge salınımından sonra dik durur.',
    'Carry_Stretcher_Walk': 'İki el önde sedye saplarında, kollar gergin; dizler kırık, kalça sallanan, yük altında sendeleyen ağır paytak yürüyüş (1.4 m/sn).',
    'Carry_OneHand_Walk': 'Sağ el yanda koni tutuyor, sol kol serbest sallanıyor; alçak, uçuş fazlı telaşlı seğirtme (2.4 m/sn).',
    'PickUp': 'Çömelip öne eğilerek iki elle yerden alma, doğrulma; önde taşıma pozunda biter.',
    'PutDown': 'Önde taşıma pozundan öne eğilip iki elle yere bırakma, doğrulma.',
    'GetUp_FromBack': 'Sırt üstü yatıştan sendeleyerek kalkma, baş sallayıp toparlanma (kalça yerden ayağa).',
    'Revive_Helper': 'Diz çökmüş, yerdekini iki eliyle omzundan sallıyor ("hadi kalk!").',
    'Jump': 'Çömelme, kollar yukarı savrulur, iniş ve küçük sekme.',
    'Startled': 'Korna irkilmesi: kollar havaya, sol bacak kalkar, gövde geriye kaykılır, toparlanır.',
    'Lying_HelpWave': 'Yatma pozu (dik tanımlı): sol kol havada sallanıyor, kafa kalkıp iniyor.',
    'Lying_Groan': 'Yatma pozu (dik tanımlı): omuz ve dizler kıvranıyor, kafa yana dönüp geliyor.',
    'Celebrate': 'Zıplayıp iki yumruk havaya, kalça sallama.',
    'Patient_SitDazed': 'Koltukta oturan sersem hasta: kafa yavaş daireler çizip öne düşer, eller kucakta, kısa bacaklar sarkıp sallanır.',
    'Patient_SitDazed_ArmSling': 'Patient_SitDazed ile aynı; sağ kol askı pozunda (SK_Patient_Civilian için).',
    'Patient_DraggedOut': 'Oturuştan geriye (+Y) çekilip sırt üstü yatışa: kollar yel değirmeni, bacaklar havada çırpınır, sonra düşer. Kök hareketi var; sonda fiziksel olarak yatık (kalça z≈0.14, GetUp_FromBack başlangıcı gibi), uzuvlar Patient_LiePose düzeninde.',
    'Piggyback_Rider': 'Sırttaki hasta: kollar taşıyanın omuzlarında, bacaklar yanlarda sallanır, kafa sersem sallanır. Kök, taşıyanın Spine1 soketine bağlanır (bkz. piggyback_socket).',
    'Extract_Pull': 'Arka kapıda öne eğilip içeriden iki elle tutar; iki küçük, bir büyük çekiş; son çekişte eller boşalır, geri sendeleyip toparlanır.',
    'Rope_Descend': 'Halatta iniş: eller halatta (sol üstte), ayaklar duvarda döngüde ayak başına iki tıpış adım; kafa yukarı, gözler kenarda.',
    'Rope_Climb': 'Halatta elden ele tırmanış: üstteki el tutup aşağı çeker, alttaki bırakıp yukarı uzanır; ayaklar duvarda adımlar.',
    'Rope_Hang_Idle': 'Halatta asılı bekleme: hafif sarkaç sallanması, aşağıya ürkek bakışlar, titreyen dizler.',
    'Piggyback_PickUp': 'Sırtı dönük çömelip "hadi bin!" diye ellerini arkada sallar, diz altlarını kavrar, yük altında titreyerek doğrulur.',
    'Piggyback_Rope_Climb': 'Sırtta hastayla halatta tırmanış: öne eğik, çene yukarı, titreyerek zorlanan elden ele çekiş.',
    'Piggyback_Walk': 'Sırtta hastayla çömelmiş, öne eğik, sallanan ağır yürüyüş; eller binenin diz altlarında (1.2 m/sn).',
    'Piggyback_PutDown': 'Çömelip sırttakini bırakır, elleri belinde bel ağrısıyla geriye kaykılır, doğrulur.',
    'Rope_Pull': 'Kazığın yanında, ayaklar açık, dizler kırık: halatı elden ele çeker; her çekişte gövde geriye kaykılır, dizler titrer.',
    'Carry_Stretcher_Front_Walk': 'Sedyenin ön ucu: kollar arkada, iki el kalçanın arkasında aşağıda sedye saplarında; gövde öne eğik, '
                                  'yük çeker gibi; Carry_Stretcher_Walk ile aynı ritimde sendeleyen paytak yürüyüş (1.4 m/sn).',
}
PREVIEW = {'Patient_SitDazed_ArmSling': 'SK_Patient_Civilian'}

# ---------------------------------------------------------------- matematik

def clamp(x, a, b):
    return a if x < a else b if x > b else x


def smooth(x):
    x = clamp(x, 0.0, 1.0)
    return x * x * (3 - 2 * x)


def sp(t, keys):
    """Zamanlı Catmull-Rom (Hermite). keys=[(zaman, değer)], değer sayı ya da demet. Uçlarda hız sıfır."""
    tup = isinstance(keys[0][1], (tuple, list))
    vals = [tuple(k[1]) if tup else (k[1],) for k in keys]
    ts = [k[0] for k in keys]
    n = len(keys)
    if t <= ts[0]:
        out = vals[0]
    elif t >= ts[-1]:
        out = vals[-1]
    else:
        i = 0
        while ts[i + 1] < t:
            i += 1
        t0, t1 = ts[i], ts[i + 1]
        h = t1 - t0
        u = (t - t0) / h

        def tan(j):
            if j == 0 or j == n - 1:
                return tuple(0.0 for _ in vals[0])
            return tuple((vals[j + 1][c] - vals[j - 1][c]) / (ts[j + 1] - ts[j - 1]) for c in range(len(vals[0])))
        m0, m1 = tan(i), tan(i + 1)
        h00 = 2 * u ** 3 - 3 * u ** 2 + 1
        h10 = u ** 3 - 2 * u ** 2 + u
        h01 = -2 * u ** 3 + 3 * u ** 2
        h11 = u ** 3 - u ** 2
        out = tuple(h00 * vals[i][c] + h10 * h * m0[c] + h01 * vals[i + 1][c] + h11 * h * m1[c] for c in range(len(vals[0])))
    return out if tup else out[0]


def euler(pitch=0.0, roll=0.0, yaw=0.0):
    """Karakter eksenleri: +pitch öne eğilme (X), +roll tepe sola/+X (Y), +yaw sola dönüş (Z). Derece."""
    return (Matrix.Rotation(math.radians(yaw), 3, 'Z') @ Matrix.Rotation(math.radians(pitch), 3, 'X')
            @ Matrix.Rotation(math.radians(roll), 3, 'Y'))


def nv(*v):
    return Vector(v).normalized()


# ---------------------------------------------------------------- poz yardımcısı

class Poser:
    def __init__(self, rig):
        self.rig = rig
        self.pb = rig.pose.bones
        self.rest3 = {b.name: b.matrix_local.to_3x3().normalized() for b in rig.data.bones}
        self.rest_head = {b.name: b.head_local.copy() for b in rig.data.bones}
        self.hip_rot = Matrix.Identity(3)

    def b(self, n):
        return self.pb[PREFIX + n]

    def upd(self):
        bpy.context.view_layer.update()

    def reset(self):
        for pb in self.pb:
            pb.rotation_mode = 'QUATERNION'
            pb.location = (0, 0, 0)
            pb.rotation_quaternion = (1, 0, 0, 0)
            pb.scale = (1, 1, 1)
        self.hip_rot = Matrix.Identity(3)
        self.upd()

    def length(self, a, b):
        return (self.rest_head[PREFIX + b] - self.rest_head[PREFIX + a]).length

    def hips(self, offset=(0, 0, 0), pitch=0, roll=0, yaw=0):
        """offset: kalçanın bağ pozuna göre dünya ötelemesi."""
        pb = self.b('Hips')
        R = self.rest3[pb.name]
        E = euler(pitch, roll, yaw)
        self.hip_rot = E
        pb.location = R.inverted() @ Vector(offset)
        pb.rotation_quaternion = (R.inverted() @ E @ R).to_quaternion()
        self.upd()

    def rot(self, n, pitch=0, roll=0, yaw=0):
        """Ebeveyne göre göreli dönüş (karakter eksenlerinde ifade edilir, zincirde birikir)."""
        pb = self.b(n)
        R = self.rest3[pb.name]
        pb.rotation_quaternion = (R.inverted() @ euler(pitch, roll, yaw) @ R).to_quaternion()
        self.upd()

    def inherited(self, pb):
        parent = pb.parent
        rest = pb.bone.matrix_local
        return (parent.matrix @ parent.bone.matrix_local.inverted() @ rest).to_3x3().normalized()

    def orient(self, n, E):
        """Kemiğin dünya yönelimi = E @ bağ yönelimi (mutlak)."""
        pb = self.b(n)
        inh = self.inherited(pb)
        pb.rotation_quaternion = (inh.inverted() @ E @ self.rest3[pb.name]).to_quaternion()
        self.upd()

    def aim(self, n, direction, twist=0.0):
        pb = self.b(n)
        inh = self.inherited(pb)
        local = inh.inverted() @ Vector(direction).normalized()
        q = Vector((0, 1, 0)).rotation_difference(local)
        if twist:
            q = q @ Quaternion((0, 1, 0), math.radians(twist))
        pb.rotation_quaternion = q
        self.upd()

    def head(self, n):
        return self.b(n).head.copy()

    def chest(self):
        pb = self.b('Spine2')
        return (pb.matrix.to_3x3() @ self.rest3[pb.name].inverted()).normalized()

    def ik(self, upper, lower, end, target, pole):
        a = self.head(upper)
        l1 = self.length(upper, lower)
        l2 = self.length(lower, end)
        d = Vector(target) - a
        dist = clamp(d.length, abs(l1 - l2) + 1e-3, (l1 + l2) * 0.9995)
        dn = d.normalized()
        cos_a = clamp((l1 * l1 + dist * dist - l2 * l2) / (2 * l1 * dist), -1.0, 1.0)
        p = Vector(pole)
        pdir = (p - dn * p.dot(dn)).normalized()
        knee = a + dn * (l1 * cos_a) + pdir * (l1 * math.sqrt(max(0.0, 1 - cos_a * cos_a)))
        tip = a + dn * dist
        self.aim(upper, knee - a)
        self.aim(lower, tip - knee)

    # ---- uzuv kısayolları
    def leg(self, side, ankle, pole=None, pitch=None, yaw=0.0, auto_toe=True):
        s = 1 if side == 'Left' else -1
        ankle = Vector(ankle)
        if pole is None:
            pole = (s * 0.3, -1, 0)
        hj = self.head(side + 'UpLeg')
        reach = (self.length(side + 'UpLeg', side + 'Leg') + self.length(side + 'Leg', side + 'Foot')) * 0.985
        lift = 0.0
        if auto_toe and pitch is None:
            # Kalça yükselip bacak yetişemezse topuk kalkar, parmak yerde kalır.
            need = (ankle - hj).length - reach
            if need > 0:
                lift = min(need, 0.11)
                ankle = ankle + Vector((0, 0, lift))
        self.ik(side + 'UpLeg', side + 'Leg', side + 'Foot', ankle, pole)
        if pitch is None:
            pitch = math.degrees(math.asin(clamp(lift / 0.13, 0, 0.95)))
        self.orient(side + 'Foot', euler(pitch, 0, yaw))
        if pitch > 0 and auto_toe:
            self.rot(side + 'ToeBase', pitch=-min(pitch, 60) * 0.9)

    def arm_ik(self, side, wrist, pole=None, hand_dir=None, hand_twist=0.0):
        s = 1 if side == 'Left' else -1
        if pole is None:
            pole = (s * 0.6, 0.6, -0.5)
        self.ik(side + 'Arm', side + 'ForeArm', side + 'Hand', wrist, pole)
        if hand_dir is not None:
            self.aim(side + 'Hand', hand_dir, hand_twist)

    def arm(self, side, upper, fore, hand=None, frame=None, twist=0.0):
        """Göğüs çerçevesinde (frame verilmezse göğüs) yön verilen FK kol."""
        C = self.chest() if frame is None else frame
        self.aim(side + 'Arm', C @ Vector(upper))
        self.aim(side + 'ForeArm', C @ Vector(fore))
        self.aim(side + 'Hand', C @ Vector(hand if hand is not None else fore), twist)

    def fist(self, side, amount):
        s = 1 if side == 'Left' else -1
        self.rot(side + 'HandIndex1', roll=s * 55 * amount)
        self.rot(side + 'HandIndex2', roll=s * 65 * amount)
        self.rot(side + 'HandThumb1', yaw=-s * 20 * amount)
        self.rot(side + 'HandThumb2', roll=s * 30 * amount)

    def shrug(self, side, up=0.0, fwd=0.0):
        s = 1 if side == 'Left' else -1
        self.rot(side + 'Shoulder', roll=-s * up, yaw=-s * fwd)

    def spine(self, pitch=0, roll=0, yaw=0, w=(0.3, 0.35, 0.35)):
        for n, k in zip(('Spine', 'Spine1', 'Spine2'), w):
            self.rot(n, pitch * k, roll * k, yaw * k)

    def neck_head(self, pitch=0, roll=0, yaw=0):
        self.rot('Neck', pitch * 0.35, roll * 0.35, yaw * 0.35)
        self.rot('Head', pitch * 0.65, roll * 0.65, yaw * 0.65)


# ---------------------------------------------------------------- ortak pozlar
HIP_Z = 0.457          # bağ pozunda kalça yüksekliği
STAND = -0.012         # rahat duruşta dizler hafif kırık
FOOT_X = 0.134
ANKLE_Z = 0.096


def neutral_arms(P, sway=0.0):
    for s, side in ((1, 'Left'), (-1, 'Right')):
        P.arm(side, (s * 0.62, 0.04 + sway, -0.78), (s * 0.5, -0.16 + sway, -0.85), (s * 0.45, -0.2, -0.87))
        P.fist(side, 0.25)


def stand_legs(P, dx=0.0, toe_out=6.0):
    for s, side in ((1, 'Left'), (-1, 'Right')):
        P.leg(side, (s * (FOOT_X + dx), 0.0, ANKLE_Z), yaw=s * toe_out)


def neutral(P, hz=STAND):
    P.hips((0, 0, hz))
    P.spine()
    P.neck_head()
    neutral_arms(P)
    stand_legs(P)


def hold_front(P, lift=0.0, spread=0.0):
    """İki el önde, bel hizasında bir şey tutuyor (PickUp sonu / PutDown başı)."""
    for s, side in ((1, 'Left'), (-1, 'Right')):
        P.arm_ik(side, (s * (0.30 + spread), -0.25, 0.60 + lift), pole=(s * 0.8, 0.5, -0.4), hand_dir=(-s * 0.25, -1, 0.05))
        P.fist(side, 0.8)


def walk_foot(phase, stride, lift, stance=0.56):
    """Yerinde yürüyüş ayağı: (y, z ek, pitch). phase 0'da ayak önde yere basar."""
    p = phase % 1.0
    if p < stance:
        u = p / stance
        y = -stride / 2 + stride * u
        return y, 0.0, 28 * smooth((u - 0.72) / 0.28)
    u = (p - stance) / (1 - stance)
    m = stride * (1 - stance) / stance
    h00 = 2 * u ** 3 - 3 * u ** 2 + 1
    h10 = u ** 3 - 2 * u ** 2 + u
    h01 = -2 * u ** 3 + 3 * u ** 2
    h11 = u ** 3 - u ** 2
    y = h00 * (stride / 2) + h10 * m + h01 * (-stride / 2) + h11 * m
    z = lift * math.sin(PI * u) ** 1.3
    pitch = 28 * (1 - smooth(u / 0.35)) - 18 * math.sin(PI * clamp((u - 0.45) / 0.55, 0, 1))
    return y, z, pitch


def walk_legs(P, phase, stride, lift, width=0.0, toe_out=8.0, stance=0.56):
    for s, side, off in ((1, 'Left', 0.0), (-1, 'Right', 0.5)):
        y, z, pitch = walk_foot(phase + off, stride, lift, stance)
        P.leg(side, (s * (FOOT_X + width), y, ANKLE_Z + z), pole=(s * 0.25, -1, 0), pitch=pitch, yaw=s * toe_out, auto_toe=False)
        if pitch > 0:
            P.rot(side + 'ToeBase', pitch=-pitch * 0.9)


# ---------------------------------------------------------------- klipler (t saniye, T süre)

def walk_stride(name, T):
    v, stance = WALK_SPEEDS[name]
    return v * stance * T, stance


def clip_carry_stretcher(P, t, T):
    ph = t / T
    stride, stance = walk_stride('Carry_Stretcher_Walk', T)
    c2 = math.cos(2 * TAU * ph)
    s1 = math.sin(TAU * ph)
    # Adım uçlarında (iki ayak yerde) en alçak: bacak ±stride/2'ye yetişsin diye dizler hep kırık.
    bob = -0.080 - 0.016 * c2
    P.hips((0, 0, bob), pitch=-9 + 3 * c2, roll=13 * s1, yaw=8 * math.sin(TAU * ph + 0.4))
    P.spine(pitch=-7 + 4 * math.cos(2 * TAU * ph - 0.6), roll=-15 * math.sin(TAU * ph - 0.35), yaw=-10 * math.sin(TAU * ph + 0.4))
    P.neck_head(pitch=12 + 8 * math.cos(2 * TAU * ph - 1.3), roll=9 * math.sin(TAU * ph - 1.1), yaw=4 * s1)
    for s, side in ((1, 'Left'), (-1, 'Right')):
        P.shrug(side, up=10 + 5 * c2, fwd=12)
    hz = 0.60 + 0.5 * (bob + 0.080)
    for s, side in ((1, 'Left'), (-1, 'Right')):
        P.arm_ik(side, (s * 0.30, -0.31, hz + 0.01), pole=(s * 0.9, 0.4, -0.5), hand_dir=(s * 0.05, -1, -0.15), hand_twist=0)
        P.fist(side, 0.9)
    walk_legs(P, ph, stride=stride, lift=0.085, width=0.04, toe_out=16, stance=stance)


def clip_carry_onehand(P, t, T):
    ph = t / T
    stride, stance = walk_stride('Carry_OneHand_Walk', T)
    c2 = math.cos(2 * TAU * (ph - stance / 2))
    # Basmanın ortasında en alçak, uçuşta en yüksek; bacak uçlara yetişsin diye kalça alçak.
    bob = -0.082 - 0.018 * c2
    P.hips((0, 0, bob), pitch=12 + 3 * c2, roll=7 * math.sin(TAU * ph), yaw=-5 * math.sin(TAU * ph))
    P.spine(pitch=8 + 3 * c2, roll=7 - 5 * math.sin(TAU * ph), yaw=14 * math.sin(TAU * ph + 0.3))
    P.neck_head(pitch=-12 + 7 * math.cos(2 * TAU * ph - 1.0), roll=-6, yaw=-8 * math.sin(TAU * ph))
    # Sağ el: yanda aşağıda koni tutuyor, omuz ağırlıkla düşük, koni sekiyor.
    P.shrug('Right', up=-6 + 3 * c2)
    P.arm('Right', (-0.72, 0.1, -0.69), (-0.42, -0.34 + 0.07 * c2, -0.84), (-0.3, -0.3, -0.9))
    P.fist('Right', 1.0)
    sw = math.cos(TAU * ph)
    P.arm('Left', (0.5, 0.85 * sw, -0.62 + 0.5 * max(0.0, -sw)), (0.4, 0.45 * sw - 0.6, -0.45), (0.35, 0.4 * sw - 0.65, -0.45))
    P.fist('Left', 0.5)
    walk_legs(P, ph, stride=stride, lift=0.10, width=0.01, toe_out=8, stance=stance)


def clip_pickup(P, t, T):
    hz = sp(t, [(0, STAND), (0.12, 0.006), (0.40, -0.245), (0.58, -0.235), (0.80, 0.02), (0.90, -0.03), (1.0, STAND)])
    pitch = sp(t, [(0, 0), (0.12, -6), (0.40, 62), (0.58, 58), (0.80, -10), (0.90, 3), (1.0, -2)])
    P.hips((0, 0, hz), pitch=pitch * 0.45)
    P.spine(pitch=pitch * 0.55)
    P.neck_head(pitch=sp(t, [(0, 0), (0.12, -8), (0.40, -38), (0.58, -30), (0.80, 10), (0.90, -4), (1.0, 0)]))
    reach = smooth((t - 0.14) / 0.24) * (1 - smooth((t - 0.56) / 0.18))
    holdw = smooth((t - 0.56) / 0.22)
    for s, side in ((1, 'Left'), (-1, 'Right')):
        ground = Vector((s * sp(t, [(0.3, 0.26), (0.45, 0.23), (0.55, 0.18)]), -0.40, 0.11 + 0.03 * smooth((t - 0.5) / 0.08)))
        front = Vector((s * 0.32, -0.32, 0.62 + sp(t, [(0.56, -0.15), (0.82, 0.05), (0.92, -0.02), (1.0, 0.0)])))
        rest = P.head(side + 'Arm') + Vector((s * 0.19, -0.06, -0.24))
        target = (rest.lerp(ground, reach) + Vector((s * 0.07, -0.14, 0.0)) * math.sin(PI * reach)) if t < 0.56 else (ground.lerp(front, holdw) + Vector((s * 0.08, -0.2, 0.0)) * math.sin(PI * holdw))
        P.arm_ik(side, target, pole=(s * 0.8, 0.5, -0.3), hand_dir=(s * 0.1 - s * 0.35 * holdw, -1, -1.2 + 1.25 * holdw))
        P.fist(side, sp(t, [(0, 0.25), (0.38, 0.0), (0.52, 0.9), (1.0, 0.8)]))
    for s, side in ((1, 'Left'), (-1, 'Right')):
        P.leg(side, (s * (FOOT_X + 0.04), 0.0, ANKLE_Z), pole=(s * 0.55, -1, 0.05), yaw=s * 12)


def clip_putdown(P, t, T):
    hz = sp(t, [(0, STAND), (0.38, -0.245), (0.52, -0.24), (0.74, 0.02), (0.82, -0.03), (0.9, STAND)])
    pitch = sp(t, [(0, 0), (0.38, 62), (0.52, 60), (0.74, -9), (0.82, 3), (0.9, 0)])
    P.hips((0, 0, hz), pitch=pitch * 0.45)
    P.spine(pitch=pitch * 0.55)
    P.neck_head(pitch=sp(t, [(0, 0), (0.38, -36), (0.52, -30), (0.74, 8), (0.9, 0)]))
    down = smooth(t / 0.38)
    release = smooth((t - 0.44) / 0.06)
    back = smooth((t - 0.52) / 0.3)
    for s, side in ((1, 'Left'), (-1, 'Right')):
        front = Vector((s * 0.32, -0.32, 0.62))
        ground = Vector((s * (0.18 + 0.07 * release), -0.40, 0.11 + 0.04 * release))
        idle = P.head(side + 'Arm') + Vector((s * 0.19, -0.06, -0.24))
        target = (front.lerp(ground, down) + Vector((s * 0.08, -0.2, 0.0)) * math.sin(PI * down)) if t < 0.52 else (ground.lerp(idle, back) + Vector((s * 0.07, -0.14, 0.0)) * math.sin(PI * back))
        P.arm_ik(side, target, pole=(s * 0.8, 0.5, -0.3), hand_dir=(s * (-0.25 + 0.35 * down), -1, -1.2 * down + 0.05))
        P.fist(side, sp(t, [(0, 0.8), (0.42, 0.85), (0.5, 0.0), (0.9, 0.25)]))
    if t > 0.52:
        # Ellerin silkelenmesi ("tamam bıraktım").
        k = math.sin(TAU * 7 * (t - 0.52)) * (1 - smooth((t - 0.52) / 0.3)) * 18
        for s, side in ((1, 'Left'), (-1, 'Right')):
            P.rot(side + 'Hand', roll=s * k)
    for s, side in ((1, 'Left'), (-1, 'Right')):
        P.leg(side, (s * (FOOT_X + 0.04), 0.0, ANKLE_Z), pole=(s * 0.55, -1, 0.05), yaw=s * 12)


def clip_getup(P, t, T):
    hy = sp(t, [(0, 0.10), (0.30, 0.10), (0.58, 0.04), (0.82, -0.02), (1.05, 0.0), (1.25, 0.02), (1.6, 0.0)])
    hz = sp(t, [(0, 0.14 - HIP_Z), (0.30, 0.14 - HIP_Z), (0.58, 0.16 - HIP_Z), (0.82, 0.22 - HIP_Z), (1.05, 0.02),
                (1.18, -0.05), (1.32, 0.005), (1.6, STAND)])
    pitch = sp(t, [(0, -90), (0.30, -93), (0.58, -20), (0.82, 38), (1.05, -8), (1.18, 6), (1.32, -3), (1.6, 0)])
    roll = sp(t, [(0, 0), (0.9, 0), (1.05, 10), (1.18, -9), (1.32, 5), (1.45, -2), (1.6, 0)])
    yaw = sp(t, [(0, 0), (0.9, 0), (1.05, -8), (1.2, 7), (1.4, -2), (1.6, 0)])
    P.hips((0, hy, hz), pitch=pitch * 0.85, roll=roll, yaw=yaw)
    P.spine(pitch=pitch * 0.15 + sp(t, [(0, 0), (0.3, -4), (0.58, 18), (0.82, 12), (1.05, -6), (1.6, 0)]), roll=-roll * 0.6, yaw=-yaw * 0.4)
    shake = math.sin(TAU * 5.5 * (t - 1.22)) * 28 * smooth((t - 1.2) / 0.06) * (1 - smooth((t - 1.36) / 0.22))
    P.neck_head(pitch=sp(t, [(0, 0), (0.15, 16), (0.30, 0), (0.58, 10), (0.82, -22), (1.05, 6), (1.6, 0)]),
                roll=sp(t, [(0, 0), (0.1, 10), (0.25, -6), (0.4, 0), (1.6, 0)]) - roll * 0.5,
                yaw=shake)
    Rp = P.hip_rot
    # Kollar: yatarken yanlarda, otururken yere dayanır, sonra dizlerde, sonra savrulup toparlanır.
    for s, side in ((1, 'Left'), (-1, 'Right')):
        lying = (s * 0.42, 0.05, -0.9)
        push = (s * 0.5, 0.75, -0.45)
        knees = (s * 0.25, -0.75, -0.6)
        flail = (s * 0.9, -0.1 * s, 0.35)
        up = sp(t, [(0, 0), (0.30, 0), (0.50, 1), (0.70, 1), (0.85, 2), (1.0, 2), (1.12, 3), (1.3, 3), (1.6, 4)])
        idx = min(int(up), 3)
        poses = [lying, push, knees, flail, (s * 0.62, 0.04, -0.78)]
        a = Vector(poses[idx]).lerp(Vector(poses[idx + 1]), up - idx).normalized()
        f = (a + Vector((0, -0.25, -0.1))).normalized()
        P.arm(side, a, f, f)
        P.fist(side, 0.2 + 0.5 * smooth((t - 0.75) / 0.2) * (1 - smooth((t - 1.05) / 0.1)))
    # Bacaklar: yatışta gövdeyi izler (düz), sonra ayaklar yere basar.
    plant = smooth((t - 0.28) / 0.3)
    for s, side in ((1, 'Left'), (-1, 'Right')):
        hj = P.head(side + 'UpLeg')
        fk = hj + Rp @ Vector((s * 0.01, -0.03, -0.36))
        ay = sp(t, [(0.3, -0.38), (0.58, -0.30), (0.82, -0.12), (1.0, -0.02), (1.18, 0.03 * s), (1.6, 0.0)])
        ax = sp(t, [(0.3, 0.17), (0.82, 0.19), (1.05, 0.16), (1.6, FOOT_X)])
        step = 0.06 * math.sin(PI * clamp((t - 1.12 - (0.1 if s > 0 else 0)) / 0.14, 0, 1))
        ground = Vector((s * ax, ay, ANKLE_Z + step))
        target = fk.lerp(ground, plant)
        pole = (Rp @ Vector((s * 0.3, -1, 0))).lerp(Vector((s * 0.45, -1, 0.2)), plant)
        fp = sp(t, [(0, -88), (0.3, -85), (0.6, 0), (1.6, 0)])
        P.leg(side, target, pole=pole, pitch=fp if t < 0.6 else None, yaw=s * 8)


def clip_revive(P, t, T):
    ph = t / T
    amp = 1.0 + 0.45 * math.sin(TAU * ph)      # bir çekiş daha sert
    sh = math.sin(TAU * 3 * ph)
    sh2 = math.sin(TAU * 6 * ph)
    P.hips((0, 0, 0.215 - HIP_Z + 0.012 * sh2), pitch=24 + 4 * sh * amp, roll=4 * sh, yaw=-5 * sh * amp)
    P.spine(pitch=22 + 7 * sh * amp, roll=-4 * sh, yaw=-15 * sh * amp)
    P.neck_head(pitch=-30 + 8 * math.sin(TAU * 3 * ph + 1.2), roll=6 * math.sin(TAU * ph), yaw=8 * sh)
    for s, side in ((1, 'Left'), (-1, 'Right')):
        P.shrug(side, up=10 + 6 * sh * s, fwd=18)
        wrist = (s * 0.215, -0.42 + 0.085 * sh * s * amp, 0.24 + 0.03 * sh2)
        P.arm_ik(side, wrist, pole=(s * 0.9, 0.3, -0.2), hand_dir=(-s * 0.15, -0.8, -0.6))
        P.fist(side, 0.75)
    for s, side in ((1, 'Left'), (-1, 'Right')):
        P.leg(side, (s * 0.15, 0.19, 0.07), pole=(s * 0.1, -1, -1.2), pitch=118, yaw=0, auto_toe=False)
        P.rot(side + 'ToeBase', pitch=-25)


def clip_jump(P, t, T):
    hz = sp(t, [(0, STAND), (0.17, -0.12), (0.27, 0.0), (0.42, 0.22), (0.53, 0.06), (0.58, -0.005), (0.64, -0.10),
                (0.71, 0.04), (0.75, 0.0), (0.8, STAND)])
    pitch = sp(t, [(0, 0), (0.17, 28), (0.27, -6), (0.42, -10), (0.58, 6), (0.64, 22), (0.72, -4), (0.8, 0)])
    P.hips((0, 0, hz), pitch=pitch * 0.4)
    P.spine(pitch=pitch * 0.6)
    P.neck_head(pitch=sp(t, [(0, 0), (0.17, -18), (0.3, -14), (0.42, -18), (0.62, 14), (0.72, -6), (0.8, 0)]))
    swing = sp(t, [(0, 0), (0.17, -1.0), (0.30, 1.0), (0.45, 1.05), (0.58, 0.55), (0.66, -0.25), (0.74, 0.15), (0.8, 0)])
    for s, side in ((1, 'Left'), (-1, 'Right')):
        if swing < 0:
            up = Vector((s * 0.62, 0.04, -0.78)).lerp(Vector((s * 0.35, 0.75, -0.55)), -swing)
        else:
            up = Vector((s * 0.62, 0.04, -0.78)).lerp(Vector((s * 0.82, -0.12, 0.56)), min(swing, 1.0))
        f = up + Vector((0, -0.2, 0.15 * max(swing, 0)))
        P.arm(side, up.normalized(), f.normalized(), f.normalized())
        P.fist(side, 0.2 + 0.4 * max(swing, 0))
    air = max(0.0, hz - STAND - 0.005)
    tuck = sp(t, [(0.27, 0), (0.42, 0.10), (0.53, 0.02), (0.58, 0)])
    hop = max(0.0, hz - 0.0)
    for s, side in ((1, 'Left'), (-1, 'Right')):
        z = ANKLE_Z + max(0.0, air - 0.04) + tuck if t < 0.6 else ANKLE_Z + max(0.0, hop - 0.025)
        y = 0.04 * tuck / 0.10 if t < 0.6 else 0.0
        fp = sp(t, [(0.2, 0), (0.30, 40), (0.45, 25), (0.55, 10), (0.58, 0), (0.70, 0), (0.73, 20), (0.78, 0)])
        P.leg(side, (s * (FOOT_X + 0.02), y, z), pole=(s * 0.4, -1, 0), pitch=fp if fp > 1 else None, yaw=s * 10)


def clip_startled(P, t, T):
    hz = sp(t, [(0, STAND), (0.06, -0.035), (0.15, 0.045), (0.42, 0.04), (0.58, -0.01), (0.70, -0.045), (0.82, -0.005), (0.9, STAND)])
    trem = math.sin(TAU * 13 * t) * smooth((t - 0.13) / 0.04) * (1 - smooth((t - 0.40) / 0.12))
    back = sp(t, [(0, 0), (0.06, 9), (0.15, -36), (0.42, -30), (0.58, 0), (0.70, 10), (0.82, -2), (0.9, 0)])
    P.hips((0, 0, hz), pitch=back * 0.35, roll=-6 * smooth((t - 0.08) / 0.07) * (1 - smooth((t - 0.5) / 0.15)))
    P.spine(pitch=back * 0.65, roll=4 * trem)
    P.neck_head(pitch=sp(t, [(0, 0), (0.06, 10), (0.15, -18), (0.42, -12), (0.6, 6), (0.75, 4), (0.9, 0)]), roll=3 * trem, yaw=6 * trem)
    fling = sp(t, [(0, 0), (0.06, -0.15), (0.15, 1.08), (0.42, 1.0), (0.62, 0.15), (0.75, -0.08), (0.9, 0)])
    for s, side in ((1, 'Left'), (-1, 'Right')):
        P.shrug(side, up=sp(t, [(0, 0), (0.06, 12), (0.15, 18), (0.42, 14), (0.65, 0), (0.9, 0)]))
        base = Vector((s * 0.62, 0.04, -0.78))
        up = Vector((s * 0.72, 0.18, 0.67))
        a = base.lerp(up, clamp(fling, -0.2, 1.1)).normalized() + Vector((0, 0, 0.04 * trem))
        f = (a + Vector((s * -0.1, -0.15, 0.25 * max(0.0, fling)))).normalized()
        P.arm(side, a.normalized(), f, f)
        P.fist(side, -0.35 * max(0.0, fling) + 0.25 * (1 - min(1.0, max(0.0, fling))))
    raise_l = sp(t, [(0, 0), (0.08, 0), (0.17, 1.05), (0.40, 0.95), (0.58, 0.0), (0.9, 0)])
    hjL = P.head('LeftUpLeg')
    ground_l = Vector((FOOT_X + 0.01, 0.0, ANKLE_Z))
    up_l = Vector((FOOT_X + 0.07, -0.24, hjL.z - 0.09))
    P.leg('Left', ground_l.lerp(up_l, clamp(raise_l, 0, 1.05)), pole=(0.3, -1, 0.3),
          pitch=-15 * raise_l if raise_l > 0.02 else None, yaw=8)
    P.leg('Right', (-(FOOT_X + 0.01), 0.0, ANKLE_Z), pole=(-0.3, -1, 0), yaw=-8)


def lie_base(P):
    """Patient_LiePose ile aynı temel: kollar yanda, bacaklar düz ve hafif içe, kafa 7°."""
    for s, side in ((1, 'Left'), (-1, 'Right')):
        pb = P.b(side + 'UpLeg')
        pb.location = pb.bone.matrix_local.to_3x3().inverted() @ Vector((-s * 0.031, 0, 0))
    P.upd()


def clip_lying_wave(P, t, T):
    ph = t / T
    lift = 0.5 - 0.5 * math.cos(TAU * ph)
    w3 = math.sin(TAU * 3 * ph)
    P.hips((0, 0, 0), pitch=3 * lift, roll=2 * math.sin(TAU * ph))
    lie_base(P)
    P.spine(pitch=9 * lift, roll=-3 * w3 * lift, yaw=4 * w3)
    P.neck_head(pitch=7 + 22 * lift + 3 * math.sin(TAU * 2 * ph), yaw=8 * w3, roll=4 * math.sin(TAU * ph))
    # Sol kol göğe (dik çerçevede -Y) kalkık, önkol yana sallanıyor.
    P.shrug('Left', up=6 + 6 * lift, fwd=14)
    up = Vector((0.5, -0.84, 0.12 + 0.06 * lift)).normalized()
    fore = Vector((0.36 + 0.45 * w3, -0.88, 0.08 + 0.22 * w3)).normalized()
    hand = (fore + Vector((0.35 * math.sin(TAU * 3 * ph - 0.9), 0, 0.1))).normalized()
    P.arm('Left', up, fore, hand)
    P.fist('Left', -0.3)
    # Sağ kol yanda, küçük çırpınma.
    P.arm('Right', (-0.27, -0.06 - 0.06 * lift, -0.96), (-0.25, -0.18 - 0.1 * lift, -0.95), (-0.2, -0.2, -0.96))
    P.fist('Right', 0.3 + 0.4 * lift)
    for s, side in ((1, 'Left'), (-1, 'Right')):
        hj = P.head(side + 'UpLeg')
        kick = 0.03 * max(0.0, math.sin(TAU * 2 * ph + (0 if s > 0 else PI)))
        P.leg(side, (hj.x, hj.y - kick * 0.5, hj.z - 0.37 + kick), pole=(0, -1, 0), pitch=-6 * kick / 0.03, yaw=s * 6, auto_toe=False)


def clip_lying_groan(P, t, T):
    ph = t / T
    s1 = math.sin(TAU * ph)
    c1 = math.cos(TAU * ph)
    s2 = math.sin(TAU * 2 * ph)
    P.hips((0, 0, 0), pitch=2 * (1 - c1), roll=4 * s1, yaw=3 * s1)
    lie_base(P)
    P.spine(pitch=4 * (1 - c1), roll=-5 * s1, yaw=-6 * s1)
    turn = 32 * math.sin(TAU * ph) ** 3
    P.neck_head(pitch=7 + 6 * (0.5 - 0.5 * math.cos(TAU * 2 * ph)), yaw=turn, roll=-6 * s1)
    for s, side in ((1, 'Left'), (-1, 'Right')):
        P.shrug(side, up=7 * math.sin(TAU * ph + (0 if s > 0 else PI)) + 4 * (0.5 - 0.5 * math.cos(TAU * 2 * ph)), fwd=6 * s2)
        bend = 0.5 - 0.5 * math.cos(TAU * ph + (0 if s > 0 else PI))
        P.arm(side, (s * 0.27, -0.04 - 0.06 * bend, -0.96), (s * 0.24, -0.25 - 0.35 * bend, -0.9), (s * 0.15, -0.3 - 0.3 * bend, -0.9))
        P.fist(side, 0.3 + 0.5 * bend)
    for s, side in ((1, 'Left'), (-1, 'Right')):
        hj = P.head(side + 'UpLeg')
        k = 0.5 - 0.5 * math.cos(TAU * ph + (0.6 if s > 0 else 0.6 + PI))
        d = 0.03 + 0.13 * k
        P.leg(side, (hj.x + s * 0.01 * k, hj.y, hj.z - 0.372 + d), pole=(s * 0.2, -1, 0), pitch=-12 + 14 * k, yaw=s * 6, auto_toe=False)


def clip_celebrate(P, t, T):
    jump = t < 0.62
    hz = sp(t, [(0, STAND), (0.14, -0.13), (0.24, 0.0), (0.36, 0.23), (0.48, 0.05), (0.53, -0.005), (0.60, -0.09), (0.70, -0.03), (1.5, STAND)])
    if t > 0.62:
        hz = sp(t, [(0.62, -0.075), (0.70, -0.04), (1.32, -0.04), (1.5, STAND)]) + 0.012 * math.cos(TAU * 5 * (t - 0.62))
    pitch = sp(t, [(0, 0), (0.14, 26), (0.24, -10), (0.36, -16), (0.5, 4), (0.6, 18), (0.72, -8), (1.32, -6), (1.5, 0)])
    wig = smooth((t - 0.66) / 0.08) * (1 - smooth((t - 1.28) / 0.16))
    roll = 13 * math.sin(TAU * 3.33 * (t - 0.66)) * wig
    P.hips((0, 0, hz), pitch=pitch * 0.4, roll=roll, yaw=6 * math.sin(TAU * 3.33 * (t - 0.66) - 0.6) * wig)
    P.spine(pitch=pitch * 0.6, roll=-roll * 1.4, yaw=-4 * math.sin(TAU * 3.33 * (t - 0.66)) * wig)
    P.neck_head(pitch=sp(t, [(0, 0), (0.14, -10), (0.36, -26), (0.6, 6), (0.72, -14), (1.32, -10), (1.5, 0)]), roll=-roll * 0.4)
    upw = sp(t, [(0, 0), (0.14, -0.4), (0.26, 1.0), (0.5, 1.05), (0.6, 0.85), (0.7, 1.0), (1.3, 1.0), (1.5, 0)])
    for s, side in ((1, 'Left'), (-1, 'Right')):
        pump = 0.18 * math.sin(TAU * 3.33 * (t - 0.66) + (0 if s > 0 else PI)) * wig
        base = Vector((s * 0.62, 0.04, -0.78))
        up = Vector((s * 0.78, -0.06, 0.62 + pump)).normalized()
        if upw < 0:
            a = base.lerp(Vector((s * 0.4, -0.7, -0.6)), -upw)
            f = Vector((-s * 0.3, -0.6, 0.75))
        else:
            a = base.lerp(up, upw)
            f = base.lerp(Vector((s * 0.35, -0.12, 0.93 + pump)), upw)
        P.arm(side, a.normalized(), f.normalized(), f.normalized())
        P.fist(side, 0.3 + 0.7 * min(1.0, abs(upw) * 1.4))
    air = max(0.0, hz - STAND - 0.01)
    kick = sp(t, [(0.24, 0), (0.36, 1), (0.48, 0.2), (0.53, 0)]) if jump else 0
    for s, side in ((1, 'Left'), (-1, 'Right')):
        z = ANKLE_Z + max(0.0, air - 0.04) + 0.12 * kick
        y = 0.16 * kick
        P.leg(side, (s * (FOOT_X + 0.03), y, z), pole=(s * 0.4, -1, 0.2), pitch=(25 * kick) if kick > 0.02 else None, yaw=s * 12)


# ---------------------------------------------------------------- r3: minibüs, halat, sırtta taşıma

SIT_HZ = -0.09                         # oturuşta kalça ötelemesi (kalça z ≈ 0.367)
SEAT_Z = 0.262                         # koltuk yüzeyi (kök yerel z, yalnız önizleme ve meta)
ROPE_Y = -0.29                         # halat çizgisi: kök yerel x=0, y=ROPE_Y, dikey
WALL_Y = -0.36                         # kaya duvarı düzlemi (ayak tabanı), kök yerel
ROPE_HZ = -0.03                        # halatta kalça ötelemesi
PULL_Z = 0.80                          # Rope_Pull: halat çizgisi yüksekliği (x=0, yatay, -Y yönünden gelir)
PULL_FRONT, PULL_BACK = -0.40, -0.26   # Rope_Pull: elin halatı tuttuğu ön ve bıraktığı arka nokta (y)
PIGGY_BONE = 'Spine1'                  # sırttaki hastanın bağlandığı kemik (taşıyan)
PIGGY_OFFSET = (0.0, 0.312, 0.292)     # binen kökü, taşıyanın bağ pozu çerçevesinde (Spine1'e rijit bağlı)
RIDER_LEAN = (-17.0, -8.0)             # binen: kalça ve omurga eğimi (derece, - geriye)
RIDER_THIGH = (0.6, -0.78, -0.1)       # binen uyluk yönü (binen kök çerçevesi)
RIDER_HAND = (0.215, 0.156, 0.82)      # binenin elleri: taşıyanın bağ çerçevesinde omuz arkası
GRIP_OFFSET = (0.11, 1.0, -0.13, -0.07)   # taşıyanın bilek hedefi: binen uyluğunda (x dışa, kalça→diz oranı, z aşağı, y)
PIGGY_HEAD = (0.40, 5.0)               # taşıyan kafası: gövde eğiminin telafi oranı, yürüyüşte taban eğim
PIGGY_ROPE_LOOK = 17.0                 # sırtta yükle tırmanışta kafa daha az yukarı bakar (binenle çakışmasın)
PIGGY_ROPE_LEAN = 12.0                 # sırtta yükle tırmanışta öne eğilme
# Halat klipleri: oyunda kökün dikey hızı (m/sn, + yukarı). Duvardaki ayak, basarken tam bu hızla kayar.
ROPE_SPEEDS = {'Rope_Descend': -0.6, 'Rope_Climb': 0.3, 'Piggyback_Rope_Climb': 0.25}
# Sırt taşıma tek seferlik klipleri: binenin taşıyana bağlı olduğu kareler (1 tabanlı, dahil).
PIGGY_ATTACH = {'Piggyback_PickUp': (17, None), 'Piggyback_PutDown': (1, 16)}
_PIGGY_GRIP = {}


def socket_local(P):
    """Taşıyanın Spine1 deformasyonu × ofset: binen kökünün taşıyan armatür uzayındaki matrisi."""
    pb = P.b(PIGGY_BONE)
    return pb.matrix @ pb.bone.matrix_local.inverted() @ Matrix.Translation(PIGGY_OFFSET)


# ---- hasta: oturma

def sit_legs(P, ph):
    """Kısa bacaklar koltuktan sarkar, ayaklar yavaşça sallanır."""
    for s, side, off in ((1, 'Left', 0.0), (-1, 'Right', PI)):
        sw = math.sin(TAU * ph + off)
        P.aim(side + 'UpLeg', (s * 0.13, -1, 0.05))
        P.aim(side + 'Leg', (s * 0.04, -0.22 - 0.14 * sw, -1))
        P.orient(side + 'Foot', euler(18 + 10 * sw, 0, s * 10))


def sit_dazed(P, t, T, sling):
    ph = t / T
    s1, c1 = math.sin(TAU * ph), math.cos(TAU * ph)
    P.hips((0, 0, SIT_HZ), pitch=-6 + 2 * s1, roll=3 * c1, yaw=2 * s1)
    P.spine(pitch=10 + 3 * s1, roll=-5 * c1, yaw=-3 * s1)
    # Kafa yavaş daireler çizer, ara sıra öne düşüp toparlanır.
    nod = 6 * math.sin(TAU * 2 * ph + 0.5) ** 3
    P.neck_head(pitch=14 + 10 * s1 + nod, roll=16 * c1, yaw=10 * math.sin(TAU * ph + 0.8))
    for s, side in ((1, 'Left'), (-1, 'Right')):
        if sling and side == 'Right':
            P.arm('Right', (-.18, -.60, -.78), (.84, -.56, -.12), (1, -.06, -.05))
            P.fist('Right', 0.2)
            continue
        P.shrug(side, up=-4 + 2 * c1)
        wrist = (s * (0.15 + 0.01 * c1), -0.24, 0.47 + 0.01 * s1)
        P.arm_ik(side, wrist, pole=(s * 1.0, 0.4, -0.3), hand_dir=(-s * 0.25, -1, -0.35))
        P.fist(side, 0.35)
    sit_legs(P, ph)


def clip_sit_dazed(P, t, T):
    sit_dazed(P, t, T, False)


def clip_sit_dazed_sling(P, t, T):
    sit_dazed(P, t, T, True)


def clip_dragged_out(P, t, T):
    """Oturuştan geriye (+Y) çekilip sırt üstü yatışa; bacaklar havada savrulur."""
    hy = sp(t, [(0, 0), (0.15, -0.02), (0.32, 0.06), (0.75, 0.55), (1.0, 0.82), (1.12, 0.86), (1.5, 0.86)])
    lie_z = 0.14 - HIP_Z
    hz = sp(t, [(0, SIT_HZ), (0.15, SIT_HZ + 0.03), (0.45, SIT_HZ - 0.01), (0.8, -0.25), (0.95, lie_z - 0.015),
                (1.05, lie_z + 0.035), (1.16, lie_z), (1.5, lie_z)])
    pitch = sp(t, [(0, -6), (0.15, 6), (0.42, -38), (0.78, -80), (0.95, -94), (1.05, -86), (1.18, -90), (1.5, -90)])
    roll = sp(t, [(0, 0), (0.5, 6), (0.8, -5), (1.0, 3), (1.2, 0), (1.5, 0)])
    P.hips((0, hy, hz), pitch=pitch, roll=roll)
    P.spine(pitch=sp(t, [(0, 10), (0.15, -6), (0.5, 14), (0.95, -4), (1.15, 2), (1.5, 0)]), roll=-roll)
    P.neck_head(pitch=sp(t, [(0, 14), (0.12, -26), (0.5, 24), (0.9, -12), (1.05, 18), (1.22, 4), (1.5, 7)]),
                roll=sp(t, [(0, 16), (0.3, -10), (0.7, 8), (1.1, -4), (1.5, 0)]),
                yaw=sp(t, [(0, 4), (0.3, 0), (1.0, 0), (1.5, 0)]))
    Rp = P.hip_rot
    # Kollar: kucakta → havaya savrulur (yel değirmeni) → yanlara düşer (LiePose).
    up = sp(t, [(0, 0), (0.12, 1), (0.85, 1), (1.12, 0.2), (1.25, 0.0), (1.5, 0.0)])
    flop = smooth((t - 0.85) / 0.35)
    for s, side, off in ((1, 'Left', 0.0), (-1, 'Right', 1.6)):
        mill = TAU * 2.6 * t + off
        flail = Vector((s * 0.75, -0.35 + 0.55 * math.cos(mill), 0.45 + 0.4 * math.sin(mill))).normalized()
        lap = Vector((s * 0.35, -0.55, -0.75)).normalized()
        lie = Vector((s * 0.25, 0, -0.968))
        a = lap.lerp(flail, up) if flop <= 0 else flail.lerp(lie, flop)
        a = a.normalized()
        f = (a + Vector((0, -0.2 * (1 - flop), 0.25 * up * (1 - flop)))).normalized()
        P.arm(side, a, f, f)
        P.fist(side, 0.3 * (1 - up) - 0.3 * up * (1 - flop))
    # Bacaklar: oturuş açısı kalçaya bağlı kalır (gövde devrilince havaya kalkar), bisiklet çırpınması, sonra düşer.
    straight = smooth((t - 0.88) / 0.3)
    bounce = 0.18 * math.sin(PI * clamp((t - 1.12) / 0.2, 0, 1))
    for s, side, off in ((1, 'Left', 0.0), (-1, 'Right', PI)):
        kick = math.sin(TAU * 3.2 * t + off) * smooth((t - 0.25) / 0.15) * (1 - straight)
        thigh = Vector((s * 0.13, -1, 0.05 + 0.5 * kick)).normalized().lerp(Vector((0, 0, -1)), straight)
        shin = Vector((s * 0.04, -0.22 - 0.6 * kick, -1)).normalized().lerp(Vector((0, -bounce, -1)), straight)
        P.aim(side + 'UpLeg', Rp @ thigh.normalized())
        P.aim(side + 'Leg', Rp @ shin.normalized())
        P.orient(side + 'Foot', Rp @ euler(18 * (1 - straight) - 4 * straight, 0, s * 10 * (1 - straight)))
    if straight > 0:
        for s, side in ((1, 'Left'), (-1, 'Right')):
            pb = P.b(side + 'UpLeg')
            pb.location = pb.bone.matrix_local.to_3x3().inverted() @ Vector((-s * 0.031 * straight, 0, 0))
        P.upd()


# ---- kurtarıcı: minibüsten çekme

def clip_extract_pull(P, t, T):
    hz = sp(t, [(0, STAND), (0.3, -0.07), (0.45, -0.08), (0.6, -0.12), (0.72, -0.07), (0.9, -0.14), (1.02, -0.12), (1.1, -0.05),
                (1.2, -0.10), (1.35, -0.02), (1.5, STAND)])
    pitch = sp(t, [(0, 0), (0.3, 48), (0.45, 46), (0.6, -14), (0.72, 30), (0.9, -46), (1.02, -42), (1.12, -24),
                   (1.35, 4), (1.5, 0)])
    strain = math.sin(TAU * 11 * t) * smooth((t - 0.5) / 0.08) * (1 - smooth((t - 0.95) / 0.06))
    roll = sp(t, [(0, 0), (0.95, 0), (1.08, 10), (1.2, -8), (1.35, 3), (1.5, 0)])
    P.hips((0, 0, hz), pitch=pitch * 0.45, roll=roll)
    P.spine(pitch=pitch * 0.55, roll=-roll * 0.8 + 2 * strain)
    P.neck_head(pitch=sp(t, [(0, 0), (0.3, -32), (0.62, -8), (0.75, -26), (0.95, 22), (1.1, 14), (1.35, -4), (1.5, 0)]),
                roll=3 * strain - roll * 0.4, yaw=sp(t, [(0, 0), (1.05, 0), (1.15, 18), (1.3, -10), (1.5, 0)]))
    reach = smooth(t / 0.3)
    grip = sp(t, [(0, 0.25), (0.28, 0.0), (0.42, 1.0), (1.0, 1.0), (1.06, -0.2), (1.5, 0.25)])
    pull = sp(t, [(0.45, 0), (0.6, 0.65), (0.72, 0.2), (0.9, 1.0), (1.0, 1.0)])
    let_go = smooth((t - 0.98) / 0.1)
    for s, side in ((1, 'Left'), (-1, 'Right')):
        P.shrug(side, up=8 * pull, fwd=14 * (1 - pull) * reach)
        idle = P.head(side + 'Arm') + Vector((s * 0.19, -0.06, -0.24))
        inside = Vector((s * 0.13, -0.48, 0.60))
        chest = Vector((s * 0.20, -0.22, 0.72))
        held = inside.lerp(chest, pull)
        target = idle.lerp(held, reach) + Vector((0, 0, 0.012 * strain))
        if let_go > 0:
            # Son çekişte eller boşalır, kollar yel değirmeni gibi savrulur (geri sendeleme).
            fling = P.head(side + 'Arm') + Vector((s * 0.22, 0.05 * s, 0.18 + 0.06 * math.sin(TAU * 4 * t + s)))
            back = smooth((t - 1.25) / 0.25)
            target = held.lerp(fling, let_go).lerp(idle, back)
        P.arm_ik(side, target, pole=(s * 0.9, 0.4, -0.4), hand_dir=(-s * 0.15, -1, -0.25 + 0.6 * pull))
        P.fist(side, grip)
    # Ayaklar: sol önde, sağ geride dayanmış; sendelemede sağ ayak geri sekip yerine döner.
    stance = smooth(t / 0.25) * (1 - smooth((t - 1.3) / 0.2))
    hop = sp(t, [(0.98, 0), (1.08, 1), (1.22, 1), (1.32, 0)])
    lift = 0.07 * math.sin(PI * clamp((t - 0.98) / 0.12, 0, 1)) + 0.06 * math.sin(PI * clamp((t - 1.2) / 0.14, 0, 1))
    P.leg('Left', (FOOT_X + 0.02, -0.10 * stance, ANKLE_Z), pole=(0.4, -1, 0.1), yaw=10)
    P.leg('Right', (-(FOOT_X + 0.02), 0.12 * stance + 0.14 * hop, ANKLE_Z + lift), pole=(-0.4, -1, 0.1), yaw=-14)


# ---- kurtarıcı: halat

def wall_step(phase, stride, lift, stance, up):
    """Duvarda adım: (z ötelemesi, duvardan uzaklaşma). up=True: ayak basarken köke göre yukarı kayar (iniş)."""
    y, z, _ = walk_foot(phase, stride, lift, stance)
    return (y if up else -y), z


def rope_hand(P, side, z, grip=1.0, away=0.0):
    s = 1 if side == 'Left' else -1
    wrist = (s * (0.045 + 0.05 * away), ROPE_Y + 0.045 + 0.06 * away, z)
    P.arm_ik(side, wrist, pole=(s * 1.0, 0.3, -0.7), hand_dir=(-s * 0.75, -0.65, 0.05 + 0.3 * away))
    P.fist(side, grip)


def wall_foot(P, side, z, away=0.0):
    s = 1 if side == 'Left' else -1
    ankle = (s * 0.16, WALL_Y + 0.095 + away, z)
    P.leg(side, ankle, pole=(s * 0.45, -1, 0.6), pitch=-74 + 30 * away / 0.07, yaw=s * 12, auto_toe=False)


def rope_body(P, hz=0.0, pitch=0.0, roll=0.0, yaw=0.0, spine=(0, 0, 0), head=(0, 0, 0)):
    P.hips((0, 0, ROPE_HZ + hz), pitch=-8 + pitch, roll=roll, yaw=yaw)
    P.spine(pitch=8 + spine[0], roll=spine[1], yaw=spine[2])
    P.neck_head(pitch=-24 + head[0], roll=head[1], yaw=head[2])


def rope_climb_cycle(P, t, T, name, lean, strain, look=0.0):
    """Elden ele tırmanış: el yukarıda tutar, köke göre aşağı kayar (gövde yükselir), bırakıp yukarı uzanır."""
    ph = t / T
    v = ROPE_SPEEDS[name]
    stance = 0.6
    stroke = v * stance * T
    top = 1.07
    trem = math.sin(TAU * 13 * ph) * strain
    s2 = math.sin(TAU * 2 * ph)
    rope_body(P, hz=-0.012 * math.cos(TAU * 2 * ph), pitch=lean * 0.2, roll=3 * math.sin(TAU * ph),
              yaw=5 * math.sin(TAU * ph), spine=(lean * 1.0 + 2 * s2, -5 * math.sin(TAU * ph) + 1.5 * trem, -6 * math.sin(TAU * ph)),
              head=(look - 6 * strain + 4 * s2, 2 * trem, 10 * (1 - 0.7 * strain) * math.sin(TAU * ph)))
    for s, side, off in ((1, 'Left', 0.0), (-1, 'Right', 0.5)):
        p = (ph + off) % 1.0
        if p < stance:
            z = top - stroke * (p / stance)
            grip, away = 1.0, 0.0
        else:
            u = (p - stance) / (1 - stance)
            z = top - stroke + stroke * smooth(u)
            grip, away = 1.0 - math.sin(PI * u), math.sin(PI * u)
        P.shrug(side, up=10 * (z - (top - stroke)) / stroke + 2, fwd=12)
        rope_hand(P, side, z, grip, away)
    for s, side, off in ((1, 'Left', 0.5), (-1, 'Right', 0.0)):
        dz, away = wall_step(ph + off, stroke, 0.06, stance, up=False)
        wall_foot(P, side, 0.29 + dz, away)


def clip_rope_descend(P, t, T):
    """Hızlı tıpış tıpış iniş: ayak başına döngüde iki adım; eller frende, kafa yukarıda."""
    ph = t / T
    v = -ROPE_SPEEDS['Rope_Descend']
    stance = 0.55
    stride = v * stance * T / 2
    c4 = math.cos(TAU * 4 * ph)
    rope_body(P, hz=-0.01 * c4, pitch=2 * c4, roll=4 * math.sin(TAU * 2 * ph), yaw=-4 * math.sin(TAU * 2 * ph),
              spine=(-2 * c4, -4 * math.sin(TAU * 2 * ph), 3 * math.sin(TAU * 2 * ph)),
              head=(-4 + 5 * math.cos(TAU * 4 * ph - 0.8), 6 * math.sin(TAU * 2 * ph), 22 * math.sin(TAU * ph)))
    for s, side, z0, ph0 in ((1, 'Left', 1.03, 0.0), (-1, 'Right', 0.92, 0.5)):
        jig = math.sin(TAU * 4 * ph + ph0 * TAU)
        P.shrug(side, up=4 + 3 * jig, fwd=12)
        rope_hand(P, side, z0 + 0.012 * jig, 1.0)
    for s, side, off in ((1, 'Left', 0.0), (-1, 'Right', 0.5)):
        dz, away = wall_step(2 * ph + off, stride, 0.05, stance, up=True)
        wall_foot(P, side, 0.29 + dz, away)


def clip_rope_climb(P, t, T):
    rope_climb_cycle(P, t, T, 'Rope_Climb', lean=0.0, strain=0.3)


def clip_rope_hang(P, t, T):
    """Halatta asılı bekleme: sarkaç gibi hafif sallanma, aşağıya ürkek bakışlar, titreyen dizler."""
    ph = t / T
    s1, c1 = math.sin(TAU * ph), math.cos(TAU * ph)
    shiver = math.sin(TAU * 15 * ph)
    glance = math.sin(TAU * ph) ** 3
    rope_body(P, hz=0.004 * shiver, pitch=3 * c1, roll=4 * s1, yaw=6 * math.sin(TAU * ph + 0.7),
              spine=(-2 * c1, -3 * s1, -4 * math.sin(TAU * ph + 0.7)),
              head=(6 * (0.5 - 0.5 * math.cos(TAU * 2 * ph)), 4 * s1, 34 * glance))
    for s, side, z0 in ((1, 'Left', 1.03), (-1, 'Right', 0.91)):
        P.shrug(side, up=3 + 2 * c1 * s, fwd=12)
        rope_hand(P, side, z0, 1.0)
    for s, side, z0 in ((1, 'Left', 0.33), (-1, 'Right', 0.25)):
        wall_foot(P, side, z0, 0.006 * (1 + shiver * s))


# ---- kurtarıcı: sırtta taşıma

def piggy_arms(P, w=1.0, extra=None):
    """Eller binenin dizlerinin altını kavrar (hedef binenin bağ pozundaki diz noktası, Spine1'e bağlı)."""
    S = socket_local(P)
    for s, side in ((1, 'Left'), (-1, 'Right')):
        hold = S @ _PIGGY_GRIP[side]
        target = hold if extra is None else extra(side, s, hold)
        P.shrug(side, up=-6 * w, fwd=-18 * w)
        P.arm_ik(side, target, pole=(s * 1.0, 0.25, -0.1), hand_dir=(-s * 0.3, 0.7, -0.4))
        P.fist(side, 0.85)


def piggy_upper(P, hz, pitch, roll=0.0, yaw=0.0, head=(0, 0, 0), spine=(0, 0, 0), share=0.2):
    P.hips((0, 0, hz), pitch=pitch * (1 - share), roll=roll, yaw=yaw)
    P.spine(pitch=pitch * share + spine[0], roll=-roll * 0.8 + spine[1], yaw=-yaw * 0.6 + spine[2])
    P.neck_head(pitch=head[0], roll=head[1], yaw=head[2])


def clip_piggy_rider(P, t, T):
    """Hasta taşıyanın sırtında (kök = Spine1 soketi): kollar omuzlarda, bacaklar yanlarda sallanır, kafa sersem."""
    ph = t / T
    s1, c1 = math.sin(TAU * ph), math.cos(TAU * ph)
    s2 = math.sin(TAU * 2 * ph)
    P.hips((0, 0, 0.008 * s2), pitch=RIDER_LEAN[0] + 2 * s2, roll=3 * s1)
    P.spine(pitch=RIDER_LEAN[1] + 2 * s2, roll=-3 * s1, yaw=2 * s1)
    P.neck_head(pitch=8 + 6 * math.sin(TAU * 2 * ph - 0.6), roll=18 * c1, yaw=12 * s1)
    for s, side in ((1, 'Left'), (-1, 'Right')):
        P.shrug(side, up=6, fwd=16)
        shoulder = Vector((s * RIDER_HAND[0], RIDER_HAND[1], RIDER_HAND[2])) - Vector(PIGGY_OFFSET)
        P.arm_ik(side, shoulder + Vector((0, 0.01 * s1 * s, 0.01 * s2)), pole=(s * 1.0, 0.2, -0.6),
                 hand_dir=(-s * 0.15, -1, -0.9))
        P.fist(side, 0.6)
    for s, side, off in ((1, 'Left', 0.0), (-1, 'Right', PI)):
        sw = math.sin(TAU * ph + off)
        P.aim(side + 'UpLeg', (s * RIDER_THIGH[0], RIDER_THIGH[1], RIDER_THIGH[2]))
        P.aim(side + 'Leg', (s * 0.35, 0.55 + 0.25 * sw, -0.8))
        P.orient(side + 'Foot', euler(30 + 18 * sw, 0, s * 14))


def compute_piggy_grip(P):
    """Binenin t=0 pozundaki diz arkası noktası, taşıyanın bağ çerçevesinde."""
    P.reset()
    clip_piggy_rider(P, 0.0, 1.0)
    for s, side in ((1, 'Left'), (-1, 'Right')):
        knee = P.head(side + 'Leg')
        hip = P.head(side + 'UpLeg')
        # Uyluğun alt yüzü, dize yakın: el burada kavrar (bilek hedefi biraz içeride ve aşağıda).
        _PIGGY_GRIP[side] = hip.lerp(knee, GRIP_OFFSET[1]) + Vector((s * GRIP_OFFSET[0], GRIP_OFFSET[3], GRIP_OFFSET[2]))
    P.reset()


def clip_piggy_walk(P, t, T):
    ph = t / T
    stride, stance = walk_stride('Piggyback_Walk', T)
    c2 = math.cos(2 * TAU * ph)
    s1 = math.sin(TAU * ph)
    bob = -0.115 - 0.012 * c2
    piggy_upper(P, bob, pitch=22 + 3 * c2, roll=7 * s1, yaw=5 * math.sin(TAU * ph + 0.4),
                head=(PIGGY_HEAD[1] - PIGGY_HEAD[0] * (22 + 3 * c2) + 6 * math.cos(2 * TAU * ph - 1.0), 6 * math.sin(TAU * ph - 0.8), 3 * s1),
                spine=(0, -4 * math.sin(TAU * ph - 0.35), 0))
    piggy_arms(P)
    walk_legs(P, ph, stride=stride, lift=0.10, width=0.05, toe_out=14, stance=stance)


def clip_piggy_pickup(P, t, T):
    hz = sp(t, [(0, STAND), (0.2, -0.06), (0.45, -0.21), (0.6, -0.22), (0.78, -0.20), (1.0, -0.13), (1.12, -0.09),
                (1.26, -0.12), (1.5, -0.105)])
    pitch = sp(t, [(0, 0), (0.2, 12), (0.45, 44), (0.6, 50), (0.78, 44), (1.0, 34), (1.26, 28), (1.5, 31)])
    wob = math.sin(TAU * 7 * t) * smooth((t - 0.75) / 0.1) * (1 - smooth((t - 1.2) / 0.2))
    piggy_upper(P, hz + 0.01 * wob, pitch, roll=7 * wob, share=0.1,
                head=(sp(t, [(0, 0), (0.2, -10), (0.45, -14), (0.53, -6), (0.7, -9), (1.0, -5), (1.5, -5)]), 4 * wob,
                      sp(t, [(0, 0), (0.15, 40), (0.42, 46), (0.55, 0), (1.5, 0)])))
    hook = smooth((t - 0.3) / 0.22)
    wave = math.sin(TAU * 4 * t) * (1 - hook)

    def reach(side, s, hold):
        # Önce "hadi bin!" diye eller arkada sallanır, sonra diz altlarını kavrar.
        idle = P.head(side + 'Arm') + Vector((s * 0.19, -0.06, -0.24))
        start = idle.lerp(hold + Vector((s * 0.04, 0.02, 0.08 + 0.05 * wave)), smooth(t / 0.3))
        return start.lerp(hold, hook)
    piggy_arms(P, w=hook, extra=reach)
    for s, side in ((1, 'Left'), (-1, 'Right')):
        P.leg(side, (s * (FOOT_X + 0.05 * smooth(t / 0.3)), 0.0, ANKLE_Z), pole=(s * 0.55, -1, 0.05), yaw=s * 14)


def clip_piggy_putdown(P, t, T):
    hz = sp(t, [(0, -0.105), (0.35, -0.21), (0.5, -0.22), (0.66, -0.11), (0.82, -0.03), (0.98, -0.04), (1.2, STAND)])
    pitch = sp(t, [(0, 31), (0.35, 46), (0.5, 48), (0.66, 12), (0.82, -26), (0.98, -22), (1.2, 0)])
    rub = smooth((t - 0.66) / 0.12) * (1 - smooth((t - 0.98) / 0.14))
    piggy_upper(P, hz, pitch, roll=5 * math.sin(TAU * 4 * t) * rub, share=0.1,
                head=(sp(t, [(0, -5), (0.5, -6), (0.66, 0), (0.85, -25), (1.0, -15), (1.2, 0)]), 0,
                      sp(t, [(0, 0), (0.5, 0), (0.62, 30), (0.75, 0), (1.2, 0)])))
    release = smooth((t - 0.5) / 0.08)
    to_back = smooth((t - 0.62) / 0.14)
    to_idle = smooth((t - 0.98) / 0.2)

    def go(side, s, hold):
        idle = P.head(side + 'Arm') + Vector((s * 0.19, -0.06, -0.24))
        low_back = P.head('Hips') + P.hip_rot @ Vector((s * 0.19, 0.18, 0.12))
        flick = hold + Vector((s * 0.06, -0.08, 0.06))
        x = hold.lerp(flick, release).lerp(low_back, to_back).lerp(idle, to_idle)
        return x + Vector((0, 0, 0.012 * math.sin(TAU * 8 * t) * rub))
    piggy_arms(P, w=1.0 - release, extra=go)
    for s, side in ((1, 'Left'), (-1, 'Right')):
        P.leg(side, (s * (FOOT_X + 0.05 * (1 - smooth((t - 0.9) / 0.3))), 0.0, ANKLE_Z), pole=(s * 0.55, -1, 0.05), yaw=s * 14)


def clip_piggy_rope_climb(P, t, T):
    rope_climb_cycle(P, t, T, 'Piggyback_Rope_Climb', lean=PIGGY_ROPE_LEAN, strain=1.0, look=PIGGY_ROPE_LOOK)


def clip_rope_pull(P, t, T):
    """Yukarıda, ayaklar açık: halatı elden ele çeker; her çekişte gövde geriye kaykılır, dizler titrer."""
    ph = t / T
    stance = 0.55
    rock = math.cos(TAU * 2 * ph)
    trem = math.sin(TAU * 12 * ph)
    hz = -0.085 - 0.015 * rock + 0.003 * trem
    P.hips((0, 0, hz), pitch=-2 + 7 * rock, roll=4 * math.sin(TAU * ph), yaw=2 * math.sin(TAU * ph))
    P.spine(pitch=12 + 5 * rock, roll=-4 * math.sin(TAU * ph) + 1.5 * trem, yaw=-3 * math.sin(TAU * ph))
    P.neck_head(pitch=-6 - 10 * rock + 3 * math.sin(TAU * 2 * ph - 1.2), roll=3 * trem, yaw=8 * math.sin(TAU * ph))
    for s, side, off in ((1, 'Left', 0.0), (-1, 'Right', 0.5)):
        p = (ph + off) % 1.0
        if p < stance:
            u = p / stance
            y, z = PULL_FRONT + (PULL_BACK - PULL_FRONT) * u, PULL_Z
            grip, away = 1.0, 0.0
        else:
            u = (p - stance) / (1 - stance)
            away = math.sin(PI * u)
            y, z = PULL_BACK + (PULL_FRONT - PULL_BACK) * smooth(u), PULL_Z + 0.09 * away
            grip = 1.0 - away
        wrist = (s * (0.10 + 0.10 * away), y + 0.01 - 0.04 * away, z + 0.04)
        P.shrug(side, up=3, fwd=14 + 8 * (1 - u if p < stance else u))
        P.arm_ik(side, wrist, pole=(s * 1.0, 0.3, -0.2), hand_dir=(-s * 0.7, -0.6, -0.1))
        P.fist(side, grip)
    for s, side in ((1, 'Left'), (-1, 'Right')):
        P.leg(side, (s * (FOOT_X + 0.12), -0.03 * s, ANKLE_Z + 0.0), pole=(s * 0.7, -1, 0.1), yaw=s * 24)


# ---------------------------------------------------------------- r4: sedyenin ön taşıyıcısı
STRETCHER_META = ROOT / 'Export/Stretcher/stretcher_meta.json'
STRETCHER = ROOT / 'Export/Stretcher/SM_Stretcher.fbx'
GRIP_POINT = 0.05                      # avuç: el kemiği başından parmaklara doğru 5 cm (rope_grip ile aynı)
FRONT_GRIP = (0.293, 0.19, 0.665)      # ön taşıyıcının avuç hedefi: x yarım sap aralığı, y kalçanın arkası, z (arka taşıyıcıyla aynı: sedye düz)
FRONT_HAND_DIR = (0.12, 0.30, -1.0)    # el aşağı, hafif geriye ve dışa sarkar (x dışa)
FRONT_LEAN = (6.0, 8.0)                # kalça ve omurga öne eğimi (derece): yük çeker gibi
GRIP_CLIPS = {'Carry_Stretcher_Walk': 'Handle_Rear', 'Carry_Stretcher_Front_Walk': 'Handle_Front'}
_GRIP_SAMPLES = {}


def clip_carry_stretcher_front(P, t, T):
    """Carry_Stretcher_Walk'ın ön ucu: aynı bacak ritmi ve bob, ama gövde öne eğik ve kollar arkada saplarda."""
    ph = t / T
    stride, stance = walk_stride('Carry_Stretcher_Front_Walk', T)
    c2 = math.cos(2 * TAU * ph)
    s1 = math.sin(TAU * ph)
    bob = -0.080 - 0.016 * c2
    P.hips((0, 0, bob), pitch=FRONT_LEAN[0] + 3 * c2, roll=13 * s1, yaw=8 * math.sin(TAU * ph + 0.4))
    P.spine(pitch=FRONT_LEAN[1] + 4 * math.cos(2 * TAU * ph - 0.6), roll=-15 * math.sin(TAU * ph - 0.35),
            yaw=-10 * math.sin(TAU * ph + 0.4))
    # Çene önde, gözler yolda: gövde eğimini fazlasıyla telafi eden, her adımda kafa sallanır.
    P.neck_head(pitch=-18 - 7 * math.cos(2 * TAU * ph - 1.3), roll=9 * math.sin(TAU * ph - 1.1), yaw=4 * s1)
    for s, side in ((1, 'Left'), (-1, 'Right')):
        P.shrug(side, up=6 + 4 * c2, fwd=-14)
    hz = FRONT_GRIP[2] + 0.5 * (bob + 0.080)
    for s, side in ((1, 'Left'), (-1, 'Right')):
        d = Vector((s * FRONT_HAND_DIR[0], FRONT_HAND_DIR[1], FRONT_HAND_DIR[2])).normalized()
        grip = Vector((s * FRONT_GRIP[0], FRONT_GRIP[1], hz))
        P.arm_ik(side, grip - d * GRIP_POINT, pole=(s * 0.8, 0.7, 0.1), hand_dir=d, hand_twist=0)
        P.fist(side, 0.9)
    walk_legs(P, ph, stride=stride, lift=0.085, width=0.04, toe_out=16, stance=stance)


def sample_grip(P, name):
    """Klibin her karesinde avuç noktası ve el yönü (kök yerel); eğrileri değiştirmez."""
    rec = _GRIP_SAMPLES.setdefault(name, {'Left': [], 'Right': []})
    for side in ('Left', 'Right'):
        pb = P.b(side + 'Hand')
        y = pb.matrix.col[1].xyz.normalized()
        rec[side].append((pb.head + y * GRIP_POINT, y))


def grip_meta(name):
    marker = GRIP_CLIPS[name]
    sm = json.loads(STRETCHER_META.read_text(encoding='utf-8'))
    mpos = Vector(sm['markers'][marker]['position_m'])
    rec = _GRIP_SAMPLES[name]
    hands, dirs, zr = {}, {}, {}
    for side in ('Left', 'Right'):
        pts = [p for p, _ in rec[side][:-1]]          # döngü: son kare ilk kareyle aynı
        mean = sum(pts, Vector()) / len(pts)
        hands[side] = mean
        dirs[side] = (sum((d for _, d in rec[side][:-1]), Vector()) / len(pts)).normalized()
        zr[side] = (min(p.z for p in pts), max(p.z for p in pts))
        dev = max((p - mean).length for p in pts)
        print('GRIP', name, side, tuple(round(x, 3) for x in mean), 'dev=%.4f' % dev, flush=True)
    mid = (hands['Left'] + hands['Right']) / 2
    front = marker == 'Handle_Front'
    r = lambda v: [round(x, 3) for x in v]
    return {'handle_grip': {
        'bones': [PREFIX + 'LeftHand', PREFIX + 'RightHand'],
        'grip_point_hand_local_m': [0.0, GRIP_POINT, 0.0],
        'grip_root_local_m': {'Left': r(hands['Left']), 'Right': r(hands['Right'])},
        'grip_z_range_m': {s: [round(a, 3), round(b, 3)] for s, (a, b) in zr.items()},
        'hand_forward_root_local': {s: r(d) for s, d in dirs.items()},
        'handle_axis_root_local': [0.0, 1.0, 0.0],
        'stretcher_marker': marker,
        'stretcher_side': 'arkada (+Y)' if front else 'önde (-Y)',
        'marker_root_local_m': r(mid),
        'stretcher_root_local_m': r(mid - mpos),
        'handle_spacing_m': round(hands['Left'].x - hands['Right'].x, 3),
        'note': ('Avuç noktası = el kemiği başı + kemik yönünde %.2f m (parmaklara doğru). Konumlar kök yerel Blender '
                 'koordinatı (+X sol, -Y ön, +Z yukarı; Unity yerel = (-x, z, -y)), döngü ortalaması; adım sallantısıyla '
                 'z aralığında oynar. Sedye taşıyanla aynı yöne bakar (sedye -Y = yürüyüş yönü); %s işareti iki avucun '
                 'ortasına oturur, sap ekseni kök +Y boyunca. Unity IK: iki el hedefini sedyenin sap noktalarına kilitleyin '
                 '(sol el +X, sağ el -X direği).' % (GRIP_POINT, marker))}}


# ---------------------------------------------------------------- r4a: dolap → taşı → bağla → at
COIL_CLIPS = {'Cabinet_TakeCoil', 'Carry_Coil_Idle', 'Carry_Coil_Walk', 'Rope_TieStake', 'Rope_Throw'}
COIL_HALF = 0.204
COIL_HOME = (0, -0.33, 0.80)
COIL_CABINET = (.025, -0.40, 0.78)
COIL_GROUND = (.27, -0.43, 0.222)
COIL_GROUND_YAW = 35
COIL_STAKE = (0, -0.51, 0)
COIL_RELEASE = 22
_COIL_SAMPLES = {}


def coil_body(P, hz=STAND, pitch=0, roll=0, head=0, yaw=0):
    P.hips((0, 0, hz), pitch=pitch*0.4, roll=roll, yaw=yaw*.35)
    P.spine(pitch=pitch*0.6, roll=-roll*0.65, yaw=yaw*.65)
    P.neck_head(pitch=head, roll=-roll*0.5, yaw=-yaw*.55)
    stand_legs(P, dx=0.025, toe_out=12)


def coil_hands(P, center, blend=1.0, half=COIL_HALF, yaw=0):
    rot = euler(yaw=yaw)
    for s, side in ((1, 'Left'), (-1, 'Right')):
        d = rot @ Vector((-s*0.6, -0.8, 0))
        target = Vector(center) + rot @ Vector((s*half, 0, 0)) - d*GRIP_POINT
        if blend < 1:
            target = P.head(side+'Hand').lerp(target, blend)
        P.arm_ik(side, target, pole=rot @ Vector((s*1.0, 0.2, -0.35)), hand_dir=d)
        P.fist(side, 0.85*blend)


def clip_coil_idle(P, t, T):
    ph = TAU*t/T
    bob = 0.004*(1-math.cos(ph))
    coil_body(P, STAND+bob, head=2*math.sin(ph))
    coil_hands(P, Vector(COIL_HOME)+Vector((0, 0, bob)))


def clip_coil_walk(P, t, T):
    ph = t/T
    stride, stance = walk_stride('Carry_Coil_Walk', T)
    c2 = math.cos(2*TAU*(ph-stance/2))
    bob = -0.082-0.018*c2
    P.hips((0, 0, bob), pitch=8, roll=5*math.sin(TAU*ph))
    P.spine(pitch=4, roll=-4*math.sin(TAU*ph))
    P.neck_head(pitch=-8+5*math.sin(2*TAU*ph), roll=5*math.sin(TAU*ph-0.5))
    coil_hands(P, (0, -0.32, 0.75+0.3*(bob+0.082)))
    walk_legs(P, ph, stride, 0.10, width=0.01, toe_out=8, stance=stance)


def clip_take_coil(P, t, T):
    hz = sp(t, [(0, STAND), (.32, STAND), (.64, -.035), (.86, -.055), (1.1, .006), (T, STAND)])
    pitch = sp(t, [(0, 0), (.32, 10), (.55, 8), (.86, -9), (1.1, 4), (T, 0)])
    coil_body(P, hz, pitch, head=-pitch*.8)
    neutral_arms(P)
    center = sp(t, [(0, COIL_CABINET), (.4, COIL_CABINET), (.65, (.035,-.38,.78)),
                    (.86, (.025,-.35,.79)), (1.1, (0,-.35,.79)), (T, COIL_HOME)])
    coil_hands(P, center, smooth(t/.32))


def clip_tie_stake(P, t, T):
    crouch = smooth(t/.50)*(1-smooth((t-1.5)/.5))
    pull = math.sin(TAU*3*(t-.65)/.70)*smooth((t-.6)/.1)*(1-smooth((t-1.3)/.1))
    knot = smooth((t-.5)/.15)*(1-smooth((t-1.35)/.15))
    yaw = COIL_GROUND_YAW*crouch*(1-knot)
    coil_body(P, STAND-.245*crouch, 70*crouch+5*pull, head=-45*crouch,yaw=yaw)
    if t <= .50:
        center = Vector(COIL_HOME).lerp(Vector(COIL_GROUND), smooth(t/.50))
        coil_hands(P, center, yaw=yaw)
    elif t < 1.5:
        # Kangal yerde kalır; avuçlar kazık yakasına yaklaşır, halat ucunu üç kez çeker.
        w = smooth((t-.5)/.15)*(1-smooth((t-1.35)/.15))
        center = Vector(COIL_GROUND).lerp(Vector((COIL_STAKE[0], COIL_STAKE[1]+.035*pull, .30+.025*pull)), w)
        coil_hands(P, center, half=COIL_HALF*(1-w)+.055*w, yaw=yaw)
    else:
        center = Vector(COIL_GROUND).lerp(Vector(COIL_HOME), smooth((t-1.5)/.5))
        coil_hands(P, center, yaw=yaw)


def clip_throw_coil(P, t, T):
    release_t = (COIL_RELEASE-1)/FPS
    pitch = sp(t, [(0, 0), (.33, -12), (.48, -8), (release_t, 34), (.86, 26), (1.02, -10), (1.18, 9), (T, 0)])
    roll = sp(t, [(0, 0), (.70, 0), (.86, 16), (1.02, -14), (1.18, 9), (T, 0)])
    # Kalça arkasına kadar büyük hazırlık yayı; ileri-aşağı bırakış ve 0.5 sn sendeleme.
    yaw = -145*smooth(t/.33)*(1-smooth((t-.43)/.24))
    coil_body(P, sp(t, [(0, STAND), (.33,-.04), (.63,-.065), (.85,-.02), (T,STAND)]), pitch, roll, -pitch*.7, yaw)
    center = euler(yaw=yaw) @ Vector(sp(t, [(0, COIL_HOME), (.33,(0,-.34,.70)), (.48,(0,-.34,.76)), (release_t,(0,-.46,.62)), (T,(0,-.46,.62))]))
    if t <= release_t:
        coil_hands(P, center, yaw=yaw)
    else:
        follow = smooth((t-release_t)/.20)
        recover = smooth((t-1.10)/.30)
        neutral_arms(P)
        for s, side in ((1,'Left'),(-1,'Right')):
            start = Vector((s*COIL_HALF, -.46, .62))-Vector((-s*.6,-.8,0))*GRIP_POINT
            flail = P.head(side+'Arm')+Vector((s*.12,-.31,-.08+0.04*s*math.sin(TAU*(t-.7)/.45)))
            target = start.lerp(flail, follow)
            target = target.lerp(P.head(side+'Hand'), recover)
            P.arm_ik(side, target, pole=(s, .3, -.2), hand_dir=(s*.2,-.9,-.3))
            P.fist(side, .15)
        if t >= T:
            neutral(P)


def coil_attached(name, f):
    if name == 'Cabinet_TakeCoil':
        return f >= 11
    if name == 'Rope_TieStake':
        return f <= 16 or f >= 46
    if name == 'Rope_Throw':
        return f <= COIL_RELEASE
    return True


def sample_coil(P, name, frame):
    hands = {s: P.b(s+'Hand').head + P.b(s+'Hand').matrix.col[1].xyz.normalized()*GRIP_POINT for s in ('Left','Right')}
    _COIL_SAMPLES.setdefault(name, []).append({'frame': frame, 'attached': coil_attached(name, frame),
        'hands_root_local_m': {s: [round(x,6) for x in p] for s,p in hands.items()}})


def coil_meta(name):
    rec = _COIL_SAMPLES[name]
    m = {'coil_grip': {'prop': 'SM_RopeCoil', 'bones': {s: PREFIX+s+'Hand' for s in ('Left','Right')},
        'markers': {'Left':'Grip_L','Right':'Grip_R'}, 'palm_offset_hand_local_m':[0,GRIP_POINT,0],
        'grip_root_local_m': rec[0]['hands_root_local_m'], 'samples':rec,
        'attachment': 'İki avucun ortası kangal merkezi; Grip_R→Grip_L kangal +X ekseni. Kangal +Z taşıyan kök +Z yönüne en yakın dikeydir.',
        'spacing_m': COIL_HALF*2, 'space':'character root local, Blender Z up / -Y forward',
        'note':'El mesafesi yalnız attached=true karelerde Grip_L/R ile eşleşir. IK hedefleri prop boşlarıdır; uç bırakılınca eller serbesttir.'}}
    if name == 'Cabinet_TakeCoil':
        m['grab_frame'] = 11
        m['cabinet_coil_root_local_m'] = list(COIL_CABINET)
    if name == 'Rope_TieStake':
        m.update({'stake_hand_frames':[21,40], 'stake_root_local_m':list(COIL_STAKE),
            'coil_ground_frames':[17,45], 'coil_ground_root_local_m':list(COIL_GROUND),
            'coil_ground_rotation_euler_deg':[0,0,COIL_GROUND_YAW], 'tug_frames':[22,29,36]})
    if name == 'Rope_Throw':
        hands = rec[COIL_RELEASE-1]['hands_root_local_m']
        prev = rec[COIL_RELEASE-2]['hands_root_local_m']
        direction = ((Vector(hands['Left'])+Vector(hands['Right']))-(Vector(prev['Left'])+Vector(prev['Right']))).normalized()
        m.update({'release_frame':COIL_RELEASE, 'release_hand_root_local_m':hands,
            'release_position_root_local_m':[(hands['Left'][i]+hands['Right'][i])/2 for i in range(3)],
            'throw_direction_root_local':[round(c,6) for c in direction],
            'stumble_frames':[25,40], 'preview_flight_speed_mps':1.8,
            'preview_flight_note':'Şeritte bırakış sonrası balistik uçuş; oyunda aynı olayda fizik devralır.'})
    return m


# ---------------------------------------------------------------- r4b: savunma ve hırsız (ortak 31 kemik)
COMBAT_CLIPS = {name for name, _, _, _, _, ch in CLIPS if ch in ('crew', 'thief')}
COMBAT_EVENTS = {
    'Push_Shove': {'hit_frame': 9},
    'Tackle_Dive': {'hit_frame': 12, 'ground_frame': 22},
    'Throw_Release': {'release_frame': 8},
    'Whistle_Blow': {'blow_frames': [15, 27]},
    'Thief_Grab': {'grab_frame': 11},
    'Thief_Punch': {'hit_frame': 14},
    'Thief_ThrowInVan': {'release_frame': 14},
}


def capture_pose(P, fn):
    P.reset()
    fn(P)
    return {b.name: (b.location.copy(), b.rotation_quaternion.copy()) for b in P.pb}


def pose_sequence(P, t, keys):
    """Ortak ana pozlar arasında yumuşak quaternion geçişi; sınır pozları birebir korunur."""
    if t <= keys[0][0]:
        keys[0][1](P)
        return
    if t >= keys[-1][0]:
        keys[-1][1](P)
        return
    for (a, fa), (b, fb) in zip(keys, keys[1:]):
        if a <= t <= b:
            pa, pb = capture_pose(P, fa), capture_pose(P, fb)
            u = smooth((t-a)/(b-a))
            for bone in P.pb:
                la, qa = pa[bone.name]
                lb, qb = pb[bone.name]
                bone.location = la.lerp(lb, u)
                bone.rotation_quaternion = qa.slerp(qb, u)
            P.upd()
            return


def combat_body(P, hz=STAND, pitch=0, roll=0, yaw=0, head=0):
    coil_body(P, hz, pitch, roll, head, yaw)
    neutral_arms(P)


def combat_reach(P, side, offset, pole=None, fist=.15):
    """Omuzdan göğüs çerçevesinde hedef; iri gövdeden dışarıda dirsek."""
    s = 1 if side == 'Left' else -1
    C = P.chest()
    P.arm_ik(side, P.head(side+'Arm') + C @ Vector(offset),
             pole=C @ Vector(pole or (s, .3, -.3)), hand_dir=C @ Vector((s*.15, -1, 0)))
    P.fist(side, fist)


def shove_pose(P, extended=False):
    combat_body(P, -.075 if extended else -.04, 25 if extended else -8, head=-12 if extended else 4)
    for s, side in ((1,'Left'),(-1,'Right')):
        combat_reach(P, side, (s*.10, -.34 if extended else -.14, -.04 if extended else .04))


def clip_shove(P, t, T):
    pose_sequence(P,t,[(0,neutral),(.10,shove_pose),(8/FPS,lambda p:shove_pose(p,True)),(.40,lambda p:shove_pose(p,True)),(T,neutral)])


def prone_pose(P):
    # Yatay kök sabit; yalnız kalça yüksekliği ve dönüşü değişir. Kafa/eller zeminin üstünde.
    P.hips((0,0,.215-HIP_Z),pitch=90)
    P.spine()
    P.neck_head(pitch=-18)
    for s,side in ((1,'Left'),(-1,'Right')):
        P.aim(side+'UpLeg',(s*.09,1,-.12))
        P.aim(side+'Leg',(0,1,-.10))
        P.orient(side+'Foot',euler(72,0,s*8))
        P.arm(side,(s*.70,-.20,.70),(s*.15,-.05,1),(0,-.08,1))
        P.fist(side,.05)


def dive_launch(P):
    combat_body(P,.18,45,head=-20)
    for s,side in ((1,'Left'),(-1,'Right')):
        P.arm(side,(s*.65,-.4,.65),(s*.15,-.5,.85))
        P.leg(side,(s*.15,.12, .22),pitch=30,auto_toe=False)


def dive_prepare(P):
    combat_body(P,-.11,22,head=-12)
    for s,side in ((1,'Left'),(-1,'Right')):
        combat_reach(P,side,(s*.15,.17,-.19))


def clip_tackle(P,t,T):
    pose_sequence(P,t,[(0,dive_prepare),(.20,dive_launch),(21/FPS,prone_pose),(T,prone_pose)])


def getup_kneel(P):
    combat_body(P,-.19,46,head=-20)
    for s,side in ((1,'Left'),(-1,'Right')):
        P.arm_ik(side,(s*.31,-.34,.12),pole=(s,.4,-.3),hand_dir=(0,-1,0))


def clip_tackle_getup(P,t,T):
    feet = {}
    if 0 < t < .35:
        # Sadece dönüşleri karıştırmak bacakları zeminin altından geçirir. Dizleri toplarken
        # bilekler iki ana poz arasında yukarı kavis çizer, IK dizleri zeminin üstünde tutar.
        P.reset()
        prone_pose(P)
        feet = {s:P.head(s+'Foot') for s in ('Left','Right')}
        P.reset()
    pose_sequence(P,t,[(0,prone_pose),(.35,getup_kneel),(.65,lambda p:combat_body(p,-.12,25)),(T,neutral)])
    if feet:
        u = smooth(t/.35)
        for s,side in ((1,'Left'),(-1,'Right')):
            ankle = feet[side].lerp(Vector((s*(FOOT_X+.025),0,ANKLE_Z)),u)
            ankle.z += .10*math.sin(PI*u)
            P.leg(side,ankle,pole=(s*.3,1-2*u,.8*(1-u)),pitch=72*(1-u),yaw=s*(8+4*u),auto_toe=False)


def aim_pose(P, sway=0):
    combat_body(P,-.035,-5,roll=2*sway,yaw=-16,head=6)
    # Sağ el büyük kafanın dış yanından yukarı-geriye; önkol başın içinden geçmez.
    combat_reach(P,'Right',(-.16,.12,.23),pole=(-1,.35,.2),fist=.9)
    combat_reach(P,'Left',(.12,-.20,-.10),fist=.3)
    P.neck_head(pitch=6,yaw=12+3*sway)


def clip_throw_aim(P,t,T):
    aim_pose(P,math.sin(TAU*t/T))


def throw_release_pose(P):
    combat_body(P,-.065,24,yaw=12,head=-15)
    combat_reach(P,'Right',(-.12,-.32,.08),pole=(-1,-.2,.2),fist=.05)
    combat_reach(P,'Left',(.22,.08,-.14))


def throw_follow(P):
    combat_body(P,-.04,15,yaw=18,head=-10)
    combat_reach(P,'Right',(-.10,-.25,-.19))
    combat_reach(P,'Left',(.20,.06,-.15))


def clip_throw_release(P,t,T):
    pose_sequence(P,t,[(0,aim_pose),(7/FPS,throw_release_pose),(.37,throw_follow),(T,neutral)])


def whistle_mouth(P):
    combat_body(P,STAND,-7,head=6)
    # Ağız yüzün ön yüzünde; önce dışarıdan uzanır, işaret/başparmak düdüğü tutar.
    P.arm_ik('Right',(.01,-.245,1.08),pole=(-1,-.3,.05),hand_dir=(0,-1,.3))
    P.fist('Right',.65)


def whistle_exhale(P):
    combat_body(P,-.045,24,head=-14)
    for s,side in ((1,'Left'),(-1,'Right')):
        combat_reach(P,side,(s*.30,-.07,.02),fist=.1)


def clip_whistle(P,t,T):
    pose_sequence(P,t,[(0,neutral),(.23,whistle_mouth),(.40,whistle_mouth),(14/FPS,whistle_exhale),(.88,whistle_exhale),(T,neutral)])
    w=smooth((t-.44)/.10)*(1-smooth((t-.85)/.15))
    P.rot('Head',pitch=-8*w,roll=3*w*math.sin(TAU*12*t))
    P.rot('Spine2',pitch=8*w,roll=2*w*math.sin(TAU*12*t))


def hit_pose(P):
    combat_body(P,-.085,-24,roll=10,head=18)
    for s,side in ((1,'Left'),(-1,'Right')):
        combat_reach(P,side,(s*.26,.08,.12))
    P.leg('Left',(.17,.08,.15),pitch=16,auto_toe=False)


def clip_hitreact(P,t,T):
    pose_sequence(P,t,[(0,neutral),(.10,hit_pose),(.25,lambda p:combat_body(p,-.07,8,roll=-8)),(T,neutral)])


def thief_run(P,t,T,carry=True):
    ph=t/T
    stride,stance=walk_stride('Thief_Run_Carry',T)
    bob=-.105-.018*math.cos(2*TAU*(ph-stance/2))
    P.hips((0,0,bob),pitch=12,roll=5*math.sin(TAU*ph))
    P.spine(pitch=7,roll=-4*math.sin(TAU*ph))
    P.neck_head(pitch=-15+5*math.sin(2*TAU*ph),yaw=7*math.sin(TAU*ph))
    if carry:
        coil_hands(P,(0,-.34,.73+.25*(bob+.105)))
    else:
        for s,side in ((1,'Left'),(-1,'Right')):
            swing=s*math.sin(TAU*ph)
            P.arm(side,(s*.65,-.55*swing,-.7),(s*.25,-.9,.3+.4*swing))
            P.fist(side,.65)
    walk_legs(P,ph,stride,.14,width=.012,toe_out=8,stance=stance)


def clip_thief_run(P,t,T):
    thief_run(P,t,T,False)


def clip_thief_sneak(P,t,T):
    ph=t/T
    stride,stance=walk_stride('Thief_Sneak_Walk',T)
    P.hips((0,0,-.13-.012*math.cos(2*TAU*ph)),pitch=14,roll=6*math.sin(TAU*ph))
    P.spine(pitch=14,roll=-7*math.sin(TAU*ph))
    P.neck_head(pitch=-20,yaw=18*math.sin(TAU*ph))
    for s,side in ((1,'Left'),(-1,'Right')):
        P.arm(side,(s*.7,-.5,-.35),(s*.1,-.8,.5),(0,-.4,-1))
        P.fist(side,.5)
        y,z,_=walk_foot(ph+(0 if s==1 else .5),stride,.14,stance)
        # Sabit 15° parmak ucu; basış sırasında bilek yolu doğrusal kalır.
        P.leg(side,(s*.15,y,ANKLE_Z+.022+z),pitch=15,yaw=s*9,auto_toe=False)
        P.rot(side+'ToeBase',pitch=-15)


def grab_low(P):
    combat_body(P,-.23,63,head=-36)
    coil_hands(P,(0,-.48,.24))


def carry_start(P):
    thief_run(P,0,.4)


def clip_thief_grab(P,t,T):
    pose_sequence(P,t,[(0,neutral),(10/FPS,grab_low),(.46,grab_low),(T,carry_start)])


def punch_windup(P):
    combat_body(P,-.07,-12,roll=-7,yaw=-65,head=8)
    P.arm('Right',(-.9,.40,.13),(-.15,.65,.70),(-.10,-1,0))
    P.fist('Right',1)
    combat_reach(P,'Left',(.14,-.21,-.06),fist=.8)


def punch_arc(P):
    combat_body(P,-.085,6,yaw=-5,head=-4)
    P.arm('Right',(-.94,-.30,.05),(-.25,-.97,.02),(.10,-1,0))
    P.fist('Right',1)
    combat_reach(P,'Left',(.24,-.06,-.10),fist=.8)


def punch_hit(P):
    combat_body(P,-.10,22,roll=8,yaw=65,head=-12)
    P.arm('Right',(-.65,-.75,.10),(.15,-.98,.02),(.25,-1,0))
    P.fist('Right',1)
    combat_reach(P,'Left',(.26,.04,-.1),fist=.6)


def punch_follow(P):
    combat_body(P,-.12,27,roll=16,yaw=88,head=-12)
    P.arm('Right',(-.55,-.82,-.12),(.15,-.9,-.4),(.30,-.9,-.3))
    P.fist('Right',1)
    combat_reach(P,'Left',(.27,.10,.02),fist=.3)


def clip_thief_punch(P,t,T):
    pose_sequence(P,t,[(0,neutral),(.23,punch_windup),(.33,punch_arc),(13/FPS,punch_hit),(.56,punch_follow),(T,neutral)])


def clip_thief_stunned(P,t,T):
    a=TAU*t/T
    combat_body(P,-.075-.015*math.cos(2*a),6+9*math.cos(a),roll=13*math.sin(a))
    P.neck_head(pitch=15*math.cos(a),roll=16*math.sin(a),yaw=20*math.sin(a))
    for s,side in ((1,'Left'),(-1,'Right')):
        P.arm(side,(s*.65,.05,-.8),(s*.35,-.1,-1),(s*.15,.1,-1))


def van_release(P):
    combat_body(P,STAND,16,head=-12)
    for s,side in ((1,'Left'),(-1,'Right')):
        combat_reach(P,side,(s*.10,-.28,.20),pole=(s,.1,.1),fist=.05)


def clip_thief_van(P,t,T):
    pose_sequence(P,t,[(0,carry_start),(.20,lambda p:shove_pose(p)),(13/FPS,van_release),(.60,van_release),(T,neutral)])


def combat_meta(name):
    m = dict(COMBAT_EVENTS.get(name, {}))
    if name == 'Throw_Aim':
        m['hold_grip'] = {'bone':PREFIX+'RightHand', 'palm_offset_hand_local_m':[0,.05,0]}
    if name == 'Thief_Run_Carry':
        m['loot_grip'] = {'bones':{s:PREFIX+s+'Hand' for s in ('Left','Right')},
            'palm_offset_hand_local_m':[0,.05,0], 'spacing_m':COIL_HALF*2,
            'attachment':'Ganimet merkezi iki avucun ortası; nesne boyutuna göre Unity el IK hedefleri.'}
    if name in ('Tackle_Dive','Tackle_GetUp'):
        m['vertical_pose_motion'] = 'Kalça yüksekliği klipte; yatay ilerleme Unity kodundan.'
    return m


CLIP_FUNCS = {
    'Push_Shove': clip_shove,
    'Tackle_Dive': clip_tackle,
    'Tackle_GetUp': clip_tackle_getup,
    'Throw_Aim': clip_throw_aim,
    'Throw_Release': clip_throw_release,
    'Whistle_Blow': clip_whistle,
    'HitReact': clip_hitreact,
    'Thief_Sneak_Walk': clip_thief_sneak,
    'Thief_Run_Carry': thief_run,
    'Thief_Run': clip_thief_run,
    'Thief_Grab': clip_thief_grab,
    'Thief_Punch': clip_thief_punch,
    'Thief_Stunned': clip_thief_stunned,
    'Thief_ThrowInVan': clip_thief_van,
    'Cabinet_TakeCoil': clip_take_coil,
    'Carry_Coil_Idle': clip_coil_idle,
    'Carry_Coil_Walk': clip_coil_walk,
    'Rope_TieStake': clip_tie_stake,
    'Rope_Throw': clip_throw_coil,
    'Carry_Stretcher_Walk': clip_carry_stretcher,
    'Carry_OneHand_Walk': clip_carry_onehand,
    'PickUp': clip_pickup,
    'PutDown': clip_putdown,
    'GetUp_FromBack': clip_getup,
    'Revive_Helper': clip_revive,
    'Jump': clip_jump,
    'Startled': clip_startled,
    'Lying_HelpWave': clip_lying_wave,
    'Lying_Groan': clip_lying_groan,
    'Celebrate': clip_celebrate,
    'Patient_SitDazed': clip_sit_dazed,
    'Patient_SitDazed_ArmSling': clip_sit_dazed_sling,
    'Patient_DraggedOut': clip_dragged_out,
    'Piggyback_Rider': clip_piggy_rider,
    'Extract_Pull': clip_extract_pull,
    'Rope_Descend': clip_rope_descend,
    'Rope_Climb': clip_rope_climb,
    'Rope_Hang_Idle': clip_rope_hang,
    'Piggyback_PickUp': clip_piggy_pickup,
    'Piggyback_Rope_Climb': clip_piggy_rope_climb,
    'Piggyback_Walk': clip_piggy_walk,
    'Piggyback_PutDown': clip_piggy_putdown,
    'Rope_Pull': clip_rope_pull,
    'Carry_Stretcher_Front_Walk': clip_carry_stretcher_front,
}

# ---------------------------------------------------------------- aksiyon / export


def frame_count(duration):
    return int(round(duration * FPS)) + 1


def action_fcurves(action):
    curves = []
    for layer in action.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                curves.extend(bag.fcurves)
    return curves


def curve_digest(action):
    data = []
    for fc in sorted(action_fcurves(action), key=lambda c: (c.data_path, c.array_index)):
        data.append([fc.data_path, fc.array_index, [[round(k.co[0], 3), round(k.co[1], 6)] for k in fc.keyframe_points]])
    return hashlib.sha256(json.dumps(data, separators=(',', ':')).encode()).hexdigest()


def make_action(P, name, duration):
    n = frame_count(duration)
    fn = CLIP_FUNCS[name]
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    rig = P.rig
    rig.animation_data_create()
    rig.animation_data.action = action
    prev = {}
    for f in range(1, n + 1):
        t = (f - 1) / FPS
        P.reset()
        fn(P, t, duration)
        if name in COIL_CLIPS:
            sample_coil(P, name, f)
        if name in GRIP_CLIPS:
            sample_grip(P, name)
        for pb in rig.pose.bones:
            q = pb.rotation_quaternion.copy()
            if pb.name in prev and prev[pb.name].dot(q) < 0:
                q.negate()
            pb.rotation_quaternion = q
            prev[pb.name] = q.copy()
            pb.keyframe_insert('rotation_quaternion', frame=f, group=pb.name)
            pb.keyframe_insert('location', frame=f, group=pb.name)
            pb.keyframe_insert('scale', frame=f, group=pb.name)
    for fc in action_fcurves(action):
        for k in fc.keyframe_points:
            k.interpolation = 'LINEAR'
    action.frame_range = (1, n)
    return action, n


def rig_from_crew():
    bpy.ops.import_scene.fbx(filepath=str(CREW), use_anim=False)
    rig = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
    for obj in list(bpy.context.scene.objects):
        if obj != rig:
            bpy.data.objects.remove(obj, do_unlink=True)
    rig.animation_data_clear()
    return rig


def export_clip(path, rig, n):
    """build_patient.py LiePose export'uyla aynı ayarlar; 1 mm bağ çapası üçgeni bind matrislerini korur."""
    scene = bpy.context.scene
    scene.render.fps = FPS          # FBX GlobalSettings: TimeMode 30 kare/sn
    scene.render.fps_base = 1
    scene.frame_start = 1
    scene.frame_end = n
    mesh = bpy.data.meshes.new('Procedural_BindAnchor')
    mesh.from_pydata([(0, 0, .457), (.001, 0, .457), (0, .001, .457)], [], [(0, 1, 2)])
    anchor = bpy.data.objects.new('Procedural_BindAnchor', mesh)
    bpy.context.collection.objects.link(anchor)
    anchor.parent = rig
    anchor.vertex_groups.new(name=PREFIX + 'Hips').add([0, 1, 2], 1, 'REPLACE')
    mod = anchor.modifiers.new('BindAnchorSkin', 'ARMATURE')
    mod.object = rig
    old_name = scene.name
    scene.name = path.stem
    bpy.ops.object.select_all(action='DESELECT')
    for o in (rig, anchor):
        o.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True,
        object_types={'ARMATURE', 'MESH'}, apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z', axis_up='Y', add_leaf_bones=False, use_armature_deform_only=False,
        bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
        bake_anim_simplify_factor=0, bake_anim_force_startend_keying=True,
        mesh_smooth_type='FACE', use_mesh_modifiers=False, use_custom_props=True, check_existing=False)
    scene.name = old_name
    bpy.data.objects.remove(anchor, do_unlink=True)
    bpy.data.meshes.remove(mesh)


# ---------------------------------------------------------------- render


def import_character(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(path), use_anim=False)
    objs = [o for o in bpy.data.objects if o not in before]
    rig = next(o for o in objs if o.type == 'ARMATURE')
    rig.animation_data_clear()
    for o in objs:
        if o.type == 'MESH':
            for slot in o.material_slots:
                m = slot.material
                if m and m.node_tree:
                    bsdf = next((nd for nd in m.node_tree.nodes if nd.type == 'BSDF_PRINCIPLED'), None)
                    if bsdf:
                        m.diffuse_color = tuple(bsdf.inputs['Base Color'].default_value)
    return rig, objs


def setup_render():
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    sh = scene.display.shading
    sh.light = 'STUDIO'
    sh.color_type = 'MATERIAL'
    sh.show_shadows = True
    sh.show_cavity = True
    sh.cavity_type = 'WORLD'
    sh.show_object_outline = True
    scene.display.shadow_shift = 0.1
    scene.display.light_direction = (0.45, -0.35, 0.82)
    scene.render.film_transparent = False
    if scene.world is None:
        scene.world = bpy.data.worlds.new('World')
    scene.world.color = (0.62, 0.66, 0.70)
    sh.background_type = 'WORLD'
    scene.display_settings.display_device = 'sRGB'
    scene.view_settings.view_transform = 'Standard'
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGB'
    scene.render.resolution_percentage = 100
    scene.render.fps = FPS
    mesh = bpy.data.meshes.new('Floor')
    mesh.from_pydata([(-3, -3, 0), (3, -3, 0), (3, 3, 0), (-3, 3, 0)], [], [(0, 1, 2, 3)])
    floor = bpy.data.objects.new('Floor', mesh)
    mat = bpy.data.materials.new('M_Floor')
    mat.diffuse_color = (0.42, 0.45, 0.43, 1)
    mesh.materials.append(mat)
    bpy.context.collection.objects.link(floor)
    cam_data = bpy.data.cameras.new('StripCam')
    cam_data.type = 'ORTHO'
    cam = bpy.data.objects.new('StripCam', cam_data)
    bpy.context.collection.objects.link(cam)
    scene.camera = cam
    return cam


def look(cam, location, target, scale):
    cam.location = location
    d = Vector(target) - Vector(location)
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    cam.data.ortho_scale = scale


_FC_RE = re.compile(r'pose\.bones\["(.+)"\]\.(\w+)')


def pose_from_action(rig, action, frame):
    """Aksiyonu rig'e atamadan, verilen karede pozu doğrudan uygular (birden çok rig farklı kliplerde)."""
    for fc in action_fcurves(action):
        m = _FC_RE.fullmatch(fc.data_path)
        if m:
            getattr(rig.pose.bones[m.group(1)], m.group(2))[fc.array_index] = fc.evaluate(frame)


def prop_mesh(name, verts, faces, color, props):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    obj = bpy.data.objects.new(name, mesh)
    mat = bpy.data.materials.new('M_' + name)
    mat.diffuse_color = color
    mesh.materials.append(mat)
    bpy.context.collection.objects.link(obj)
    props.append(obj)
    return obj


def prop_box(name, lo, hi, color, props):
    (x0, y0, z0), (x1, y1, z1) = lo, hi
    v = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0), (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
    f = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    return prop_mesh(name, v, f, color, props)


def prop_rod(name, a, b, r, color, props, sides=8):
    a, b = Vector(a), Vector(b)
    d = (b - a).normalized()
    u = d.orthogonal().normalized()
    w = d.cross(u)
    v = []
    for p in (a, b):
        for i in range(sides):
            ang = TAU * i / sides
            v.append(tuple(p + (u * math.cos(ang) + w * math.sin(ang)) * r))
    f = [(i, (i + 1) % sides, sides + (i + 1) % sides, sides + i) for i in range(sides)]
    f += [tuple(range(sides - 1, -1, -1)), tuple(range(sides, 2 * sides))]
    return prop_mesh(name, v, f, color, props)


ROCK = (0.36, 0.31, 0.26, 1)
ROPE = (0.95, 0.62, 0.12, 1)
STEEL = (0.22, 0.23, 0.25, 1)
SEAT = (0.18, 0.22, 0.32, 1)


def on_wall(name):
    return name in ('Rope_Descend', 'Rope_Climb', 'Rope_Hang_Idle', 'Piggyback_Rope_Climb')


def strip_props(name, props):
    """Yalnız önizleme için sahne parçaları (FBX'e girmez): koltuk, kaya duvarı, halat, kazık."""
    if name.startswith('Patient_Sit') or name == 'Patient_DraggedOut':
        prop_box('Seat', (-0.27, -0.17, SEAT_Z - 0.08), (0.27, 0.16, SEAT_Z), SEAT, props)
        if name != 'Patient_DraggedOut':
            prop_box('SeatBack', (-0.27, 0.17, SEAT_Z - 0.08), (0.27, 0.25, 0.86), SEAT, props)
    if on_wall(name):
        prop_box('Wall', (-0.45, WALL_Y - 0.2, -0.6), (0.45, WALL_Y, 1.9), ROCK, props)
        prop_rod('Rope', (0, ROPE_Y, -0.6), (0, ROPE_Y, 1.9), 0.013, ROPE, props)
    if name == 'Rope_Pull':
        prop_rod('Rope', (0, -1.6, PULL_Z - 0.35), (0, PULL_BACK, PULL_Z + 0.02), 0.013, ROPE, props)
        prop_rod('RopeTail', (0, PULL_BACK, PULL_Z + 0.02), (0.55, -0.2, 0.24), 0.013, ROPE, props)
        prop_rod('Stake', (0.55, -0.2, 0.0), (0.55, -0.2, 0.32), 0.03, STEEL, props)


def strip_view(name, cam):
    if name in COMBAT_CLIPS:
        if name.startswith('Tackle_'):
            look(cam, (3.5,-2.8,1.9), (0,-.2,.60), 2.25)
            return (420,400)
        look(cam, (-3.1 if name in ('Throw_Aim','Throw_Release','Thief_Punch') else 3.1,-3.8,1.8), (0,-.10,.73), 1.95)
        return (360,400)
    if name in COIL_CLIPS:
        if name=='Rope_Throw':
            look(cam, (-3.8,-3.1,1.8), (0,-.45,.73), 2.45)
        else:
            look(cam, (3.1,-3.8,1.8), (0,-.12,.73), 1.95)
        return (360,400)
    if name.startswith('Lying_'):
        look(cam, (3.0, -1.2, 1.25), (0, 0.3, 0.3), 1.95)
        return (420, 300)
    if name == 'Patient_DraggedOut':
        look(cam, (3.6, -0.7, 1.3), (0, 0.42, 0.45), 2.3)
        return (420, 300)
    if name == 'GetUp_FromBack':
        look(cam, (3.4, -2.3, 1.7), (0, 0.15, 0.62), 2.15)
    elif name in ('Jump', 'Celebrate', 'Startled'):
        look(cam, (2.6, -3.6, 1.5), (0, 0, 0.92), 2.25)
    elif name == 'Carry_Stretcher_Front_Walk':
        look(cam, (3.0, 2.9, 1.8), (0, 0.7, 0.6), 2.9)
        return (480, 340)
    elif name == 'Extract_Pull':
        look(cam, (3.4, -1.2, 1.3), (0, -0.12, 0.72), 2.0)
    elif name.startswith('Patient_Sit'):
        look(cam, (2.6, -3.6, 1.3), (0, 0, 0.6), 1.7)
    elif name == 'Piggyback_Rope_Climb':
        look(cam, (3.3, 1.4, 1.3), (0, -0.05, 0.75), 2.1)
    elif on_wall(name):
        look(cam, (3.3, 1.3, 1.15), (0, -0.15, 0.7), 1.95)
    elif name.startswith('Piggyback_'):
        look(cam, (3.2, 1.5, 1.4), (0, 0.1, 0.78), 2.1)
    else:
        look(cam, (2.6, -3.6, 1.5), (0, 0, 0.78), 1.95)
    return (300, 400)


def import_stretcher(path=STRETCHER):
    """Yalnız önizleme: sedye FBX'i (kök boş + mesh + sap işaretleri)."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(path), use_anim=False)
    objs = [o for o in bpy.data.objects if o not in before]
    root = next(o for o in objs if o.parent is None)
    for o in objs:
        if o.type == 'MESH':
            for slot in o.material_slots:
                m = slot.material
                if m and m.node_tree:
                    bsdf = next((nd for nd in m.node_tree.nodes if nd.type == 'BSDF_PRINCIPLED'), None)
                    if bsdf:
                        m.diffuse_color = tuple(bsdf.inputs['Base Color'].default_value)
    return root, objs


def place_coil(coil, bare, cast, name):
    rig, _, _, frame, _ = cast[0]
    root, objs = coil
    hands = []
    for side in ('Left','Right'):
        pb = rig.pose.bones[PREFIX+side+'Hand']
        hands.append(pb.head+pb.matrix.col[1].xyz.normalized()*GRIP_POINT)
    x = (hands[0]-hands[1]).normalized()
    z = Vector((0,0,1))
    z = (z-x*x.dot(z)).normalized()
    y = z.cross(x).normalized()
    rot = Matrix((x,y,z)).transposed().to_4x4()
    loc = (hands[0]+hands[1])/2
    if name == 'Cabinet_TakeCoil' and frame < 11:
        loc, rot = Vector(COIL_CABINET), Matrix.Identity(4)
    if name == 'Rope_TieStake' and 17 <= frame <= 45:
        loc, rot = Vector(COIL_GROUND), Matrix.Rotation(math.radians(COIL_GROUND_YAW),4,'Z')
    if name == 'Rope_Throw' and frame > COIL_RELEASE:
        # Bırakış örneğini koru: prop aniden yok olmaz, görünür bir kısa balistik yay izler.
        m = coil_meta(name)
        dt = (frame-COIL_RELEASE)/FPS
        loc = Vector(m['release_position_root_local_m']) + Vector(m['throw_direction_root_local'])*1.8*dt
        loc.z = max(.222, loc.z-1.5*dt*dt)
        rot = Matrix.Identity(4)
    root.matrix_world = rig.matrix_world @ Matrix.Translation(loc) @ rot
    shown = list(objs)
    if name == 'Rope_TieStake':
        bare[0].matrix_world = rig.matrix_world @ Matrix.Translation(COIL_STAKE)
        shown += bare[1]
    return shown


def place_stretcher(st, carrier, marker):
    """Sedyeyi, işaretçisi o karedeki iki avucun ortasına oturacak şekilde taşıyanla aynı yöne koyar."""
    root, objs = st
    mid = Vector()
    for side in ('Left', 'Right'):
        pb = carrier.pose.bones[PREFIX + side + 'Hand']
        mid += carrier.matrix_world @ (pb.head + pb.matrix.col[1].xyz.normalized() * GRIP_POINT) / 2
    mk = next(o for o in objs if o.name.startswith(marker))
    local = root.matrix_world.inverted() @ mk.matrix_world.translation
    root.matrix_world = carrier.matrix_world @ Matrix.Translation(carrier.matrix_world.inverted() @ mid - local)
    return objs


def place_rider(rider, carrier):
    pb = carrier.pose.bones[PREFIX + PIGGY_BONE]
    S = pb.matrix @ pb.bone.matrix_local.inverted() @ Matrix.Translation(PIGGY_OFFSET)
    rider.matrix_world = carrier.matrix_world @ S


def render_strip(name, n, loop, actors, all_objs, cam, tmp, floor, props=(), extra=None):
    """actors(f) → [(rig, objs, aksiyon, kare, dünya matrisi | 'rider')]; ilk eleman ana karakterdir."""
    scene = bpy.context.scene
    tile = strip_view(name, cam)
    scene.render.resolution_x, scene.render.resolution_y = tile
    floor.hide_render = on_wall(name)
    count = 8 if n >= 40 or name in COIL_CLIPS or name == 'Thief_Punch' else 7
    span = n - 1
    frames = [1 + int(round(i * span / (count if loop else count - 1))) for i in range(count)]
    frames = {'Rope_TieStake':[1,16,22,26,29,33,46,61],
              'Rope_Throw':[1,7,11,15,19,22,28,34,40,43],
              'Thief_Punch':[1,5,8,11,14,17,21,25],
              'Cabinet_TakeCoil':[1,7,11,17,23,29,35,43]}.get(name, frames)
    tiles = []
    for f in frames:
        for o in all_objs:
            o.hide_render = True
        cast = actors(f)
        for rig, objs, action, fr, mw in cast:
            for o in objs:
                o.hide_render = False
            pose_from_action(rig, action, fr)
            if not isinstance(mw, str):
                rig.matrix_world = mw
        bpy.context.view_layer.update()
        for rig, objs, action, fr, mw in cast:
            if mw == 'rider':
                place_rider(rig, cast[0][0])
        bpy.context.view_layer.update()
        if extra is not None:
            for o in extra(cast):
                o.hide_render = False
            bpy.context.view_layer.update()
        path = os.path.join(tmp, '%s_%03d.png' % (name, f))
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        img = bpy.data.images.load(path)
        px = np.empty(img.size[0] * img.size[1] * 4, dtype=np.float32)
        img.pixels.foreach_get(px)
        px = px.reshape(img.size[1], img.size[0], 4)
        bpy.data.images.remove(img)
        lum = float(px[..., :3].mean())
        bright = max(float(p[:3].mean())*255 for p in (px[0,0],px[0,-1],px[-1,0],px[-1,-1]))
        if bright < 150:
            raise RuntimeError('karanlik kare: %s f%d %.3f' % (name, f, lum))
        tiles.append(px)
    gap = 4
    h = tile[1]
    w = tile[0] * len(tiles) + gap * (len(tiles) - 1)
    strip = np.ones((h, w, 4), dtype=np.float32) * np.array([0.15, 0.15, 0.17, 1.0], dtype=np.float32)
    for i, px in enumerate(tiles):
        x = i * (tile[0] + gap)
        strip[:, x:x + tile[0]] = px
    out = bpy.data.images.new(name + '_strip', w, h, alpha=False)
    out.pixels.foreach_set(strip.ravel())
    out.filepath_raw = str(OUT / 'renders' / (name + '_strip.png'))
    out.file_format = 'PNG'
    out.save()
    bpy.data.images.remove(out)
    return frames


# ---------------------------------------------------------------- meta (r3)

def rope_meta(name):
    if name == 'Rope_Pull':
        return {'rope_grip': {
            'bones': [PREFIX + 'LeftHand', PREFIX + 'RightHand'],
            'grip_point_hand_local_m': [0.0, 0.05, 0.0],
            'rope_line_root_local_m': {'x': 0.0, 'z': PULL_Z, 'y_from': PULL_FRONT, 'y_to': PULL_BACK},
            'note': 'Halat önden (-Y, uçurum kenarı) gelir; kök yerel x=0, z≈%.2f yatay çizgisinde elden ele çekilir: '
                    'her el y=%.2f noktasında tutar, y=%.2f noktasına çeker, bırakıp öne uzanır (iki el yarım faz '
                    'kaydırmalı). LineRenderer: uçurumdan öndeki ele, iki el arası, arkadaki elden kazığa. '
                    'Tutma noktası el kemiğinin ~5 cm ilerisi (avuç).' % (PULL_Z, PULL_FRONT, PULL_BACK)},
            'rope_speed_mps': round((PULL_BACK - PULL_FRONT) / 0.55, 3)}
    v = ROPE_SPEEDS.get(name, 0.0)
    heights = {'Rope_Descend': {'Left': 1.03, 'Right': 0.92}, 'Rope_Hang_Idle': {'Left': 1.03, 'Right': 0.91}}
    m = {'rope_grip': {
        'bones': [PREFIX + 'LeftHand', PREFIX + 'RightHand'],
        'grip_point_hand_local_m': [0.0, 0.05, 0.0],
        'rope_line_root_local_m': {'x': 0.0, 'y': ROPE_Y, 'vertical': True},
        'grip_heights_root_local_m': heights.get(name, {'range': [round(1.07 - abs(v) * 0.6, 3), 1.07]}),
        'note': 'Halat kök yerel (x=0, y=%.2f) dikey çizgisi; tutan elin avucu bu çizgide (el kemiğinin ~5 cm '
                'ilerisi). Tırmanışta tutan el köke göre aşağı kayar (halat dünyada sabit), bırakan el ~6 cm '
                'açılıp yukarı uzanır. LineRenderer: kazıktan üstteki ele, oradan alttaki ele, sonra aşağı sarkar.' % ROPE_Y},
        'wall_plane_root_local_y': WALL_Y}
    if v:
        m['vertical_speed_mps'] = v
    return m


EXTRA_META = {
    'Patient_SitDazed': lambda: {'seat_surface_root_local_z': SEAT_Z},
    'Patient_SitDazed_ArmSling': lambda: {'seat_surface_root_local_z': SEAT_Z},
    'Patient_DraggedOut': lambda: {
        'hips_displacement_m': [0.0, 0.86, round(0.14 - HIP_Z - SIT_HZ, 3)],
        'start_pose': 'Patient_SitDazed t=0 (oturuş)',
        'end_pose': 'fiziksel sırt üstü yatış (kalça z≈0.14, gövde yatay); kollar yanda, bacaklar düz (Patient_LiePose '
                    'düzeni). Dik tanımlı Lying_* kliplerine geçerken obje -90° X döndürülür.',
        'sync': 'Extract_Pull t≈0.45 sn (kare 14) anında başlatılırsa büyük çekişle eşleşir.'},
    'Extract_Pull': lambda: {'grab_time_s': 0.42, 'big_pull_time_s': [0.8, 1.0],
                             'hands_inside_root_local_m': [0.13, -0.48, 0.60],
                             'sync': 'Patient_DraggedOut bu klibin t≈0.45 sn anında başlatılır.'},
}


def piggy_meta(name):
    m = {'piggyback_socket': {
        'carrier_bone': PREFIX + PIGGY_BONE, 'offset_carrier_bind_m': list(PIGGY_OFFSET),
        'note': 'Binen (Piggyback_Rider oynatan hasta) kökü taşıyanın Spine1 kemiğine rijit bağlanır: taşıyan bağ '
                'pozundayken soket taşıyan kökünün %.2f m arkasında, %.2f m yukarısında (Blender +Y = Unity -Z), '
                'dönüşü taşıyan köküyle aynı. Unity: Spine1 altında boş "PiggybackSocket" oluşturup hastayı ona '
                'sıfır yerel dönüşümle bağlayın.' % (PIGGY_OFFSET[1], PIGGY_OFFSET[2])}}
    if name in PIGGY_ATTACH:
        a, b = PIGGY_ATTACH[name]
        m['rider_attached_frames'] = [a, b if b else 'son']
    return m


# ---------------------------------------------------------------- ana akış


def main():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument('--no-render', action='store_true')
    parser.add_argument('--only', default='')
    parser.add_argument('--audit-actions', action='store_true', help='Tüm eğrileri üretip meta hashlerini doğrular; hiçbir çıktı yazmaz.')
    args = parser.parse_args(argv)
    if args.audit_actions:
        args.no_render = True
        args.only = ''
    only = [x for x in args.only.split(',') if x]
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / 'renders').mkdir(exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1
    scene.render.fps = FPS
    scene.render.fps_base = 1
    rig = rig_from_crew()
    # FBX içe aktarımı sahne kare hızını kaynak dosyanınkine (24) çeker: export'tan önce 30'a geri al.
    scene.render.fps = FPS
    scene.render.fps_base = 1
    P = Poser(rig)
    compute_piggy_grip(P)
    bones = sorted(b.name for b in rig.data.bones)
    meta = {'fps': FPS, 'units': 'meters', 'forward': '-Y', 'up': 'Z',
            'skeleton_source': 'Export/RescueCrew/SK_RescueCrew_Blue.fbx', 'bone_count': len(bones),
            'pose_convention': 'Lying_* klipleri Patient_LiePose gibi dik tanımlı; Unity objeyi sedyeye yatırır.',
            'clips': {}}
    # Önizleme bağımlılıkları: sırt şeritleri bineni, binen şeridi taşıyanı, kaldırma/bırakma yatan hastayı gösterir.
    deps = set()
    for nm in only:
        if nm.startswith('Piggyback_') and nm != 'Piggyback_Rider':
            deps |= {'Piggyback_Rider', 'Patient_DraggedOut'}
        if nm == 'Piggyback_Rider':
            deps.add('Piggyback_Walk')
    actions = {}
    digests = []
    for name, duration, loop, inplace, rootmotion, character in CLIPS:
        if only and name not in only and name not in deps:
            continue
        action, n = make_action(P, name, duration)
        actions[name] = (action, n, loop)
        if only and name not in only:
            rig.animation_data.action = None
            P.reset()
            continue
        digest = curve_digest(action)
        digests.append(digest)
        if not args.audit_actions:
            export_clip(OUT / (name + '.fbx'), rig, n)
        rig.animation_data.action = None
        meta['clips'][name] = {
            'file': name + '.fbx', 'duration_s': round((n - 1) / FPS, 4), 'frames': n, 'loop': loop,
            'in_place': inplace, 'root_motion': rootmotion, 'character': character,
            'preview_mesh': PREVIEW.get(name, 'SK_RescueCrew_Blue' if character in ('RescueCrew','crew','thief') else 'SK_Patient_Tourist'),
            'description': DESCRIPTIONS[name], 'action_sha256': digest}
        if name in WALK_SPEEDS:
            v, stance = WALK_SPEEDS[name]
            stride, _ = walk_stride(name, (n - 1) / FPS)
            meta['clips'][name].update({'target_speed_mps': v, 'stance_ratio': stance,
                                        'step_length_m': round(v * (n - 1) / FPS / 2, 4),
                                        'stance_travel_m': round(stride, 4)})
        if (name.startswith('Rope_') and name not in COIL_CLIPS) or name == 'Piggyback_Rope_Climb':
            meta['clips'][name].update(rope_meta(name))
        if name.startswith('Piggyback_'):
            meta['clips'][name].update(piggy_meta(name))
        if name in EXTRA_META:
            meta['clips'][name].update(EXTRA_META[name]())
        if name in GRIP_CLIPS:
            meta['clips'][name].update(grip_meta(name))
        if name in COIL_CLIPS:
            meta['clips'][name].update(coil_meta(name))
        if name in COMBAT_CLIPS:
            meta['clips'][name].update(combat_meta(name))
        print('CLIP', name, n, digest, flush=True)
        P.reset()
    if only:
        # Seçili build ortak meta'yı günceller; diğer kliplerin FBX/şerit ve kayıtları korunur.
        previous = json.loads((OUT/'clips_meta.json').read_text(encoding='utf-8'))
        previous['clips'].update(meta['clips'])
        meta['clips'] = previous['clips']
    total = hashlib.sha256(''.join(meta['clips'][c[0]]['action_sha256'] for c in CLIPS).encode()).hexdigest()
    meta['actions_sha256'] = total
    meta['source_sha256'] = hashlib.sha256(Path(__file__).read_bytes().replace(b'\r\n', b'\n')).hexdigest()
    if args.audit_actions:
        recorded = json.loads((OUT/'clips_meta.json').read_text(encoding='utf-8'))
        for name in meta['clips']:
            if recorded['clips'][name]['action_sha256']!=meta['clips'][name]['action_sha256']:
                raise RuntimeError('Kaynak / export aksiyon hash uyusmazligi: '+name)
        if recorded['actions_sha256']!=total:
            raise RuntimeError('Toplam aksiyon hash uyusmazligi')
    else:
        (OUT / 'clips_meta.json').write_text(json.dumps(meta, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    if not args.no_render:
        bpy.data.objects.remove(rig, do_unlink=True)
        cam = setup_render()
        floor = bpy.data.objects['Floor']
        crew_rig, crew_objs = import_character(CREW)
        pat_rig, pat_objs = import_character(PATIENT)
        civ = import_character(CIVILIAN) if 'Patient_SitDazed_ArmSling' in actions else None
        stretcher = import_stretcher() if 'Carry_Stretcher_Front_Walk' in actions else None
        coil = import_stretcher(ROOT/'Export/Props/RopeCoil/SM_RopeCoil.fbx') if COIL_CLIPS.intersection(actions) else None
        bare = import_stretcher(ROOT/'Export/Props/RopeAnchor/SM_RopeAnchor_Bare.fbx') if 'Rope_TieStake' in actions else None
        scene.render.fps = FPS
        scene.render.fps_base = 1
        everything = crew_objs + pat_objs + (civ[1] if civ else []) + (stretcher[1] if stretcher else [])
        everything += (coil[1] if coil else []) + (bare[1] if bare else [])
        for o in everything:
            if o.type == 'ARMATURE':
                for pb in o.pose.bones:
                    pb.rotation_mode = 'QUATERNION'
        render_temp = tempfile.TemporaryDirectory(prefix='r4d_render_')
        tmp = render_temp.name
        lie_world = Matrix.Translation((0.86, 0.40, 0.0)) @ Matrix.Rotation(PI / 2, 4, 'Z')
        lying_world = Matrix.Translation((0, -HIP_Z, 0.14)) @ Matrix.Rotation(-PI / 2, 4, 'X')
        ident = Matrix.Identity(4)
        for name, (action, n, loop) in actions.items():
            if only and name not in only:
                continue
            props = []
            strip_props(name, props)
            if name.startswith('Lying_'):
                def actors(f, a=action):
                    return [(pat_rig, pat_objs, a, f, lying_world)]
            elif name == 'Patient_SitDazed_ArmSling':
                def actors(f, a=action):
                    return [(civ[0], civ[1], a, f, ident)]
            elif name.startswith('Patient_'):
                def actors(f, a=action):
                    return [(pat_rig, pat_objs, a, f, ident)]
            elif name == 'Piggyback_Rider':
                walk, wn, _ = actions['Piggyback_Walk']

                def actors(f, a=action, walk=walk, wn=wn):
                    return [(crew_rig, crew_objs, walk, 1 + (f - 1) % (wn - 1), ident),
                            (pat_rig, pat_objs, a, f, 'rider')]
            elif name.startswith('Piggyback_'):
                rider, rn, _ = actions['Piggyback_Rider']
                drag, dn, _ = actions['Patient_DraggedOut']
                lo, hi = PIGGY_ATTACH.get(name, (1, None))

                def actors(f, a=action, rider=rider, rn=rn, drag=drag, dn=dn, lo=lo, hi=hi):
                    cast = [(crew_rig, crew_objs, a, f, ident)]
                    if lo <= f <= (hi or 10 ** 6):
                        cast.append((pat_rig, pat_objs, rider, 1 + (f - 1) % (rn - 1), 'rider'))
                    else:
                        cast.append((pat_rig, pat_objs, drag, dn, lie_world))
                    return cast
            else:
                def actors(f, a=action):
                    return [(crew_rig, crew_objs, a, f, ident)]
            extra = None
            if name in COIL_CLIPS:
                def extra(cast, nm=name):
                    return place_coil(coil, bare, cast, nm)
            if name == 'Carry_Stretcher_Front_Walk':
                def extra(cast, st=stretcher):
                    return place_stretcher(st, cast[0][0], 'Handle_Front')
            frames = render_strip(name, n, loop, actors, everything, cam, tmp, floor, props, extra)
            for o in props:
                me = o.data
                bpy.data.objects.remove(o, do_unlink=True)
                bpy.data.meshes.remove(me)
            print('STRIP', name, frames, flush=True)
        render_temp.cleanup()
    print('ACTIONS_SHA256:', total, flush=True)
    print('BUILD OK', flush=True)


if __name__ == '__main__':
    try:
        main()
    except Exception:
        traceback.print_exc()
        print('BUILD FAILED', flush=True)
        sys.exit(1)
