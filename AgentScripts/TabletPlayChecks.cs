using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using TMPro;

/// <summary>Run in a disposable Play session. All disk writes go to workspace Temp.</summary>
public static class TabletPlayChecks
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    public static async Task<string> Main()
    {
        if(!Application.isPlaying)throw new Exception("Play mode required");
        await Task.Delay(200);
        int checks=0;
        void Check(bool ok,string message){if(!ok)throw new Exception("Tablet check: "+message);checks++;}
        var save=SaveGameController.Instance;
        var slots=new SaveSlotService(Path.GetFullPath("Temp/TabletPlayChecks/"+DateTime.UtcNow.Ticks));
        slots.Register(new FarmingSaveSection());slots.Register(save.Inventories);
        typeof(SaveGameController).GetField("<Slots>k__BackingField",Flags).SetValue(save,slots);
        var controller=UnityEngine.Object.FindAnyObjectByType<PlayerTabletController>();
        var player=controller.GetComponent<FirstPersonPlayerController>();
        var state=controller.GetComponent<TabletGameState>();
        var prefs=controller.GetComponent<TabletPreferences>();
        prefs.PathOverride=Path.GetFullPath("Temp/TabletPlayChecks/settings-"+DateTime.UtcNow.Ticks+".json");
        var initial=state.CaptureInventory();
        state.RestoreInventory(new InventoryData{ownerId=state.OwnerId});
        var keyboard=InputSystem.AddDevice<Keyboard>();
        void Keys(params Key[] keys){InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));InputSystem.Update();}
        void Press(Key key,object target){Keys(key);target.GetType().GetMethod("Update",Flags).Invoke(target,null);Keys();}
        try
        {
            controller.SetOpen(false); Press(Key.Tab,controller);
            Check(controller.IsOpen&&controller.View,"Tab opens actual tablet");
            Check(player.GetComponent<PlayerActionBlocker>().GameplayBlocked&&Cursor.lockState==CursorLockMode.None,"opening blocks gameplay and releases pointer");
            var view=controller.View;
            T Find<T>(string n)where T:Component=>view.GetComponentsInChildren<T>(true).First(t=>t.name==n);
            void Click(string n)=>Find<Button>(n).onClick.Invoke();
            for(int i=0;i<5;i++){Click("Nav_"+i);Check(view.Page==i,"navigation "+i);}
            Click("Nav_1");Click("AddFull_0");Click("AddRight_0");Click("Subtract_0");
            Check(view.CartTotal==100&&view.Cart[0]==1,"cart plus/minus updates amount");
            Click("ClearCart");Check(view.Cart.Count==0&&view.CartTotal==0,"clear cart");
            Check(!state.TryPurchase(new Dictionary<int,int>{{0,999}},out _),"insufficient funds rejected");
            Check(!state.TryPurchase(new Dictionary<int,int>{{-1,1}},out _),"invalid product rejected");
            Check(!state.TryPurchase(new Dictionary<int,int>{{0,-1}},out _),"invalid quantity rejected");
            long money=state.Data.money;int seed=state.Owned("seed");
            Click("AddFull_0");Click("BuyButton");
            Check(state.Data.money==money-100&&state.Owned("seed")==seed+1&&view.Cart.Count==0,"purchase updates money, stock and clears cart");
            var purchased=state.CaptureInventory();
            state.SaveOverride=()=>false;
            Check(!state.TryPurchase(new Dictionary<int,int>{{1,1}},out _)&&state.Data.money==money-100&&state.Owned("wrapper")==0,"purchase rolls back on save failure");
            Check(!state.TryResearch(0,out _)&&state.Tier(0)==0&&state.Data.researchPoints==100,"research rolls back on save failure");
            Check(!state.Rename("롤백 테스트",out _)&&state.Data.farmName=="","rename rolls back on save failure");
            state.SaveOverride=null;
            Check(!state.TryResearch(2,out _)&&!state.TryResearch(-1,out _),"research prerequisite and invalid index rejected");
            Click("Nav_2");Click("ResearchNode_0");Press(Key.E,view);
            Check(state.IsUnlocked(0)&&state.Tier(0)==1&&state.Data.researchPoints==80,"E research unlocks growth tier");
            var farm=UnityEngine.Object.FindAnyObjectByType<PotatoFarmingSystem>();
            var ground=UnityEngine.Object.FindAnyObjectByType<GroundGrid>();
            var clock=UnityEngine.Object.FindAnyObjectByType<WorldClock>();clock.IsPaused=true;
            var cell=new Vector2Int(45,45);farm.StopFarming(ground,cell);ground.TrySetCellState(cell,GroundCellState.PlantingHole);
            int beforeSeed=state.Owned("seed");Check(state.TryPlant(farm,ground,cell,out _),"purchased seed can be planted");
            Check(state.Owned("seed")==beforeSeed-1,"plant consumes one seed");
            farm.TryWater(ground,cell);farm.TryGetFarmingTile(ground,cell,out var crop);
            double age=crop.AgeSeconds;clock.AdvanceBy(20);Check(Math.Abs(crop.AgeSeconds-age-25)<.01,"growth research changes actual growth rate");
            age=crop.AgeSeconds;Check(state.TryResearch(1,out _),"second growth tier");Check(Math.Abs(crop.AgeSeconds-age)<.01,"research preserves existing growth age");
            clock.AdvanceBy(10);Check(Math.Abs(crop.AgeSeconds-age-15)<.01,"second tier rate is 1.5");
            var failCell=new Vector2Int(44,45);farm.StopFarming(ground,failCell);ground.TrySetCellState(failCell,GroundCellState.PlantingHole);
            state.SaveOverride=()=>false;beforeSeed=state.Owned("seed");
            Check(!state.TryPlant(farm,ground,failCell,out _)&&!farm.TryGetFarmingTile(ground,failCell,out _)&&state.Owned("seed")==beforeSeed,"plant rollback restores crop and seed");state.SaveOverride=null;
            int harvest=state.Data.totalHarvested;Check(state.RecordHarvestAndSave(2)&&state.Data.totalHarvested==harvest+2&&state.Data.experience==10,"harvest persists yield, XP and research points");
            state.SaveOverride=()=>false;Check(!state.RecordHarvestAndSave(2)&&state.Data.totalHarvested==harvest+2,"harvest stats rollback");state.SaveOverride=null;
            Check(state.TryResearch(9,out _)&&state.Price(state.Catalog.products[0])==95,"discount research affects prices");
            Check(state.Rename("감자 농장",out _),"farm rename");
            Check(save.Save(1),"slot one writes");string expected=JsonUtility.ToJson(state.Data);
            Check(save.Save(2)&&state.Rename("두 번째 농장",out _),"slot two writes");
            Check(save.Load(1)&&JsonUtility.ToJson(state.Data)==expected,"slot one round trip restores complete tablet profile");
            Check(save.Load(2)&&state.Data.farmName=="두 번째 농장","slot two isolation");
            Check(state.TryPurchase(new Dictionary<int,int>{{1,2},{12,1}},out _)&&state.Owned("wrapper")==2&&state.Owned("machine-0")==1,"wrapper and machine purchases grant saved inventory");
            Check(state.TryConsume("wrapper",1,out _)&&state.Owned("wrapper")==1,"wrapper consumption");
            Check(!state.TryConsume("wrapper",2,out _)&&state.Owned("wrapper")==1,"stock cannot become negative");
            Click("Nav_3");Click("GuideNext");Check(Find<TMP_Text>("GuideBody").text.StartsWith("2."),"guide next page");
            Press(Key.A,view);Check(Find<TMP_Text>("GuideBody").text.StartsWith("1."),"guide A shortcut");
            Click("GuideTab_1");Check(Find<TMP_Text>("GuideTitle").text=="감자 전분 만드는 법","processing guide category");
            Click("GuideTab_2");Check(Find<TMP_Text>("GuideTitle").text=="전기와 물 연결","automation guide category");
            Click("Nav_4");Find<Slider>("Setting_mouseSensitivity").value=1.5f;Check(Math.Abs(prefs.Data.mouseSensitivity-1.5f)<.001,"slider changes sensitivity");
            Click("CameraShake");Check(!prefs.Data.cameraShake&&!player.GetComponent<PlayerVisualController>().MotionEffectsEnabled,"camera shake toggle applied");
            Click("SettingsTab_1");Find<TMP_Dropdown>("Choice_fps").value=1;Check(prefs.Data.maxFps==60&&Application.targetFrameRate==60,"FPS dropdown applies");
            Find<Slider>("Setting_fov").value=75;Check(Math.Abs(player.ViewCamera.GetComponent<Camera>().fieldOfView-75)<.01,"FOV slider applies");
            Click("SettingsTab_2");Find<Slider>("Setting_master").value=.6f;Check(Math.Abs(AudioListener.volume-.6f)<.001,"master volume applies");
            Find<TMP_Dropdown>("Choice_microphoneMode").value=2;Check(prefs.Data.microphoneMode==2,"mute mode retained");
            Check(prefs.Flush()&&File.Exists(prefs.PathOverride),"preferences persisted to separate local JSON");
            var loaded=JsonUtility.FromJson<TabletPreferences.Values>(File.ReadAllText(prefs.PathOverride));Check(loaded.maxFps==60&&loaded.microphoneMode==2&&Math.Abs(loaded.master-.6f)<.001,"preferences disk round trip");
            var hud=UnityEngine.Object.FindAnyObjectByType<BasketFillHud>();
            for(int count=0;count<=10;count++){hud.Refresh(count,10);int stage=count==10?3:count>=7?2:count>=5?1:0;Check(hud.DisplayedStage==stage,"basket count "+count);}
            for(int i=0;i<5;i++){view.SelectPage(i);foreach(var t in view.GetComponentsInChildren<TMP_Text>(false)){t.ForceMeshUpdate();Check(!t.isTextOverflowing||t.overflowMode==TextOverflowModes.Ellipsis||t.name.StartsWith("Item "),"text fits: "+t.name);}}
            Press(Key.Escape,controller);Check(!controller.IsOpen&&!player.GetComponent<PlayerActionBlocker>().GameplayBlocked,"Escape closes and restores gameplay");
            Check(Cursor.lockState==CursorLockMode.Locked||!Application.isFocused,"closing restores cursor capture");
            state.RestoreInventory(new InventoryData{ownerId=state.OwnerId});Check(state.Data.money==1000&&state.Tier(0)==0&&state.Owned("seed")==100,"old save without tablet gets defaults");
            return "PASS: "+checks+" tablet play checks. Isolated save slots, rollback, research growth, input, inventory, settings and all basket thresholds.";
        }
        finally{Keys();InputSystem.RemoveDevice(keyboard);controller.SetOpen(false);state.SaveOverride=null;AudioListener.volume=.8f;}
    }
}
