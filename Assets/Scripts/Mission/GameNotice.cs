using System;
using UnityEngine;

// Kısa süreli oyun uyarıları ("İki kişi gerekli", "Ambulans dolu").
// Oyun mantığı yalnız mesajı yayınlar; nasıl gösterileceğine UI karar verir (mantık ile arayüz ayrı).
public static class GameNotice
{
    public static event Action<string> Raised;

    // Son yayınlanan mesaj ve zamanı (testler ve HUD için).
    public static string LastMessage { get; private set; }
    public static float LastTime { get; private set; } = -100f;

    public static void Show(string message)
    {
        LastMessage = message;
        LastTime = Time.time;
        Raised?.Invoke(message);
    }

    // Sahne yeniden yüklenince eski mesaj kalmasın.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Raised = null;
        LastMessage = null;
        LastTime = -100f;
    }

    public static void Clear()
    {
        LastMessage = null;
        LastTime = -100f;
    }
}
