using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace CompanySim
{
    public sealed partial class ComputerUI
    {
        string selectedProduct="toolbox",selectedMail="jobs";
        readonly HashSet<string> readMail=new HashSet<string>();
        void JobFacts(JobDefinition job)
        {
            var row=Obj("JobFacts",mainContent); Height(row,112);
            var facts=new[]{("Müşteri",job.client),("Konum",job.location),("Mesafe",job.distanceKm+" km"),("Süre",job.durationMinutes+" dk")};
            for(int i=0;i<facts.Length;i++)
            {
                float left=i%2*.5f,top=i<2?0:-56;
                var key=Label("FactName_"+i,row.transform,facts[i].Item1,17,Soft);
                Rect(key.gameObject,new Vector2(left,1),new Vector2(left+.5f,1),new Vector2(0,top-23),new Vector2(-18,top));
                var value=Label("FactValue_"+i,row.transform,facts[i].Item2,22,Paper);
                Rect(value.gameObject,new Vector2(left,1),new Vector2(left+.5f,1),new Vector2(0,top-53),new Vector2(-18,top-22));
            }
        }
        void RequirementRow(string icon,string caption)
        {
            var row=Obj("Requirement_"+icon,mainContent); Height(row,48);
            Place(Icon(row.transform,EquipmentIcon(icon),Paper).gameObject,3,9,30,30);
            var text=Label("RequirementName",row.transform,caption,22,Paper);
            Rect(text.gameObject,Vector2.zero,new Vector2(.72f,1),new Vector2(52,0),Vector2.zero);
            var meta=Label("RequirementType",row.transform,"Gerekli",17,Soft);
            Rect(meta.gameObject,new Vector2(.75f,0),Vector2.one,Vector2.zero,Vector2.zero);
            var line=Block("RequirementRule",row.transform,Rule); Rect(line,Vector2.zero,new Vector2(1,0),Vector2.zero,new Vector2(0,1)); line.GetComponent<Image>().raycastTarget=false;
        }
        string EquipmentIcon(string id) => id switch
        { "Module"=>"truck", "toolbox"=>"toolbox", "case"=>"toolbox", "jack"=>"wrench", "tire"=>"tire", _=>"package" };
        void JobActions(JobDefinition job,bool available)
        {
            var line=Block("ActionRule",jobFooter,Rule); Place(line,0,0,jobFooter.rect.width,1); line.GetComponent<Image>().raycastTarget=false;
            if(job.contractBonus>0)
            {
                var box=Block("JobContract",jobFooter,Surface); Rect(box,new Vector2(0,1),Vector2.one,new Vector2(0,-67),new Vector2(0,-8));
                var checkBox=Block("Checkbox",box.transform,Surface); Place(checkBox,1,8,23,23); Border(checkBox);
                var check=Icon(checkBox.transform,"Check",Mint); Fill(check.gameObject,3);
                var toggle=box.AddComponent<Toggle>(); toggle.targetGraphic=checkBox.GetComponent<Image>(); toggle.graphic=check;
                toggle.SetIsOnWithoutNotify(selectedContract); toggle.interactable=available;
                toggle.onValueChanged.AddListener(value=>{selectedContract=value; Click();});
                var title=Label("ContractTitle",box.transform,job.contractTitle,20,Paper); Place(title.gameObject,39,0,500,31);
                var caption=Label("ContractTerms",box.transform,"Aşınma en fazla %"+job.contractMaxEquipmentWear+" / %"+job.contractMaxVehicleWear+", kayıp yok. +"+Money(job.contractBonus),16,Soft);
                Rect(caption.gameObject,Vector2.zero,Vector2.one,new Vector2(39,0),new Vector2(0,-29));
            }
            var price=Label("JobPriceLabel",jobFooter,"İş bedeli",17,Soft); Place(price.gameObject,0,82,190,23);
            var value=Label("JobPrice",jobFooter,Money(job.payment),28,Paper); value.font=headingFont; Place(value.gameObject,0,100,200,44);
            var go=Block("AcceptJob",jobFooter,Mint); Rect(go,new Vector2(.55f,0),new Vector2(1,0),Vector2.zero,new Vector2(0,52));
            var button=Bind(go,()=>{if(Runtime.AcceptJob(job.id,selectedContract)) {selectedContract=false; Show("Dispatch");}},false); button.interactable=available;
            var text=Label("Caption",go.transform,"İşi kabul et",21,Surface,TextAnchor.MiddleCenter); text.font=headingFont; Fill(text.gameObject,5);
            if(!available)
            {
                var reason=Label("AcceptDisabledReason",jobFooter,Runtime.Data.phase!=JobPhase.None?"Önce aktif işi tamamlayın.":job.minimumReputation+" itibar gerekli.",16,Red);
                Place(reason.gameObject,0,57,jobFooter.rect.width,26);
            }
        }
        void ProductImage(Transform parent,string id,float x,float y,float width,float height)
        {
            var go=Obj("ModelPreview_"+id,parent); var image=go.AddComponent<Image>(); image.sprite=Resources.Load<Sprite>("OfficeThumbnails/"+id);
            image.preserveAspect=true; image.raycastTarget=false; Place(go,x,y,width,height);
        }
        void ReportActions(Transform parent)
        {
            var footer=Obj("ReportPayment",parent);
            Rect(footer,Vector2.zero,new Vector2(1,0),Vector2.zero,new Vector2(0,100));
            var line=Block("ReportPaymentRule",footer.transform,Rule);
            Rect(line,new Vector2(0,1),Vector2.one,new Vector2(0,-1),Vector2.zero); line.GetComponent<Image>().raycastTarget=false;
            var caption=Label("ReportTotalLabel",footer.transform,"Tahsil edilecek toplam",18,Soft);
            Place(caption.gameObject,0,16,300,27);
            var total=Label("ReportTotal",footer.transform,Money(Runtime.Data.pendingPayment),28,Paper);
            total.font=headingFont; Place(total.gameObject,0,42,300,44);
            var go=Block("ApproveReport",footer.transform,Mint);
            Rect(go,new Vector2(.55f,0),new Vector2(1,0),new Vector2(0,16),new Vector2(0,68));
            Bind(go,()=>Runtime.FileReport(),false);
            var text=Label("Caption",go.transform,"Onayla ve tahsil et",21,Surface,TextAnchor.MiddleCenter);
            text.font=headingFont; Fill(text.gameObject,5);
        }
        void VehicleOverview()
        {
            SectionHeading("Servis vanı");
            var row=Obj("VehicleSummary",mainContent); Height(row,264);
            ProductImage(row.transform,"vehicle",0,0,380,250);
            var fields=Obj("VehicleProperties",row.transform); Rect(fields,new Vector2(.43f,0),Vector2.one,Vector2.zero,Vector2.zero);
            var parent=mainContent; mainContent=fields.GetComponent<RectTransform>(); Layout(mainContent,8); mainContent.GetComponent<VerticalLayoutGroup>().padding=new RectOffset(16,0,18,0);
            Property("Durum","%"+Mathf.RoundToInt(Runtime.Data.vehicleCondition));
            Property("Modül",catalog.Module(Runtime.vehicle.InstalledModuleId)?.displayName??"Takılı ve sabit modül yok");
            Property("Yüklü eşya",Runtime.vehicle.LoadedItems.Count()+" ekipman"); mainContent=parent;
            if(Runtime.manualModules && !Runtime.manualModules.IsSecured) Row("Modül bağlantıları tamamlanmadı. Araç hazır değil.",18,Red,42);
            Divider();
            ToolbarButton("OpenVehicleModules","Modülleri aç",()=>{appSection="modules"; Show(app);});
            ToolbarButton("OpenCargoManifest","Yük listesini aç",()=>{appSection="cargo"; Show(app);});
        }
        void CargoManifest()
        {
            SectionHeading("Yük listesi");
            var items=Runtime.vehicle.LoadedItems.ToArray();
            if(items.Length==0) { Row("Araçta ekipman yok.",19,Soft,42); return; }
            var table=Table("CargoManifest",new[]{"Ekipman","Ağırlık","Durum"},new[]{.60f,.20f,.20f},new[]{1,2});
            foreach(var item in items) table.Add("Cargo_"+item.uid,new[]{item.DisplayName,(catalog.Item(item.definitionId)?.weightKg??0)+" kg","%"+Mathf.RoundToInt(item.durability)});
        }
        void StockView()
        {
            SectionHeading("Ekipman");
            var items=Runtime.EquipmentItems.OrderBy(x=>x.DisplayName).ThenBy(x=>x.uid).ToArray();
            StatStrip(("Ekipman",items.Length.ToString()),("Yedek parça",Runtime.Data.spareParts.ToString()));
            foreach(var item in items)
            {
                var row=Block("StockItem_"+item.uid,mainContent,Surface); Height(row,76);
                ProductImage(row.transform,item.definitionId,0,6,64,64);
                Place(Label("EquipmentName",row.transform,item.DisplayName,20,Paper).gameObject,86,5,360,35);
                string location=player.HeldItem==item?"Elde":item.Loaded?"Araçta":"Garajda";
                Place(Label("EquipmentLocation",row.transform,location,17,Soft).gameObject,86,40,260,27);
                var status=Label("EquipmentCondition",row.transform,"%"+Mathf.RoundToInt(item.durability)+(item.durability<20?"  Bakım gerekli":""),18,item.durability<20?Red:Soft,TextAnchor.MiddleRight);
                Rect(status.gameObject,new Vector2(.56f,0),Vector2.one,Vector2.zero,new Vector2(-16,0));
                var rule=Block("EquipmentRule",row.transform,Rule); Rect(rule,Vector2.zero,new Vector2(1,0),Vector2.zero,new Vector2(0,1)); rule.GetComponent<Image>().raycastTarget=false;
            }
            if(items.Length==0) Row("Depoda ekipman yok. Mağazadan sipariş verebilirsiniz.",19,Soft,48);
            ToolbarButton("OrderParts","Yedek parça al — "+Money(catalog.economy.sparePartsPackPrice),()=>Runtime.OrderParts(),Runtime.Data.balance>=catalog.economy.sparePartsPackPrice);
            if(Runtime.Data.balance<catalog.economy.sparePartsPackPrice) Row("Yedek parça için bakiye yetersiz.",17,Red,30);
        }
        void Deliveries()
        {
            SectionHeading("Teslimatlar","Siparişler garajdaki teslimat noktasına gelir.");
            bool hasModules=Runtime.Data.modulePackages.Any(x=>x.arrivalUnix>0);
            if(Runtime.Data.orders.Count==0 && !hasModules) { Row("Henüz sipariş yok.",19,Soft,42); return; }
            var table=Table("Deliveries",new[]{"Ekipman","Durum","Kalan süre"},new[]{.52f,.25f,.23f},new[]{2});
            foreach(var order in Runtime.Data.orders.AsEnumerable().Reverse())
            {
                long seconds=Math.Max(0,order.arrivalUnix-DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                table.Add("Delivery_"+order.uid,new[]{catalog.Item(order.itemId)?.displayName??order.itemId,order.delivered?"Teslim edildi":"Yolda",order.delivered?"—":seconds+" sn"});
            }
            foreach(var order in Runtime.Data.modulePackages.Where(x=>x.arrivalUnix>0).Reverse())
                table.Add("ModuleDelivery_"+order.uid,new[]{catalog.Module(order.moduleId)?.displayName??order.moduleId,order.delivered?"Teslim edildi":"Yolda",order.delivered?"—":Math.Max(0,order.arrivalUnix-DateTimeOffset.UtcNow.ToUnixTimeSeconds())+" sn"});
        }
        bool InCategory(EquipmentDefinition item) => appSection switch
        { "tools"=>item.id=="toolbox" || item.id=="jack", "safety"=>item.id=="absorbent", "service"=>item.id!="toolbox" && item.id!="jack" && item.id!="absorbent", _=>true };
        void SplitWorkspace(string name,float height,float leftWidth,out RectTransform list,out RectTransform detail)
        {
            var row=Obj(name,mainContent); Height(row,height); var h=row.AddComponent<HorizontalLayoutGroup>(); h.spacing=28;
            h.childControlWidth=true; h.childControlHeight=true; h.childForceExpandWidth=false; h.childForceExpandHeight=true;
            var left=Obj("ListPane",row.transform); var ls=left.AddComponent<LayoutElement>(); ls.preferredWidth=leftWidth; ls.minWidth=leftWidth;
            var right=Obj("DetailPane",row.transform); var rs=right.AddComponent<LayoutElement>(); rs.flexibleWidth=1; rs.minWidth=0; rs.preferredWidth=0;
            list=ScrollColumn(name+"List",left.transform,0,0); detail=ScrollColumn(name+"Detail",right.transform,0,0);
        }
        void ShopView()
        {
            SectionHeading("Mağaza");
            var items=Runtime.EquipmentItems;
            int capacity=Runtime.Data.upgrades.Contains("storage")?24:12, occupied=items.Length+Runtime.Data.orders.Count(x=>!x.delivered);
            StatStrip(("Bakiye",Money(Runtime.Data.balance)),("Depo",occupied+" / "+capacity));
            var products=catalog.equipment.Where(InCategory).ToArray();
            if(!products.Any(x=>x.id==selectedProduct)) selectedProduct=products.FirstOrDefault()?.id;
            SplitWorkspace("ShopWorkspace",appScroll.viewport.rect.height-158,430,out var list,out var detail);
            var detailViewport=(RectTransform)detail.parent;
            detailViewport.offsetMin=new Vector2(0,110);
            var footer=Obj("ShopPurchase",detailViewport.parent);
            var footerRect=Rect(footer,Vector2.zero,new Vector2(1,0),Vector2.zero,new Vector2(0,100));
            Layout(footerRect,4); footerRect.GetComponent<VerticalLayoutGroup>().padding=new RectOffset();
            foreach(var def in products)
            {
                var product=def; bool selected=selectedProduct==def.id;
                var go=Block("Product_"+def.id,list,selected?Selection:Surface); Height(go,90);
                Bind(go,()=>{selectedProduct=product.id; Show(app);},false); ProductImage(go.transform,def.id,8,9,72,72);
                Place(Label("ProductName",go.transform,def.displayName,20,Paper).gameObject,98,12,300,32);
                Place(Label("ProductMeta",go.transform,def.weightKg+" kg  ·  "+items.Count(x=>x.definitionId==def.id)+" adet depoda",17,Soft).gameObject,98,47,250,26);
                var price=Label("ProductPrice",go.transform,Money(def.price),18,Paper,TextAnchor.MiddleRight);
                Rect(price.gameObject,new Vector2(1,0),new Vector2(1,0),new Vector2(-92,14),new Vector2(-12,40));
            }
            var selectedDef=catalog.Item(selectedProduct); if(!selectedDef) return;
            var parent=mainContent; mainContent=detail;
            var picture=Obj("SelectedEquipment",mainContent); Height(picture,180); ProductImage(picture.transform,selectedDef.id,0,0,300,180);
            Row(selectedDef.displayName,27,Paper,43); Divider();
            Property("Ağırlık",selectedDef.weightKg+" kg"); Property("Birim fiyat",Money(selectedDef.price));
            int pending=Runtime.Data.orders.Count(x=>x.itemId==selectedDef.id&&!x.delivered);
            Property("Teslimatta",pending+" adet"); Row("Garajdaki teslimat noktasına gönderilir.",18,Soft,52);
            mainContent=footerRect;
            var buy=ActionButton("Sipariş ver — "+Money(selectedDef.price),()=>Runtime.OrderEquipment(selectedDef.id));
            buy.name="Buy_"+selectedDef.displayName; buy.interactable=Runtime.Data.balance>=selectedDef.price && occupied<capacity;
            if(!buy.interactable) Row(occupied>=capacity?"Depo dolu. Şirket'ten ek raf alın.":"Sipariş için bakiye yetersiz.",17,Red,48);
            mainContent=parent;
        }
        sealed class OfficeMessage
        { public string id,title,sender,body,action; public Action open; }
        void MailView()
        {
            SectionHeading("Gelen kutusu");
            var messages=new List<OfficeMessage>{new OfficeMessage{id="jobs",title="İş ilanları",sender="İşler",body="Güncel ilanları İşler uygulamasından inceleyebilir, gereken modül ve ekipmanı iş fişinde görebilirsiniz.",action="İlanları aç",open=()=>Show("JobDesk")}};
            if(Runtime.Data.inspectionPending) messages.Add(new OfficeMessage{id="inspection",title="Denetim bildirimi",sender="Şirket",body="Araç ve ekipmanın denetimi bekleniyor. Durumlarını kontrol edip gerekli bakımı tamamlayın.",action="Denetimi aç",open=()=>Show("Safety")});
            foreach(var order in Runtime.Data.orders.AsEnumerable().Reverse())
                messages.Add(new OfficeMessage{id=order.uid,title=catalog.Item(order.itemId)?.displayName??order.itemId,sender="Teslimat",body=order.delivered?"Siparişiniz garajdaki teslimat noktasına bırakıldı.":"Siparişiniz yolda. Teslimat durumunu Depo uygulamasından takip edebilirsiniz.",action="Teslimatları aç",open=()=>{Show("StockManager"); appSection="deliveries"; Show(app);}});
            if(!messages.Any(x=>x.id==selectedMail)) selectedMail=messages[0].id;
            SplitWorkspace("InboxWorkspace",appScroll.viewport.rect.height-112,330,out var list,out var detail);
            foreach(var message in messages)
            {
                var entry=message; bool selected=entry.id==selectedMail;
                var go=Block("Message_"+entry.id,list,selected?Selection:Surface); Height(go,82);
                Bind(go,()=>{selectedMail=entry.id; readMail.Add(entry.id); Show(app);},false);
                var title=Label("MessageSubject",go.transform,entry.title,20,Paper); if(!readMail.Contains(entry.id)) title.font=headingFont;
                Place(title.gameObject,14,7,302,39); Place(Label("MessageSender",go.transform,entry.sender+(readMail.Contains(entry.id)?"":" · Okunmadı"),17,Soft).gameObject,14,46,302,28);
            }
            var chosen=messages.First(x=>x.id==selectedMail); var parent=mainContent; mainContent=detail;
            Row(chosen.title,28,Paper,48); Row(chosen.sender,18,Soft,30); Divider(); Row(chosen.body,20,Paper,152);
            ToolbarButton("OpenMessageAction",chosen.action,chosen.open); mainContent=parent;
        }
    }
}
