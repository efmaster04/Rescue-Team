using System.Collections.Generic;
using UnityEngine;

// Doğrulama yardımcısı (D2): D2'den önceki taşıma / ambulans / trafik testleri hastaları patikada yatar bulmayı bekler.
// D2'de hastalar minibüste ve kaya çıkıntısında başlar; eski senaryolar başında hastalar eski patika yerlerine
// (Bolum1AmbulanceBuilder.PathLyingSpots, baş Unity +X) yatırılır. Oyun kodu değişmez, yalnız test kurulumu.
public static class Bolum1TestPatients
{
    public static void LayOnPath(IList<Patient> patients)
    {
        var markers = Bolum1Coordinates.LoadJson(Bolum1Coordinates.MarkersPath);
        var van = GameObject.Find(Bolum1SceneBuilder.MinivanName).transform;
        foreach (var p in patients)
            foreach (var s in StretcherController.All)
                if (s != null && s.Passenger == p) s.TakePatient();
        Physics.SyncTransforms();
        var spots = Bolum1AmbulanceBuilder.PathLyingSpots(markers, van, patients.Count, out List<Vector3> normals);
        for (int i = 0; i < patients.Count; i++)
        {
            var p = patients[i];
            p.onLedge = false;
            p.LieOnGround(spots[i], Vector3.right, normals[i]);
        }
        Physics.SyncTransforms();
    }
}
