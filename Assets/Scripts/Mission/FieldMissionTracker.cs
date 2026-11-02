using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Görev köprüsü, saha tarafı: yalnız ofisten gelinen oturumda (MissionBridge.Active) kurulur.
//  - Setup (sceneLoaded, Start'lardan önce): FieldEquipmentMap tablosuyla yüklenen eşyaları sahnedeki nesnelere eşler,
//    yüklenmeyenleri kaldırır. LootRegistry toplamı Start'ta sayıldığı için kaldırılanlar çalınma çubuğuna girmez.
//  - Update: koni çarpması, ekipman fırlatma ve hırsızdan geri alma sayaçlarını izler; geç doğan müşteri eşyalarını ekler.
//  - Görev bitince (MissionDirector.IsFinished) sonucu bir kez hesaplar (FieldPayout), sonuç panelini açar;
//    Enter ile ofise dönülür.
public sealed class FieldMissionTracker : MonoBehaviour
{
    public static FieldMissionTracker Instance { get; private set; }

    public sealed class Mapped
    {
        public MissionBridge.Gear gear;
        public FieldEquipmentMap.Entry entry;
        public GameObject go;
        public int coneHits, throws, recovered;
    }

    public MissionBridge.Session Session { get; private set; }
    public readonly List<Mapped> MappedGear = new List<Mapped>();
    public readonly List<MissionBridge.Gear> UnmappedGear = new List<MissionBridge.Gear>(); // sahada karşılığı olmayan
    public readonly Dictionary<string, int> PresentCounts = new Dictionary<string, int>();  // ekipman id → sahada bırakılan
    public int HiddenCount { get; private set; }
    public bool ResultComputed { get; private set; }
    private readonly HashSet<LootItem> goods = new HashSet<LootItem>();

    private void Awake() { Instance = this; }
    private void OnDestroy() { if (Instance == this) Instance = null; }

    public void Setup(MissionBridge.Session session)
    {
        Session = session;
        foreach (var entry in FieldEquipmentMap.Entries)
        {
            var objects = FieldEquipmentMap.SceneObjects(entry);
            var loaded = session.gear.Where(g => g.definitionId == entry.equipmentId).ToList();
            for (int i = 0; i < objects.Count; i++)
            {
                if (i < loaded.Count) MappedGear.Add(new Mapped { gear = loaded[i], entry = entry, go = objects[i] });
                else { FieldEquipmentMap.Hide(objects[i]); HiddenCount++; }
            }
            for (int i = objects.Count; i < loaded.Count; i++) UnmappedGear.Add(loaded[i]);
            PresentCounts[entry.equipmentId] = Mathf.Min(objects.Count, loaded.Count);
        }
        foreach (var g in session.gear)
            if (FieldEquipmentMap.Find(g.definitionId) == null) UnmappedGear.Add(g);
        foreach (var item in FindObjectsByType<LootItem>(FindObjectsInactive.Include))
            if (item.kind != LootKind.Equipment) goods.Add(item);
        Debug.Log("MissionBridge: saha kuruldu (" + session.jobTitle + "): eşlenen " + MappedGear.Count +
                  ", kaldırılan " + HiddenCount + ", sahada karşılığı olmayan " + UnmappedGear.Count);
    }

    private void Update()
    {
        if (Session == null) return;
        foreach (var item in LootItem.All) if (item != null && item.kind != LootKind.Equipment) goods.Add(item);
        foreach (var m in MappedGear)
        {
            if (m.go == null) continue; // yok olduysa son sayılar kalır
            var cone = m.go.GetComponent<TrafficCone>();
            if (cone != null) m.coneHits = Mathf.Max(m.coneHits, cone.VehicleHits);
            var loot = m.go.GetComponent<LootItem>();
            if (loot != null) { m.throws = Mathf.Max(m.throws, loot.Throws); m.recovered = Mathf.Max(m.recovered, loot.VanRetrievals); }
        }
        var mission = MissionDirector.Instance;
        if (!ResultComputed && mission != null && mission.IsFinished) Finish(mission);
        if (ResultComputed && !MissionBridge.IsTransitioning && MissionBridge.ReturnKeyPressed()) MissionBridge.ReturnToOffice();
    }

    // Sahadaki olayları formül girdisine çevirir.
    public FieldPayout.Inputs Collect(MissionDirector mission)
    {
        var i = new FieldPayout.Inputs
        {
            succeeded = mission != null && mission.Succeeded,
            jobPayment = Session.jobPayment,
            distanceKm = Session.distanceKm,
            fieldSeconds = mission != null ? mission.Elapsed : 0f,
        };
        foreach (var g in goods)
        {
            if (g == null) continue;
            if (g.IsSecured) i.rescuedGoodsValue += g.TotalValue;
            else if (g.IsStolen || g.IsInVan) i.stolenGoodsValue += g.TotalValue;
        }
        if (mission != null) foreach (var p in mission.patients) if (p != null) i.missedAmbulances += p.MissedAmbulances;
        foreach (var m in MappedGear)
        {
            i.coneHits += m.coneHits;
            i.equipmentThrows += m.throws;
            i.equipmentRecovered += m.recovered;
            if (FieldEquipmentMap.IsLost(m.go)) { i.lostEquipmentUids.Add(m.gear.uid); i.lostEquipmentNames.Add(m.gear.label); }
        }
        return i;
    }

    private void Finish(MissionDirector mission)
    {
        ResultComputed = true;
        var breakdown = FieldPayout.Compute(Collect(mission));
        MissionBridge.SetResult(Session, breakdown);
        MissionBridgeRunner.Instance.ShowEndPanel("Saha raporu · " + Session.jobTitle, FieldPayout.Lines(breakdown, Session.jobPayment));
    }
}
