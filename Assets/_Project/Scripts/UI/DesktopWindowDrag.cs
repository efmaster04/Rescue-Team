using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CompanySim
{
    public sealed class DesktopWindowDrag : MonoBehaviour,IBeginDragHandler,IDragHandler,IEndDragHandler,IPointerClickHandler
    {
        public RectTransform window;
        public Func<bool> canDrag;
        public Action doubleClick;
        Vector2 pointerStart,windowStart;
        bool dragging;
        readonly Vector3[] corners=new Vector3[4];
        public void OnBeginDrag(PointerEventData data)
        {
            dragging=data.button==PointerEventData.InputButton.Left && window && (canDrag==null || canDrag()) &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)window.parent,data.position,data.pressEventCamera,out pointerStart);
            if(dragging) windowStart=window.anchoredPosition;
        }
        public void OnDrag(PointerEventData data)
        {
            if(!dragging) return;
            var parent=(RectTransform)window.parent;
            if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent,data.position,data.pressEventCamera,out var pointer)) return;
            window.anchoredPosition=windowStart+pointer-pointerStart;
            window.GetWorldCorners(corners);
            var low=parent.InverseTransformPoint(corners[0]); var high=parent.InverseTransformPoint(corners[2]);
            var area=parent.rect; Vector2 correction=Vector2.zero;
            if(low.x<area.xMin+4) correction.x=area.xMin+4-low.x;
            if(high.x>area.xMax-4) correction.x=area.xMax-4-high.x;
            if(low.y<area.yMin+48) correction.y=area.yMin+48-low.y;
            if(high.y>area.yMax-4) correction.y=area.yMax-4-high.y;
            window.anchoredPosition+=correction;
        }
        public void OnEndDrag(PointerEventData data) { dragging=false; }
        public void OnPointerClick(PointerEventData data)
        {
            if(data.button==PointerEventData.InputButton.Left && data.clickCount==2) doubleClick?.Invoke();
        }
    }
}
