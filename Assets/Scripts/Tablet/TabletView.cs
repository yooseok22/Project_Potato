using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class TabletView : MonoBehaviour
{
    [SerializeField] private TabletCatalog catalog;
    [SerializeField] private Shader backdropShader;
    private PlayerTabletController owner;
    private TabletGameState state;
    private TabletPreferences preferences;
    private readonly Dictionary<string, Transform> elements = new();
    private readonly Dictionary<int, int> cart = new();
    private Material backdropMaterial;
    private Texture2D backdropTexture;
    private Texture2D mapTexture;
    private Sprite mapSprite;
    private float refreshAt, toastUntil, zoom = 1f;
    private int page, shopTab, guideCategory, guideIndex, guidePage, selectedProduct = -1, selectedResearch = -1;
    public int Page => page;
    public long CartTotal => state ? cart.Sum(p => (long)state.Price(catalog.products[p.Key]) * p.Value) : 0;
    public IReadOnlyDictionary<int, int> Cart => cart;
    public void Configure(TabletCatalog value, Shader shader) { catalog = value; backdropShader = shader; }
    private T Find<T>(string name) where T : Component => elements.TryGetValue(name, out var t) && t ? t.GetComponent<T>() : null;
    private void Show(string name, bool visible) { if (elements.TryGetValue(name, out var t) && t) t.gameObject.SetActive(visible); }
    private void Label(string name, string value) { var label = Find<TMP_Text>(name); if (label) label.text = value; }
    private void Tint(string name, Color color) { var image = Find<UnityEngine.UI.Image>(name); if (image) image.color = color; }
    private void Cache()
    {
        elements.Clear();
        foreach (var t in GetComponentsInChildren<Transform>(true)) if (!elements.ContainsKey(t.name)) elements.Add(t.name, t);
    }
    public void Open(PlayerTabletController controller, TabletGameState profile, TabletPreferences settings)
    {
        owner = controller; state = profile; preferences = settings;
        Cache();
        gameObject.SetActive(true);
        state.Changed -= Refresh; state.Changed += Refresh;
        preferences.Changed -= RefreshSettings; preferences.Changed += RefreshSettings;
        SelectPage(page);
        StartCoroutine(CaptureBackdrop());
    }
    private IEnumerator CaptureBackdrop()
    {
        var group = GetComponent<CanvasGroup>();
        group.alpha = 0f;
        yield return new WaitForEndOfFrame();
        if (!owner || !owner.IsOpen) { group.alpha = 1f; yield break; }
        if (backdropTexture) Destroy(backdropTexture);
        backdropTexture = ScreenCapture.CaptureScreenshotAsTexture();
        var image = Find<UnityEngine.UI.RawImage>("TabletBackdrop");
        if (image)
        {
            image.texture = backdropTexture;
            if (backdropShader)
            {
                if (!backdropMaterial) backdropMaterial = new Material(backdropShader);
                image.material = backdropMaterial;
            }
        }
        group.alpha = 1f;
    }
    public void Close()
    {
        if (state) state.Changed -= Refresh;
        if (preferences) { preferences.Changed -= RefreshSettings; preferences.Flush(); }
        gameObject.SetActive(false);
    }
    private void Update()
    {
        if (!owner || !owner.IsOpen) return;
        if (Time.unscaledTime >= refreshAt) { refreshAt = Time.unscaledTime + 0.5f; RefreshHome(); }
        if (Time.unscaledTime > toastUntil) Show("TabletToast", false);
        var keyboard = Keyboard.current;
        bool typing = UnityEngine.EventSystems.EventSystem.current && UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject &&
            UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>();
        if (typing || keyboard == null) return;
        if (page == 2 && keyboard.eKey.wasPressedThisFrame && selectedResearch >= 0) Research();
        if (page == 3 && keyboard.aKey.wasPressedThisFrame) TurnGuide(-1);
        if (page == 3 && keyboard.dKey.wasPressedThisFrame) TurnGuide(1);
        if (page == 2 && Mouse.current != null && Mouse.current.scroll.ReadValue().y != 0f)
            SetZoom(zoom + Mathf.Sign(Mouse.current.scroll.ReadValue().y) * 0.1f);
    }
    public void SelectPage(int value)
    {
        page = Mathf.Clamp(value, 0, 4);
        for (int i = 0; i < 5; i++)
        {
            Show("TabletPage_" + i, i == page);
            Tint("NavIcon_" + i, i == page ? Color.white : TabletUiFactory.Paper);
        }
        var badge = Find<UnityEngine.UI.Image>("ActivePageIcon");
        if (badge) badge.sprite = catalog.Art(new[] { "Tablet_UI_28", "Tablet_UI_29", "Tablet_UI_30", "Tablet_UI_32", "Tablet_UI_31" }[page]);
        Refresh();
    }
    public void Action(string action, int index)
    {
        switch (action)
        {
            case "nav": SelectPage(index); break;
            case "close": owner.SetOpen(false); break;
            case "shopTab": shopTab = index; RefreshShop(); break;
            case "selectProduct": selectedProduct = index; RefreshShop(); break;
            case "add": selectedProduct = index; cart[index] = Mathf.Min(999, (cart.TryGetValue(index, out int q) ? q : 0) + 1); RefreshShop(); break;
            case "subtract": if (cart.TryGetValue(index, out int count)) { if (count <= 1) cart.Remove(index); else cart[index] = count - 1; } RefreshShop(); break;
            case "clear": cart.Clear(); RefreshShop(); break;
            case "buy":
                if (state.TryPurchase(cart, out string error)) { cart.Clear(); Toast("구매가 완료되었습니다."); }
                else Toast(error);
                RefreshShop(); break;
            case "researchSelect": selectedResearch = index; RefreshResearch(); break;
            case "research": Research(); break;
            case "researchDismiss": selectedResearch = -1; RefreshResearch(); break;
            case "zoom": SetZoom(zoom + index * 0.1f); break;
            case "guideTab":
                guideCategory = index; guideIndex = Array.FindIndex(catalog.guides, g => g.category == index); guidePage = 0; RefreshGuide(); break;
            case "guide": guideIndex = index; guidePage = 0; RefreshGuide(); break;
            case "guidePage": TurnGuide(index); break;
            case "settingsTab": for (int i = 0; i < 3; i++) { Show("SettingsPage_" + i, i == index); Tint("SettingsTab_" + i, i == index ? TabletUiFactory.Brown : TabletUiFactory.DarkBrown); } RefreshSettings(); break;
            case "shake": preferences.Set("shake", 0); break;
            case "vsync": preferences.Set("vsync", 0); break;
            case "map": var p = owner.transform.position; Toast($"현재 위치  X {p.x:0}  ·  Z {p.z:0}"); break;
            case "stocks": Toast("관심 종목 PBCO · 218.5 $  (+0.52%)"); break;
        }
    }
    public void Value(string action, float value) { preferences?.Set(action, value); }
    public void Text(string action, string value)
    {
        if (action == "farmName" && state && !state.Rename(value, out string error)) { Toast(error); RefreshHome(); }
    }
    private void Research()
    {
        if (selectedResearch < 0) return;
        if (state.TryResearch(selectedResearch, out string error)) Toast("연구를 완료했습니다."); else Toast(error);
        RefreshResearch();
    }
    private void SetZoom(float value)
    {
        zoom = Mathf.Clamp(value, 0.65f, 1.6f);
        var content = Find<RectTransform>("ResearchContent");
        if (content) content.localScale = Vector3.one * zoom;
    }
    private void TurnGuide(int offset) { guidePage = Mathf.Clamp(guidePage + offset, 0, catalog.guides[guideIndex].pages.Length - 1); RefreshGuide(); }
    private void Toast(string message) { Label("ToastText", message); toastUntil = Time.unscaledTime + 3f; Show("TabletToast", true); }
    private void Refresh()
    {
        if (!state || !catalog) return;
        RefreshHome(); RefreshShop(); RefreshResearch(); RefreshGuide(); RefreshSettings();
    }
    private void RefreshHome()
    {
        if (!state) return;
        var clock = FindAnyObjectByType<WorldClock>();
        Label("TabletTime", clock ? $"{(int)clock.HourOfDay:00}:{(int)(clock.HourOfDay * 60) % 60:00}   DAY {clock.DayNumber}" : "00:00   DAY 1");
        Label("HomeDay", clock ? clock.DayNumber.ToString() : "1");
        Label("HomeMoney", state.Data.money.ToString("N0")); Label("ShopMoney", state.Data.money.ToString("N0") + " M");
        Label("HomeYield", state.Data.totalHarvested.ToString("N0")); Label("HomeLevel", "농장 등급 Lv." + state.Level);
        Label("WeatherName", state.Data.weather.ToUpperInvariant());
        var weather = Find<UnityEngine.UI.Image>("HomeWeather");
        if (weather) weather.sprite = catalog.Art("Tablet_UI_" + state.Data.weather + (state.Data.weather == "Thunder" ? "" : "_1"));
        var name = Find<TMP_InputField>("FarmNameInput");
        if (name && !name.isFocused) name.SetTextWithoutNotify(state.Data.farmName);
        var systems = FindObjectsByType<PotatoFarmingSystem>();
        int growing = systems.Sum(f => f.GetFarmingTiles().Count(c => !c.IsDead));
        int capacity = FindObjectsByType<GroundGrid>().Sum(g => g.Width * g.Depth);
        float area = FindObjectsByType<GroundGrid>().Sum(g => g.Width * g.Depth * g.CellSize * g.CellSize);
        Label("FieldProduction", growing.ToString("N0") + "개 재배 중");
        Label("FieldSize", $"{growing:N0}/{capacity:N0}칸 · {area:N0}㎡");
        var processors = FindObjectsByType<PotatoStarchProcessor>();
        Label("FactoryProduction", processors.Sum(p => p.TotalProducedPackCount).ToString("N0") + "팩 생산");
        Label("FactorySales", "0 M / 1min");
        Label("ResearchSummary", state.Data.unlocked.Count + " / " + catalog.research.Length + " 완료");
        Label("HarvestSummary", "누적 수확  " + state.Data.totalHarvested.ToString("N0") + "개");
        var marker = Find<RectTransform>("MapPlayer");
        var grid = FindAnyObjectByType<GroundGrid>();
        if (marker && grid && owner)
        {
            RefreshMap(grid);
            var p = owner.transform.position;
            var plot = Find<RectTransform>("MapPlot");
            float width = plot ? plot.rect.width : 290, height = plot ? plot.rect.height : 286;
            marker.anchoredPosition = new Vector2((Mathf.Clamp01((p.x - grid.Origin.x) / (grid.Width * grid.CellSize)) - .5f) * width,
                (Mathf.Clamp01((p.z - grid.Origin.z) / (grid.Depth * grid.CellSize)) - .5f) * height);
        }
    }
    private void RefreshMap(GroundGrid grid)
    {
        if (!mapTexture || mapTexture.width != grid.Width || mapTexture.height != grid.Depth)
        {
            if(mapSprite) Destroy(mapSprite); if(mapTexture) Destroy(mapTexture);
            mapTexture = new Texture2D(grid.Width, grid.Depth, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            mapSprite = Sprite.Create(mapTexture, new Rect(0,0,grid.Width,grid.Depth), Vector2.one * .5f, 1);
            var image = Find<UnityEngine.UI.Image>("MapPlot");
            if(image) { image.sprite = mapSprite; image.type = UnityEngine.UI.Image.Type.Simple; image.color = Color.white; }
            for(int x=0;x<6;x++)for(int y=0;y<5;y++)Show("MapTile_"+x+"_"+y,false);
        }
        var colors = new Color32[grid.Width*grid.Depth];
        for(int y=0;y<grid.Depth;y++)for(int x=0;x<grid.Width;x++)
        {
            var cell=grid.GetCellState(new Vector2Int(x,y));
            colors[y*grid.Width+x]=cell==GroundCellState.BasicSoil?new Color32(126,145,93,255):
                cell==GroundCellState.FieldSoil||cell==GroundCellState.PlantingHole?new Color32(133,91,61,255):
                cell==GroundCellState.BoarDugField?new Color32(83,69,54,255):new Color32(73,111,53,255);
        }
        var world=AutomationWorld.Instance;
        if(world)foreach(var block in world.Blocks)
        {
            if(!block)continue;var cell=grid.WorldToCell(block.transform.position);
            if(grid.ContainsCell(cell))colors[cell.y*grid.Width+cell.x]=new Color32(220,190,142,255);
        }
        mapTexture.SetPixels32(colors);mapTexture.Apply(false);
    }
    private void RefreshShop()
    {
        if (!state || !catalog) return;
        for (int i = 0; i < catalog.products.Length; i++)
        {
            var item = catalog.products[i];
            Show("Product_" + i, item.machine == (shopTab == 1));
            int count = cart.TryGetValue(i, out int q) ? q : 0;
            Label("ProductPrice_" + i, state.Price(item).ToString("N0") + " M");
            Label("ProductOwned_" + i, "보유 " + state.Owned(item.grantId));
            Label("Quantity_" + i, count == 0 ? "+" : count.ToString());
            Show("Subtract_" + i, count > 0); Show("AddRight_" + i, count > 0); Show("AddFull_" + i, count == 0);
            Tint("Product_" + i, i == selectedProduct ? TabletUiFactory.Paper : TabletUiFactory.Tan);
        }
        Label("CartTotal", CartTotal.ToString("N0") + " M");
        var buy = Find<UnityEngine.UI.Button>("BuyButton");
        if (buy) buy.interactable = cart.Count > 0 && CartTotal <= state.Data.money;
        Show("ProductInfoIcon", selectedProduct < 0);
        Show("ProductDetail", selectedProduct >= 0);
        if (selectedProduct >= 0)
        {
            var item = catalog.products[selectedProduct];
            var icon = Find<UnityEngine.UI.Image>("DetailIcon");
            if (icon) icon.sprite = catalog.Art(item.machine ? "Tablet_UI_31" : item.grantId == "seed" ? "Tablet_UI_63" : "Tablet_UI_29");
            Label("DetailTitle", item.title); Label("DetailPrice", state.Price(item).ToString("N0") + " M");
            Label("DetailDescription", item.description + "\n\n보유 수량  " + state.Owned(item.grantId));
        }
        Tint("ShopTab_0", shopTab == 0 ? TabletUiFactory.Brown : TabletUiFactory.DarkBrown);
        Tint("ShopTab_1", shopTab == 1 ? TabletUiFactory.Brown : TabletUiFactory.DarkBrown);
    }
    private void RefreshResearch()
    {
        if (!state || !catalog) return;
        Label("ResearchPoints", state.Data.researchPoints + " R");
        Label("ResearchLevel", state.Level.ToString());
        var fill = Find<UnityEngine.UI.Image>("ResearchExperienceFill");
        if (fill) fill.fillAmount = (state.Data.experience % 100) / 100f;
        for (int i = 0; i < catalog.research.Length; i++)
        {
            bool unlocked = state.IsUnlocked(i);
            Tint("ResearchNode_" + i, unlocked ? TabletUiFactory.Paper : TabletUiFactory.DarkBrown);
            Tint("ResearchPotato_" + i, unlocked ? TabletUiFactory.DarkBrown : TabletUiFactory.Brown);
            Show("ResearchSelected_" + i, i == selectedResearch);
        }
        Show("ResearchTooltip", selectedResearch >= 0);
        if (selectedResearch < 0) return;
        var research = catalog.research[selectedResearch];
        Label("ResearchTitle", research.title);
        Label("ResearchDescription", research.description + "\n\n연구 비용  " + research.cost + " R");
        Label("ResearchButtonLabel", state.IsUnlocked(selectedResearch) ? "연구 완료" : "[E]  연구하기");
        var button = Find<UnityEngine.UI.Button>("ResearchButton");
        if (button) button.interactable = state.CanResearch(selectedResearch);
    }
    private void RefreshGuide()
    {
        if (!catalog || catalog.guides == null || catalog.guides.Length == 0) return;
        for (int i = 0; i < catalog.guides.Length; i++)
        {
            Show("GuideItem_" + i, catalog.guides[i].category == guideCategory);
            Tint("GuideItem_" + i, i == guideIndex ? TabletUiFactory.Cream : TabletUiFactory.Tan);
            var label = Find<TMP_Text>("GuideItem_" + i + "Label");
            if (label) label.color = i == guideIndex ? TabletUiFactory.Brown : TabletUiFactory.Cream;
        }
        var guide = catalog.guides[guideIndex];
        Label("GuideTitle", guide.title);
        Label("GuideIntro", guideIndex == 0 ? "감자는 다음과 같이 재배할 수 있다." : "");
        Label("GuideBody", guide.pages[guidePage].Replace("감자는 다음과 같이 재배할 수 있다.\n\n", ""));
        Show("GuidePicture", guideIndex == 0);
        for (int i = 0; i < 6; i++) { Show("GuideDot_" + i, i < guide.pages.Length); Tint("GuideDot_" + i, i == guidePage ? TabletUiFactory.Cream : TabletUiFactory.Brown); }
        for (int i = 0; i < 3; i++) Tint("GuideTab_" + i, i == guideCategory ? TabletUiFactory.Brown : TabletUiFactory.DarkBrown);
        var previous = Find<UnityEngine.UI.Button>("GuidePrevious"); var next = Find<UnityEngine.UI.Button>("GuideNext");
        if (previous) previous.interactable = guidePage > 0;
        if (next) next.interactable = guidePage + 1 < guide.pages.Length;
    }
    private void RefreshSettings()
    {
        if (!preferences) return;
        var data = preferences.Data;
        SetSlider("cameraSensitivity", data.cameraSensitivity, "0.00"); SetSlider("mouseSensitivity", data.mouseSensitivity, "0.00");
        SetSlider("fov", data.fov, "0"); SetSlider("master", data.master, "percent"); SetSlider("music", data.music, "percent");
        SetSlider("effects", data.effects, "percent"); SetSlider("ambience", data.ambience, "percent"); SetSlider("microphoneVolume", data.microphoneVolume, "percent");
        Label("ShakeValue", data.cameraShake ? "ON" : "OFF"); Label("VsyncValue", data.vSync ? "ON" : "OFF");
        SetChoice("window", data.windowMode);
        SetChoice("fps", Array.IndexOf(new[] { 30, 60, 90, 120, 144, 240, 360 }, data.maxFps));
        var resolutions = TabletPreferences.Resolutions;
        var resolution = Find<TMP_Dropdown>("Choice_resolution");
        if (resolution) { resolution.options = resolutions.Select(r => new TMP_Dropdown.OptionData(r.x + " × " + r.y)).ToList(); resolution.SetValueWithoutNotify(Mathf.Max(0, Array.FindIndex(resolutions, r => r.x == data.width && r.y == data.height))); resolution.RefreshShownValue(); }
        var microphone = Find<TMP_Dropdown>("Choice_microphone");
        if (microphone)
        {
            string[] devices = preferences.Microphones;
            microphone.options = (devices.Length == 0 ? new[] { "장치 없음" } : devices).Select(d => new TMP_Dropdown.OptionData(d)).ToList();
            microphone.SetValueWithoutNotify(Mathf.Max(0, Array.IndexOf(devices, data.microphone))); microphone.RefreshShownValue(); microphone.interactable = devices.Length > 0;
        }
        SetChoice("microphoneMode", data.microphoneMode);
    }
    private void SetSlider(string name, float value, string format)
    {
        var slider = Find<UnityEngine.UI.Slider>("Setting_" + name);
        if (slider) slider.SetValueWithoutNotify(value);
        Label("Value_" + name, format == "percent" ? Mathf.RoundToInt(value * 100).ToString() : value.ToString(format));
    }
    private void SetChoice(string name, int index) { var choice = Find<TMP_Dropdown>("Choice_" + name); if (choice) choice.SetValueWithoutNotify(Mathf.Max(0, index)); }
    private void OnDestroy() { if (backdropTexture) Destroy(backdropTexture); if (backdropMaterial) Destroy(backdropMaterial); if(mapSprite)Destroy(mapSprite);if(mapTexture)Destroy(mapTexture); }
}
