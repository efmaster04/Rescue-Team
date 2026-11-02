using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Görev köprüsü: ofisteki ekipman tanımı (EquipmentDefinition.id) ↔ Bölüm 1'deki sahne nesnesi. TEK EŞLEME TABLOSU.
// Bölüm 1'deki adlar değişebilir (çekici → servis minibüsü), bu yüzden nesneler önce bileşene göre aranır;
// bileşen hiç bulunamazsa ad öneklerine bakılır. Nesneler ada göre sıralanır: yüklenen N eşya ilk N nesneye eşlenir,
// kalanlar sahneden kaldırılır.
public static class FieldEquipmentMap
{
    public sealed class Entry
    {
        public readonly string equipmentId;   // EquipmentDefinition.id (ofis kataloğu)
        public readonly string label;         // yükleme ekranı ve rapor
        public readonly Type component;       // Bölüm 1'de aranan bileşen
        public readonly string[] namePrefixes; // bileşen bulunamazsa yedek ad önekleri

        public Entry(string equipmentId, string label, Type component, params string[] namePrefixes)
        {
            this.equipmentId = equipmentId;
            this.label = label;
            this.component = component;
            this.namePrefixes = namePrefixes;
        }
    }

    public static readonly Entry[] Entries =
    {
        new Entry("rope", "Halat", typeof(RopeCoil), "RopeCoil", "SM_RopeCoil"),
        new Entry("stretcher", "Sedye", typeof(StretcherController), "Stretcher_"),
        new Entry("cone", "Trafik konisi", typeof(TrafficCone), "TrafficCone_"),
    };

    public static Entry Find(string equipmentId)
    {
        foreach (var e in Entries) if (e.equipmentId == equipmentId) return e;
        return null;
    }

    // Sahnede etkin, bu girdiye uyan nesneler (ada göre sıralı).
    public static List<GameObject> SceneObjects(Entry e)
    {
        var found = new List<GameObject>();
        if (e.component != null)
            foreach (var o in UnityEngine.Object.FindObjectsByType(e.component, FindObjectsInactive.Exclude))
                if (o is Component c) found.Add(c.gameObject);
        if (found.Count == 0 && e.namePrefixes != null)
            foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
                foreach (var p in e.namePrefixes)
                    if (t.name.StartsWith(p, StringComparison.Ordinal)) { found.Add(t.gameObject); break; }
        return found.Distinct().OrderBy(g => g.name, StringComparer.Ordinal).ToList();
    }

    // Yüklenmemiş ekipmanı sahneden kaldırır (koni: kontrol objesi + yönettiği koni parçası).
    public static void Hide(GameObject go)
    {
        if (go == null) return;
        var cone = go.GetComponent<TrafficCone>();
        if (cone != null && cone.target != null) cone.target.gameObject.SetActive(false);
        go.SetActive(false);
    }

    // Eşya sahada kayboldu mu: yok edildi (uçuruma düşen koni), çalındı ya da görev bittiğinde hırsız kamyonetinde.
    public static bool IsLost(GameObject go)
    {
        if (go == null) return true;
        var cone = go.GetComponent<TrafficCone>();
        if (cone != null && cone.target == null) return true;
        var loot = go.GetComponent<LootItem>();
        return loot != null && (loot.IsStolen || loot.IsInVan);
    }
}
