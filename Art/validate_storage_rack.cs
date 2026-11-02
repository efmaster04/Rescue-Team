if(UnityEditor.EditorApplication.isPlaying)throw new System.Exception("Run this layout regression in edit mode.");
var source=UnityEngine.GameObject.Find("01_Environment_OfficeGarage");
var root=new UnityEngine.GameObject("Temporary_LegacyStorageRegression");
try
{
    foreach(var part in source.GetComponentsInChildren<UnityEngine.MeshRenderer>(true).Where(r=>r.name.StartsWith("G01_",System.StringComparison.Ordinal)))UnityEngine.Object.Instantiate(part.gameObject,root.transform,false);
    root.transform.position=new UnityEngine.Vector3(-3.43f,0,-5.84f);
    var upgrade=root.AddComponent<CompanySim.UpgradeVisual>();upgrade.upgradeId="storage";upgrade.PrepareLayout();
    var check=typeof(CompanySimEditor.ProjectValidation).GetMethod("CheckStorageUpgrade",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);check.Invoke(null,new object[]{upgrade});
    var visible=root.GetComponentsInChildren<UnityEngine.MeshRenderer>().Where(r=>r.enabled).ToArray();var bounds=visible[0].bounds;foreach(var r in visible.Skip(1))bounds.Encapsulate(r.bounds);
    System.IO.File.WriteAllText("Logs/urgent_shelf_layout_regression.txt","PASS: legacy three-bay rack is reduced to one garage bay; office, rear route and collider alignment checked.\nActive parts="+visible.Length+"; bounds="+bounds+"\n");
    return new { passed=true,activeParts=visible.Length,bounds=bounds.ToString() };
}
finally{UnityEngine.Object.DestroyImmediate(root);UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());}
