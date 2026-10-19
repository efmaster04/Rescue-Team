using System;
using UnityEngine;

// U3 (kullanıcı kararı "Can ve para" + gece kararı (a)/(b), 2026-10-19): eşya değerleri (₺). Koda gömülmez, buradan ayarlanır.
//  - Valizin kendisi az eder (20); içindekiler çok ("Valizden çok içindekiler para etsin"): sabit tohumla 2–4 eşya, toplam 150–450.
//  - Hasta eşyası: Turist Kamera 150, Sivil Cüzdan 100, Yaşlı Cüzdan 100.
//  - Ekipman para kazandırmaz; çalınırsa − yazılır ve çubuğa sayılır: Sedye 300, Halat 150, Sağlık çantası 120, Koni 20.
//  - Çalınan değer toplamın failFraction'ına (%50) ulaşınca görev başarısız.
[CreateAssetMenu(menuName = "Rescue Team/Bölüm 1/Eşya Değerleri", fileName = "LootValues")]
public class LootValues : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public string name;
        public int value;
        public Entry() { }
        public Entry(string n, int v) { name = n; value = v; }
    }

    [Header("Valiz")]
    public int suitcaseValue = 20;
    [Tooltip("Valiz içeriği listesi (rastgele 2–4 tanesi seçilir, tekrarsız).")]
    public Entry[] catalog =
    {
        new Entry("Saat", 80), new Entry("Laptop", 300), new Entry("Kamera", 150), new Entry("Parfüm", 60),
        new Entry("Kolye", 200), new Entry("Tablet", 250), new Entry("Kulaklık", 90),
    };
    public int minItems = 2, maxItems = 4;
    [Tooltip("İçindekilerin toplamı bu aralıkta kalana kadar yeniden çekilir.")]
    public int minContentTotal = 150, maxContentTotal = 450;
    [Tooltip("Sabit tohum: aynı sahne hep aynı valiz içeriğini verir.")]
    public int seed = 20261019;

    [Header("Hasta eşyası (hasta türüne göre)")]
    public Entry touristItem = new Entry("Kamera", 150);
    public Entry civilianItem = new Entry("Cüzdan", 100);
    public Entry elderItem = new Entry("Cüzdan", 100);

    [Header("Ekipman (para kazandırmaz, çalınırsa −)")]
    public int stretcherValue = 300;
    public int ropeValue = 150;
    public int medkitValue = 120;
    public int coneValue = 20;

    [Header("Çalınma")]
    [Range(0.05f, 1f)] public float failFraction = 0.5f;
    [Tooltip("Valiz içindekiler bu aralıkla (sn) tek tek yazılır.")]
    public float creditInterval = 0.4f;

    public Entry PatientItem(string type)
    {
        if (type.Contains("Tourist")) return touristItem;
        if (type.Contains("Civilian")) return civilianItem;
        return elderItem;
    }

    // Sabit tohumla 2–4 tekrarsız içerik; toplam [min, max] aralığında kalana kadar yeniden çekilir.
    // index: valizin sırası (aynı sahnede her valiz farklı, ama her oyunda aynı). Builder ve U4 tavan valizleri kullanır.
    public System.Collections.Generic.List<Entry> Roll(int index)
    {
        var rng = new System.Random(seed + index * 7919);
        System.Collections.Generic.List<Entry> last = null;
        for (int attempt = 0; attempt < 500; attempt++)
        {
            int count = rng.Next(minItems, maxItems + 1);
            var order = new System.Collections.Generic.List<int>();
            for (int i = 0; i < catalog.Length; i++) order.Add(i);
            for (int i = 0; i < order.Count; i++) { int j = rng.Next(i, order.Count); int t = order[i]; order[i] = order[j]; order[j] = t; }
            last = new System.Collections.Generic.List<Entry>();
            int total = 0;
            for (int i = 0; i < count && i < order.Count; i++) { var e = catalog[order[i]]; last.Add(new Entry(e.name, e.value)); total += e.value; }
            if (total >= minContentTotal && total <= maxContentTotal) return last;
        }
        return last ?? new System.Collections.Generic.List<Entry>();
    }
}
