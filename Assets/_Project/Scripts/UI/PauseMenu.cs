using System;
using UnityEngine;
using UnityEngine.UI;

namespace CompanySim
{
    public sealed partial class ComputerUI
    {
        public const string VolumePreference="Company.Player.MasterVolume";
        public const string SensitivityPreference="Company.Player.LookSensitivity";
        public const string SwayPreference="Company.Player.EquipmentSway";
        GameObject pausePanel,pauseSettings,pauseControls;
        Slider volumeSlider,sensitivitySlider;
        Toggle swayToggle;
        Text volumeValue,sensitivityValue;
        float previousTimeScale=1;
        bool previousAudioPause,previousControlLock,pauseActive;
        public bool IsPaused => pauseActive;
        void BuildPauseMenu(Transform root)
        {
            pausePanel=Block("PauseMenu",root,new Color(.018f,.022f,.024f,.58f)); Fill(pausePanel);
            var sheet=Block("PauseSheet",pausePanel.transform,Surface);
            var rect=Rect(sheet,new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,Vector2.zero);
            rect.sizeDelta=new Vector2(880,472); Border(sheet);
            var navigation=Block("PauseNavigation",sheet.transform,Navy); Place(navigation,0,0,260,472);
            var title=Label("PauseTitle",navigation.transform,"Duraklatıldı",28,Surface); title.font=headingFont;
            Place(title.gameObject,28,30,210,46);
            PauseButton(navigation.transform,"ResumeGame","Devam et",110,()=>SetPaused(false));
            PauseButton(navigation.transform,"PauseSave","Kaydet",166,()=>{if(Runtime.Save()) Toast("Oyun kaydedildi.");});
            PauseButton(navigation.transform,"PauseSettings","Ayarlar",244,()=>PausePage(true));
            PauseButton(navigation.transform,"PauseControls","Kontroller",300,()=>PausePage(false));
            KeyCap(navigation.transform,"ESC",28,413,47); Place(Label("PauseBackHint",navigation.transform,"Oyuna dön",16,Surface).gameObject,89,413,140,27);
            pauseSettings=Obj("PauseSettingsPage",sheet.transform); Place(pauseSettings,260,0,620,472);
            pauseControls=Obj("PauseControlsPage",sheet.transform); Place(pauseControls,260,0,620,472);
            var heading=Label("OptionsHeading",pauseSettings.transform,"Ayarlar",28,Paper); heading.font=headingFont; Place(heading.gameObject,36,31,520,44);
            volumeSlider=PauseSlider(pauseSettings.transform,"MasterVolume","Ses",110,0,1,out volumeValue);
            volumeSlider.onValueChanged.AddListener(value=>{AudioListener.volume=value; volumeValue.text=Mathf.RoundToInt(value*100)+"%"; PlayerPrefs.SetFloat(VolumePreference,value);});
            sensitivitySlider=PauseSlider(pauseSettings.transform,"LookSensitivity","Fare hassasiyeti",208,.04f,.25f,out sensitivityValue);
            sensitivitySlider.onValueChanged.AddListener(value=>{player.lookSensitivity=value; sensitivityValue.text=Mathf.RoundToInt(value/.11f*100)+"%"; PlayerPrefs.SetFloat(SensitivityPreference,value);});
            var toggle=Block("EquipmentSway",pauseSettings.transform,Surface); Place(toggle,36,322,540,48);
            var box=Block("ToggleBox",toggle.transform,Surface); Place(box,0,13,23,23); Border(box);
            var check=Icon(box.transform,"Check",Mint); Fill(check.gameObject,3);
            swayToggle=toggle.AddComponent<Toggle>(); swayToggle.targetGraphic=box.GetComponent<Image>(); swayToggle.graphic=check;
            var nav=swayToggle.navigation; nav.mode=Navigation.Mode.None; swayToggle.navigation=nav;
            swayToggle.onValueChanged.AddListener(value=>{player.carrySway=value; PlayerPrefs.SetInt(SwayPreference,value?1:0); Click();});
            Place(Label("SwayLabel",toggle.transform,"Elde eşya salınımı",20,Paper).gameObject,40,0,450,48);
            var controlsTitle=Label("ControlsHeading",pauseControls.transform,"Kontroller",28,Paper); controlsTitle.font=headingFont;
            Place(controlsTitle.gameObject,36,31,520,44);
            string[] keys={"W A S D","Fare","E","F / R","TAB","ESC"};
            string[] text={"Yürü / Shift ile koş","Etrafa bak","Etkileşim / eşyayı al","Eşyayı bırak / döndür","İş fişini aç / kapat","Menü / geri"};
            for(int i=0;i<keys.Length;i++)
            {
                Place(Label("ControlKey_"+i,pauseControls.transform,keys[i],19,Mint).gameObject,36,105+i*48,130,40);
                Place(Label("ControlText_"+i,pauseControls.transform,text[i],19,Paper).gameObject,180,105+i*48,400,40);
            }
            PausePage(true); pausePanel.SetActive(false);
        }
        void PausePage(bool settings) { pauseSettings.SetActive(settings); pauseControls.SetActive(!settings); }
        void PauseButton(Transform parent,string name,string text,float y,Action action)
        {
            var go=Block(name,parent,new Color(.17f,.20f,.21f)); Place(go,16,y,228,44); Bind(go,action,false);
            Place(Label("Caption",go.transform,text,21,Surface).gameObject,15,0,208,44);
        }
        Slider PauseSlider(Transform parent,string name,string text,float y,float min,float max,out Text value)
        {
            var go=Obj(name,parent); Place(go,36,y,540,65);
            Place(Label("Caption",go.transform,text,20,Paper).gameObject,0,0,400,28);
            value=Label("Value",go.transform,"",18,Soft,TextAnchor.MiddleRight); Place(value.gameObject,450,0,90,28);
            var hit=Block("SliderHit",go.transform,Color.clear); Place(hit,0,34,540,30);
            var track=Block("Track",hit.transform,Rule); Rect(track,new Vector2(0,.5f),new Vector2(1,.5f),new Vector2(0,-2),new Vector2(0,2));
            var fill=Block("Fill",track.transform,Mint); Fill(fill);
            var area=Obj("HandleArea",hit.transform); Fill(area,5); var handle=Block("Handle",area.transform,Mint);
            var hr=Rect(handle,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero); hr.sizeDelta=new Vector2(10,0);
            var slider=hit.AddComponent<Slider>(); slider.minValue=min; slider.maxValue=max; slider.fillRect=fill.GetComponent<RectTransform>(); slider.handleRect=hr; slider.targetGraphic=handle.GetComponent<Image>();
            var nav=slider.navigation; nav.mode=Navigation.Mode.None; slider.navigation=nav; return slider;
        }
        void LoadPlayerPreferences()
        {
            float volume=Mathf.Clamp01(PlayerPrefs.GetFloat(VolumePreference,AudioListener.volume));
            float sensitivity=Mathf.Clamp(PlayerPrefs.GetFloat(SensitivityPreference,player.lookSensitivity),.04f,.25f);
            bool sway=PlayerPrefs.GetInt(SwayPreference,player.carrySway?1:0)!=0;
            AudioListener.volume=volume; player.lookSensitivity=sensitivity; player.carrySway=sway;
            volumeSlider.SetValueWithoutNotify(volume); sensitivitySlider.SetValueWithoutNotify(sensitivity); swayToggle.SetIsOnWithoutNotify(sway);
            volumeValue.text=Mathf.RoundToInt(volume*100)+"%"; sensitivityValue.text=Mathf.RoundToInt(sensitivity/.11f*100)+"%";
        }
        public void SetPaused(bool paused)
        {
            if(paused==pauseActive || (paused && (!pausePanel || IsOpen))) return; pauseActive=paused;
            if(paused)
            {
                previousTimeScale=Time.timeScale; previousAudioPause=AudioListener.pause; previousControlLock=player.InputLocked;
                pausePanel.SetActive(true); hud.SetActive(false); pausePanel.transform.SetAsLastSibling(); Time.timeScale=0; AudioListener.pause=true; player.SetCursor(true);
            }
            else
            {
                Time.timeScale=previousTimeScale; AudioListener.pause=previousAudioPause;
                if(pausePanel) pausePanel.SetActive(false); if(hud) hud.SetActive(!IsOpen); if(player) player.SetCursor(previousControlLock); PlayerPrefs.Save();
            }
        }
        void OnDisable() { if(IsPaused) SetPaused(false); }
    }
}
