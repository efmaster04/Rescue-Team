using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CompanySim
{
    public sealed class ServiceVehicle : InteractableBehaviour
    {
        public CompanyCatalog catalog;
        public CompanyRuntime runtime;
        public Transform moduleSocket;
        public Transform[] cargoSockets;
        public GarageLift lift;
        public ManualModuleSystem manualModules;
        public VehicleDoor[] doors;
        public BoxCollider rearStepCollision;
        public bool RearAccessOpen => doors!=null && doors.Any(d=>d.name.StartsWith("Rear") && Mathf.Abs(d.CurrentAngle)>75);
        public string InstalledModuleId { get; private set; } = "cargo";
        public IEnumerable<CarryableItem> LoadedItems => cargoSockets.SelectMany(s => s.GetComponentsInChildren<CarryableItem>());
        GameObject moduleVisual;
        static readonly Dictionary<string, Material> AccessoryMaterials = new Dictionary<string, Material>();

        static Material AccessoryMaterial(string key, Color color)
        {
            if (AccessoryMaterials.TryGetValue(key, out var material)) return material;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            material = new Material(shader);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", .62f);
            AccessoryMaterials[key] = material;
            return material;
        }

        static void AccessoryPart(Transform parent, string name, Vector3 position,
            Vector3 size, Material material)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = size;
            part.GetComponent<Renderer>().sharedMaterial = material;
            Destroy(part.GetComponent<Collider>());
        }

        // Truck-sized interchangeable bodies covered the supplied enclosed van.
        // A compact roof signal shows the selected specialty without cutting its bodywork.
        static GameObject CreateAccessory(string id)
        {
            if (id == "cargo") return null;
            var color = id == "fire" ? new Color(.9f,.13f,.10f) :
                        id == "towing" ? new Color(1f,.57f,.08f) :
                        id == "water" ? new Color(.10f,.77f,.86f) :
                        new Color(.13f,.42f,.95f);
            var dark = AccessoryMaterial("housing", new Color(.07f,.09f,.11f));
            var lens = AccessoryMaterial(id, color);
            var root = new GameObject("VanRoofSignal_" + id);
            foreach(float x in new[]{-.42f,.42f})
                AccessoryPart(root.transform,"RoofMountFoot",new Vector3(x,-.0675f,0),new Vector3(.10f,.075f,.20f),dark);
            AccessoryPart(root.transform,"LowProfileHousing",Vector3.zero,
                new Vector3(1.05f,.07f,.24f),dark);
            AccessoryPart(root.transform,"LeftLens",new Vector3(-.29f,.06f,0),
                new Vector3(.43f,.09f,.20f),lens);
            AccessoryPart(root.transform,"RightLens",new Vector3(.29f,.06f,0),
                new Vector3(.43f,.09f,.20f),lens);
            return root;
        }
        public void Initialize(CompanyRuntime company)
        {
            runtime = company;
            SetModuleImmediate(company.Data.installedModuleId);
        }
        public void SetModuleImmediate(string id)
        {
            if(string.IsNullOrEmpty(id)) {if(moduleVisual) Destroy(moduleVisual); moduleVisual=null; InstalledModuleId=""; return;}
            var def = catalog.Module(id);
            if (!def || !moduleSocket) return;
            // The original cab roof is lower than the cargo roof. Seat the
            // compact signal above the cargo shell, with its feet on the roof.
            moduleSocket.localPosition=new Vector3(0,2.79f,.12f);
            if(InstalledModuleId==id && (id=="cargo" || moduleVisual)) return;
            if (moduleVisual) Destroy(moduleVisual);
            moduleVisual = CreateAccessory(id);
            if (moduleVisual)
            {
                moduleVisual.transform.SetParent(moduleSocket, false);
                moduleVisual.transform.localPosition = Vector3.zero;
                moduleVisual.transform.localRotation = Quaternion.identity;
            }
            InstalledModuleId = id;
        }
        public override string Prompt(PlayerInteractor player)
        {
            if(player.HeldItem && player.HeldItem.GetComponent<ManualModulePackage>()) return "Modülü arka kapının dışındaki taşıyıcıya getirin";
            if(transform.InverseTransformPoint(player.transform.position).z<2) return "Yükleme noktası arka kapıda";
            if(!RearAccessOpen) return "Yükleme için arka kapıyı açın";
            if (player.HeldItem) return "E — Ekipmanı araca sabitle";
            return LoadedItems.Any() ? "E — Son ekipmanı araçtan al" : "Araç yük bölümü boş";
        }
        public override void Interact(PlayerInteractor player)
        {
            if(transform.InverseTransformPoint(player.transform.position).z<2 || !RearAccessOpen || (player.HeldItem && player.HeldItem.GetComponent<ManualModulePackage>())) return;
            if (player.HeldItem) TryLoad(player);
            else
            {
                var item = LoadedItems.LastOrDefault();
                if (item)
                {
                    player.Hold(item);
                    runtime.WorldChanged();
                }
            }
        }
        public bool TryLoad(PlayerInteractor player)
        {
            if(lift && !lift.IsLowered) { runtime.Notify("Yükleme için lifti tamamen indirin."); return false; }
            if(manualModules && !manualModules.IsSecured) {runtime.Notify("Önce modülün bütün bağlantılarını tamamlayın."); return false;}
            var item = player.HeldItem;
            if (!item) return false;
            var itemDef = catalog.Item(item.definitionId);
            var moduleDef = catalog.Module(InstalledModuleId);
            if (!itemDef || !moduleDef) return false;
            float weight = LoadedItems.Sum(x => catalog.Item(x.definitionId)?.weightKg ?? 0);
            if (weight + itemDef.weightKg > moduleDef.payloadKg)
            { runtime.Notify("Araç yük kapasitesi aşılıyor."); return false; }
            int usable = Mathf.Min(moduleDef.cargoSlots, cargoSockets.Length);
            for (int i = 0; i < usable; i++)
            {
                if (cargoSockets[i].GetComponentInChildren<CarryableItem>()) continue;
                player.TakeHeld().LoadInto(cargoSockets[i], i);
                runtime.WorldChanged();
                runtime.Notify(item.DisplayName + " araca sabitlendi.");
                return true;
            }
            runtime.Notify("Araçta boş yük noktası yok."); return false;
        }
        public void RestoreCargo(CarryableItem item, int slot)
        {
            if (slot >= 0 && slot < cargoSockets.Length) item.LoadInto(cargoSockets[slot], slot);
        }
        public bool TryChangeModule(string id, ModuleStation station)
        {
            if(manualModules) {runtime.Notify("Modül değişimi elle yapılır: arka kapının dışındaki taşıyıcıyı kullanın."); return false;}
            if(lift && !lift.IsLowered) { runtime.Notify("Modül değişimi için lifti tamamen indirin."); return false; }
            if (!runtime || !station || station.Busy || !catalog.Module(id) ||
                !runtime.Data.ownedModules.Contains(id)) return false;
            if (runtime.Data.phase == JobPhase.Deployed || LoadedItems.Any())
            { runtime.Notify("Modül değişimi için aracı boşaltıp ofiste bırakın."); return false; }
            if (id == InstalledModuleId) return false;
            station.Busy = true;
            if (station.ui.audioFeedback) station.ui.audioFeedback.Motor();
            station.StartCoroutine(SwapRoutine(id, station)); return true;
        }
        IEnumerator SwapRoutine(string id, ModuleStation station)
        {
            station.Busy = true;
            var nextDef = catalog.Module(id);
            var next = CreateAccessory(id);
            if (next) next.transform.SetPositionAndRotation(station.storageStand.position, station.storageStand.rotation);
            var old = moduleVisual;
            if (old) old.transform.SetParent(null, true);
            Vector3 oldStart = old ? old.transform.position : Vector3.zero;
            Vector3 newStart = next ? next.transform.position : Vector3.zero;
            Vector3 oldEnd = station.storageStand.position + station.storageStand.right * 2.7f;
            Vector3 newEnd = moduleSocket.position;
            float t = 0;
            while (t < 1)
            {
                t += Time.deltaTime / 1.9f;
                float u = Mathf.SmoothStep(0,1,Mathf.Clamp01(t));
                if (old) old.transform.position = Vector3.Lerp(oldStart, oldEnd, u) + Vector3.up * Mathf.Sin(u*Mathf.PI)*1.2f;
                if (next) next.transform.position = Vector3.Lerp(newStart, newEnd, u) + Vector3.up * Mathf.Sin(u*Mathf.PI)*1.2f;
                yield return null;
            }
            if (old) Destroy(old);
            if (next)
            {
                next.transform.SetParent(moduleSocket, false);
                next.transform.localPosition = Vector3.zero;
                next.transform.localRotation = Quaternion.identity;
            }
            moduleVisual = next; InstalledModuleId = id;
            station.Busy = false;
            runtime.WorldChanged();
            runtime.Notify(nextDef.displayName + " bağlandı; araç yetenekleri güncellendi.");
        }
    }
}
