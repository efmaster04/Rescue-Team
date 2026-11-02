using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace CompanySim
{
    public sealed partial class ComputerUI : MonoBehaviour
    {
        public CompanyRuntime Runtime;
        public CompanyCatalog catalog;
        public PlayerInteractor player;
        public ModuleStation station;
        public AudioFeedback audioFeedback;
        string app="Desktop", selectedJob="", prompt="";
        string appSection="list";
        bool selectedContract;
        readonly Dictionary<string,string> appSections=new Dictionary<string,string>();
        float toastUntil;
        bool prepVisible;
        bool? lastFocus;
        float nextClockUpdate;
        public bool IsOpen => computer && computer.activeSelf;
        public string ActiveApplication => app;
        void Awake()
        {
            font=Resources.Load<Font>("Fonts/SourceSans3-Regular");
            headingFont=Resources.Load<Font>("Fonts/SourceSans3-Semibold");
            if(!font) font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if(!headingFont) headingFont=font;
            if (!FindFirstObjectByType<EventSystem>())
            {
                var events=new GameObject("UI_EventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule));
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            BuildCanvas();
            LoadPlayerPreferences();
        }
        void Start()
        {
            Runtime.Changed+=Refresh; Runtime.Notice+=Toast; Refresh();
        }
        void OnDestroy()
        {
            if(IsPaused) SetPaused(false);
            if(Runtime) { Runtime.Changed-=Refresh; Runtime.Notice-=Toast; }
            if(roundSprite) Destroy(roundSprite.texture);
            if(roundSprite) Destroy(roundSprite);
        }
        void Update()
        {
            if(Keyboard.current!=null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if(IsOpen) { if(app=="Desktop") Close(); else CloseApplicationWindow(); }
                else SetPaused(!IsPaused);
            }
            UpdateGameHud();
            if(IsOpen && Time.unscaledTime>=nextClockUpdate)
            { nextClockUpdate=Time.unscaledTime+1; clockText.text=DateTime.Now.ToString("dd.MM.yyyy    HH:mm"); }
        }
        public void SetFocus(bool focused)
        {
            if(!focusReticle || lastFocus==focused) return;
            lastFocus=focused;
            focusReticle.color=focused?new Color(1,.86f,.55f,.95f):new Color(.9f,.96f,.94f,.65f);
            focusReticle.rectTransform.sizeDelta=Vector2.one*(focused?10:6);
        }
        public void SetCarriedItem(CarryableItem item)
        {
            carriedItem=item;
            if(carriedPanel) carriedPanel.SetActive(item);
            lastCarriedCondition=-1; UpdateCarriedInfo();
        }
        void Click() { if(audioFeedback) audioFeedback.Click(); }
        public void Open(string target)
        {
            if(IsPaused) SetPaused(false);
            Click(); computer.SetActive(true); hud.SetActive(false);
            promptPanel.SetActive(false); prepPanel.SetActive(false);
            player.SetCursor(true); Show(target);
        }
        public void Close()
        {
            computer.SetActive(false); hud.SetActive(true); prepPanel.SetActive(prepVisible);
            prompt="";
            player.SetCursor(false);
        }
        public void SetPrompt(string value)
        {
            if(prompt==value) return;
            prompt=value; PresentPrompt(value);
            promptPanel.SetActive(!IsOpen && !string.IsNullOrWhiteSpace(value));
        }
        public void Toast(string value)
        {
            if(audioFeedback) audioFeedback.Notice();
            toastText.text=value; toastStart=Time.unscaledTime; toastUntil=toastStart+4.5f;
            LayoutNotification();
            toastPanel.SetActive(true); toastPanel.transform.SetAsLastSibling();
        }
        public void TogglePreparation()
        {
            prepVisible=!prepVisible; prepPanel.SetActive(prepVisible && !IsOpen);
        }
        public void Refresh()
        {
            if(!Runtime || !catalog || !mainContent) return;
            PrepRefresh(); if(IsOpen) Show(app);
        }
        string Money(int value) => "₺"+value.ToString("N0",System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));
        string PhaseName() => Runtime.Data.phase==JobPhase.Accepted ? "Hazırlık aşamasında" :
            Runtime.Data.phase==JobPhase.Deployed ? "Ekip sahada" :
            Runtime.Data.phase==JobPhase.AwaitingReport ? "Rapor bekliyor" : "Yeni işe hazır";
        void Show(string target)
        {
            string requested=null;
            switch(target)
            {
                case "Dispatch": target="JobDesk"; requested="active"; break;
                case "Reports": target="JobDesk"; requested="reports"; break;
                case "BuildingManager": target="CompanyStats"; requested="upgrades"; break;
                case "Safety": target="CompanyStats"; requested="inspection"; break;
            }
            if(target!="Desktop" && !Apps.Any(x=>x.id==target)) target="Desktop";
            if(target=="Desktop" && app!="Desktop") lastWindowScroll=appScroll.verticalNormalizedPosition;
            if(target!="Desktop") lastWindowApp=target;
            bool changed=app!=target;
            if(app!="Desktop") appSections[app]=appSection;
            if(requested!=null) appSection=requested;
            else if(changed && target!="Desktop") appSection=appSections.TryGetValue(target,out var section)?section:DefaultSection(target);
            app=target;
            desktop.SetActive(true); appWindow.SetActive(app!="Desktop");
            Clear(); RefreshDesktop(); UpdateTaskWindow();
            if(app=="Desktop") return;
            Canvas.ForceUpdateCanvases();
            var definition=Apps.First(x=>x.id==app);
            titleText.text=definition.title; appIcon.kind=app; appIcon.color=Paper;
            var layout=mainContent.GetComponent<VerticalLayoutGroup>();
            bool pilot=app=="JobDesk" && appSection=="list";
            layout.padding=pilot?new RectOffset():new RectOffset(30,30,26,30);
            layout.spacing=pilot?0:10;
            switch(app)
            {
                case "JobDesk":
                    RenderNavigation(("list","İlanlar"),("active","Aktif iş"),("reports","Raporlar"));
                    if(appSection=="active") { SectionHeading("Aktif iş"); Dispatch(); }
                    else if(appSection=="reports") { SectionHeading("Raporlar"); Reports(); }
                    else Jobs(); break;
                case "FleetManager":
                    RenderNavigation(("vehicle","Araç"),("modules","Modüller"),("cargo","Yük listesi"));
                    if(appSection=="modules") { SectionHeading("Modüller"); Fleet(); }
                    else if(appSection=="cargo") CargoManifest(); else VehicleOverview(); break;
                case "StockManager":
                    RenderNavigation(("equipment","Ekipman"),("deliveries","Teslimatlar"));
                    if(appSection=="deliveries") Deliveries(); else StockView(); break;
                case "CompanyBank": RenderNavigation(("ledger","Hesap defteri")); SectionHeading("Hesap defteri"); Bank(); break;
                case "EquipmentShop":
                    RenderNavigation(("all","Tüm ürünler"),("tools","El aletleri"),("service","Servis"),("safety","Güvenlik")); ShopView(); break;
                case "CompanyStats":
                    RenderNavigation(("overview","Genel bakış"),("upgrades","Geliştirmeler"),("inspection","Denetim"));
                    if(appSection=="upgrades") { SectionHeading("Geliştirmeler"); Building(); }
                    else if(appSection=="inspection") { SectionHeading("Denetim"); Safety(); }
                    else { SectionHeading("Şirket"); Stats(); } break;
                case "MailBox":
                    RenderNavigation(("inbox","Gelen kutusu"));
                    // Görev köprüsü: sahadan dönen ekibin raporu Posta'da da duyurulur.
                    if(Runtime.Data.phase==JobPhase.AwaitingReport)
                        ToolbarButton("FieldReportMail","Saha raporu geldi · Aç",()=>Show("Reports"));
                    MailView(); break;
            }
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(mainContent);
            Canvas.ForceUpdateCanvases();
            if(changed) appScroll.verticalNormalizedPosition=1;
        }
        string DefaultSection(string target) => target switch
        {
            "JobDesk"=>"list", "FleetManager"=>"vehicle", "StockManager"=>"equipment", "EquipmentShop"=>"all",
            "CompanyBank"=>"ledger", "CompanyStats"=>"overview", "MailBox"=>"inbox", _=>"list"
        };
        void Jobs()
        {
            if(string.IsNullOrEmpty(selectedJob)) selectedJob=catalog.jobs.First().id;
            JobColumns(out var list,out var detail);
            var parent=mainContent; mainContent=list;
            Row("İlanlar",22,Paper,46);
            foreach(var job in catalog.jobs) { JobCard(job); if(!job.HasFieldScene) MarkComingSoon(list,job); }
            mainContent=detail; JobDetail(); mainContent=parent;
        }
        // Görev köprüsü: saha sahnesi olmayan iş kartı "Yakında" etiketiyle kilitli görünür (kart yine seçilebilir).
        const string ComingSoonText="Yakında · kilitli";
        void MarkComingSoon(Transform list,JobDefinition job)
        {
            var card=list.Find("Job_"+job.id); if(!card) return;
            var meta=card.Find("JobMeta")?.GetComponent<Text>();
            if(meta) { meta.text=ComingSoonText; meta.color=selectedJob==job.id?Surface:Peach; }
            var fee=card.Find("JobPayment"); if(fee) fee.gameObject.SetActive(false);
        }
        void JobDetail()
        {
            var job=catalog.Job(selectedJob); if (!job) return;
            Row(job.title,34,Paper,56); Divider();
            JobFacts(job);
            Divider(); Row(job.description,22,Paper,58);
            Row("Ekipman",22,Paper,32);
            RequirementRow("Module",catalog.Module(job.requiredModule)?.displayName??job.requiredModule);
            foreach(var id in job.requiredEquipment) RequirementRow(id,catalog.Item(id)?.displayName??id);
            if(job.recommendedEquipment.Length>0) Row("Önerilen: "+string.Join(", ",job.recommendedEquipment.Select(x=>catalog.Item(x)?.displayName??x)),17,Soft,34);
            if(!string.IsNullOrWhiteSpace(job.specialTerms)) Row(job.specialTerms,17,Soft,48);
            if(!job.HasFieldScene) Row("Yakında: bu işin saha görevi henüz hazır değil.",17,Peach,34);
            bool available=Runtime.Data.phase==JobPhase.None && Runtime.Data.reputation>=job.minimumReputation && job.HasFieldScene;
            JobActions(job,available);
            if(!job.HasFieldScene && jobFooter)
            {
                // Kabul düğmesi ve gerekçe "Yakında" der (itibar/aktif iş gerekçesi yerine).
                var caption=jobFooter.Find("AcceptJob/Caption")?.GetComponent<Text>(); if(caption) caption.text="Yakında";
                var reason=jobFooter.Find("AcceptDisabledReason")?.GetComponent<Text>(); if(reason) reason.text="Bu iş yakında açılacak.";
            }
        }
        void Fleet()
        {
            StatStrip(("ARAÇ","Servis vanı"),("DURUM","%"+Mathf.RoundToInt(Runtime.Data.vehicleCondition)),
                ("YÜK",Runtime.vehicle.LoadedItems.Count()+" ekipman"));
            Row("Modüller arka kapının dışına elle takılır. Aracı boşaltın, taşıyıcı kapısını kapatın ve bağlantıları kullanın.",20,Soft,58);
            var table=Table("Modules",new[]{"Modül","Kapasite","Yuva","Durum","Fiyat","İşlem"},
                new[]{.30f,.11f,.07f,.20f,.13f,.19f},new[]{1,2,4});
            foreach (var mod in catalog.modules)
            {
                bool owned=Runtime.Data.ownedModules.Contains(mod.id);
                bool installed=Runtime.vehicle.InstalledModuleId==mod.id;
                string status=Runtime.manualModules?Runtime.manualModules.Status(mod.id):installed?"Takılı":owned?"Garajda":"Satışta";
                table.Add("Module_"+mod.id,new[]{mod.displayName,mod.payloadKg+" kg",mod.cargoSlots.ToString(),
                    status,owned?"—":Money(mod.price),""},
                    installed?"Takılı":owned?status:"Satın al",()=>Runtime.BuyModule(mod.id),!owned && Runtime.Data.balance>=mod.price);
            }
        }
        void Stock()
        {
            var items=Runtime.EquipmentItems;
            StatStrip(("EKİPMAN",items.Length.ToString()),
                ("TESLİMATTA",Runtime.Data.orders.Count(x=>!x.delivered).ToString()),("YEDEK PARÇA",Runtime.Data.spareParts.ToString()));
            ToolbarButton("OrderParts","Yedek parça al  ("+Money(catalog.economy.sparePartsPackPrice)+")",()=>Runtime.OrderParts(),
                Runtime.Data.balance>=catalog.economy.sparePartsPackPrice);
            var table=Table("Stock",new[]{"Ekipman","Toplam","Araçta","Teslimatta","En düşük durum"},
                new[]{.40f,.12f,.12f,.14f,.22f},new[]{1,2,3,4});
            foreach(var def in catalog.equipment)
            {
                var stock=items.Where(x=>x.definitionId==def.id).ToArray();
                int pending=Runtime.Data.orders.Count(x=>x.itemId==def.id&&!x.delivered);
                table.Add("Stock_"+def.id,new[]{def.displayName,stock.Length.ToString(),stock.Count(x=>x.Loaded).ToString(),
                    pending.ToString(),stock.Length==0?"-":"%"+Mathf.RoundToInt(stock.Min(x=>x.durability))});
            }
        }
        void Bank()
        {
            StatStrip(("BAKİYE",Money(Runtime.Data.balance)),("KAYITLI İŞLEM",Runtime.Data.transactions.Count.ToString()));
            var table=Table("Transactions",new[]{"Tarih","Açıklama","Tutar","Bakiye"},
                new[]{.18f,.50f,.16f,.16f},new[]{2,3});
            foreach (var entry in Runtime.Data.transactions.AsEnumerable().Reverse())
                table.Add("Transaction_"+entry.id,new[]{DateTimeOffset.FromUnixTimeSeconds(entry.createdUnix).ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
                    entry.reason,(entry.amount>=0?"+":"−")+Money(Mathf.Abs(entry.amount)),Money(entry.balanceAfter)});
            if(Runtime.Data.transactions.Count==0) Row("Henüz hesap hareketi yok.",14,Soft,30);
        }
        void Shop()
        {
            var items=Runtime.EquipmentItems;
            int capacity=Runtime.Data.upgrades.Contains("storage")?24:12;
            int occupied=items.Length+Runtime.Data.orders.Count(x=>!x.delivered);
            StatStrip(("BAKİYE",Money(Runtime.Data.balance)),("DEPO KULLANIMI",occupied+" / "+capacity));
            Row("Siparişler garajdaki teslimat noktasına gelir.",14,Soft,28);
            var table=Table("EquipmentCatalog",new[]{"Ekipman","Ağırlık","Stok","Yolda","Birim fiyat","Sipariş"},
                new[]{.35f,.11f,.08f,.08f,.16f,.22f},new[]{1,2,3,4});
            foreach(var def in catalog.equipment)
            {
                int owned=items.Count(x=>x.definitionId==def.id);
                int pending=Runtime.Data.orders.Count(x=>x.itemId==def.id&&!x.delivered);
                var button=table.Add("Product_"+def.id,new[]{def.displayName,def.weightKg+" kg",owned.ToString(),pending.ToString(),Money(def.price),""},
                    "Sipariş ver",()=>Runtime.OrderEquipment(def.id),Runtime.Data.balance>=def.price && occupied<capacity);
                button.gameObject.name="Buy_"+def.displayName;
            }
            if(occupied>=capacity) Row("Depo kapasitesi dolu. Yeni sipariş için depoyu genişletin.",14,Red,30);
        }
        void Stats()
        {
            Property("Bakiye",Money(Runtime.Data.balance));
            Property("Tamamlanan iş",Runtime.Data.jobsCompleted.ToString());
            Property("İtibar",Runtime.Data.reputation.ToString());
            Property("Ekipman",Runtime.EquipmentItems.Length.ToString());
            Property("Satın alınan modül",Runtime.Data.ownedModules.Count.ToString());
            Property("Bina geliştirmesi",Runtime.Data.upgrades.Count.ToString());
            Property("Başarılı denetim",Runtime.Data.inspectionsPassed.ToString());
        }
        void Building()
        {
            var table=Table("Upgrades",new[]{"Geliştirme","Açıklama","Fiyat","İşlem"},
                new[]{.23f,.43f,.14f,.20f},new[]{2},46);
            foreach (var upgrade in new[]{"storage","lighting","operations"})
            {
                string name=upgrade=="storage"?"Ek depo rafı":upgrade=="lighting"?"Yeni atölye ışığı":"Operasyon panosu";
                int price=catalog.economy.UpgradePrice(upgrade);
                bool owned=Runtime.Data.upgrades.Contains(upgrade);
                string description=upgrade=="storage"?"Ekipman kapasitesi: 12 → 24":
                    upgrade=="lighting"?"Ek atölye aydınlatması":"Teslimat süresi ve operasyon bonusu";
                table.Add("Upgrade_"+upgrade,new[]{name,description,owned?"-":Money(price),""},
                    owned?"Kurulu":"Satın al",()=>Runtime.BuyUpgrade(upgrade),!owned && Runtime.Data.balance>=price);
            }
        }
        void Mail()
        {
            var table=Table("Inbox",new[]{"Konu","Durum","İşlem"},new[]{.58f,.22f,.20f});
            table.Add("Mail_jobs",new[]{"Güncel iş ilanları","",""},"Aç",()=>Show("JobDesk"));
            if (Runtime.Data.inspectionPending)
                table.Add("Mail_inspection",new[]{"Güvenlik denetimi","Bekliyor",""},"Aç",()=>Show("Safety"));
            foreach(var order in Runtime.Data.orders.AsEnumerable().Reverse())
                table.Add("Mail_order_"+order.uid,new[]{catalog.Item(order.itemId)?.displayName??order.itemId,
                    order.delivered?"Teslim edildi":"Yolda",""},"Depoyu aç",()=>Show("StockManager"));
        }
        void Safety()
        {
            float required=catalog.economy.inspectionMinimumCondition;
            StatStrip(("GEREKEN DURUM","%"+required),("DENETİM",Runtime.Data.inspectionPending?"Bekliyor":"Beklenmiyor"));
            var table=Table("Inspection",new[]{"Araç / ekipman","Durum","Kontrol"},new[]{.55f,.20f,.25f},new[]{1});
            table.Add("Inspection_vehicle",new[]{"Servis vanı","%"+Mathf.RoundToInt(Runtime.Data.vehicleCondition),
                Runtime.Data.vehicleCondition>=required?"Uygun":"Bakım gerekli"});
            foreach(var item in Runtime.EquipmentItems.OrderBy(x=>x.DisplayName))
                table.Add("Inspection_"+item.uid,new[]{item.DisplayName,"%"+Mathf.RoundToInt(item.durability),
                    item.durability>=required?"Uygun":"Bakım gerekli"});
            if (!Runtime.Data.inspectionPending)
            {
                if(!string.IsNullOrEmpty(Runtime.Data.lastInspectionSummary)) Row(Runtime.Data.lastInspectionSummary,14,Soft,32);
                Row("Sonraki denetim: en erken "+Runtime.Data.nextInspectionAtCompletedJobs+" tamamlanan işte.",14,Soft,30);
                return;
            }
            var problems=Runtime.InspectionProblems();
            Row(problems.Count==0?"Araç ve ekipman denetime hazır.":"Eksikler tamamlanmadan onay verilirse ceza ve itibar kaybı uygulanır.",14,
                problems.Count==0?Soft:Red,32);
            ToolbarButton("CompleteInspection","Denetimi tamamla",()=>Runtime.ResolveInspection(),Runtime.Data.phase==JobPhase.None);
            if(Runtime.Data.phase!=JobPhase.None) Row("Denetim için aktif işin bitmesi gerekiyor.",14,Soft,28);
        }
        void Dispatch()
        {
            var job=catalog.Job(Runtime.Data.activeJobId);
            if (!job)
            {
                Row("Aktif iş yok.",14,Soft,30);
                ToolbarButton("DispatchJobs","İlanları aç",()=>{appSection="list"; Show("JobDesk");}); return;
            }
            Property("Görev",job.title);
            Property("Durum",PhaseName());
            if (Runtime.Data.contractAccepted)
                Property("Sözleşme",job.contractTitle+" (ek "+Money(job.contractBonus)+")");
            var missing=Runtime.MissingPreparation();
            if (Runtime.Data.phase==JobPhase.Accepted)
            {
                var moduleTable=Table("RequiredModule",new[]{"Gerekli modül","Takılı modül","Kontrol"},new[]{.40f,.40f,.20f});
                moduleTable.Add("ModuleCheck",new[]{catalog.Module(job.requiredModule)?.displayName??job.requiredModule,
                    catalog.Module(Runtime.Data.installedModuleId)?.displayName??Runtime.Data.installedModuleId,
                    Runtime.Data.installedModuleId==job.requiredModule?"Tamam":"Değiştirilmeli"});
                var equipment=Table("RequiredEquipment",new[]{"Ekipman","Gerekli","Yüklü","Kontrol"},
                    new[]{.45f,.15f,.15f,.25f},new[]{1,2});
                foreach(var group in job.requiredEquipment.GroupBy(x=>x))
                {
                    var loaded=Runtime.vehicle.LoadedItems.Where(x=>x.definitionId==group.Key).ToArray();
                    equipment.Add("Required_"+group.Key,new[]{catalog.Item(group.Key)?.displayName??group.Key,
                        group.Count().ToString(),loaded.Length.ToString(),loaded.Length<group.Count()?"Eksik":
                        loaded.Any(x=>x.durability<20)?"Bakım gerekli":"Tamam"});
                }
                Property("Araç durumu","%"+Mathf.RoundToInt(Runtime.Data.vehicleCondition));
                Row(missing.Count==0?"Çıkış için hazırlık tamam.":"Eksik: "+string.Join(", ",missing),14,
                    missing.Count==0?Soft:Red,42);
                ToolbarButton("DepartJob","Göreve çık",()=>Runtime.Depart(),missing.Count==0);
            }
            else if (Runtime.Data.phase==JobPhase.Deployed)
            {
                // Görev köprüsü: oyun sahadayken kapandıysa ekip sahaya yeniden gönderilir.
                Row("Ekip sahada. Görev bitince rapor burada görünür.",14,Soft,36);
                if(job.HasFieldScene) ToolbarButton("ResumeField","Sahaya git",()=>Runtime.ResumeField());
            }
            else ToolbarButton("DispatchReports","Raporu aç",()=>Show("Reports"));
        }
        void Reports()
        {
            if (Runtime.Data.phase==JobPhase.AwaitingReport)
            {
                var job=catalog.Job(Runtime.Data.activeJobId);
                var parent=mainContent;
                var workspace=Obj("ReportWorkspace",parent); Height(workspace,appScroll.viewport.rect.height-109);
                mainContent=ScrollColumn("ReportDetails",workspace.transform,108,0);
                Property("Görev",job.title);
                Property("Müşteri",job.client);
                Property("Sonuç",Runtime.Data.fieldSummary);
                var table=Table("ReportAmounts",new[]{"Kalem","Tutar"},new[]{.75f,.25f},new[]{1},30);
                table.Add("BasePayment",new[]{"İş bedeli",Money(Runtime.Data.reportBasePayment)});
                table.Add("FieldBonus",new[]{"Saha bonusu",Money(Runtime.Data.reportFieldBonus)});
                table.Add("ContractBonus",new[]{"Sözleşme bonusu",Money(Runtime.Data.reportContractBonus)});
                table.Add("Expenses",new[]{"Masraflar","−"+Money(Runtime.Data.reportExpenses)});
                Property("Kullanılan ekipman",Runtime.Data.reportEquipment);
                Property("Ekipman aşınması","%"+Runtime.Data.reportEquipmentWear);
                Property("Araç aşınması","%"+Runtime.Data.reportVehicleWear);
                if (Runtime.Data.contractAccepted)
                    Property("Sözleşme",job.contractTitle+" / "+(Runtime.Data.contractSatisfied?"Koşul sağlandı":"Koşul sağlanmadı"));
                mainContent=parent;
                ReportActions(workspace.transform);
            }
            else Row("Bekleyen rapor yok.",14,Soft,30);
        }
    }
}
