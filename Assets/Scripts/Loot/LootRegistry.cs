using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// U3: para ve çalınan çubuğu (kullanıcı kararı "Can ve para", 2026-10-19).
//  - TotalValue: sahnedeki bütün eşyaların (valiz içerikleri + hasta eşyaları + ekipman) toplam değeri (Start'ta sayılır).
//  - Secure(item): eşya güvende; valiz + içindekiler creditInterval (0.4 sn) arayla tek tek paraya eklenir ("+300 ₺ Laptop").
//  - ReportStolen(item): "−300 ₺ Sedye", çubuk artar; çalınan / toplam ≥ failFraction (%50) → MissionDirector görevi başarısız bitirir.
//    Güvenli yere konmuş eşya çalınamaz. (Hırsızlar U4'te; şimdilik testler çağırır.)
// Oyun mantığı yalnız olay yayınlar; nasıl gösterileceğine HUD karar verir.
[DefaultExecutionOrder(-40)]
public class LootRegistry : MonoBehaviour
{
    public static LootRegistry Instance { get; private set; }

    public LootValues values;
    public MissionDirector mission;

    public const string FailText = "Görev başarısız: çok şey çalındı";
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");
    // Para simgesi: yazı tipinde ₺ yoksa HUD "TL" kullanır (MissionHud ayarlar)
    public static string MoneySymbol = "₺";

    public int TotalValue { get; private set; }
    public int StolenValue { get; private set; }
    public int Money { get; private set; }
    public int SecuredValue { get; private set; }
    public float StolenFraction { get { return TotalValue > 0 ? (float)StolenValue / TotalValue : 0f; } }
    public int PendingCredits { get { return queue.Count; } }
    public bool Failed { get; private set; }
    public int SecuredCount { get; private set; }
    public int StolenCount { get; private set; }
    // U4b (kullanıcı kararı 2026-10-20): kamyonete atılmış ama kamyonet kalkmadığı için geri alınabilir eşyalar ("risk").
    // %50 kontrolüne SAYILMAZ; kamyonet kaçınca ReportStolen ile kesinleşir (kontrol o anda).
    public int InVanValue { get; private set; }
    public int InVanCount { get; private set; }
    public float InVanFraction { get { return TotalValue > 0 ? (float)InVanValue / TotalValue : 0f; } }
    public int RetrievedCount { get; private set; }

    // (etiket, miktar): "+300 ₺ Laptop" / "−300 ₺ Sedye" uçan yazıları HUD üretir
    public event Action<string, int> Credited;
    public event Action<string, int> Stolen;
    // U4b: kamyonete atıldı ("−400 ₺ Çanta (kamyonette)", turuncu) / bagajdan geri alındı ("+400 ₺ Çanta (geri alındı)")
    public event Action<string, int> PutInVan;
    public event Action<string, int> Retrieved;
    public const string InVanSuffix = " (kamyonette)", RetrievedSuffix = " (geri alındı)";

    private readonly Queue<KeyValuePair<string, int>> queue = new Queue<KeyValuePair<string, int>>();
    private Coroutine crediting;

    private void Awake() { Instance = this; }
    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Start()
    {
        if (mission == null) mission = MissionDirector.Instance;
        ComputeTotal();
    }

    public void ComputeTotal()
    {
        int t = 0;
        foreach (var i in LootItem.All) if (i != null) t += i.TotalValue;
        TotalValue = t;
    }

    // "₺ 1.240" (Türkçe binlik ayırıcı)
    public static string FormatMoney(int amount) { return MoneySymbol + " " + amount.ToString("N0", Tr); }
    public static string CreditLabel(string name, int amount) { return "+" + amount.ToString("N0", Tr) + " " + MoneySymbol + " " + name; }
    public static string StolenLabel(string name, int amount) { return "-" + amount.ToString("N0", Tr) + " " + MoneySymbol + " " + name; }

    // Eşya güvenli yere kondu (ya da hastayla ambulansa bindi).
    public bool Secure(LootItem item)
    {
        if (item == null || item.IsSecured || item.IsStolen) return false;
        item.Secure();
        SecuredCount++;
        SecuredValue += item.TotalValue;
        queue.Enqueue(new KeyValuePair<string, int>(item.displayName, item.value));
        foreach (var c in item.contents) if (c != null) queue.Enqueue(new KeyValuePair<string, int>(c.name, c.value));
        if (crediting == null && isActiveAndEnabled) crediting = StartCoroutine(CreditRoutine());
        return true;
    }

