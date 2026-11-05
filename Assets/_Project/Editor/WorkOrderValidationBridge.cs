using System.IO;
using CompanySim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CompanySimEditor
{
    // A project-local test request also works while the user's editor owns the checkout.
    // Never enters play mode over a dirty scene, and never interrupts an existing play session.
    [InitializeOnLoad]
    public static class WorkOrderValidationBridge
    {
        const string Request="Temp/CompanyUIValidation.request",Status="Logs/workorder_bridge_status.txt";
        static double nextPoll;
        static string lastStatus;
        static WorkOrderValidationBridge() { EditorApplication.update+=Poll; }
        static void StatusText(string text)
        { if(text==lastStatus) return; lastStatus=text; Directory.CreateDirectory("Logs"); File.WriteAllText(Status,text); }
        static void Poll()
        {
            if(EditorApplication.timeSinceStartup<nextPoll) return; nextPoll=EditorApplication.timeSinceStartup+.5;
            if(!File.Exists(Request)) return;
            if(EditorApplication.isCompiling || EditorApplication.isUpdating) { StatusText("Waiting for asset imports."); return; }
            if(EditorApplication.isPlayingOrWillChangePlaymode) { StatusText("Waiting for the current play session to finish."); return; }
            if(EditorSceneManager.GetActiveScene().isDirty) { StatusText("Waiting: the open scene has unsaved changes; no scene was replaced."); return; }
            string mode=File.ReadAllText(Request).Trim(); File.Delete(Request);
            StatusText("Started "+mode+" validation in the open editor.");
            if(mode=="inspect") GarageSystemsBuilder.Inspect();
            else if(mode=="garage") GarageSystemsBuilder.Apply();
            else if(mode=="garage-test") ProjectValidation.RunGarageOnly();
            else if(mode=="manual-test") ProjectValidation.RunManualOnly();
            else if(mode=="full") ProjectValidation.Run();
            else if(mode=="rear-workshop") WorkshopVisualFinishBuilder.ApplyAndValidate();
            else if(mode=="workshop-craft") WorkshopCraftBuilder.ApplyAndValidate();
            else if(mode=="craft-preview") WorkshopCraftBuilder.Apply();
            else if(mode=="vehicle-detail") VehicleDetailBuilder.ApplyAndValidate();
            else if(mode=="detail-preview") VehicleDetailBuilder.Apply();
            else if(mode=="vehicle-interior") VehicleInteriorBuilder.ApplyAndValidate();
            else if(mode=="interior-preview") VehicleInteriorBuilder.Apply();
            else if(mode=="workbench-detail") WorkbenchDetailBuilder.ApplyAndValidate();
            else if(mode=="workbench-preview") WorkbenchDetailBuilder.Apply();
            else if(mode=="storage-detail") GarageStorageBuilder.ApplyAndValidate();
            else if(mode=="storage-preview") GarageStorageBuilder.Apply();
            else if(mode=="module-equipment") ModuleEquipmentBuilder.ApplyAndValidate();
            else if(mode=="module-equipment-preview") ModuleEquipmentBuilder.Apply();
            else if(mode=="module-equipment-manual") {ModuleEquipmentBuilder.Apply();ProjectValidation.RunManualOnly();}
            else if(mode=="portable-equipment-preview") PortableEquipmentBuilder.Apply();
            else if(mode=="portable-equipment") PortableEquipmentBuilder.ApplyAndValidate();
            else if(mode=="blender-equipment-preview") BlenderEquipmentBuilder.Apply();
            else if(mode=="blender-equipment") BlenderEquipmentBuilder.ApplyAndValidate();
            else if(mode=="blender-modules-preview") BlenderModuleBuilder.Apply();
            else if(mode=="blender-modules") BlenderModuleBuilder.ApplyAndValidate();
            else if(mode=="service-modules-preview") ServiceModuleArtImporter.Apply();
            else if(mode=="service-modules") ServiceModuleArtImporter.ApplyAndValidate();
            else if(mode=="rear-garage") GarageSystemsBuilder.ApplyAndValidate();
            else if(mode=="rear-manual") GarageSystemsBuilder.ApplyAndValidateManual();
            else if(mode=="ui") ProjectValidation.RunUIOnly();
            else StatusText("Unknown validation request: "+mode);
        }
    }
}
