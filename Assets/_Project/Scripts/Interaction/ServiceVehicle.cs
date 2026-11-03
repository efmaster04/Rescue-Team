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
        public IEnumerable<CarryableItem> LoadedItems => (cargoSockets ?? Array.Empty<Transform>())
            .Where(s => s).SelectMany(s => s.GetComponentsInChildren<CarryableItem>()).Distinct();
        readonly Dictionary<CarryableItem, float> cargoArrival = new Dictionary<CarryableItem, float>();
        readonly Dictionary<CarryableItem, string> cargoRejections = new Dictionary<CarryableItem, string>();
        readonly HashSet<CarryableItem> cargoCandidates = new HashSet<CarryableItem>();
        readonly Collider[] cargoOverlap = new Collider[64];
        // Soketlerin çevresindeki yerel kutu: yük zemini ile tavan arasındaki iç hacim.
        public Bounds CargoBounds
        {
            get
            {
                var bounds = new Bounds();
                bool found = false;
                foreach (var socket in cargoSockets ?? Array.Empty<Transform>())
                {
                    if (!socket) continue;
                    var position = transform.InverseTransformPoint(socket.position);
                    if (!found) { bounds = new Bounds(position, Vector3.zero); found = true; }
                    else bounds.Encapsulate(position);
                }
                if (!found) return bounds;
                bounds.SetMinMax(bounds.min - new Vector3(.38f, .12f, .4f),
                    bounds.max + new Vector3(.38f, 1.55f, .4f));
                return bounds;
            }
        }
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
            if(lift && !lift.IsLowered) return "Yükleme için lifti tamamen indirin";
            if(transform.InverseTransformPoint(player.transform.position).z<2) return "Yükleme noktası arka kapıda";
            if(!RearAccessOpen) return "Yükleme için arka kapıyı açın";
            if (player.HeldItem) return "E — Ekipmanı araca sabitle" +
                (string.IsNullOrEmpty(InstalledModuleId) ? " · Modül görev çıkışında gerekli" : "");
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
            if (!player || !player.HeldItem) return false;
            return LoadIntoFreeSocket(player.HeldItem, player, out _);
        }
        bool CanLoad(CarryableItem item, out int slot, out string reason)
        {
            slot = -1; reason = "";
            if (lift && !lift.IsLowered) { reason = "Yükleme için lifti tamamen indirin."; return false; }
            var itemDef = item && catalog ? catalog.Item(item.definitionId) : null;
            if (!itemDef || item.Loaded || item.GetComponent<ManualModulePackage>())
            { reason = "Bu eşya yük bölümüne sabitlenemez."; return false; }
            var moduleDef = manualModules && !manualModules.IsSecured ? null : catalog.Module(InstalledModuleId);
            var loaded = LoadedItems.ToArray();
            if (moduleDef && loaded.Sum(x => catalog.Item(x.definitionId)?.weightKg ?? 0) + itemDef.weightKg > moduleDef.payloadKg)
            { reason = "Araç yük kapasitesi aşılıyor."; return false; }
            int usable = cargoSockets?.Length ?? 0;
            if (moduleDef) usable = Mathf.Min(moduleDef.cargoSlots, usable);
            if (loaded.Length >= usable) { reason = "Araçta boş yük noktası yok."; return false; }
            for (int i = 0; i < usable; i++)
            {
                if (!cargoSockets[i] || cargoSockets[i].GetComponentInChildren<CarryableItem>()) continue;
                slot = i; return true;
            }
            reason = "Araçta boş yük noktası yok."; return false;
        }
        bool LoadIntoFreeSocket(CarryableItem item, PlayerInteractor player, out string reason)
        {
            if (!CanLoad(item, out int slot, out reason))
            {
                // Serbest eşya için aynı reddi her fizik adımında tekrarlama.
                if (player || !cargoRejections.TryGetValue(item, out var previous) || previous != reason)
                    runtime?.Notify(reason);
                if (!player) cargoRejections[item] = reason;
                return false;
            }
            if (player) player.TakeHeld();
            item.LoadInto(cargoSockets[slot], slot);
            cargoRejections.Remove(item);
            runtime?.WorldChanged();
            runtime?.Notify(item.DisplayName + " araca sabitlendi.");
            return true;
        }
        void FixedUpdate()
        {
            if (!runtime || !catalog || (lift && !lift.IsLowered))
            { cargoArrival.Clear(); cargoRejections.Clear(); return; }
            var bounds = CargoBounds;
            if (bounds.size == Vector3.zero) return;
            var scale = transform.lossyScale;
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            cargoCandidates.Clear();
            // Her fizik adımında çalışır: tahsissiz sorgu (NonAlloc), tampon dolarsa fazlası sonraki adımda yakalanır.
            int count = Physics.OverlapBoxNonAlloc(transform.TransformPoint(bounds.center),
                Vector3.Scale(bounds.extents, scale), cargoOverlap, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int c = 0; c < count; c++)
            {
                var item = cargoOverlap[c].GetComponentInParent<CarryableItem>();
                if (!item || item.Loaded || item.GetComponent<ManualModulePackage>()) continue;
                var body = item.GetComponent<Rigidbody>();
                if (!body || body.isKinematic || !bounds.Contains(transform.InverseTransformPoint(item.transform.position + item.CenterOffset))) continue;
                if (!cargoCandidates.Add(item)) continue;
                if (!cargoArrival.TryGetValue(item, out float arrival)) cargoArrival[item] = arrival = Time.fixedTime;
                if (body.linearVelocity.sqrMagnitude < .04f || Time.fixedTime - arrival >= .5f)
                    LoadIntoFreeSocket(item, null, out _);
            }
            foreach (var item in cargoArrival.Keys.Where(x => !x || x.Loaded || !cargoCandidates.Contains(x)).ToArray())
            { cargoArrival.Remove(item); cargoRejections.Remove(item); }
        }
        void OnDisable() { cargoArrival.Clear(); cargoRejections.Clear(); cargoCandidates.Clear(); }
        public void RestoreCargo(CarryableItem item, int slot)
        {
            if (item && cargoSockets != null && slot >= 0 && slot < cargoSockets.Length && cargoSockets[slot]) item.LoadInto(cargoSockets[slot], slot);
        }
        public bool TryChangeModule(string id, ModuleStation station)
        {
            if(manualModules) {runtime.Notify("Modül değişimi elle yapılır: arka kapının dışındaki taşıyıcıyı kullanın."); return false;}
            if(lift && !lift.IsLowered) { runtime.Notify("Modül değişimi için lifti tamamen indirin."); return false; }
            if (!runtime || !station || station.Busy || !catalog.Module(id) ||
                !runtime.Data.ownedModules.Contains(id)) return false;
            if (runtime.Data.phase == JobPhase.Deployed)
            { runtime.Notify("Modül değişimi için saha görevini tamamlayın."); return false; }
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
