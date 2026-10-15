using System.Collections.Generic;
using UnityEngine;

// "Taşıyıcı" soyutlaması (kullanıcı kararı 8): sedyeyi tutabilen her şey (oyuncu, test botu, ileride ikinci oyuncu).
// Sedye yalnız bu arayüzü bilir; kimin tuttuğu onu ilgilendirmez.
public interface ICarrier
{
    // Taşıyıcının kökü (ayak hizası).
    Transform Root { get; }

    // Şu an tuttuğu sedye (yoksa null). Yalnız StretcherController yazar.
    StretcherController HeldStretcher { get; set; }

    // Sedyenin tutulan ucunun gideceği el noktası. towards: sedyenin merkezi (el o yöne uzanır).
    Vector3 GetHandPoint(Vector3 towards);
}

// Sahnedeki aktif taşıyıcılar (ambulansın "önümde biri var mı" kontrolü bunu tarar).
public static class CarrierRegistry
{
    public static readonly List<ICarrier> All = new List<ICarrier>();

    public static void Add(ICarrier c) { if (!All.Contains(c)) All.Add(c); }
    public static void Remove(ICarrier c) { All.Remove(c); }

    // Ortak el noktası hesabı: kök + tutma yüksekliği + sedyeye doğru kısa uzanma.
    public static Vector3 HandPoint(Transform root, float handHeight, float reach, Vector3 towards)
    {
        Vector3 basePoint = root.position + Vector3.up * handHeight;
        Vector3 dir = towards - root.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f) dir = root.forward;
        return basePoint + dir.normalized * reach;
    }
}
