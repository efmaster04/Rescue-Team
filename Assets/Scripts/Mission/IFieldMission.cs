using System.Collections.Generic;

// Saha görevi arayüzü: FieldMissionTracker görevin bittiğini, başarısını ve süresini hangi bölüm olursa olsun buradan okur.
// Bölüm 1'in MissionDirector'ı ve Sel Vadisi'nin SelVadisiMission'ı bunu uygular.
public interface IFieldMission
{
    bool IsFinished { get; }
    bool Succeeded { get; }
    float Elapsed { get; }
}

// Sahnedeki etkin saha görevi. MissionDirector (Bölüm 1) varsa o, yoksa kayıtlı son görev.
public static class FieldMission
{
    private static readonly List<IFieldMission> registered = new List<IFieldMission>();

    public static IFieldMission Current
    {
        get
        {
            if (MissionDirector.Instance != null) return MissionDirector.Instance;
            for (int i = registered.Count - 1; i >= 0; i--)
            {
                var m = registered[i];
                if (m is UnityEngine.Object o && o == null) { registered.RemoveAt(i); continue; }
                if (m != null) return m;
            }
            return null;
        }
    }

    public static void Register(IFieldMission mission)
    {
        if (mission != null && !registered.Contains(mission)) registered.Add(mission);
    }

    public static void Unregister(IFieldMission mission)
    {
        registered.Remove(mission);
    }

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { registered.Clear(); }
}
