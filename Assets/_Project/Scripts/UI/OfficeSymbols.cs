using UnityEngine;
using UnityEngine.UI;

namespace CompanySim
{
    // Small, solid desktop objects. Coordinates follow a 32-pixel icon grid.
    internal static class OfficeSymbols
    {
        sealed class Painter
        {
            readonly VertexHelper mesh;
            readonly Vector2 origin;
            readonly float scale;
            public Painter(VertexHelper mesh,Rect rect)
            {
                this.mesh=mesh; scale=Mathf.Min(rect.width,rect.height)/32;
                origin=new Vector2(rect.center.x-16*scale,rect.center.y+16*scale);
            }
            Vector2 P(float x,float y) => origin+new Vector2(x*scale,-y*scale);
            public void Polygon(uint rgb,params Vector2[] points)
            {
                int first=mesh.currentVertCount;
                var color=new Color32((byte)(rgb>>16),(byte)(rgb>>8),(byte)rgb,255);
                foreach(var point in points) mesh.AddVert(P(point.x,point.y),color,Vector2.zero);
                for(int i=1;i<points.Length-1;i++) mesh.AddTriangle(first,first+i,first+i+1);
            }
            public void R(float x,float y,float w,float h,uint rgb) => Polygon(rgb,
                new Vector2(x,y),new Vector2(x+w,y),new Vector2(x+w,y+h),new Vector2(x,y+h));
            public void Box(int x,int y,int w,int h,uint fill)
            {
                R(x,y,w,h,0x343536); R(x+1,y+1,w-2,h-2,fill);
                R(x+1,y+1,w-2,1,0xf4f4e8); R(x+1,y+1,1,h-2,0xf4f4e8);
            }
            public void Paper()
            {
                R(7,2,20,28,0x383a3d); R(8,3,18,26,0xf8f8ef);
                R(11,8,11,1,0x7b8795); R(11,11,12,1,0x7b8795);
            }
            public void Folder()
            {
                R(2,8,12,5,0x69531a); R(3,9,10,4,0xf0cf6d);
                R(2,12,27,17,0x69531a); R(3,13,25,15,0xe1b24c);
                R(4,14,24,1,0xffe59b); R(4,15,1,12,0xffe59b);
            }
        }
        public static bool Draw(VertexHelper mesh,Rect rect,string kind,bool shortcut)
        {
            var p=new Painter(mesh,rect);
            switch(kind)
            {
                case "JobDesk":
                    p.Box(6,4,21,26,0x987745); p.R(9,7,15,20,0xf8f7e8);
                    p.Box(11,1,11,7,0xa4a6a5); p.R(14,3,5,2,0x454749);
                    for(int i=0;i<4;i++) {p.R(11,12+i*3,2,1,0x214b79); p.R(15,12+i*3,7,1,0x858c91);} break;
                case "FleetManager":
                    p.Box(2,8,16,15,0x3c6d9e); p.Box(18,13,12,11,0xe5e4d5);
                    p.R(21,14,7,5,0x99c6d9); p.R(2,22,28,3,0x777b80);
                    p.Box(5,22,7,7,0x444446); p.R(7,24,3,3,0xb0b3b1);
                    p.Box(21,22,7,7,0x444446); p.R(23,24,3,3,0xb0b3b1); break;
                case "StockManager":
                    p.Box(4,9,24,20,0xb18a51); p.R(5,10,22,5,0xd6b982);
                    p.R(15,10,3,18,0xf0dbaf); p.R(6,18,7,5,0xeee7d0); p.R(7,20,5,1,0x6d6a60);
                    p.Polygon(0xe2c79b,new Vector2(4,9),new Vector2(12,3),new Vector2(29,9)); break;
                case "EquipmentShop":
                    p.Box(10,4,13,7,0x818687); p.R(13,6,7,3,0x15181b);
                    p.Box(3,11,27,17,0xa1392d); p.R(4,12,25,5,0xd15c42);
                    p.R(4,18,25,2,0x752b25); p.Box(14,16,5,6,0xbbbba9); break;
                case "CompanyBank":
                    p.Box(7,2,20,29,0xa8aa9f); p.R(10,5,14,6,0x304c3e); p.R(12,7,10,2,0xc3d8b3);
                    for(int row=0;row<3;row++) for(int col=0;col<3;col++)
                        p.Box(10+col*5,14+row*5,4,4,col==2?0x446689u:0xe2e3dcu); break;
                case "Dispatch":
                    p.Folder(); p.R(9,15,14,4,0x4d7242); p.R(9,19,4,4,0x4d7242);
                    p.Polygon(0x4d7242,new Vector2(18,11),new Vector2(27,17),new Vector2(18,22)); break;
                case "Reports":
                    p.Paper(); p.R(11,22,3,4,0x738d4e); p.R(16,18,3,8,0x5a7d9e); p.R(21,15,3,11,0x9e6d4f); break;
                case "BuildingManager":
                    p.Box(6,12,21,18,0xc7c5b7);
                    p.Polygon(0x843f33,new Vector2(3,13),new Vector2(16,3),new Vector2(30,13));
                    p.R(10,15,5,5,0x5882a3); p.R(20,15,4,5,0x5882a3); p.R(15,23,6,7,0x626469); break;
                case "CompanyStats":
                    p.Box(3,3,27,25,0xd3d2c6); p.R(6,6,21,19,0xf7f7ed);
                    p.R(9,17,4,6,0x487c9d); p.R(15,13,4,10,0x6d8c45); p.R(21,8,4,15,0xab7651); break;
                case "Safety":
                    p.Polygon(0x504a27,new Vector2(16,2),new Vector2(31,29),new Vector2(1,29));
                    p.Polygon(0xf4ce45,new Vector2(16,5),new Vector2(28,27),new Vector2(4,27));
                    p.R(14,12,4,9,0x2e3030); p.R(14,23,4,3,0x2e3030); break;
                case "MailBox":
                    p.Box(2,8,28,21,0xf3f0dd);
                    p.Polygon(0xcac6b4,new Vector2(3,28),new Vector2(15,17),new Vector2(29,28));
                    p.Polygon(0x777769,new Vector2(3,9),new Vector2(16,21),new Vector2(29,9));
                    p.Polygon(0xfff8dd,new Vector2(4,9),new Vector2(16,19),new Vector2(28,9)); break;
                case "Home":
                    p.Box(3,3,26,20,0xbfc0b5); p.R(6,6,20,14,0x305f83); p.R(8,8,16,10,0x5089a6);
                    p.R(13,23,6,4,0x8b8e88); p.Box(8,27,17,4,0xbfc0b5); break;
                case "Save":
                    p.Box(4,2,25,29,0x305e8e); p.R(8,3,16,10,0xc3c6c3); p.R(17,4,4,7,0x54585b);
                    p.R(8,18,17,12,0xf1f1e8); p.R(11,21,11,1,0xa8aaa5); p.R(11,25,11,1,0xa8aaa5); break;
                default: return false;
            }
            if(shortcut)
            {
                p.Box(0,23,10,9,0xffffff); p.R(2,27,5,2,0x1b3761); p.R(5,25,2,5,0x1b3761);
            }
            return true;
        }
    }
}
