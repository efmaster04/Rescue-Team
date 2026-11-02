using System;
using UnityEngine;
using UnityEngine.UI;

namespace CompanySim
{
    public sealed partial class ComputerUI
    {
        sealed class OfficeTable
        {
            readonly ComputerUI owner;
            readonly Transform root;
            readonly float[] boundaries;
            readonly int[] rightAligned;
            readonly float rowHeight;
            int rowCount;

            public OfficeTable(ComputerUI owner,string name,string[] headings,float[] weights,
                int[] rightAligned,float rowHeight)
            {
                if(headings.Length!=weights.Length) throw new ArgumentException("Table column count does not match.");
                this.owner=owner; this.rightAligned=rightAligned??Array.Empty<int>(); this.rowHeight=rowHeight;
                var go=owner.Obj("Table_"+name,owner.mainContent); root=go.transform;
                var layout=go.AddComponent<VerticalLayoutGroup>(); layout.spacing=0;
                layout.childControlHeight=true; layout.childControlWidth=true;
                layout.childForceExpandHeight=false; layout.childForceExpandWidth=true;
                boundaries=new float[weights.Length+1];
                float total=0; foreach(float weight in weights) total+=weight;
                for(int i=0;i<weights.Length;i++) boundaries[i+1]=boundaries[i]+weights[i]/total;
                boundaries[weights.Length]=1;
                var header=owner.Block("ColumnHeadings",root,Slate); owner.Height(header,40);
                for(int i=0;i<headings.Length;i++) Cell(header.transform,i,headings[i],true);
            }
            bool AlignRight(int column) => Array.IndexOf(rightAligned,column)>=0;
            Text Cell(Transform row,int column,string value,bool heading=false)
            {
                var text=owner.Label("Cell_"+column,row,value,heading?18:20,heading?Soft:Paper,
                    AlignRight(column)?TextAnchor.MiddleRight:TextAnchor.MiddleLeft);
                if(heading) text.font=owner.headingFont;
                owner.Rect(text.gameObject,new Vector2(boundaries[column],0),new Vector2(boundaries[column+1],1),
                    new Vector2(10,2),new Vector2(-10,-2));
                return text;
            }
            public Button Add(string name,string[] values,string action=null,Action click=null,bool enabled=true)
            {
                if(values.Length!=boundaries.Length-1) throw new ArgumentException("Table row column count does not match.");
                rowCount++;
                var row=owner.Block(name,root,Surface);
                owner.Height(row,Mathf.Max(46,rowHeight));
                var rule=owner.Block("RowSeparator",row.transform,Rule);
                owner.Rect(rule,Vector2.zero,new Vector2(1,0),Vector2.zero,new Vector2(0,1));
                rule.GetComponent<Image>().raycastTarget=false;
                for(int i=0;i<values.Length;i++)
                    if(action==null || i<values.Length-1) Cell(row.transform,i,values[i]);
                if(action==null) return null;
                int last=values.Length-1;
                var go=owner.Block(name+"_Action",row.transform,Selection);
                var button=owner.Bind(go,click??(()=>{}),false); button.interactable=enabled;
                owner.Rect(go,new Vector2(boundaries[last],.5f),new Vector2(1,.5f),
                    new Vector2(7,-18),new Vector2(-7,18));
                var caption=owner.Label("Caption",go.transform,action,18,Mint,TextAnchor.MiddleCenter);
                owner.Fill(caption.gameObject,2);
                return button;
            }
        }
        OfficeTable Table(string name,string[] headings,float[] widths,int[] rightAligned=null,float height=36)
            => new OfficeTable(this,name,headings,widths,rightAligned,height);

        void Property(string label,string value)
        {
            var row=Obj("Property_"+label,mainContent); Height(row,36);
            var title=Label("Name",row.transform,label,20,Soft);
            Rect(title.gameObject,Vector2.zero,new Vector2(.32f,1),new Vector2(0,0),new Vector2(-16,0));
            var content=Label("Value",row.transform,value,21,Paper);
            Rect(content.gameObject,new Vector2(.32f,0),Vector2.one,Vector2.zero,Vector2.zero);
            if(value.Length>48) Height(row,value.Length>100?96:64);
        }
        Button ToolbarButton(string name,string caption,Action click,bool enabled=true)
        {
            var row=Obj("Toolbar",mainContent); Height(row,52);
            var go=Block(name,row.transform,Mint); Place(go,0,4,320,44);
            var button=Bind(go,click,false); button.interactable=enabled;
            var text=Label("Caption",go.transform,caption,18,Surface,TextAnchor.MiddleCenter); Fill(text.gameObject,3);
            return button;
        }
    }
}
