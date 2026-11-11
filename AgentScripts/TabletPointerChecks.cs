using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using TMPro;

public static class TabletPointerChecks
{
    public static async Task<string> Main()
    {
        int count=0;
        void Check(bool ok,string message){if(!ok)throw new Exception("Tablet pointer: "+message);count++;}
        var controller=UnityEngine.Object.FindAnyObjectByType<PlayerTabletController>();controller.SetOpen(true);
        var view=controller.View;
        T Find<T>(string n)where T:Component=>view.GetComponentsInChildren<T>(true).First(t=>t.name==n);
        await Task.Delay(300);
        var mouse=InputSystem.AddDevice<Mouse>();
        Vector2 Point(RectTransform t)=>RectTransformUtility.WorldToScreenPoint(null,t.TransformPoint(t.rect.center));
        async Task Click(RectTransform t)
        {
            var p=Point(t);
            InputSystem.QueueStateEvent(mouse,new MouseState{position=p});InputSystem.Update();await Task.Delay(60);
            InputSystem.QueueStateEvent(mouse,new MouseState{position=p}.WithButton(MouseButton.Left));InputSystem.Update();await Task.Delay(60);
            InputSystem.QueueStateEvent(mouse,new MouseState{position=p});InputSystem.Update();await Task.Delay(60);
        }
        try
        {
            await Click(Find<RectTransform>("Nav_1"));Check(view.Page==1,"mouse reaches shop button");
            long total=view.CartTotal;await Click(Find<RectTransform>("AddFull_0"));Check(view.CartTotal>total,"mouse reaches product + button");
            await Click(Find<RectTransform>("Nav_4"));Check(view.Page==4,"mouse reaches settings button");
            await Click(Find<RectTransform>("SettingsTab_1"));
            var fps=Find<TMP_Dropdown>("Choice_fps");await Click(fps.GetComponent<RectTransform>());
            var toggles=UnityEngine.Object.FindObjectsByType<Toggle>().Where(t=>t.name.StartsWith("Item ")&&t.gameObject.activeInHierarchy).ToArray();
            Check(toggles.Length>=7,"dropdown creates seven visible choices");
            var option=toggles.First(t=>t.name.StartsWith("Item 3:"));await Click(option.GetComponent<RectTransform>());
            Check(controller.GetComponent<TabletPreferences>().Data.maxFps==120,"mouse selection applies FPS");
            await Click(Find<RectTransform>("SettingsTab_2"));var slider=Find<Slider>("Setting_master");
            var p=RectTransformUtility.WorldToScreenPoint(null,slider.transform.TransformPoint(new Vector3(420,-20,0)));
            InputSystem.QueueStateEvent(mouse,new MouseState{position=p}.WithButton(MouseButton.Left));InputSystem.Update();await Task.Delay(60);
            InputSystem.QueueStateEvent(mouse,new MouseState{position=p});InputSystem.Update();await Task.Delay(60);
            Check(controller.GetComponent<TabletPreferences>().Data.master>.7f,"mouse positions volume slider");
            return "PASS: "+count+" real Input System mouse checks (navigation, product +, dropdown and slider).";
        }
        finally{InputSystem.RemoveDevice(mouse);}
    }
}
