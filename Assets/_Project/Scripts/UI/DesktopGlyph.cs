using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CompanySim
{
    // Phosphor Regular upstream paths, rasterized at 4x and imported as UI sprites.
    public sealed class DesktopGlyph : Image
    {
        static readonly Dictionary<string,Sprite> Cache=new Dictionary<string,Sprite>();
        string currentKind;
        public bool shortcut;
        public string kind
        {
            get=>currentKind;
            set
            {
                currentKind=value;
                string asset=value switch
                {
                    "JobDesk"=>"clipboard-text", "FleetManager"=>"truck", "StockManager"=>"package",
                    "EquipmentShop"=>"toolbox", "CompanyBank"=>"calculator", "CompanyStats"=>"buildings",
                    "MailBox"=>"envelope-simple", "Home"=>"desktop", "Save"=>"floppy-disk", "Power"=>"power",
                    "Minimize"=>"minus", "Maximize"=>"square", "Close"=>"x", "Check"=>"check", "Crosshair"=>"circle",
                    "Dispatch"=>"truck", "Reports"=>"file-text", "BuildingManager"=>"buildings", "Safety"=>"shield-check",
                    "toolbox"=>"toolbox", "jack"=>"wrench", "tire"=>"tire", "Module"=>"truck", _=>value
                };
                if(!Cache.TryGetValue(asset,out var loaded)) { loaded=Resources.Load<Sprite>("OfficeIcons/"+asset); Cache[asset]=loaded; }
                sprite=loaded; preserveAspect=true;
            }
        }
    }
}
