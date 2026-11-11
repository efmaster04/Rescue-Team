using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace CompanySim
{
    public enum JobPhase { None, Accepted, Deployed, AwaitingReport }

    [Serializable] public sealed class EquipmentRecord
    {
        public string uid, typeId;
        public float durability;
        public bool loaded;
        public int cargoSlot;
        public Vector3 position;
        public Quaternion rotation;
    }
    [Serializable] public sealed class OrderRecord
    {
        public string uid, itemId;
        public long arrivalUnix;
        public bool delivered;
    }
    [Serializable] public sealed class TransactionRecord
    {
        public string id, reason;
        public int amount, balanceAfter;
        public long createdUnix;
    }
    [Serializable] public sealed class FieldJobResult
    {
        public string resultId, jobId;
        public bool succeeded;
        public int bonus, expenses;
        public float equipmentWear;
        public float vehicleWear;
        public float fieldSeconds;
        public List<string> consumedItemUids = new List<string>();
    }
    [Serializable] public sealed class CompanySave
    {
        public int version = 5;
        public int balance, reputation, spareParts, jobsCompleted;
        public float vehicleCondition = 100;
        public bool inspectionPending;
        public bool rearAccessLayoutReady;
        public int nextInspectionAtCompletedJobs = 3, inspectionsPassed;
        public string lastInspectionSummary = "";
        public string activeJobId = "", installedModuleId = "cargo";
        public string lastResultId = "", fieldSummary = "";
        public int pendingPayment;
        public bool contractAccepted, contractSatisfied;
        public int reportBasePayment, reportFieldBonus, reportExpenses, reportContractBonus;
        public float reportEquipmentWear, reportVehicleWear;
        public string reportEquipment = "";
        public bool fieldSucceeded;
        public JobPhase phase;
        public List<string> ownedModules = new List<string> { "cargo", "towing" };
        public List<string> upgrades = new List<string>();
        public List<EquipmentRecord> equipment = new List<EquipmentRecord>();
        public List<OrderRecord> orders = new List<OrderRecord>();
        public List<TransactionRecord> transactions = new List<TransactionRecord>();
        public List<ModulePackageRecord> modulePackages = new List<ModulePackageRecord>();
    }

    public static class PreparationRules
    {
        public static List<string> Missing(JobDefinition job, string moduleId, IEnumerable<string> loadedIds)
        {
            var missing = new List<string>();
            if (!job) return missing;
            if (job.requiredModule != moduleId) missing.Add("Modül: " + job.requiredModule);
            var counts = new Dictionary<string, int>();
            foreach (string id in loadedIds)
            {
                if (!counts.ContainsKey(id)) counts[id] = 0;
                counts[id]++;
            }
            foreach (string id in job.requiredEquipment)
            {
                if (!counts.TryGetValue(id, out int count) || count == 0) missing.Add("Ekipman: " + id);
                else counts[id]--;
            }
            return missing;
        }
    }

    public sealed class CompanyRuntime : MonoBehaviour
    {
        public CompanyCatalog catalog;
        public ServiceVehicle vehicle;
        public Transform deliveryPoint;
        public ManualModuleSystem manualModules;
        public CarryableItem[] EquipmentItems => FindObjectsByType<CarryableItem>(FindObjectsSortMode.None).Where(x=>!x.GetComponent<ManualModulePackage>()).ToArray();
        public bool autoSave = true;
        public CompanySave Data { get; private set; } = new CompanySave();
        public event Action Changed;
        public event Action<string> Notice;
        // Görev köprüsü: doğrulama betikleri gerçek kaydı ezmemek için geçici bir yol verir (null = normal kayıt).
        public static string SavePathOverride;
        string SavePath => !string.IsNullOrEmpty(SavePathOverride) ? SavePathOverride :
            Path.Combine(Application.persistentDataPath, "office_garage_v1.json");
        // Görev köprüsü: sahası olan bir iş için ekip yola çıktığında (Depart / ResumeField) yayınlanır.
        // Sahne geçişini köprü yapar; Core saha sahnesini tanımaz.
        public static event Action<CompanyRuntime, JobDefinition> FieldDeparture;
        // Start bitti ve kayıt yüklendi mi (köprü ReturnFromField'ı bundan sonra çağırır).
        public bool IsReady { get; private set; }

        void Start()
        {
            if (!catalog || !catalog.economy || !vehicle) { Debug.LogError("CompanyRuntime references are missing."); return; }
            if (!Load())
            {
                Data = new CompanySave {
                    balance = catalog.economy.startingBalance,
                    spareParts = catalog.economy.startingSpareParts,
                    rearAccessLayoutReady = true
                };
                foreach (var item in FindObjectsByType<CarryableItem>(FindObjectsSortMode.None))
                    if (string.IsNullOrEmpty(item.uid)) item.uid = Guid.NewGuid().ToString("N");
                Save();
            }
            vehicle.Initialize(this);
            if(manualModules) manualModules.Initialize(this);
            StartCoroutine(DeliverOrders());
            IsReady = true;
            Changed?.Invoke();
        }

        public void Notify(string message) { Notice?.Invoke(message); Debug.Log(message); }
        void Mutated(string notice)
        {
            if (autoSave) Save();
            Changed?.Invoke();
            if (!string.IsNullOrEmpty(notice)) Notify(notice);
        }

        public void WorldChanged() => Mutated(null);

        void PostTransaction(int amount, string reason)
        {
            Data.balance += amount;
            Data.transactions.Add(new TransactionRecord {
                id = Guid.NewGuid().ToString("N"), reason = reason, amount = amount,
                balanceAfter = Data.balance, createdUnix = Now()
            });
        }

        public bool AcceptJob(string id) => AcceptJob(id, false);

        public bool AcceptJob(string id, bool acceptContract)
        {
            if (Data.phase != JobPhase.None) { Notify("Önce aktif işi tamamlayın."); return false; }
            var job = catalog.Job(id);
            // Görev köprüsü: saha sahnesi olmayan işler kilitli ("Yakında").
            if (job && !job.HasFieldScene) { Notify("Bu iş yakında açılacak."); return false; }
            // saha haritası bu bilgisayarda yoksa (Sel Vadisi her makinede üretilir) kabul edilmez; yoksa çıkış hiç yapılamaz
            if (job && !Application.CanStreamedLevelBeLoaded(job.fieldScene)) { Notify("Bu işin saha haritası bu bilgisayarda yok: " + job.fieldScene); return false; }
            if (!job || (acceptContract && (string.IsNullOrEmpty(job.contractTitle) || job.contractBonus <= 0))) return false;
            if (Data.reputation < job.minimumReputation)
            {
                Notify("Bu iş için en az " + job.minimumReputation + " itibar gerekli.");
                return false;
            }
            Data.activeJobId = id; Data.phase = JobPhase.Accepted;
            Data.contractAccepted = acceptContract;
            Mutated("Görev kabul edildi. Garaj hazırlık listesini kontrol edin.");
            return true;
        }

        // Kabul edilmiş ama göreve çıkılmamış iş bırakılabilir (hazırlık tamamlanamıyorsa kayıt kilitlenmesin).
        public bool CancelAcceptedJob()
        {
            if (Data.phase != JobPhase.Accepted) return false;
            Data.activeJobId = ""; Data.contractAccepted = false; Data.phase = JobPhase.None;
            Mutated("İş bırakıldı.");
            return true;
        }

        public List<string> MissingPreparation()
        {
            var loaded = vehicle ? vehicle.LoadedItems.ToArray() : Array.Empty<CarryableItem>();
            var job = catalog.Job(Data.activeJobId);
            var installedId = vehicle ? vehicle.InstalledModuleId : "";
            var module = catalog.Module(installedId);
            var modules = manualModules ? manualModules : vehicle ? vehicle.manualModules : null;
            var missing = PreparationRules.Missing(job, installedId, loaded.Select(x => x.definitionId));
            if (!job) missing.Add("Önce bilgisayardan bir iş kabul edin");
            if (!module || (modules && (!modules.IsSecured || modules.Current.record.moduleId != installedId)))
                missing.Add("Gerekli modül takılı ve sabit değil");
            if (module)
            {
                if (loaded.Length > module.cargoSlots)
                    missing.Add("Fazla ekipman: " + loaded.Length + ", modül " + module.cargoSlots + " yuva alır");
                float weight = loaded.Sum(x => catalog.Item(x.definitionId)?.weightKg ?? 0);
                if (weight > module.payloadKg)
                    missing.Add("Fazla ağırlık: " + weight.ToString("0.#") + " kg, modül " + module.payloadKg.ToString("0.#") + " kg taşır");
            }
            if (Data.vehicleCondition < catalog.economy.minimumVehicleCondition)
                missing.Add("Araç bakımı gerekli");
            foreach (var item in loaded)
                if (item.durability < 20) missing.Add("Ekipman bakım: " + item.DisplayName);
            if(vehicle && vehicle.lift && !vehicle.lift.IsLowered) missing.Add("Lift tamamen indirilmedi");
            if(vehicle && vehicle.doors!=null && vehicle.doors.Any(x=>x && !x.IsClosed)) missing.Add("Aracın kapıları açık");
            return missing;
        }

        public bool Depart()
        {
            if (Data.phase != JobPhase.Accepted) return false;
            var missing = MissingPreparation();
            if (missing.Count > 0) { Notify("Hazırlık eksik: " + string.Join(", ", missing)); return false; }
            var job = catalog.Job(Data.activeJobId);
            // Saha sahnesi yüklenemiyorsa faz değişmez (oyuncu "sahada" kaydıyla ofiste kilitli kalmasın)
            if (job && job.HasFieldScene && !FieldSceneLoadable(job)) return false;
            Data.phase = JobPhase.Deployed;
            Mutated("Ekip göreve çıktı.");
            if (job && job.HasFieldScene) FieldDeparture?.Invoke(this, job);
            return true;
        }

        // Görev köprüsü: ekip sahadayken oyun kapanmışsa (kayıt "Deployed" kalır) sahaya yeniden gidilir.
        public bool ResumeField()
        {
            if (Data.phase != JobPhase.Deployed) return false;
            var job = catalog.Job(Data.activeJobId);
            if (!job || !job.HasFieldScene) return false;
            if (!FieldSceneLoadable(job)) return false;
            FieldDeparture?.Invoke(this, job);
            return true;
        }

        // Saha sahnesi Build Settings'te var mı; yoksa kullanıcıya bildirir.
        private bool FieldSceneLoadable(JobDefinition job)
        {
            if (Application.CanStreamedLevelBeLoaded(job.fieldScene)) return true;
            Notify("Saha sahnesi bulunamadı: " + job.fieldScene);
            Debug.LogError("CompanyRuntime: '" + job.fieldScene + "' Build Settings'te yok.");
            return false;
        }

        // Called by a field scene or a mission adapter exactly once per result.
        public bool ReturnFromField(FieldJobResult result)
        {
            if (result == null || Data.phase != JobPhase.Deployed ||
                string.IsNullOrEmpty(result.resultId) || result.resultId == Data.lastResultId ||
                result.jobId != Data.activeJobId) return false;
            var job = catalog.Job(result.jobId);
            if (!job) return false;
            var loaded = vehicle.LoadedItems.ToArray();
            float wear = Mathf.Clamp(result.equipmentWear, 0, 100);
            Data.vehicleCondition = Mathf.Max(0, Data.vehicleCondition -
                Mathf.Clamp(result.vehicleWear, 0, 100));
            foreach (var item in loaded)
            {
                if (result.consumedItemUids != null && result.consumedItemUids.Contains(item.uid))
                { item.gameObject.SetActive(false); Destroy(item.gameObject); }
                else item.durability = Mathf.Max(0, item.durability - wear);
            }
            Data.lastResultId = result.resultId;
            Data.fieldSucceeded = result.succeeded;
            Data.reportBasePayment = result.succeeded ? job.payment : 0;
            Data.reportFieldBonus = result.succeeded ? Mathf.Clamp(result.bonus, 0, job.payment) : 0;
            Data.reportExpenses = Mathf.Clamp(result.expenses, 0, job.payment);
            Data.reportEquipmentWear = wear;
            Data.reportVehicleWear = Mathf.Clamp(result.vehicleWear, 0, 100);
            Data.reportEquipment = string.Join(", ", loaded.Select(x => x.DisplayName));
            Data.contractSatisfied = Data.contractAccepted && result.succeeded &&
                wear <= job.contractMaxEquipmentWear &&
                Data.reportVehicleWear <= job.contractMaxVehicleWear &&
                (job.contractMaxFieldMinutes <= 0f || result.fieldSeconds <= job.contractMaxFieldMinutes * 60f) &&
                (result.consumedItemUids == null || result.consumedItemUids.Count == 0);
            Data.reportContractBonus = Data.contractSatisfied ? job.contractBonus : 0;
            Data.pendingPayment = Mathf.Max(0, Data.reportBasePayment + Data.reportFieldBonus +
                Data.reportContractBonus - Data.reportExpenses);
            Data.fieldSummary = result.succeeded ? "İş tamamlandı" : "İş başarısız";
            Data.phase = JobPhase.AwaitingReport;
            Mutated("Ekip ofise döndü. Görev raporu hazır.");
            return true;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public bool DevCompleteFieldJob()
        {
            if (Data.phase != JobPhase.Deployed) return false;
            return ReturnFromField(new FieldJobResult {
                resultId = Guid.NewGuid().ToString("N"), jobId = Data.activeJobId,
                succeeded = true, equipmentWear = 18, vehicleWear = 8
            });
        }
#endif

        public bool FileReport()
        {
            if (Data.phase != JobPhase.AwaitingReport) return false;
            var job = catalog.Job(Data.activeJobId);
            if (!job) return false;
            PostTransaction(Data.pendingPayment, "Görev raporu: " + job.title);
            Data.reputation += Data.fieldSucceeded
                ? Mathf.Max(1, 6 - job.risk) + (Data.upgrades.Contains("operations") ? 1 : 0)
                : -Mathf.Max(1, job.risk);
            if (Data.fieldSucceeded) Data.jobsCompleted++;
            if (Data.jobsCompleted >= Data.nextInspectionAtCompletedJobs)
            {
                Data.inspectionPending = true;
                Data.nextInspectionAtCompletedJobs = Data.jobsCompleted + 5;
            }
            int paid = Data.pendingPayment;
            Data.pendingPayment = 0; Data.fieldSummary = ""; Data.fieldSucceeded = false;
            Data.contractAccepted = false; Data.contractSatisfied = false;
            Data.reportBasePayment = 0; Data.reportFieldBonus = 0; Data.reportExpenses = 0;
            Data.reportContractBonus = 0; Data.reportEquipmentWear = 0; Data.reportVehicleWear = 0;
            Data.reportEquipment = "";
            Data.activeJobId = ""; Data.phase = JobPhase.None;
            Mutated("Rapor onaylandı. ₺" + paid + " hesaba yatırıldı." +
                (Data.inspectionPending ? " Güvenlik denetimi bekliyor." : ""));
            return true;
        }

        public List<string> InspectionProblems()
        {
            var problems = new List<string>();
            if (Data.vehicleCondition < catalog.economy.inspectionMinimumCondition)
                problems.Add("Servis vanının bakımı eksik");
            foreach (var item in FindObjectsByType<CarryableItem>(FindObjectsSortMode.None))
                if (item.durability < catalog.economy.inspectionMinimumCondition)
                    problems.Add(item.DisplayName + " hasarlı");
            return problems;
        }

        public bool ResolveInspection()
        {
            if (!Data.inspectionPending || Data.phase != JobPhase.None) return false;
            var problems = InspectionProblems();
            Data.inspectionPending = false;
            if (problems.Count == 0)
            {
                PostTransaction(catalog.economy.inspectionReward, "Güvenlik denetimi ödülü");
                Data.reputation += 2;
                Data.inspectionsPassed++;
                Data.lastInspectionSummary = "Geçildi: araç ve ekipman güvenli.";
            }
            else
            {
                PostTransaction(-Mathf.Min(Mathf.Max(0, Data.balance), catalog.economy.inspectionFine),
                    "Güvenlik denetimi cezası");
                Data.reputation -= 1;
                Data.lastInspectionSummary = "Eksikler: " + string.Join(", ", problems);
            }
            Mutated("Denetim tamamlandı. " + Data.lastInspectionSummary);
            return true;
        }

        public bool BuyModule(string id)
        {
            var m = catalog.Module(id);
            if (!m || Data.ownedModules.Contains(id)) return false;
            if (Data.balance < m.price) { Notify("Yetersiz bakiye."); return false; }
            PostTransaction(-m.price, "Modül: " + m.displayName); Data.ownedModules.Add(id);
            if(manualModules) manualModules.Order(id);
            Mutated(m.displayName + " sipariş edildi. Garaja gelince elle takılmalı.");
            return true;
        }

        public bool OrderEquipment(string id)
        {
            var def = catalog.Item(id);
            if (!def) return false;
            int capacity = Data.upgrades.Contains("storage") ? 24 : 12;
            int occupied = EquipmentItems.Length + Data.orders.Count(x=>!x.delivered);
            if (occupied >= capacity) { Notify("Depo kapasitesi dolu."); return false; }
            if (Data.balance < def.price) { Notify("Yetersiz bakiye."); return false; }
            PostTransaction(-def.price, "Ekipman siparişi: " + def.displayName);
            Data.orders.Add(new OrderRecord { uid = Guid.NewGuid().ToString("N"), itemId = id,
                arrivalUnix = Now() + (Data.upgrades.Contains("operations") ? 6 : 12) });
            Mutated(def.displayName + " sipariş edildi. Teslimat kapısına gelecek.");
            return true;
        }

        public bool OrderParts()
        {
            var settings = catalog.economy;
            if (Data.balance < settings.sparePartsPackPrice) { Notify("Yetersiz bakiye."); return false; }
            PostTransaction(-settings.sparePartsPackPrice, "Yedek parça paketi");
            Data.spareParts += settings.sparePartsPerPack;
            Mutated(settings.sparePartsPerPack + " yedek parça depoya eklendi.");
            return true;
        }

        public bool Repair(CarryableItem item)
        {
            if (!item || item.durability >= 99) { Notify("Bu ekipman bakım gerektirmiyor."); return false; }
            var settings = catalog.economy;
            if (Data.spareParts < settings.equipmentRepairParts || Data.balance < settings.equipmentRepairCost)
            { Notify("Tamir için parça ve ₺" + settings.equipmentRepairCost + " gerekli."); return false; }
            Data.spareParts -= settings.equipmentRepairParts;
            PostTransaction(-settings.equipmentRepairCost, "Ekipman tamiri: " + item.DisplayName);
            item.durability = 100;
            Mutated(item.DisplayName + " tamir edildi."); return true;
        }

        public bool RepairVehicle()
        {
            if (Data.vehicleCondition >= 99) { Notify("Araç bakım gerektirmiyor."); return false; }
            var settings = catalog.economy;
            if (Data.spareParts < settings.vehicleRepairParts || Data.balance < settings.vehicleRepairCost)
            { Notify("Araç bakımı için parça ve ₺" + settings.vehicleRepairCost + " gerekli."); return false; }
            Data.spareParts -= settings.vehicleRepairParts;
            PostTransaction(-settings.vehicleRepairCost, "Servis vanı bakımı");
            Data.vehicleCondition = 100;
            Mutated("Servis vanının bakımı tamamlandı.");
            return true;
        }

        public bool BuyUpgrade(string id)
        {
            int price = catalog.economy.UpgradePrice(id);
            if (price < 0) return false;
            if (Data.upgrades.Contains(id)) return false;
            if (Data.balance < price) { Notify("Yetersiz bakiye."); return false; }
            PostTransaction(-price, "Bina geliştirmesi: " + id); Data.upgrades.Add(id);
            ApplyUpgrades(); Mutated("Şirket geliştirmesi tamamlandı."); return true;
        }

        void ApplyUpgrades()
        {
            foreach (var visual in FindObjectsByType<UpgradeVisual>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                visual.PrepareLayout();
                visual.gameObject.SetActive(Data.upgrades.Contains(visual.upgradeId));
            }
        }

        static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        IEnumerator DeliverOrders()
        {
            while (true)
            {
                foreach (var order in Data.orders)
                {
                    if (order.delivered || Now() < order.arrivalUnix) continue;
                    var def = catalog.Item(order.itemId);
                    if (def && def.visualPrefab)
                    {
                        var go = Instantiate(def.visualPrefab, deliveryPoint.position + UnityEngine.Random.insideUnitSphere * .25f, Quaternion.identity);
                        var item = go.GetComponent<CarryableItem>();
                        item.uid = order.uid; item.definitionId = def.id; item.durability = 100;
                        order.delivered = true;
                        Mutated(def.displayName + " teslim edildi.");
                    }
                }
                yield return new WaitForSeconds(1);
            }
        }

        public bool Save()
        {
            if (!vehicle) return false;
            Data.version=5;
            if(manualModules) manualModules.Snapshot();
            Data.equipment.Clear();
            foreach (var item in EquipmentItems)
            {
                if (string.IsNullOrEmpty(item.uid)) item.uid = Guid.NewGuid().ToString("N");
                bool held = !item.Loaded && item.GetComponent<Rigidbody>().isKinematic;
                Vector3 savedPosition = held ? item.transform.root.position + item.transform.root.forward * 1.1f + Vector3.up : item.transform.position;
                Data.equipment.Add(new EquipmentRecord { uid=item.uid, typeId=item.definitionId,
                    durability=item.durability, loaded=item.Loaded, cargoSlot=item.CargoSlot,
                    position=savedPosition, rotation=item.transform.rotation });
            }
            Data.installedModuleId = vehicle.InstalledModuleId;
            try
            {
                Directory.CreateDirectory(Application.persistentDataPath);
                string tmp = SavePath + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(Data, true));
                if (File.Exists(SavePath)) File.Replace(tmp, SavePath, null);
                else File.Move(tmp, SavePath);
                return true;
            }
            catch (Exception e) { Debug.LogError("Save failed: " + e); Notify("Kayıt başarısız."); return false; }
        }

        public bool Load()
        {
            if (!File.Exists(SavePath)) return false;
            try
            {
                var loaded = JsonUtility.FromJson<CompanySave>(File.ReadAllText(SavePath));
                if (loaded == null || loaded.version < 1 || loaded.version > 5 ||
                    loaded.equipment == null || loaded.orders == null)
                    throw new InvalidDataException("Unsupported or damaged save version");
                Data = loaded;
                if (Data.version == 1)
                {
                    Data.vehicleCondition = 100;
                    if (Data.phase == JobPhase.AwaitingReport)
                    {
                        Data.pendingPayment = catalog.Job(Data.activeJobId)?.payment ?? 0;
                        Data.fieldSucceeded = true;
                    }
                }
                if (Data.version < 3)
                {
                    if (Data.phase == JobPhase.AwaitingReport)
                        Data.reportBasePayment = Data.pendingPayment;
                    Data.version = 3;
                }
                if (Data.version < 4)
                {
                    Data.nextInspectionAtCompletedJobs = Mathf.Max(3, Data.jobsCompleted + 3);
                    Data.version = 4;
                }
                if (Data.transactions == null) Data.transactions = new List<TransactionRecord>();
                if (Data.modulePackages == null) Data.modulePackages = new List<ModulePackageRecord>();
                Data.version=5;
                if(!Data.rearAccessLayoutReady && vehicle && vehicle.rearStepCollision)
                {
                    foreach(var record in Data.equipment.Where(r=>!r.loaded).ToArray())
                    {
                        var local=vehicle.transform.InverseTransformPoint(record.position);
                        if(Mathf.Abs(local.x)>.95f || local.z<2.50f || local.z>3.75f || local.y>.65f) continue;
                        for(int slot=0;slot<10;slot++)
                        {
                            var candidate=new Vector3(2.10f,.04f,-4.20f+slot*.65f);
                            if(Data.equipment.Any(other=>other!=record && !other.loaded && new Vector2(other.position.x-candidate.x,other.position.z-candidate.z).magnitude<.57f)) continue;
                            record.position=candidate; record.rotation=Quaternion.identity; break;
                        }
                    }
                    Data.rearAccessLayoutReady=true;
                }
                foreach(var owner in FindObjectsByType<PlayerInteractor>()) if(owner.HeldItem) owner.TakeHeld();
                foreach (var item in FindObjectsByType<CarryableItem>(FindObjectsSortMode.None))
                { item.gameObject.SetActive(false); Destroy(item.gameObject); }
                foreach (var rec in Data.equipment)
                {
                    var def = catalog.Item(rec.typeId);
                    if (!def || !def.visualPrefab) continue;
                    var go = Instantiate(def.visualPrefab, rec.position, rec.rotation);
                    var item = go.GetComponent<CarryableItem>();
                    item.uid = rec.uid; item.definitionId = rec.typeId; item.durability = rec.durability;
                    if (rec.loaded) vehicle.RestoreCargo(item, rec.cargoSlot);
                }
                vehicle.SetModuleImmediate(Data.installedModuleId);
                if(manualModules && manualModules.Initialized) manualModules.Initialize(this);
                ApplyUpgrades();
                Changed?.Invoke();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Save load failed; starting fresh: " + e.Message);
                Data = new CompanySave(); return false;
            }
        }
    }

}
