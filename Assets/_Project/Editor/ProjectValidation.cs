using System;
using System.IO;
using System.Linq;
using CompanySim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CompanySimEditor
{
    [InitializeOnLoad]
    public static class ProjectValidation
    {
        const string ActiveKey = "CompanyValidationActive";
        const string SaveKey = "CompanyValidationSavePath";
        const string BackupKey = "CompanyValidationBackupPath";
        const string HadSaveKey = "CompanyValidationHadSave";
        const string FailedKey = "CompanyValidationFailed";
        const string UIOnlyKey = "CompanyValidationUIOnly";
        const string GarageOnlyKey = "CompanyValidationGarageOnly";
        const string ManualOnlyKey = "CompanyValidationManualOnly";
        const string BackgroundKey = "CompanyValidationRunInBackground";
        const string Scene = "Assets/_Project/Scenes/OfficeGarage.unity";
        const string Report = "Logs/office_garage_validation.txt";
        static int stage, expectedBalance, expectedItems;
        static float stageStarted;
        static string orderedUid;
        static CompanyRuntime runtime;
        static ServiceVehicle vehicle;
        static PlayerInteractor player;
        static ModuleStation station;
        static double exitNotBefore;

        static ProjectValidation()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        [MenuItem("Company/Validate Play Loop")]
        public static void Run()
        { SessionState.SetBool(ManualOnlyKey,false); SessionState.SetBool(UIOnlyKey,false); SessionState.SetBool(GarageOnlyKey,false); SessionState.SetBool("CompanyGarageBenchmark",true); StartValidation(); }

        [MenuItem("Company/Validate Work Order UI")]
        public static void RunUIOnly()
        { SessionState.SetBool(ManualOnlyKey,false); SessionState.SetBool(UIOnlyKey,true); SessionState.SetBool(GarageOnlyKey,false); SessionState.SetBool("CompanyGarageBenchmark",false); StartValidation(); }

        [MenuItem("Company/Validate Lift, Vehicle Doors and FPS")]
        public static void RunGarageOnly()
        { SessionState.SetBool(ManualOnlyKey,false); SessionState.SetBool(UIOnlyKey,false); SessionState.SetBool(GarageOnlyKey,true); SessionState.SetBool("CompanyGarageBenchmark",true); StartValidation(); }

        [MenuItem("Company/Validate Manual Module Installation")]
        public static void RunManualOnly()
        {SessionState.SetBool(ManualOnlyKey,true); SessionState.SetBool(UIOnlyKey,false); SessionState.SetBool(GarageOnlyKey,false); SessionState.SetBool("CompanyGarageBenchmark",false); StartValidation();}

        static void StartValidation()
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText(Report, "Office/Garage Unity Play Mode validation\n");
            var save = Path.Combine(Application.persistentDataPath, "office_garage_v1.json");
            var backup = Path.GetFullPath("Logs/office_garage_user_save.backup");
            bool hadSave = File.Exists(save);
            SessionState.SetString(SaveKey, save);
            SessionState.SetString(BackupKey, backup);
            SessionState.SetBool(HadSaveKey, hadSave);
            if (hadSave) File.Copy(save, backup, true);
            if (File.Exists(save)) File.Delete(save);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(FailedKey, false);
            SessionState.SetBool(BackgroundKey,Application.runInBackground);
            EditorSceneManager.OpenScene(Scene);
            EditorApplication.EnterPlaymode();
        }

        public static void BuildAndRun()
        {
            ProjectBuilder.Build();
            Run();
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                // Automated keyboard/coroutine checks must advance when Codex has focus.
                Application.runInBackground=true;
                stage = 0;
                EditorApplication.update += Tick;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                Application.runInBackground=SessionState.GetBool(BackgroundKey,false);
                EditorApplication.update -= Tick;
                RestoreSave();
                SessionState.SetBool(ActiveKey, false);
                SessionState.SetBool("CompanyGarageBenchmark",false);
                File.AppendAllText(Report, "Validation complete.\n");
                if(Application.isBatchMode)
                {
                    // Finish the play-mode transition before shutting down the
                    // native asset worker manager. Exit inside this callback
                    // can interrupt its cleanup on Unity 6000.5.
                    exitNotBefore=EditorApplication.timeSinceStartup+2;
                    EditorApplication.update+=ExitWhenIdle;
                }
            }
        }

        static void ExitWhenIdle()
        {
            if(EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode)
                exitNotBefore=EditorApplication.timeSinceStartup+2;
            if(EditorApplication.timeSinceStartup<exitNotBefore) return;
            EditorApplication.update-=ExitWhenIdle;
            EditorApplication.Exit(SessionState.GetBool(FailedKey,false)?1:0);
        }

        static void Tick()
        {
            if (Time.frameCount < 5) return;
            try { Step(); }
            catch (Exception ex)
            {
                SessionState.SetBool(FailedKey, true);
                File.AppendAllText(Report, "FAIL: " + ex + "\n");
                Debug.LogException(ex);
                EditorApplication.update -= Tick;
                EditorApplication.ExitPlaymode();
            }
        }

        static void Step()
        {
            switch (stage)
            {
                case 0:
                    runtime = UnityEngine.Object.FindFirstObjectByType<CompanyRuntime>();
                    vehicle = UnityEngine.Object.FindFirstObjectByType<ServiceVehicle>();
                    player = UnityEngine.Object.FindFirstObjectByType<PlayerInteractor>();
                    station = UnityEngine.Object.FindFirstObjectByType<ModuleStation>();
                    var ui = UnityEngine.Object.FindFirstObjectByType<ComputerUI>();
                    Check(runtime && vehicle && player && station && ui, "Scene systems");
                    if(SessionState.GetBool(ManualOnlyKey,false))
                    {ManualModuleValidation.Begin(player); stageStarted=Time.realtimeSinceStartup; stage=24; break;}
                    if(SessionState.GetBool(GarageOnlyKey,false))
                    {
                        // Exercise the real owned-upgrade layout during garage movement.
                        runtime.Data.balance=Mathf.Max(runtime.Data.balance,runtime.catalog.economy.UpgradePrice("storage"));
                        Check(runtime.BuyUpgrade("storage"),"garage fixture buys the extra storage bay");
                        CheckStorageUpgrade(UnityEngine.Object.FindObjectsByType<UpgradeVisual>(FindObjectsInactive.Include).First(x=>x.upgradeId=="storage"));
                        Check(runtime.Save()&&runtime.Load(),"owned storage fixture reloads");
                        CheckStorageUpgrade(UnityEngine.Object.FindObjectsByType<UpgradeVisual>(FindObjectsInactive.Include).First(x=>x.upgradeId=="storage"));
                        GarageSystemsValidation.Begin(player); stageStarted=Time.realtimeSinceStartup; stage=21; break;
                    }
                    if(SessionState.GetBool(UIOnlyKey,false))
                    {
                        ComputerDesktopValidation.Begin(ui,player); stageStarted=Time.realtimeSinceStartup; stage=19; break;
                    }
                    Check(runtime.catalog && runtime.catalog.economy && runtime.catalog.jobs.Length == 6 &&
                        runtime.catalog.modules.Length == 5 && runtime.catalog.equipment.Length == 9, "Catalog references");
                    Check(runtime.EquipmentItems.Length == 8, "Physical initial equipment stock");
                    var sourceEnvironment = UnityEngine.GameObject.Find("01_Environment_OfficeGarage");
                    Check(sourceEnvironment && !UnityEngine.GameObject.Find("00_Environment_AxisCorrection") &&
                        sourceEnvironment.GetComponentsInChildren<Renderer>().Length > 3000,
                        "Source environment geometry is present without axis correction");
                    Check(Quaternion.Angle(sourceEnvironment.transform.rotation, Quaternion.identity) < .1f,
                        "Environment root has original Blender orientation");
                    var liftPads = sourceEnvironment.GetComponentsInChildren<Renderer>()
                        .Where(x => x.name.StartsWith("T31_01_Lift_Taban_Plakasi__welded anchor plate") ||
                                    x.name.StartsWith("T31_02_Lift_Taban_Plakasi__welded anchor plate"))
                        .OrderBy(x => x.bounds.center.x).ToArray();
                    Check(liftPads.Length == 2 &&
                        Mathf.Abs(liftPads[0].bounds.center.x - 3.04f) < .1f &&
                        Mathf.Abs(liftPads[1].bounds.center.x - 6.26f) < .1f &&
                        liftPads.All(x => Mathf.Abs(x.bounds.center.z - .46f) < .1f),
                        "Original garage lifts align with source vehicle bay");
                    var originalVan = UnityEngine.GameObject.Find("04_Vehicle_ServiceVan_Original");
                    Check(originalVan && !UnityEngine.GameObject.Find("04_Vehicle_ServiceTruck"),
                        "Original van replaces generated truck");
                    // The van's physical geometry excludes a LineRenderer's padded
                    // culling bounds. Cable vertices are checked separately.
                    var carrier=runtime.manualModules.mount.parent;
                    var steps=vehicle.transform.Find("RearAccessSteps");
                    var vanRenderers = originalVan.GetComponentsInChildren<MeshRenderer>().Where(r=>!r.transform.IsChildOf(carrier) && !r.transform.IsChildOf(steps)).ToArray();
                    Check(vanRenderers.Length > 20, "Original V01–V22 van mesh hierarchy");
                    VehicleVisualValidation.Run(vehicle);
                    var vanBounds = vanRenderers[0].bounds;
                    foreach (var renderer in vanRenderers.Skip(1)) vanBounds.Encapsulate(renderer.bounds);
                    Check(Mathf.Abs(vanBounds.center.x - 4.65f) < .35f &&
                        Mathf.Abs(vanBounds.center.z + .30f) < .45f &&
                        vanBounds.size.x > 2.5f && vanBounds.size.x < 3.2f &&
                        vanBounds.size.y > 2.4f && vanBounds.size.y < 3.1f &&
                        vanBounds.size.z > 5.5f && vanBounds.size.z < 6.2f,
                        "Van restored to source garage bounds: " + vanBounds);
                    Check(UnityEngine.Object.FindObjectsByType<Renderer>().Any(x => x.name.StartsWith("T28")),
                        "Original garage lift restored");
                    var workerVisual = player.transform.Find("Character_ServiceWorker_Visual");
                    Check(workerVisual && workerVisual.GetComponentsInChildren<Renderer>().All(x =>
                        x.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly),
                        "First-person body cannot obstruct the camera");
                    Check(!vehicle.transform.Cast<Transform>().Any(x => x.name.StartsWith("Module_")) &&
                        !vehicle.GetComponentsInChildren<Renderer>().Any(x => x.name.StartsWith("Timber cargo floor") ||
                            x.name.StartsWith("Load frame longitudinal")),
                        "Truck-sized module meshes do not intersect the enclosed van");
                    var worldCollision=UnityEngine.GameObject.Find("00_WorldCollision");
                    Check(worldCollision && new[]{"Office_Desk","Office_Chair","Office_FilingCabinet",
                        "Office_DocumentShelf","Garage_RearRack_01","Garage_RearRack_02",
                        "Garage_RearRack_03","Garage_RightRack","Garage_Workbench",
                        "Garage_LiftTower_01","Garage_LiftTower_02"}.All(n=>
                            worldCollision.transform.Find(n)?.GetComponent<BoxCollider>() != null),
                        "Major source furniture and lift towers have solid collision");
                    CheckSolidBarrier("Office west wall",new Vector3(-4.2f,.11f,-2.7f),
                        new Vector3(-5.3f,.11f,-2.7f));
                    CheckSolidBarrier("Office rear wall",new Vector3(-4.2f,.11f,.65f),
                        new Vector3(-4.2f,.11f,1.65f));
                    CheckSolidBarrier("Office desk",new Vector3(-2.1f,.11f,-.5f),
                        new Vector3(-2.1f,.11f,.80f));
                    var playerCollider=player.GetComponent<CharacterController>();
                    Check(UnityEngine.Object.FindObjectsByType<CarryableItem>().All(item=>
                        item.GetComponentsInChildren<Collider>().All(col=>
                            Physics.GetIgnoreCollision(playerCollider,col))),
                        "Loose equipment cannot snag the player controller");
                    var computerTarget=UnityEngine.GameObject.Find("Office_Computer_Main");
                    Check(computerTarget && computerTarget.GetComponent<BoxCollider>().isTrigger &&
                        Vector3.Distance(computerTarget.transform.position,new Vector3(-2.85f,1.10f,.84f))<.05f,
                        "Computer interaction target follows the visible monitor");
                    CheckDoorPassage("OfficeDoor_A11",new Vector3(-.85f,.11f,-2.87f),
                        new Vector3(.85f,.11f,-2.87f));
                    CheckDoorPassage("OfficeDoor_A13",new Vector3(-3.18f,.11f,-4.37f),
                        new Vector3(-3.18f,.11f,-5.90f));
                    CheckOfficeGarageRoute();
                    CaptureVanPreview();
                    CaptureVanSidePreview();
                    CaptureVanRearPreview();
                    foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>())
                        Check(t.GetComponents<Component>().All(c => c), "Missing script on " + t.name);
                    var computer = UnityEngine.Object.FindFirstObjectByType<OfficeComputer>();
                    Check(computer, "Office computer");
                    computer.Interact(player);
                    Check(player.InputLocked, "Computer locks movement");
                    ui.Close();
                    Check(!player.InputLocked, "Computer restores movement");
                    GameFeelValidation.Begin(player);
                    stageStarted=Time.realtimeSinceStartup;
                    stage=14;
                    break;
                case 14:
                    Check(Time.realtimeSinceStartup-stageStarted<30,"Presentation test timeout");
                    if(!GameFeelValidation.Finished) return;
                    if(GameFeelValidation.Error!=null) throw GameFeelValidation.Error;
                    DoorRegressionValidation.Begin(player);
                    stageStarted=Time.realtimeSinceStartup;
                    stage=15;
                    break;
                case 15:
                    Check(Time.realtimeSinceStartup-stageStarted<70,"Door traversal test timeout");
                    if(!DoorRegressionValidation.Finished) return;
                    if(DoorRegressionValidation.Error!=null) throw DoorRegressionValidation.Error;
                    WorkshopInteractionValidation.Begin(player);
                    stageStarted=Time.realtimeSinceStartup;
                    stage=16;
                    break;
                case 16:
                    Check(Time.realtimeSinceStartup-stageStarted<45,"Workshop drawer test timeout");
                    if(!WorkshopInteractionValidation.Finished) return;
                    if(WorkshopInteractionValidation.Error!=null) throw WorkshopInteractionValidation.Error;
                    HudInteractionValidation.Begin(player);
                    stageStarted=Time.realtimeSinceStartup;
                    stage=17;
                    break;
                case 17:
                    Check(Time.realtimeSinceStartup-stageStarted<35,"HUD and pause test timeout");
                    if(!HudInteractionValidation.Finished) return;
                    if(HudInteractionValidation.Error!=null) throw HudInteractionValidation.Error;
                    GarageSystemsValidation.Begin(player); stageStarted=Time.realtimeSinceStartup; stage=20;
                    break;
                case 20:
                    Check(Time.realtimeSinceStartup-stageStarted<120,"Garage lift, doors and performance timeout");
                    if(!GarageSystemsValidation.Finished) return;
                    if(GarageSystemsValidation.Error!=null) throw GarageSystemsValidation.Error;
                    expectedBalance = runtime.Data.balance;
                    runtime.Data.nextInspectionAtCompletedJobs = 2;
                    Check(!runtime.AcceptJob("heavy") && runtime.Data.phase == JobPhase.None,
                        "Reputation lock blocks advanced job");
                    ComputerDesktopValidation.Begin(player.ui,player);
                    stageStarted=Time.realtimeSinceStartup;
                    stage=10;
                    break;
                case 10:
                    Check(Time.realtimeSinceStartup-stageStarted<40,"Desktop UI test timeout");
                    if(!ComputerDesktopValidation.Finished) return;
                    if(ComputerDesktopValidation.Error!=null) throw ComputerDesktopValidation.Error;
                    Check(runtime.Data.activeJobId=="recovery", "Desktop job application accepts recovery contract");
                    Check(runtime.Data.contractAccepted, "Contract recorded");
                    Check(!runtime.Depart() && runtime.Data.phase == JobPhase.Accepted, "Incomplete preparation blocks departure");
                    Check(runtime.MissingPreparation().Any(x => x.StartsWith("Modül:")), "Wrong module warning");
                    ManualModuleValidation.Begin(player);
                    stageStarted=Time.realtimeSinceStartup; stage=22;
                    break;
                case 22:
                    Check(Time.realtimeSinceStartup-stageStarted<120,"Manual module installation timeout");
                    if(!ManualModuleValidation.Finished) return;
                    if(ManualModuleValidation.Error!=null) throw ManualModuleValidation.Error;
                    expectedBalance=runtime.Data.balance; stageStarted=Time.realtimeSinceStartup; stage=1;
                    break;
                case 24:
                    Check(Time.realtimeSinceStartup-stageStarted<140,"Manual-only test timeout");
                    if(!ManualModuleValidation.Finished) return;
                    if(ManualModuleValidation.Error!=null) throw ManualModuleValidation.Error;
                    EditorApplication.ExitPlaymode(); break;
                case 19:
                    Check(Time.realtimeSinceStartup-stageStarted<50,"UI-only validation timeout");
                    if(!ComputerDesktopValidation.Finished) return;
                    if(ComputerDesktopValidation.Error!=null) throw ComputerDesktopValidation.Error;
                    EditorApplication.ExitPlaymode();
                    break;
                case 21:
                    Check(Time.realtimeSinceStartup-stageStarted<120,"Garage-only test timeout");
                    if(!GarageSystemsValidation.Finished) return;
                    if(GarageSystemsValidation.Error!=null) throw GarageSystemsValidation.Error;
                    EditorApplication.ExitPlaymode();
                    break;
                case 1:
                    if (station.Busy)
                    {
                        Check(Time.realtimeSinceStartup - stageStarted < 8, "Module swap timeout");
                        return;
                    }
                    Check(vehicle.InstalledModuleId == "towing", "Module changes vehicle data");
                    foreach (var id in new[] { "toolbox", "jack" })
                    {
                        var item = UnityEngine.Object.FindObjectsByType<CarryableItem>().First(x => x.definitionId == id);
                        player.Hold(item);
                        Check(vehicle.TryLoad(player), "Load physical " + id);
                    }
                    Check(vehicle.LoadedItems.Count() == 2, "Cargo item count");
                    Check(runtime.MissingPreparation().Count == 0, "Preparation reflects physical cargo");
                    Check(runtime.Depart(), "Depart prepared job");
                    var result = new FieldJobResult {
                        resultId = Guid.NewGuid().ToString("N"), jobId = "recovery",
                        succeeded = true, equipmentWear = 24, vehicleWear = 82
                    };
                    Check(runtime.ReturnFromField(result), "Field result processed");
                    Check(!runtime.ReturnFromField(result), "Duplicate field result rejected");
                    Check(runtime.Data.phase == JobPhase.AwaitingReport, "Report phase");
                    Check(!runtime.Data.contractSatisfied && runtime.Data.reportContractBonus == 0,
                        "Contract wear condition enforced");
                    Check(runtime.Data.reportEquipment.Contains("Alet çantası") &&
                        runtime.Data.reportVehicleWear == 82, "Report evidence captured");
                    player.ui.Open("Reports");
                    stage=12;
                    break;
                case 12:
                    ComputerDesktopValidation.Capture(player.ui,"Documentation/ComputerReportPreview.png",1600,900);
                    ComputerDesktopValidation.Capture(player.ui,"Documentation/ComputerReport720Preview.png",1280,720);
                    ComputerDesktopValidation.Click(player.ui,"ApproveReport");
                    player.ui.Close();
                    Check(!runtime.FileReport(), "Duplicate payout rejected");
                    Check(runtime.Data.balance == expectedBalance + 850, "Job payment");
                    expectedBalance = runtime.Data.balance;
                    var damaged = vehicle.LoadedItems.First();
                    Check(damaged.durability < 100, "Equipment wear");
                    Check(runtime.Repair(damaged), "Repair equipment");
                    Check(damaged.durability == 100 && runtime.Data.balance == expectedBalance - 45, "Repair cost and durability");
                    expectedBalance = runtime.Data.balance;
                    Check(runtime.Data.vehicleCondition < runtime.catalog.economy.minimumVehicleCondition, "Vehicle wear");
                    Check(runtime.RepairVehicle(), "Repair vehicle");
                    Check(runtime.Data.vehicleCondition == 100 &&
                        runtime.Data.balance == expectedBalance - runtime.catalog.economy.vehicleRepairCost,
                        "Vehicle repair cost and condition");
                    player.ui.Open("CompanyBank");
                    stage=13;
                    break;
                case 13:
                    ComputerDesktopValidation.Capture(player.ui,"Documentation/ComputerFinancePreview.png",1600,900);
                    player.ui.Close();
                    expectedBalance = runtime.Data.balance;
                    expectedItems = runtime.EquipmentItems.Length;
                    player.ui.Open("EquipmentShop");
                    stage=11;
                    break;
                case 11:
                    ComputerDesktopValidation.Click(player.ui,"Product_case");
                    stage=18;
                    break;
                case 18:
                    var caseDefinition=runtime.catalog.Item("case");
                    ComputerDesktopValidation.Click(player.ui,"Buy_"+caseDefinition.displayName);
                    Check(runtime.Data.orders.Last().itemId=="case", "Shop button orders real equipment");
                    player.ui.Close();
                    Check(runtime.Data.balance == expectedBalance - 100, "Order cost");
                    orderedUid = runtime.Data.orders.Last().uid;
                    runtime.Data.orders.Last().arrivalUnix = 0;
                    stageStarted = Time.realtimeSinceStartup;
                    stage = 2;
                    break;
                case 2:
                    if (!runtime.Data.orders.Last().delivered)
                    {
                        Check(Time.realtimeSinceStartup - stageStarted < 8, "Delivery timeout");
                        return;
                    }
                    var delivered=UnityEngine.Object.FindObjectsByType<CarryableItem>()
                        .FirstOrDefault(x => x.uid == orderedUid);
                    Check(delivered, "Delivered physical item");
                    Check(runtime.EquipmentItems.Length == expectedItems + 1, "Delivery stock count");
                    var deliveredCollider=delivered.GetComponent<Collider>();
                    var playerController=player.GetComponent<CharacterController>();
                    Check(Physics.GetIgnoreCollision(playerController,deliveredCollider),
                        "Newly delivered equipment cannot snag the player");
                    var deliveryPosition=delivered.transform.position;
                    delivered.AttachToHand(player.viewCamera.transform);
                    delivered.Drop(deliveryPosition);
                    Check(Physics.GetIgnoreCollision(playerController,deliveredCollider),
                        "Dropped equipment cannot snag the player again");
                    expectedBalance = runtime.Data.balance;
                    Check(runtime.BuyUpgrade("storage"), "Buy storage upgrade");
                    Check(!runtime.BuyUpgrade("storage"), "Duplicate upgrade rejected");
                    Check(runtime.Data.balance == expectedBalance - 450, "Upgrade cost once");
                    var upgrade = UnityEngine.Object.FindObjectsByType<UpgradeVisual>(FindObjectsInactive.Include)
                        .First(x => x.upgradeId == "storage");
                    Check(upgrade.gameObject.activeSelf, "Physical upgrade visible");
                    CheckStorageUpgrade(upgrade);
                    expectedBalance = runtime.Data.balance;
                    expectedItems = runtime.EquipmentItems.Length;
                    runtime.Save();
                    Check(runtime.Load(), "Save reload");
                    stage = 3;
                    break;
                case 3:
                    Check(runtime.Data.balance == expectedBalance, "Balance persisted");
                    Check(runtime.Data.ownedModules.Contains("towing") && vehicle.InstalledModuleId == "towing", "Module persisted");
                    Check(runtime.Data.upgrades.Contains("storage"), "Upgrade persisted");
                    CheckStorageUpgrade(UnityEngine.Object.FindObjectsByType<UpgradeVisual>(FindObjectsInactive.Include).First(x=>x.upgradeId=="storage"));
                    Check(runtime.Data.transactions.Count >= 4, "Financial ledger persisted");
                    Check(runtime.EquipmentItems.Length == expectedItems, "Equipment persisted");
                    Check(UnityEngine.Object.FindObjectsByType<CarryableItem>().All(item=>
                        item.GetComponentsInChildren<Collider>().All(col=>
                            !col.enabled || Physics.GetIgnoreCollision(player.GetComponent<CharacterController>(),col))),
                        "Reloaded equipment does not snag the player");
                    Check(vehicle.LoadedItems.Count() == 2, "Cargo persisted");
                    Check(runtime.AcceptJob("recovery", true), "Second contract accepted");
                    Check(runtime.MissingPreparation().Count == 0, "Second job is ready");
                    Check(runtime.Depart(), "Second job departed");
                    var contractJob = runtime.catalog.Job("recovery");
                    Check(runtime.ReturnFromField(new FieldJobResult {
                        resultId = Guid.NewGuid().ToString("N"), jobId = "recovery",
                        succeeded = true, bonus = 40, expenses = 20,
                        equipmentWear = 4, vehicleWear = 6
                    }), "Low-wear result processed");
                    Check(runtime.Data.contractSatisfied &&
                        runtime.Data.reportContractBonus == contractJob.contractBonus,
                        "Optional contract earned");
                    Check(runtime.Data.pendingPayment == contractJob.payment + contractJob.contractBonus + 20,
                        "Report itemization and total");
                    Check(runtime.FileReport(), "Contract report paid");
                    Check(runtime.Data.balance == expectedBalance + contractJob.payment +
                        contractJob.contractBonus + 20, "Contract payment once");
                    Check(runtime.Data.reputation >= runtime.catalog.Job("heavy").minimumReputation,
                        "Successful jobs unlock advanced job");
                    Check(runtime.Data.inspectionPending, "Scheduled safety inspection");
                    Check(runtime.InspectionProblems().Count == 0, "Safety checklist reads physical condition");
                    int preInspectionBalance = runtime.Data.balance;
                    Check(runtime.ResolveInspection(), "Complete safety inspection");
                    Check(runtime.Data.inspectionsPassed == 1 && !runtime.Data.inspectionPending &&
                        runtime.Data.balance == preInspectionBalance + runtime.catalog.economy.inspectionReward,
                        "Safety reward and reputation persisted in company state");
                    Check(!runtime.ResolveInspection(), "Duplicate inspection reward rejected");
                    var wornItem = vehicle.LoadedItems.First();
                    wornItem.durability = 25;
                    runtime.Data.inspectionPending = true;
                    int beforeFine = runtime.Data.balance;
                    int beforeReputation = runtime.Data.reputation;
                    Check(runtime.InspectionProblems().Count > 0, "Damaged physical item fails safety checklist");
                    Check(runtime.ResolveInspection(), "Unsafe inspection processed");
                    Check(runtime.Data.balance == beforeFine - runtime.catalog.economy.inspectionFine &&
                        runtime.Data.reputation == beforeReputation - 1 &&
                        runtime.Data.inspectionsPassed == 1, "Safety fine and reputation loss");
                    Check(!runtime.ResolveInspection(), "Duplicate inspection fine rejected");
                    runtime.Save();
                    stage = 4;
                    break;
                case 4:
                    Check(runtime.Load(), "Contract result save reload");
                    Check(runtime.Data.version == 5 && !runtime.Data.contractAccepted &&
                        runtime.Data.pendingPayment == 0, "Contract state reset and persisted");
                    Check(runtime.Data.inspectionsPassed == 1 && !runtime.Data.inspectionPending,
                        "Inspection outcome persisted");
                    Check(runtime.AcceptJob("heavy"), "Reputation unlock persists");
                    File.AppendAllText(Report, "PASS: original van position and garage lift, scene references, computer, reputation job lock/unlock, module, cargo, preparation, result, itemized report, optional contract failure/success, scheduled safety inspection reward/fine, economy, equipment and vehicle repair, order, delivery, upgrade, save/load\n");
                    EditorApplication.update -= Tick;
                    EditorApplication.ExitPlaymode();
                    break;
            }
        }

        static void CheckStorageUpgrade(UpgradeVisual upgrade)
        {
            Physics.SyncTransforms();
            var renderers=upgrade.GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled).ToArray();
            Check(upgrade.storageLayoutVersion==3&&renderers.Length>20&&renderers.Length<100,"storage upgrade contains one rack bay");
            var bounds=renderers[0].bounds;foreach(var r in renderers.Skip(1))bounds.Encapsulate(r.bounds);
            Check(bounds.min.x>.15f&&bounds.max.x<.90f&&bounds.min.z>-2.20f&&bounds.max.z<-.80f&&bounds.size.y<1.90f,"storage upgrade clears the office door, plant, shutter and main walking route");
            var collision=upgrade.GetComponent<BoxCollider>();
            Check(collision&&collision.enabled&&Vector3.Distance(collision.bounds.center,bounds.center)<.02f&&Vector3.Distance(collision.bounds.size,bounds.size)<.02f,"storage rack collision matches its visible bay");
        }
        static void Check(bool okay, string label)
        {
            if (!okay) throw new InvalidOperationException(label);
        }

        static void CheckDoorPassage(string name, Vector3 start, Vector3 destination)
        {
            var doorObject = UnityEngine.GameObject.Find(name);
            Check(doorObject, "Missing usable door: " + name);
            var door = doorObject.GetComponent<OfficeDoor>();
            Check(door && doorObject.GetComponent<BoxCollider>(), "Missing door interaction: " + name);
            var hardware=doorObject.GetComponentsInChildren<Renderer>()
                .Where(x=>x.name.StartsWith("A15_")).ToArray();
            Check(hardware.Length>=10, "Source door handles follow " + name);
            if(name=="OfficeDoor_A13")
                Check(doorObject.GetComponentsInChildren<Renderer>().Any(x=>x.name.StartsWith("A34_")),
                    "Front door notice follows the leaf");
            var grip=hardware.FirstOrDefault(x=>x.name.Contains("rounded lever grip"));
            Check(grip, "Visible door lever on " + name);
            var gripBefore=grip.bounds.center;
            var direction = (destination-start).normalized;
            Check(Physics.Raycast(start + Vector3.up*1.30f,direction,out var firstHit,2f) &&
                firstHit.collider.GetComponentInParent<OfficeDoor>() == door,
                "Player can aim at closed " + name + " without a hidden collider blocking E");
            var closedRotation = door.transform.localRotation;
            door.Interact(player);
            door.transform.localRotation = closedRotation * Quaternion.Euler(0,door.openAngle,0);
            Physics.SyncTransforms();
            Check(Vector3.Distance(gripBefore,grip.bounds.center)>.25f,
                "Door handle moves with " + name);
            var probe = new GameObject("DoorPassageProbe");
            probe.transform.position = start;
            var capsule = probe.AddComponent<CharacterController>();
            capsule.height = PlayerInteractor.BodyHeight;
            capsule.radius = PlayerInteractor.BodyRadius;
            capsule.center = new Vector3(0,PlayerInteractor.BodyHeight*.5f,0);
            for (int i=0;i<40;i++) capsule.Move((destination-start)/40f);
            var reached = Vector3.Distance(new Vector3(probe.transform.position.x,0,probe.transform.position.z),
                new Vector3(destination.x,0,destination.z)) < .16f;
            var stoppedAt = probe.transform.position;
            if (name == "OfficeDoor_A11")
            {
                var oldPosition = player.transform.position;
                var oldRotation = player.transform.rotation;
                var oldCameraRotation = player.viewCamera.transform.localRotation;
                player.transform.SetPositionAndRotation(new Vector3(-.80f,.11f,-2.87f),
                    Quaternion.Euler(0,90,0));
                player.viewCamera.transform.localRotation = Quaternion.identity;
                Physics.SyncTransforms();
                CaptureCamera(player.viewCamera,"Documentation/FirstPersonGaragePreview.png");
                player.transform.SetPositionAndRotation(oldPosition,oldRotation);
                player.viewCamera.transform.localRotation = oldCameraRotation;
            }
            var nearby = Physics.OverlapBox(new Vector3(stoppedAt.x+.34f,1.0f,stoppedAt.z),
                new Vector3(.40f,.85f,.42f)).Select(x => x.name).Distinct().ToArray();
            probe.SetActive(false);
            UnityEngine.Object.Destroy(probe);
            door.Interact(player);
            door.transform.localRotation = closedRotation;
            Physics.SyncTransforms();
            Check(reached, "Character controller can cross open " + name + " (stopped at " + stoppedAt +
                "; nearby: " + string.Join(", ",nearby) + ")");
        }

        static void CheckSolidBarrier(string name,Vector3 start,Vector3 destination)
        {
            var probe=new GameObject("SolidBarrierProbe");
            probe.transform.position=start;
            var capsule=probe.AddComponent<CharacterController>();
            capsule.height=PlayerInteractor.BodyHeight;
            capsule.radius=PlayerInteractor.BodyRadius;
            capsule.center=new Vector3(0,PlayerInteractor.BodyHeight*.5f,0);
            for(int i=0;i<30;i++) capsule.Move((destination-start)/30f);
            var distance=Vector3.Distance(probe.transform.position,destination);
            probe.SetActive(false);
            UnityEngine.Object.Destroy(probe);
            Check(distance>.35f,name+" prevents walking through visible furniture/wall");
        }

        static void CheckOfficeGarageRoute()
        {
            var door=UnityEngine.GameObject.Find("OfficeDoor_A11").GetComponent<OfficeDoor>();
            var closed=door.transform.localRotation;
            door.Interact(player);
            door.transform.localRotation=closed*Quaternion.Euler(0,door.openAngle,0);
            Physics.SyncTransforms();
            var probe=new GameObject("OfficeGarageRouteProbe");
            probe.transform.position=new Vector3(-2.8f,.11f,-1.35f);
            var capsule=probe.AddComponent<CharacterController>();
            capsule.height=PlayerInteractor.BodyHeight;
            capsule.radius=PlayerInteractor.BodyRadius;
            capsule.center=new Vector3(0,PlayerInteractor.BodyHeight*.5f,0);
            foreach(var item in UnityEngine.Object.FindObjectsByType<CarryableItem>())
                foreach(var col in item.GetComponentsInChildren<Collider>())
                    Physics.IgnoreCollision(capsule,col,true);
            foreach(var target in new[]{
                new Vector3(-1.1f,.11f,-1.35f),
                new Vector3(-1.1f,.11f,-2.87f),
                new Vector3(-.85f,.11f,-2.87f),
                new Vector3(.85f,.11f,-2.87f),
                new Vector3(1.6f,.11f,-3.8f),
                new Vector3(7.4f,.11f,-3.8f),
                new Vector3(7.4f,.11f,-1.8f)})
            {
                var start=probe.transform.position;
                int steps=Mathf.CeilToInt(Vector3.Distance(start,target)/.05f);
                for(int i=0;i<steps;i++) capsule.Move((target-start)/steps);
                Check(Vector3.Distance(probe.transform.position,target)<.18f,
                    "Player-size route reaches "+target+" (stopped at "+probe.transform.position+")");
            }
            probe.SetActive(false);
            UnityEngine.Object.Destroy(probe);
            door.Interact(player);
            door.transform.localRotation=closed;
            Physics.SyncTransforms();
        }

        static void CaptureVanPreview()
        {
            var view = new GameObject("TemporaryVanPreviewCamera");
            var camera = view.AddComponent<Camera>();
            camera.fieldOfView = 67;
            camera.nearClipPlane = .05f;
            camera.transform.position = new Vector3(7.9f, 2.6f, -4.15f);
            camera.transform.LookAt(new Vector3(4.65f, 1.25f, -.20f));
            CaptureCamera(camera,"Documentation/VanRestoredPlayPreview.png");
            UnityEngine.Object.Destroy(view);
        }

        static void CaptureVanSidePreview()
        {
            var view = new GameObject("TemporaryVanSideCamera");
            var camera = view.AddComponent<Camera>();
            camera.fieldOfView = 67;
            camera.nearClipPlane = .05f;
            camera.transform.position = new Vector3(1.25f,3.35f,.15f);
            camera.transform.LookAt(new Vector3(4.65f,1.45f,.15f));
            CaptureCamera(camera,"Documentation/VanOfficeSidePreview.png");
            UnityEngine.Object.Destroy(view);
        }

        static void CaptureVanRearPreview()
        {
            var view = new GameObject("TemporaryVanRearPreviewCamera");
            var camera = view.AddComponent<Camera>();
            camera.fieldOfView = 58;
            camera.nearClipPlane = .05f;
            camera.transform.position = new Vector3(4.65f, 2.22f, 3.88f);
            camera.transform.LookAt(new Vector3(4.65f, 1.48f, 2.35f));
            CaptureCamera(camera,"Documentation/VanRearPreview.png");
            UnityEngine.Object.Destroy(view);
        }

        internal static void CaptureCamera(Camera camera, string path)
        {
            OfficePresentationBuilder.ConfigureCamera(camera);
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            target.Create();
            try
            {
                RenderPipeline.SubmitRenderRequest(camera,
                    new RenderPipeline.StandardRequest { destination = target });
                RenderTexture.active = target;
                var image = new Texture2D(1280, 720, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                image.Apply();
                Directory.CreateDirectory("Documentation");
                File.WriteAllBytes(path, image.EncodeToPNG());
                UnityEngine.Object.Destroy(image);
            }
            finally
            {
                RenderTexture.active = null;
                target.Release();
                UnityEngine.Object.Destroy(target);
            }
        }

        static void RestoreSave()
        {
            string save = SessionState.GetString(SaveKey, "");
            string backup = SessionState.GetString(BackupKey, "");
            if (string.IsNullOrEmpty(save)) return;
            if (SessionState.GetBool(HadSaveKey, false) && File.Exists(backup))
            {
                File.Copy(backup, save, true);
                File.Delete(backup);
            }
            else if (File.Exists(save)) File.Delete(save);
        }
    }
}
