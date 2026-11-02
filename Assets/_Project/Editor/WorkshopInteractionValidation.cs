using System;
using System.Collections;
using System.IO;
using System.Linq;
using CompanySim;
using UnityEngine;

namespace CompanySimEditor
{
    public static class WorkshopInteractionValidation
    {
        public static bool Finished { get; private set; }
        public static Exception Error { get; private set; }
        static void Check(bool value,string message) { if(!value) throw new Exception("Workshop drawers: "+message); }
        public static void Begin(PlayerInteractor player)
        { Finished=false; Error=null; player.StartCoroutine(Drive(Run(player))); }
        static IEnumerator Drive(IEnumerator routine)
        {
            while(true)
            {
                object next=null; bool more=false;
                try { more=routine.MoveNext(); if(more) next=routine.Current; }
                catch(Exception error) { Error=error; }
                if(!more || Error!=null) break;
                yield return next;
            }
            Finished=true;
        }
        static void Teleport(PlayerInteractor player,Vector3 position)
        {
            var controller=player.GetComponent<CharacterController>(); controller.enabled=false;
            player.transform.position=position; controller.enabled=true; Physics.SyncTransforms();
        }
        static IEnumerator Run(PlayerInteractor player)
        {
            var drawers=UnityEngine.Object.FindObjectsByType<WorkshopDrawer>();
            Check(drawers.Length==16,"sixteen physical drawers");
            Check(!player.ui.audioFeedback.drawerSlide,"drawer sound has been removed");
            var drawer=drawers.First(x=>x.name=="WorkshopDrawer_3_3");
            var body=drawer.GetComponent<Rigidbody>(); var closed=drawer.transform.position;
            var initialPosition=player.transform.position; var initialRotation=player.transform.rotation;
            var initialView=player.viewCamera.transform.localRotation;
            var jack=UnityEngine.Object.FindObjectsByType<CarryableItem>().First(x=>x.definitionId=="jack");
            var toolbox=UnityEngine.Object.FindObjectsByType<CarryableItem>().First(x=>x.definitionId=="toolbox");
            var jackPosition=jack.transform.position; var jackRotation=jack.transform.rotation;
            var toolboxPosition=toolbox.transform.position; var toolboxRotation=toolbox.transform.rotation;
            var jackBody=jack.GetComponent<Rigidbody>(); var toolboxBody=toolbox.GetComponent<Rigidbody>();
            bool jackKinematic=jackBody.isKinematic,toolboxKinematic=toolboxBody.isKinematic;
            player.SetCursor(true);
            try
            {
                jackBody.isKinematic=true; jack.transform.position=new Vector3(7.1f,.02f,-3.9f); Physics.SyncTransforms();
                foreach(var next in drawers) next.Interact(player);
                yield return new WaitForSeconds(1.6f);
                Check(drawers.All(x=>x.Extension>.355f && !x.IsBlocked),"all sixteen trays open without hitting their neighbours: "+
                    string.Join(";",drawers.Where(x=>x.Extension<.355f).Select(x=>x.name+"="+x.Extension)));
                foreach(var next in drawers) next.Interact(player);
                yield return new WaitForSeconds(1.6f);
                Check(drawers.All(x=>x.Extension<.001f),"all drawers return flush");
                Teleport(player,new Vector3(7.65f,.01f,closed.z));
                var occupied=player.transform.position;
                drawer.Interact(player); yield return new WaitForSeconds(1.1f);
                Check(drawer.IsBlocked && drawer.Extension<.12f,"opening waits for a stationary player; blocked="+drawer.IsBlocked+
                    ", extension="+drawer.Extension+", player="+player.transform.position+", drawer="+drawer.transform.position+
                    ", box="+drawer.GetComponent<BoxCollider>().bounds+", capsule="+player.GetComponent<CharacterController>().bounds);
                Check(Vector3.Distance(occupied,player.transform.position)<.01f,"drawer never ejects the player");
                Check(drawer.GetComponent<BoxCollider>().bounds.min.x>player.GetComponent<CharacterController>().bounds.max.x+.01f,
                    "a safety gap remains between the stationary capsule and the tray");
                Teleport(player,new Vector3(7.0f,.01f,closed.z)); yield return new WaitForSeconds(1.3f);
                Check(drawer.Extension>.355f,"opening resumes when player steps back");
                drawer.Interact(player); yield return new WaitForSeconds(1.3f);
                Check(drawer.Extension<.001f,"drawer closes after an interrupted opening");
                toolboxBody.isKinematic=true;
                toolbox.transform.position=new Vector3(7.72f,.66f,closed.z); Physics.SyncTransforms();
                var equipmentPosition=toolbox.transform.position;
                drawer.Interact(player); yield return new WaitForSeconds(1.2f);
                Check(drawer.IsBlocked && drawer.Extension<.20f,"opening respects loose equipment");
                Check(Vector3.Distance(equipmentPosition,toolbox.transform.position)<.001f,"equipment is not flung by the tray");
                toolbox.transform.SetPositionAndRotation(toolboxPosition,toolboxRotation); Physics.SyncTransforms();
                yield return new WaitForSeconds(1.3f);
                Check(drawer.Extension>.355f,"opening resumes after equipment is removed");
                drawer.Interact(player); yield return new WaitForSeconds(1.2f);
                Teleport(player,new Vector3(7.05f,.01f,closed.z));
                player.viewCamera.transform.LookAt(closed);
                Check(player.FindTarget()==drawer,"drawer handle is reachable with the real interaction ray");
                drawer.Interact(player); yield return new WaitForSeconds(1.3f);
                player.ui.SetPrompt("E — Çekmeceyi kapat"); player.ui.SetFocus(true);
                player.viewCamera.transform.position=new Vector3(7.03f,1.50f,closed.z-1.1f);
                player.viewCamera.transform.LookAt(new Vector3(7.94f,.74f,closed.z));
                ComputerDesktopValidation.Capture(player.ui,"Documentation/WorkshopDrawerOpen.png",1600,900);
                for(int i=0;i<11;i++) {drawer.Interact(player); yield return new WaitForSeconds(.04f);}
                yield return new WaitForSeconds(1.3f);
                Check(!drawer.IsOpen && drawer.Extension<.001f,"rapid toggles settle closed");
                Teleport(player,new Vector3(7.10f,.01f,.12f));
                player.viewCamera.transform.localPosition=new Vector3(0,PlayerInteractor.EyeHeight,0);
                player.viewCamera.transform.LookAt(new Vector3(8.82f,1.48f,.12f));
                player.ui.SetPrompt("");player.ui.SetFocus(false);
                yield return new WaitForSeconds(.15f);
                ComputerDesktopValidation.Capture(player.ui,"Documentation/WorkbenchDetailPlayView.png",1600,900);
                File.AppendAllText("Logs/office_garage_validation.txt","PASS: sixteen cabinet trays open/close, player and loose equipment block opening safely, motion resumes when clear, handle interaction ray, rapid toggle recovery, drawer audio removed\n");
            }
            finally
            {
                jack.transform.SetPositionAndRotation(jackPosition,jackRotation); jackBody.isKinematic=jackKinematic;
                toolbox.transform.SetPositionAndRotation(toolboxPosition,toolboxRotation); toolboxBody.isKinematic=toolboxKinematic;
                if(!jackKinematic) {jackBody.linearVelocity=Vector3.zero;jackBody.angularVelocity=Vector3.zero;}
                if(!toolboxKinematic) {toolboxBody.linearVelocity=Vector3.zero;toolboxBody.angularVelocity=Vector3.zero;}
                Teleport(player,initialPosition); player.transform.rotation=initialRotation;
                player.viewCamera.transform.localPosition=new Vector3(0,PlayerInteractor.EyeHeight,0);
                player.viewCamera.transform.localRotation=initialView;
                player.ui.SetPrompt(""); player.ui.SetFocus(false); player.SetCursor(false);
            }
        }
    }
}
