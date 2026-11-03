using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace CompanySim
{
    [Serializable] public sealed class ModulePackageVisual
    {
        public string moduleId;
        public GameObject prefab;
    }
    [Serializable] public sealed class ModulePackageRecord
    {
        public string uid,moduleId;
        public bool delivered=true,mounted,connected;
        public int bolts;
        public long arrivalUnix;
        public Vector3 position;
        public Quaternion rotation=Quaternion.identity;
    }
    public sealed class ManualModuleSystem : MonoBehaviour
    {
        public CompanyRuntime runtime;
        public ServiceVehicle vehicle;
        public GarageLift lift;
        public Transform mount;
        public VehicleDoor carrierDoor;
        public BoxCollider carrierCollision;
        public GameObject packagePrefab;
        public ModulePackageVisual[] packageVisuals;
        public ManualModulePackage Current { get; private set; }
        public bool Initialized { get; private set; }
        public bool IsSecured => Current && Current.Secured;
        public event Action Changed;
        readonly List<ManualModulePackage> packages=new List<ManualModulePackage>();
        public IReadOnlyList<ManualModulePackage> Packages => packages;
        Vector3 StoragePosition(int index) => new Vector3(7.10f+(index%2)*.70f,.35f,-3.50f+(index/2)*.60f);
        public void Initialize(CompanyRuntime company)
        {
            runtime=company; StopAllCoroutines();
            foreach(var package in packages) if(package) {package.gameObject.SetActive(false); Destroy(package.gameObject);}
            packages.Clear(); Current=null;
            if(runtime.Data.modulePackages==null) runtime.Data.modulePackages=new List<ModulePackageRecord>();
            if(runtime.Data.modulePackages.Count==0)
            {
                int spare=0;
                foreach(var id in runtime.Data.ownedModules)
                {
                    bool mounted=id==runtime.Data.installedModuleId;
                    runtime.Data.modulePackages.Add(new ModulePackageRecord {uid=Guid.NewGuid().ToString("N"),moduleId=id,mounted=mounted,bolts=mounted?15:0,connected=mounted,position=StoragePosition(mounted?0:spare++)});
                }
            }
            foreach(var record in runtime.Data.modulePackages)
                if(record.delivered && runtime.catalog.Module(record.moduleId)) Spawn(record);
            if(Current && Current.Secured) vehicle.SetModuleImmediate(Current.record.moduleId);
            else vehicle.SetModuleImmediate("");
            Initialized=true; StartCoroutine(Deliver()); Changed?.Invoke();
        }
        void Spawn(ModulePackageRecord record)
        {
            var visual=packageVisuals?.FirstOrDefault(x=>x.moduleId==record.moduleId && x.prefab);
            var go=Instantiate(visual!=null?visual.prefab:packagePrefab,record.position,record.rotation); go.name="ModulePackage_"+record.moduleId;
            var package=go.GetComponent<ManualModulePackage>(); package.system=this; package.record=record;
            package.Item.uid=record.uid; package.Item.definitionId="module:"+record.moduleId;
            var label=go.GetComponentInChildren<Text>(); if(label) label.text=package.DisplayName;
            packages.Add(package);
            if(record.mounted && !Current) {Current=package; package.Item.DockAt(mount);}
            else record.mounted=false;
        }
        public void Order(string id)
        {
            runtime.Data.modulePackages.Add(new ModulePackageRecord {uid=Guid.NewGuid().ToString("N"),moduleId=id,delivered=false,arrivalUnix=DateTimeOffset.UtcNow.ToUnixTimeSeconds()+12,position=StoragePosition(runtime.Data.modulePackages.Count-1)});
        }
        IEnumerator Deliver()
        {
            while(true)
            {
                foreach(var record in runtime.Data.modulePackages.Where(r=>!r.delivered && r.arrivalUnix<=DateTimeOffset.UtcNow.ToUnixTimeSeconds()).ToArray())
                {
                    var definition = runtime.catalog.Module(record.moduleId);
                    if (!definition) continue;
                    record.delivered=true; Spawn(record); runtime.WorldChanged(); runtime.Notify(definition.displayName+" garaja teslim edildi."); Changed?.Invoke();
                }
                yield return new WaitForSeconds(1);
            }
        }
        public bool CanWork(PlayerInteractor player,out string reason)
        {
            reason="";
            if(lift && !lift.IsLowered) reason="Modül işi için lifti tamamen indirin";
            else if(carrierDoor && !carrierDoor.IsClosed) reason="Montaj için modül taşıyan arka kapıyı kapatın";
            else if(runtime.Data.phase==JobPhase.Deployed) reason="Saha görevi bitmeden modül değiştirilemez";
            return reason.Length==0;
        }
        public string MountPrompt(PlayerInteractor player)
        {
            if(!CanWork(player,out var reason)) return reason;
            if(Current)
            {
                if(Current.record.bolts==0 && !Current.record.connected) return player.HeldItem?"Modülü almak için elinizi boşaltın":"E — Modülü sök ve al";
                return Current.DisplayName+" · "+CountBolts(Current.record.bolts)+"/4 cıvata · "+(Current.record.connected?"Bağlı":"Bağlantı açık");
            }
            return player.HeldItem && player.HeldItem.GetComponent<ManualModulePackage>()?"E — Modülü yuvaya yerleştir":"Modülü taşıyıp bağlantı yuvasına getirin";
        }
        static int CountBolts(int mask) {int total=0; for(int i=0;i<4;i++) if((mask&(1<<i))!=0) total++; return total;}
        public bool Stage(ManualModulePackage package,PlayerInteractor player)
        {
            if(!package || package.system!=this || Current || player.HeldItem!=package.Item || !CanWork(player,out _)) return false;
            player.TakeHeld(); Current=package; package.record.mounted=true; package.record.bolts=0; package.record.connected=false; package.Item.DockAt(mount);
            Commit(); runtime.Notify("Modül yerleşti. Alet çantasıyla 4 cıvatayı sıkıp bağlantıyı takın."); return true;
        }
        public bool TakeMounted(PlayerInteractor player)
        {
            if(!Current || player.HeldItem || !CanWork(player,out _)) return false;
            if(Current.record.bolts!=0 || Current.record.connected) {runtime.Notify("Önce bağlantıyı ayırıp dört cıvatayı sökün."); return false;}
            var package=Current; package.record.mounted=false; Current=null; player.Hold(package.Item); Commit(); return true;
        }
        public bool ToggleBolt(int index,PlayerInteractor player)
        {
            if(!Current || index<0 || index>3 || !CanWork(player,out _) || !player.HeldItem || player.HeldItem.definitionId!="toolbox") return false;
            if(Current.record.connected && (Current.record.bolts&(1<<index))!=0) {runtime.Notify("Cıvataları sökmeden önce bağlantıyı ayırın."); return false;}
            Current.record.bolts^=1<<index; Commit(); return true;
        }
        public bool ToggleConnection(PlayerInteractor player)
        {
            if(!Current || !CanWork(player,out _)) return false;
            if(!Current.record.connected && Current.record.bolts!=15) {runtime.Notify("Bağlantıdan önce dört cıvatayı sıkın."); return false;}
            Current.record.connected=!Current.record.connected; Commit(); return true;
        }
        void Commit()
        {
            vehicle.SetModuleImmediate(IsSecured?Current.record.moduleId:"");
            Snapshot(); Changed?.Invoke(); runtime.WorldChanged();
            if(IsSecured) runtime.Notify(Current.DisplayName+" sabitlendi; araç kullanıma hazır.");
        }
        public string Status(string id)
        {
            var record=runtime.Data.modulePackages.FirstOrDefault(r=>r.moduleId==id);
            if(record==null) return "Satışta";
            if(!record.delivered) return "Teslimatta";
            if(record.mounted) return IsSecured?"Takılı":"Bağlantılar eksik";
            return "Garajda";
        }
        public void Snapshot()
        {
            if(!Initialized) return;
            foreach(var package in packages)
            {
                if(!package) continue;
                bool held=runtime.vehicle.runtime && package.Item.transform.GetComponentInParent<PlayerInteractor>();
                package.record.position=held?package.Item.transform.root.position+package.Item.transform.root.forward*1.2f+Vector3.up*.4f:package.transform.position;
                package.record.rotation=package.transform.rotation;
            }
        }
    }
}
