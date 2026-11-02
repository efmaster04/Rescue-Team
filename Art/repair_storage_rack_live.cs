var old = UnityEngine.Object.FindObjectsByType<CompanySim.UpgradeVisual>(UnityEngine.FindObjectsInactive.Include,UnityEngine.FindObjectsSortMode.None).First(x=>x.upgradeId=="storage").gameObject;
var capture=typeof(CompanySimEditor.GarageRefinementBuilder).GetMethod("SignagePreview",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
capture.Invoke(null,new object[]{new UnityEngine.Vector3(-2f,1.6f,-3.2f),new UnityEngine.Vector3(-1.8f,1.6f,.6f),"Documentation/UrgentShelfBefore.png",70f});
var preview=UnityEditor.SceneManagement.EditorSceneManager.OpenPreviewScene("Assets/_Project/Scenes/OfficeGarage.unity");
UnityEngine.GameObject replacement=null;
try
{
    var env=preview.GetRootGameObjects().First(x=>x.name=="01_Environment_OfficeGarage");
    var parts=env.GetComponentsInChildren<UnityEngine.MeshRenderer>(true).Where(r=>r.name.StartsWith("G01_",System.StringComparison.Ordinal)&&r.bounds.center.x<2.6f).ToArray();
    if(parts.Length<20)throw new System.Exception("Expected one complete source rack bay, got "+parts.Length);
    var bounds=parts[0].bounds;foreach(var r in parts.Skip(1))bounds.Encapsulate(r.bounds);
    if(bounds.size.x>2.3f||bounds.size.y>2.5f||bounds.size.z>.9f)throw new System.Exception("Incorrect single-bay source bounds: "+bounds);
    replacement=new UnityEngine.GameObject("Upgrade_ExtraStorageRack");
    foreach(var r in parts)
    {
        var part=UnityEngine.Object.Instantiate(r.gameObject,replacement.transform,false);
        part.GetComponent<UnityEngine.MeshRenderer>().enabled=true;
    }
    var rotation=UnityEngine.Quaternion.Euler(0,-90,0);
    replacement.transform.rotation=rotation;
    replacement.transform.position=new UnityEngine.Vector3(.75f,.01f,.35f)-rotation*new UnityEngine.Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
    var collider=replacement.AddComponent<UnityEngine.BoxCollider>();collider.center=bounds.center;collider.size=bounds.size;
    replacement.AddComponent<CompanySim.UpgradeVisual>().upgradeId="storage";
    old.GetComponent<CompanySim.UpgradeVisual>().upgradeId="__retired_storage_layout";old.name="Retired_ExtraStorageRack_Layout";old.SetActive(false);
}
finally{UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview);}
capture.Invoke(null,new object[]{new UnityEngine.Vector3(-2f,1.6f,-3.2f),new UnityEngine.Vector3(-1.8f,1.6f,.6f),"Documentation/UrgentShelfAfter.png",70f});
var visible=replacement.GetComponentsInChildren<UnityEngine.MeshRenderer>();var result=visible[0].bounds;foreach(var r in visible.Skip(1))result.Encapsulate(r.bounds);
return new { parts=visible.Length, bounds=result.ToString(), officeClear=result.min.x>.2f, player=UnityEngine.Object.FindAnyObjectByType<CompanySim.PlayerInteractor>().transform.position.ToString() };
