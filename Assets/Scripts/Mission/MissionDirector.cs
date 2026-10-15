using System;
using System.Collections.Generic;
using UnityEngine;
namespace RescueTeam.Bolum1
{
    [DefaultExecutionOrder(-100)]
    public sealed class MissionDirector : MonoBehaviour
    {
        public static MissionDirector Instance { get; private set; }
        public AmbulanceSchedule schedule;
        public AmbulanceController ambulancePrefab;
        public LanePath lane;
        public Transform ambulanceStop;
        public Patient[] patients;
        public StretcherController[] stretchers;
        public event Action MissionSucceeded;
        public event Action<Patient> MissionFailed;
        public event Action<AmbulanceController> AmbulanceSpawned;
        public bool IsFinished { get; private set; }
        public bool Succeeded { get; private set; }
        public string ResultMessage { get; private set; }
        public float Elapsed { get; private set; }
        public float NextArrival { get; private set; }
        public float TravelDuration { get; private set; }
        public readonly List<AmbulanceController> ambulances = new List<AmbulanceController>();
        float nextSpawnArrival;
        void Awake() { Instance = this; }
        void Start() { ResetClock(); }
        public void ResetClock()
        {
            Elapsed = 0; nextSpawnArrival = NextArrival = schedule.firstArrival;
            TravelDuration = ambulancePrefab.follower.TravelTime(lane.Project(ambulanceStop.position));
        }
        void FixedUpdate()
        {
            Elapsed += Time.fixedDeltaTime;
            if (Elapsed >= nextSpawnArrival - TravelDuration)
            {
                var ambulance = Instantiate(ambulancePrefab);
                ambulance.Initialize(lane, ambulanceStop, schedule);
                ambulances.Add(ambulance);
                ambulance.Arrived += OnArrival; ambulance.Departed += OnDeparture;
                ambulance.Despawned += a => ambulances.Remove(a);
                AmbulanceSpawned?.Invoke(ambulance);
                nextSpawnArrival += Mathf.Max(1, schedule.interval);
            }
        }
        void OnArrival(AmbulanceController ambulance) { NextArrival += Mathf.Max(1, schedule.interval); }
        public void OnDeparture(AmbulanceController ambulance)
        {
            foreach (var patient in patients)
                if (patient.MissAmbulance() >= 3 && !IsFinished)
                {
                    IsFinished = true; ResultMessage = "Görev başarısız: " + patient.displayName + " 3 ambulansı kaçırdı";
                    MissionFailed?.Invoke(patient);
                }
        }
        public void PatientLoaded()
        {
            if (IsFinished) return;
            foreach (var p in patients) if (p.State != PatientState.InAmbulance && p.State != PatientState.Rescued) return;
            IsFinished = true; Succeeded = true; ResultMessage = "Hastaların hepsi ambulansta!";
            MissionSucceeded?.Invoke();
        }
        void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
