using UnityEngine;

namespace CompanySim
{
    public sealed class AudioFeedback : MonoBehaviour
    {
        public AudioClip uiClick, notification, garageMotor, itemDrop, footstep, doorOpen, doorClose, drawerSlide;
        void Awake()
        {
            // Retain the interaction API for existing scenes, with no audio allocation/playback.
            uiClick=notification=garageMotor=itemDrop=footstep=doorOpen=doorClose=drawerSlide=null;
            foreach(var source in GetComponents<AudioSource>()) { source.Stop(); source.mute=true; source.enabled=false; }
        }
        public void Click() { }
        public void Notice() { }
        public void Motor() { }
        public void Drop() { }
        public void Step() { }
        public void Drawer(Vector3 position) { }
        public void Door(bool opening,Vector3 position) { }
        public void Impact(Vector3 position,float strength) { }
    }
}
