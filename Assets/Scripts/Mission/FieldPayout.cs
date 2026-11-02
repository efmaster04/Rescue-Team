using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// Görev köprüsü: saha sonucunun para, aşınma ve kayıp formülü. TEK YER; bütün sabitler burada.
// Ölçek: iş bedelleri ₺680–1.100 (arkadaşın JobDefinition'ları), Bölüm 1'deki müşteri eşyalarının toplamı ~₺1.900
// (LootValues: valiz/çanta 170–400, hasta eşyası 100–150). Ekipman değerleri (sedye 300, halat 150, koni 20) mağaza
// fiyatlarıyla aynıdır.
//
//   bonus      = kurtarılan müşteri eşyası değeri × RescueBonusShare           (yalnız başarıda; en çok iş bedeli)
//   gider      = çalınan müşteri eşyası değeri × StolenGoodsLiability
//              + kaçırılan ambulans sayısı (hasta başına toplam) × MissedAmbulanceFine   (en çok iş bedeli)
//   ekipman aşınması (%) = BaseEquipmentWear + koniye araç çarpması × WearPerConeHit
//              + fırlatılan ekipman × WearPerEquipmentThrow + hırsızdan geri alınan ekipman × WearPerEquipmentRecovered
//   araç aşınması (%)    = gidiş-dönüş km × VehicleWearPerKm + sahada geçen dakika × VehicleWearPerFieldMinute
//   kayıp (consumedItemUids) = çalınan / hırsız kamyonetinde kalan / uçuruma düşüp yok olan ekipman
// Çalınan EKİPMAN gidere ayrıca yazılmaz: kaybolduğu için depodan düşer (yeniden almak zaten masraf).
// CompanyRuntime.ReturnFromField: başarıda iş bedeli + bonus + sözleşme − gider; başarısızlıkta yalnız −gider; sonuç < 0 ise 0.
public static class FieldPayout
{
    public const float RescueBonusShare = 0.4f;        // kurtarılan müşteri eşyası değerinin bonusa geçen payı
    public const float StolenGoodsLiability = 0.4f;    // çalınan müşteri eşyası için ödenen tazminat payı
    public const int MissedAmbulanceFine = 50;         // her kaçırılan ambulans (hasta başına) için ceza (₺)
    public const float BaseEquipmentWear = 3f;         // olaysız bir görevde ekipman aşınması (%)
    public const float WearPerConeHit = 2f;            // koniye araç çarptığında (%)
    public const float WearPerEquipmentThrow = 2f;     // ekipman fırlatıldığında (%)
    public const float WearPerEquipmentRecovered = 4f; // hırsızın kamyonetinden geri alınan ekipman (%)
    public const float VehicleWearPerKm = 0.15f;       // gidiş-dönüş yol (%)
    public const float VehicleWearPerFieldMinute = 0.25f; // sahada geçen süre (%/dk)

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    // Sahadan toplanan ham olaylar (FieldMissionTracker doldurur; testler elle verir).
    public sealed class Inputs
    {
        public bool succeeded;
        public int jobPayment;
        public float distanceKm;
        public float fieldSeconds;
        public int rescuedGoodsValue;   // güvenli yere konan / ambulansa binen müşteri eşyası (₺)
        public int stolenGoodsValue;    // çalınan ya da kamyonette kalan müşteri eşyası (₺)
        public int missedAmbulances;    // bütün hastaların kaçırdığı ambulans sayısı toplamı
        public int coneHits;
        public int equipmentThrows;
        public int equipmentRecovered;
        public List<string> lostEquipmentUids = new List<string>();
        public List<string> lostEquipmentNames = new List<string>();
    }

    public sealed class Breakdown
    {
        public bool succeeded;
        public int bonus, expenses, stolenGoodsCharge, missedAmbulanceCharge;
        public float equipmentWear, vehicleWear;
        public List<string> consumedItemUids = new List<string>();
        public List<string> lostNames = new List<string>();
        // Ofiste rapor onaylanınca kasaya girecek tutar (sözleşme bonusu hariç; CompanyRuntime ekler).
        public int expectedPayout;
    }

    public static Breakdown Compute(Inputs i)
    {
        var b = new Breakdown { succeeded = i.succeeded };
        int cap = Mathf.Max(0, i.jobPayment);
        b.bonus = i.succeeded ? Mathf.Clamp(Mathf.RoundToInt(i.rescuedGoodsValue * RescueBonusShare), 0, cap) : 0;
        b.stolenGoodsCharge = Mathf.RoundToInt(i.stolenGoodsValue * StolenGoodsLiability);
        b.missedAmbulanceCharge = i.missedAmbulances * MissedAmbulanceFine;
        b.expenses = Mathf.Clamp(b.stolenGoodsCharge + b.missedAmbulanceCharge, 0, cap);
        b.equipmentWear = Mathf.Clamp(Round1(BaseEquipmentWear + i.coneHits * WearPerConeHit +
            i.equipmentThrows * WearPerEquipmentThrow + i.equipmentRecovered * WearPerEquipmentRecovered), 0f, 100f);
        b.vehicleWear = Mathf.Clamp(Round1(i.distanceKm * 2f * VehicleWearPerKm +
            i.fieldSeconds / 60f * VehicleWearPerFieldMinute), 0f, 100f);
        b.consumedItemUids.AddRange(i.lostEquipmentUids);
        b.lostNames.AddRange(i.lostEquipmentNames);
        b.expectedPayout = Mathf.Max(0, (i.succeeded ? cap + b.bonus : 0) - b.expenses);
        return b;
    }

    static float Round1(float v) { return Mathf.Round(v * 10f) / 10f; }

    public static string Money(int amount) { return "₺" + amount.ToString("N0", Tr); }

    // Sonuç ekranı satırları (Bölüm 1'de görev bitince gösterilir).
    public static List<string> Lines(Breakdown b, int jobPayment)
    {
        var lines = new List<string>();
        lines.Add("İş bedeli: " + (b.succeeded ? Money(jobPayment) : Money(0) + " (görev başarısız)"));
        lines.Add("Kurtarılan eşya bonusu: +" + Money(b.bonus));
        lines.Add("Giderler: −" + Money(b.expenses) + "  (çalınan eşya " + Money(b.stolenGoodsCharge) +
                  ", kaçırılan ambulans " + Money(b.missedAmbulanceCharge) + ")");
        lines.Add("Ekipman aşınması %" + b.equipmentWear.ToString("0.#", Tr) + " · araç aşınması %" + b.vehicleWear.ToString("0.#", Tr));
        lines.Add(b.lostNames.Count > 0 ? "Kaybolan ekipman: " + string.Join(", ", b.lostNames) : "Kaybolan ekipman yok");
        lines.Add("Kasaya (rapor onayında): " + Money(b.expectedPayout));
        return lines;
    }
}