    private IEnumerator CreditRoutine()
    {
        float interval = values != null ? values.creditInterval : 0.4f;
        while (queue.Count > 0)
        {
            var e = queue.Dequeue();
            Money += e.Value;
            Credited?.Invoke(CreditLabel(e.Key, e.Value), e.Value);
            yield return new WaitForSeconds(interval);
        }
        crediting = null;
    }

    // U4b: hırsız eşyayı kamyonete attı; kamyonet durdukça geri alınabilir. (Eşyayı ThiefVan.AddLoot gizler: MarkInVan.)
    public bool ReportInVan(LootItem item)
    {
        if (item == null || item.IsSecured || item.IsStolen) return false;
        int v = item.TotalValue;
        InVanCount++;
        InVanValue += v;
        PutInVan?.Invoke(StolenLabel(item.displayName, v) + InVanSuffix, v);
        return true;
    }

    // U4b: kamyonetteki eşya geri alındı (ya da kamyonet kalkmadan yok edildi): yalnız kayıt düşer; eşyayı ThiefVan.Retrieve yerleştirir.
    public bool ReportRetrieved(LootItem item)
    {
        if (item == null || !item.IsInVan) return false;
        int v = item.TotalValue;
        InVanCount = Mathf.Max(0, InVanCount - 1);
        InVanValue = Mathf.Max(0, InVanValue - v);
        RetrievedCount++;
        Retrieved?.Invoke(CreditLabel(item.displayName, v) + RetrievedSuffix, v);
        return true;
    }

    // Hırsız eşyayı çaldı (U4; U4b: kamyonet kaçınca kamyonettekiler için). Güvende/zaten çalınmış eşya çalınamaz.
    public bool ReportStolen(LootItem item)
    {
        if (item == null || item.IsSecured || item.IsStolen) return false;
        int v = item.TotalValue;
        if (item.IsInVan) { InVanCount = Mathf.Max(0, InVanCount - 1); InVanValue = Mathf.Max(0, InVanValue - v); }
        item.MarkStolen();
        StolenCount++;
        StolenValue += v;
        Stolen?.Invoke(StolenLabel(item.displayName, v), v);
        float limit = values != null ? values.failFraction : 0.5f;
        if (!Failed && StolenFraction >= limit - 1e-4f)
        {
            Failed = true;
            if (mission != null) mission.Fail(FailText);
        }
        return true;
    }

    // Test: çalınmış (ya da kamyonetteki) bir eşyayı geri getirir (yerine döner, değer ve sayaç düşer). Başarısızlık geri alınmaz.
    public void RestoreForTest(LootItem item)
    {
        if (item == null) return;
        if (item.IsInVan)
        {
            int iv = item.TotalValue;
            InVanCount = Mathf.Max(0, InVanCount - 1);
            InVanValue = Mathf.Max(0, InVanValue - iv);
            ThiefVan.ForgetLoot(item);
            item.ResetToStart();
            return;
        }
        if (!item.IsStolen) return;
        int v = item.TotalValue;
        item.ResetToStart();
        StolenCount = Mathf.Max(0, StolenCount - 1);
        StolenValue = Mathf.Max(0, StolenValue - v);
    }

    // Test: her şey başa döner (eşyalar yerine, para ve çalınan sıfır).
    public void ResetForTest()
    {
        if (crediting != null) { StopCoroutine(crediting); crediting = null; }
        queue.Clear();
        Money = 0;
        StolenValue = 0;
        SecuredValue = 0;
        SecuredCount = 0;
        StolenCount = 0;
        InVanValue = 0;
        InVanCount = 0;
        RetrievedCount = 0;
        Failed = false;
        foreach (var v in ThiefVan.All) if (v != null) v.Loot.Clear();
        foreach (var i in LootItem.All.ToArray()) if (i != null) i.ResetToStart();
        // Kapalı (güvende/çalınmış) eşyalar All'da değildir: sahnedeki hepsini bul
        foreach (var i in FindObjectsByType<LootItem>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (!i.gameObject.activeSelf) i.ResetToStart();
        ComputeTotal();
    }
}
