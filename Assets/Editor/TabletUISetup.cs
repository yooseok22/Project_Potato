using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using static TabletUiFactory;

/// <summary>Authors real uGUI assets. Rebuilding the tablet is explicit; Configure only updates references.</summary>
public static class TabletUISetup
{
    private const string Folder = "Assets/UI/Tablet";
    private static TabletCatalog cat;
    private static TMP_FontAsset font;
    private static Sprite rounded, circle;

    [MenuItem("Tools/Project Potato/Build Tablet UI")]
    public static void Build()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop Play mode first.");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/UI", "Tablet");
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Resources/Fonts/MalgunGothic-Dynamic.asset");
        rounded = Shape("Rounded", false); circle = Shape("Circle", true);
        cat = AssetDatabase.LoadAssetAtPath<TabletCatalog>(Folder + "/TabletCatalog.asset");
        if (!cat) { cat = ScriptableObject.CreateInstance<TabletCatalog>(); TabletCatalog.SetDefaults(cat); AssetDatabase.CreateAsset(cat, Folder + "/TabletCatalog.asset"); }
        cat.artwork = AssetDatabase.LoadAllAssetsAtPath("Assets/UI/Tablet_UI.png").OfType<Sprite>().ToArray();
        cat.roundedPanel = rounded; cat.roundIcon = circle; cat.checker = Checker();
        EditorUtility.SetDirty(cat);
        var root = new GameObject("TabletUI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup), typeof(TabletView));
        var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 100;
        var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        root.GetComponent<TabletView>().Configure(cat, Shader.Find("ProjectPotato/TabletBackdrop"));
        var bg = Box(root.transform, "TabletBackdrop", 0, 0, 1920, 1080, Color.white, false); Object.DestroyImmediate(bg.GetComponent<Image>()); bg.gameObject.AddComponent<RawImage>().raycastTarget = true;
        var frame = Box(root.transform, "TabletFrame", 295, 112, 1330, 850, Color.black);
        var screen = Box(frame, "TabletScreen", 49, 49, 1232, 752, DarkBrown); screen.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        var status = Box(screen, "StatusBar", 0, 0, 1232, 34, new Color32(113, 113, 113, 255), false);
        Text(status, "TabletTime", "00:00   DAY 1", 16, 3, 480, 28, 18, Color.white);
        Icon(status, "Wifi", "Tablet_UI_6", 1100, 5, 29, 24, Color.white);
        Icon(status, "Signal", "Tablet_UI_15", 1138, 5, 29, 24, Color.white);
        Icon(status, "Battery", "Tablet_UI_9", 1178, 6, 40, 22, Color.white);
        var side = Box(screen, "Sidebar", 0, 34, 128, 718, Brown, false);
        string[] nav = { "Tablet_UI_28", "Tablet_UI_29", "Tablet_UI_30", "Tablet_UI_32", "Tablet_UI_31" };
        string[] navLabels = { "홈", "상점", "연구", "가이드", "설정" };
        for (int i = 0; i < 5; i++)
        {
            var b = Button(side, "Nav_" + i, "", 10, 9 + i * 140, 108, 128, new Color(0,0,0,0), "nav", i);
            Icon(b, "NavIcon_" + i, nav[i], 28, 20, 56, 62, Paper);
            Text(b, "NavLabel_" + i, navLabels[i], 0, 90, 108, 25, 18, Paper, TextAlignmentOptions.Center);
        }
        Button(frame, "CloseTablet", "×", 1279, 9, 37, 37, DarkBrown, "close");
        var badge = Box(root.transform, "PageBadge", 65, 81, 159, 159, new Color32(241, 145, 41, 255));
        Box(badge, "BadgeInner", 5, 5, 149, 149, DarkBrown);
        Icon(badge, "ActivePageIcon", nav[0], 32, 31, 95, 95, Cream);
        for (int i = 0; i < 5; i++)
        {
            var p = Box(screen, "TabletPage_" + i, 128, 34, 1104, 718, i == 2 ? Brown : Paper, false);
            switch(i) { case 0: Home(p); break; case 1: Shop(p); break; case 2: Research(p); break; case 3: Guide(p); break; case 4: Settings(p); break; }
            p.gameObject.SetActive(i == 0);
        }
        var toast = Box(root.transform, "TabletToast", 610, 977, 850, 63, DarkBrown);
        Text(toast, "ToastText", "", 18, 7, 814, 49, 23, Cream, TextAlignmentOptions.Center); toast.gameObject.SetActive(false);
        PolishHome(root);
        FitText(root);
        root.SetActive(false);
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, Folder + "/TabletUI.prefab");
        Object.DestroyImmediate(root);
        Configure(prefab.GetComponent<TabletView>());
        AssetDatabase.SaveAssets();
    }
    public static void Configure(TabletView prefab = null)
    {
        cat = AssetDatabase.LoadAssetAtPath<TabletCatalog>(Folder + "/TabletCatalog.asset");
        if (!prefab) prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/TabletUI.prefab").GetComponent<TabletView>();
        var player = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Player.prefab");
        ConnectPlayer(player, prefab);
        PrefabUtility.SaveAsPrefabAsset(player, "Assets/Prefabs/Player.prefab"); PrefabUtility.UnloadPrefabContents(player);
        foreach (var p in Object.FindObjectsByType<FirstPersonPlayerController>(FindObjectsSortMode.None)) ConnectPlayer(p.gameObject, prefab);
        var hud = PrefabUtility.LoadPrefabContents("Assets/UI/Prefabs/PotatoHUD_Design.prefab");
        ConnectBasket(hud); PrefabUtility.SaveAsPrefabAsset(hud, "Assets/UI/Prefabs/PotatoHUD_Design.prefab"); PrefabUtility.UnloadPrefabContents(hud);
        foreach (var h in Object.FindObjectsByType<PlayerHudNavigation>(FindObjectsSortMode.None)) ConnectBasket(h.gameObject);
        if (!Object.FindAnyObjectByType<EventSystem>())
        { var e = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)); e.GetComponent<InputSystemUIInputModule>().AssignDefaultActions(); }
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
    }
    private static void ConnectPlayer(GameObject p, TabletView prefab)
    {
        var state = p.GetComponent<TabletGameState>() ?? p.AddComponent<TabletGameState>(); state.Configure(cat);
        if (!p.GetComponent<TabletPreferences>()) p.AddComponent<TabletPreferences>();
        var controller = p.GetComponent<PlayerTabletController>() ?? p.AddComponent<PlayerTabletController>(); controller.Configure(prefab);
        EditorUtility.SetDirty(state); EditorUtility.SetDirty(controller); PrefabUtility.RecordPrefabInstancePropertyModifications(state); PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
    }
    private static void FitText(GameObject root)
    {
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (t.font && t.rectTransform.rect.height > 0)
                t.fontSize = Mathf.Min(t.fontSize, t.rectTransform.rect.height * t.font.faceInfo.pointSize / t.font.faceInfo.lineHeight * .98f);
            t.ForceMeshUpdate(true);
            if (t.preferredHeight > t.rectTransform.rect.height && t.rectTransform.rect.height > 0)
                t.fontSize *= t.rectTransform.rect.height / t.preferredHeight * .98f;
            t.enableAutoSizing = false;
        }
    }
    public static void PolishExisting()
    {
        cat=AssetDatabase.LoadAssetAtPath<TabletCatalog>(Folder+"/TabletCatalog.asset");rounded=cat.roundedPanel;circle=cat.roundIcon;
        font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Resources/Fonts/MalgunGothic-Dynamic.asset");
        var root=PrefabUtility.LoadPrefabContents(Folder+"/TabletUI.prefab");PolishHome(root);FitText(root);
        PrefabUtility.SaveAsPrefabAsset(root,Folder+"/TabletUI.prefab");PrefabUtility.UnloadPrefabContents(root);AssetDatabase.SaveAssets();
    }
    private static void PolishHome(GameObject root)
    {
        Transform Find(string n)=>root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==n);
        root.GetComponent<CanvasScaler>().screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        var frame=Find("TabletFrame").GetComponent<RectTransform>();frame.anchorMin=frame.anchorMax=frame.pivot=Vector2.one*.5f;frame.anchoredPosition=new Vector2(0,3);
        var backdrop=Find("TabletBackdrop").GetComponent<RectTransform>();backdrop.anchorMin=Vector2.zero;backdrop.anchorMax=Vector2.one;backdrop.offsetMin=backdrop.offsetMax=Vector2.zero;
        var badge=Find("PageBadge").GetComponent<RectTransform>();badge.SetParent(frame,false);Position(badge,-230,-31,159,159);
        void SetText(string n,float x,float y,float w,float h,float size,TextAlignmentOptions align=TextAlignmentOptions.MidlineLeft)
        {var t=Find(n).GetComponent<TMP_Text>();Position(t.rectTransform,x,y,w,h);t.fontSize=size;t.alignment=align;}
        for(int i=0;i<5;i++)if(Find("NavLabel_"+i))Find("NavLabel_"+i).gameObject.SetActive(false);
        for(int i=0;i<5;i++)Position(Find("NavIcon_"+i).GetComponent<RectTransform>(),28,33,56,62);
        var status=Find("StatusBar"); if(Find("Signal"))Find("Signal").gameObject.SetActive(false);
        if(!Find("SignalBar_0"))for(int i=0;i<3;i++)Box(status,"SignalBar_"+i,1138+i*6,22-i*6,4,7+i*6,Color.white,false);
        SetText("CardTitle_0",4,5,142,35,25,TextAlignmentOptions.Center);Find("CardTitle_0").GetComponent<TMP_Text>().fontStyle=FontStyles.Bold;
        SetText("HomeDay",5,44,140,98,70,TextAlignmentOptions.Center);
        for(int i=2;i<4;i++)
        {
            SetText("CardTitle_"+i,4,47,142,38,25,TextAlignmentOptions.Center);Find("CardTitle_"+i).GetComponent<TMP_Text>().fontStyle=FontStyles.Bold;
            SetText(i==2?"HomeMoney":"HomeYield",5,83,140,55,34,TextAlignmentOptions.Center);
            if(!Find("HomeCardIcon_"+i))Icon(Find("HomeCard_"+i),"HomeCardIcon_"+i,i==2?"Tablet_UI_58":"Tablet_UI_63",103,13,31,31,Brown);
        }
        var avatar=Find("FarmOwnerPortrait");avatar.GetComponent<Image>().color=new Color32(68,68,68,255);
        if(!Find("OwnerHeader")){var header=Box(avatar,"OwnerHeader",0,0,222,39,DarkBrown);Text(header,"OwnerHeaderLabel","주  인",12,0,198,39,22,Cream,TextAlignmentOptions.Center);var dot=Box(header,"OwnerOnline",7,7,25,25,new Color32(255,158,0,255));dot.GetComponent<Image>().sprite=circle;}
        Position(Find("OwnerHead").GetComponent<RectTransform>(),76,64,73,86);Position(Find("OwnerBody").GetComponent<RectTransform>(),37,164,148,138);
        Find("OwnerHead").GetComponent<Image>().color=Cream;Find("OwnerBody").GetComponent<Image>().color=Cream;Find("OwnerName").gameObject.SetActive(false);
        for(int i=0;i<3;i++)
        {
            var stat=Find("FarmStat_"+i);
            if(!Find("StatIconPanel_"+i)){var b=Box(stat,"StatIconPanel_"+i,6,6,95,84,Brown);Icon(b,"StatIcon_"+i,i==2?"Tablet_UI_30":i==0?"Tablet_UI_63":"Tablet_UI_31",22,7,50,48,Tan);Text(b,"StatIconLabel_"+i,i==0?"밭 상태":i==1?"공장 상태":"연구",4,59,87,23,15,Tan,TextAlignmentOptions.Center);}
            SetText("StatTitle_"+i,115,4,270,25,17);Find("StatTitle_"+i).GetComponent<TMP_Text>().text=i==2?"연구 진행":"생산량";
        }
        SetText("FieldProduction",115,27,270,35,25,TextAlignmentOptions.Right);SetText("FieldSize",115,66,270,24,15,TextAlignmentOptions.Right);
        SetText("FactoryProduction",115,27,270,35,25,TextAlignmentOptions.Right);SetText("FactorySales",115,66,270,24,15,TextAlignmentOptions.Right);
        Find("FarmStatus").gameObject.SetActive(false);
        if(!Find("ResearchSummary")){Text(Find("FarmStat_2"),"ResearchSummary","0 / 15 완료",115,27,270,35,25,Brown,TextAlignmentOptions.Right);Text(Find("FarmStat_2"),"HarvestSummary","누적 수확 0개",115,66,270,24,15,Brown,TextAlignmentOptions.Right);}
        var stocks=Find("StockPanel");Find("StockTitle").GetComponent<TMP_Text>().text="FAVORITE STOCKS";Find("StockTitle").GetComponent<TMP_Text>().fontStyle=FontStyles.Bold;SetText("StockTitle",12,9,306,37,24,TextAlignmentOptions.Center);
        for(int i=0;i<12;i++)if(Find("ChartBar_"+i))Object.DestroyImmediate(Find("ChartBar_"+i).gameObject);
        SetText("StockSymbol",28,250,125,32,24);SetText("StockPrice",160,250,150,32,24,TextAlignmentOptions.Right);Find("StockPrice").GetComponent<TMP_Text>().text="218.5 $";
        SetText("StockChange",154,286,156,26,20,TextAlignmentOptions.Right);Find("StockChange").GetComponent<TMP_Text>().text="▲ 0.52%";Find("StockChange").GetComponent<TMP_Text>().color=new Color32(85,140,204,255);
        if(!Find("StockChart")){var chart=Rect(stocks,"StockChart",20,57,290,170);chart.gameObject.AddComponent<TabletLineGraphic>().Configure(new[]{new Vector2(8,-145),new Vector2(40,-108),new Vector2(70,-125),new Vector2(100,-76),new Vector2(130,-145),new Vector2(160,-109),new Vector2(185,-123),new Vector2(214,-60),new Vector2(245,-94),new Vector2(275,-76)},new Color32(229,146,57,255),5);}
        if(!Find("StockChart").GetComponent<CanvasRenderer>())Find("StockChart").gameObject.AddComponent<CanvasRenderer>();
        if(Find("MapTitle"))Find("MapTitle").gameObject.SetActive(false);Position(Find("MapPlot").GetComponent<RectTransform>(),20,20,290,286);
    }
    private static void ConnectBasket(GameObject h)
    {
        var image = h.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.name == "BasketIcon");
        if (!image) return;
        var stage = h.GetComponent<BasketFillHud>() ?? h.AddComponent<BasketFillHud>();
        stage.Configure(image, Enumerable.Range(0,4).Select(i => AssetDatabase.LoadAllAssetsAtPath("Assets/UI/basket_tool.png").OfType<Sprite>().First(s => s.name == "basket_tool_2_" + i)).ToArray());
        EditorUtility.SetDirty(stage); EditorUtility.SetDirty(image); PrefabUtility.RecordPrefabInstancePropertyModifications(stage); PrefabUtility.RecordPrefabInstancePropertyModifications(image);
    }
    private static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
    {
        var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(parent, false);
        r.anchorMin = r.anchorMax = new Vector2(0,1); r.pivot = new Vector2(0,1); r.anchoredPosition = new Vector2(x,-y); r.sizeDelta = new Vector2(w,h); return r;
    }
    private static RectTransform Box(Transform p, string n, float x, float y, float w, float h, Color c, bool round = true)
    { var r = Rect(p,n,x,y,w,h); var im = r.gameObject.AddComponent<Image>(); im.color = c; if(round) { im.sprite = rounded; im.type = Image.Type.Sliced; } return r; }
    private static TextMeshProUGUI Text(Transform p, string n, string value, float x, float y, float w, float h, float size, Color c, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
    { var r = Rect(p,n,x,y,w,h); var t = r.gameObject.AddComponent<TextMeshProUGUI>(); t.font = font; t.text = value; t.fontSize = size; t.color = c; t.alignment = align; t.raycastTarget = false; t.textWrappingMode = TextWrappingModes.Normal; return t; }
    private static RectTransform Icon(Transform p, string n, string sprite, float x, float y, float w, float h, Color c)
    { var r = Box(p,n,x,y,w,h,c,false); var im = r.GetComponent<Image>(); im.sprite = cat.Art(sprite); im.preserveAspect = true; im.raycastTarget = false; return r; }
    private static RectTransform Button(Transform p, string n, string label, float x, float y, float w, float h, Color c, string action, int index = 0)
    {
        var r = Box(p,n,x,y,w,h,c); var b = r.gameObject.AddComponent<Button>(); b.targetGraphic = r.GetComponent<Image>(); b.navigation = new Navigation { mode = Navigation.Mode.None };
        var colors = b.colors; colors.highlightedColor = new Color(1,1,1,.82f); colors.pressedColor = new Color(.75f,.75f,.75f); colors.disabledColor = new Color(.65f,.65f,.65f,.65f); b.colors = colors;
        Bind(r,action,index); if(!string.IsNullOrEmpty(label)) Text(r,n+"Label",label,4,0,w-8,h,24,Cream,TextAlignmentOptions.Center); return r;
    }
    private static void Bind(RectTransform r, string action, int index = 0) { var b = r.gameObject.AddComponent<TabletControl>(); b.action = action; b.index = index; }
    private static RectTransform Scroll(Transform p, string n, float x, float y, float w, float h, bool grid)
    {
        var r = Rect(p,n,x,y,w,h); var sr = r.gameObject.AddComponent<ScrollRect>(); sr.horizontal = false; sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 35;
        var viewport = Box(r,n+"Viewport",0,0,w-20,h,Color.white,false); viewport.GetComponent<Image>().color = new Color(1,1,1,.01f); viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        var content = Rect(viewport,n+"Content",0,0,w-20,h); sr.viewport = viewport; sr.content = content;
        if(grid) { var layout = content.gameObject.AddComponent<GridLayoutGroup>(); layout.cellSize = new Vector2(140,182); layout.spacing = new Vector2(15,15); layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount; layout.constraintCount = 4; layout.padding = new RectOffset(1,1,1,1); }
        else { var layout = content.gameObject.AddComponent<VerticalLayoutGroup>(); layout.spacing = 10; layout.childControlWidth = true; layout.childControlHeight = false; layout.childForceExpandHeight = false; }
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var bar = Box(r,n+"Scrollbar",w-12,0,9,h,Brown); var sb = bar.gameObject.AddComponent<Scrollbar>();
        var slide = Rect(bar,n+"Sliding",0,0,9,h); var handle = Box(slide,n+"Handle",0,0,9,50,Tan); sb.handleRect = handle; sb.targetGraphic = handle.GetComponent<Image>(); sb.direction = Scrollbar.Direction.BottomToTop; sr.verticalScrollbar = sb; return content;
    }
    private static void Home(RectTransform p)
    {
        p.GetComponent<Image>().sprite = cat.checker; p.GetComponent<Image>().type = Image.Type.Tiled; p.GetComponent<Image>().color = Color.white;
        string[] titles = { "DAY", "SUNNY", "MONEY", "YIELD" }; string[] labels = { "HomeDay", "WeatherName", "HomeMoney", "HomeYield" };
        for(int i=0;i<4;i++)
        {
            var card = Box(p,"HomeCard_"+i,26+i*179,22,150,150,Paper);
            if(i==1) { Text(card,labels[i],titles[i],4,7,142,30,24,Brown,TextAlignmentOptions.Center); Icon(card,"HomeWeather","Tablet_UI_Sunny_1",32,49,86,86,Brown); }
            else { Text(card,"CardTitle_"+i,titles[i],4,7,142,35,27,Brown,TextAlignmentOptions.Center); Text(card,labels[i],i==0?"1":"0",5,55,140,75,i==0?70:40,Brown,TextAlignmentOptions.Center); }
        }
        var profile = Box(p,"FarmProfile",26,196,688,500,Paper);
        var input = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources()); input.name = "FarmNameInput"; input.transform.SetParent(profile,false);
        Position(input.GetComponent<RectTransform>(),20,12,648,65); var field = input.GetComponent<TMP_InputField>(); field.characterLimit = 24; field.textComponent.font = font; field.textComponent.fontSize = 36; field.textComponent.color = Brown; field.textComponent.alignment = TextAlignmentOptions.Center;
        input.GetComponent<Image>().color = Color.clear; ((TMP_Text)field.placeholder).font = font; ((TMP_Text)field.placeholder).text = "농장 이름을 입력하세요"; ((TMP_Text)field.placeholder).fontSize = 29; ((TMP_Text)field.placeholder).color = Brown; ((TMP_Text)field.placeholder).alignment = TextAlignmentOptions.Center; Bind(input.GetComponent<RectTransform>(),"farmName");
        Text(profile,"HomeLevel","농장 등급 Lv.1",25,79,620,35,24,Brown,TextAlignmentOptions.Center);
        var avatar = Box(profile,"FarmOwnerPortrait",20,141,222,302,Brown);
        var head = Box(avatar,"OwnerHead",76,36,73,86,Paper); head.GetComponent<Image>().sprite = circle;
        Box(avatar,"OwnerBody",37,125,148,140,Paper);
        Text(profile,"OwnerName","농장주",20,450,222,35,23,Brown,TextAlignmentOptions.Center);
        for(int i=0;i<3;i++)
        {
            var stat = Box(profile,"FarmStat_"+i,266,141+i*108,400,96,Tan);
            Text(stat,"StatTitle_"+i,i==0?"밭 생산량 / 면적":i==1?"공장 생산량 / 판매액":"농장 현황",16,7,368,30,25,Brown);
            if(i==0) { Text(stat,"FieldProduction","0개 재배 중",16,40,360,25,21,Brown); Text(stat,"FieldSize","0/0칸",16,65,360,24,18,Brown); }
            if(i==1) { Text(stat,"FactoryProduction","0개 생산",16,40,360,25,21,Brown); Text(stat,"FactorySales","0 M / 1min",16,65,360,24,18,Brown); }
            if(i==2) Text(stat,"FarmStatus","수확과 연구로 농장을 성장시키세요",16,41,368,45,21,Brown);
        }
        var stocks = Button(p,"StockPanel","",742,22,330,326,Paper,"stocks"); Text(stocks,"StockTitle","관심 종목",15,9,300,40,29,Brown,TextAlignmentOptions.Center);
        Text(stocks,"StockSymbol","PBCO",20,57,290,36,27,Brown); Text(stocks,"StockPrice","16,527  M",20,96,290,38,30,Brown);
        Text(stocks,"StockChange","+0.12%",20,138,290,30,22,new Color32(193,80,66,255));
        for(int i=0;i<12;i++) { float y = new[]{245,230,243,210,190,215,197,175,185,159,168,142}[i]; Box(stocks,"ChartBar_"+i,23+i*24,y,14,290-y,i%3==0?new Color32(180,91,80,255):new Color32(119,138,91,255),false); }
        var map = Button(p,"MapPanel","",742,370,330,326,Paper,"map"); Text(map,"MapTitle","미니맵",15,6,300,38,29,Brown,TextAlignmentOptions.Center);
        var plot = Box(map,"MapPlot",20,57,290,247,Tan);
        for(int x=0;x<6;x++) for(int y=0;y<5;y++) Box(plot,"MapTile_"+x+"_"+y,10+x*46,10+y*45,40,39,new Color32((byte)(x%2==0?124:132),143,95,255),false);
        var marker = Box(plot,"MapPlayer",145,123,15,15,Cream); marker.anchorMin = marker.anchorMax = marker.pivot = new Vector2(.5f,.5f); marker.anchoredPosition = Vector2.zero; marker.GetComponent<Image>().sprite = circle;
    }
    private static void Shop(RectTransform p)
    {
        Button(p,"ShopTab_0","물품",20,0,135,53,Brown,"shopTab",0); Button(p,"ShopTab_1","기계",159,0,135,53,DarkBrown,"shopTab",1);
        Text(p,"ShopMoney","1,000 M",739,4,328,45,31,Brown,TextAlignmentOptions.Right);
        var products = Scroll(p,"ShopScroll",22,78,651,615,true);
        for(int i=0;i<cat.products.Length;i++)
        {
            var item = cat.products[i]; var card = Button(products,"Product_"+i,"",0,0,140,182,Tan,"selectProduct",i);
            Icon(card,"ProductArt_"+i,item.machine?"Tablet_UI_31":item.grantId=="seed"?"Tablet_UI_63":"Tablet_UI_29",36,8,68,66,Brown);
            Text(card,"ProductName_"+i,item.title,5,74,130,28,17,Brown,TextAlignmentOptions.Center);
            Text(card,"ProductPrice_"+i,item.price+" M",3,100,134,27,22,Brown,TextAlignmentOptions.Center);
            Text(card,"ProductOwned_"+i,"보유 0",5,130,130,20,14,Brown,TextAlignmentOptions.Center);
            Button(card,"AddFull_"+i,"+",48,149,44,28,Brown,"add",i);
            Button(card,"Subtract_"+i,"−",3,149,36,28,Brown,"subtract",i);
            Text(card,"Quantity_"+i,"0",40,149,60,28,22,Brown,TextAlignmentOptions.Center);
            Button(card,"AddRight_"+i,"+",101,149,36,28,Brown,"add",i);
        }
        var info = Box(p,"ShopInformation",700,78,378,458,Tan);
        Icon(info,"ProductInfoIcon","Tablet_UI_59",130,162,118,118,Brown);
        var detail = Rect(info,"ProductDetail",0,0,378,458);
        Text(detail,"DetailTitle","씨감자",18,19,340,46,29,Brown); Text(detail,"DetailPrice","100 M",18,70,340,38,27,Brown);
        Icon(detail,"DetailIcon","Tablet_UI_63",130,117,118,118,Brown);
        Text(detail,"DetailDescription","",24,265,330,168,23,Brown,TextAlignmentOptions.TopLeft);
        Box(p,"CartLine",700,560,378,3,Brown,false); Text(p,"TotalLabel","총액",707,577,130,42,28,Brown);
        Text(p,"CartTotal","0 M",834,577,237,42,29,Brown,TextAlignmentOptions.Right);
        Button(p,"BuyButton","구매",700,634,311,61,Brown,"buy"); Button(p,"ClearCart","×",1025,634,53,61,Brown,"clear");
    }
    private static void Research(RectTransform p)
    {
        var background = Rect(p,"ResearchPan",0,65,1104,653); var sr = background.gameObject.AddComponent<ScrollRect>(); sr.horizontal = sr.vertical = true; sr.movementType = ScrollRect.MovementType.Unrestricted; sr.scrollSensitivity = 0;
        var viewport = Box(background,"ResearchViewport",0,0,1104,653,Brown,false); viewport.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        var content = Rect(viewport,"ResearchContent",0,0,1104,718); sr.content = content; sr.viewport = viewport;
        for(int i=0;i<cat.research.Length;i++)
        {
            int previous = cat.research[i].prerequisite; if(previous<0&&i>0) previous = i-1;
            if(previous>=0) { Vector2 a = cat.research[previous].position+Vector2.one*42, b = cat.research[i].position+Vector2.one*42; var line = Box(content,"ResearchBranch_"+i,a.x,a.y,(b-a).magnitude,9,Tan,false); line.pivot = new Vector2(0,.5f); line.localEulerAngles = new Vector3(0,0,-Mathf.Atan2(b.y-a.y,b.x-a.x)*Mathf.Rad2Deg); }
        }
        for(int i=0;i<cat.research.Length;i++)
        {
            var pos = cat.research[i].position; var node = Button(content,"ResearchNode_"+i,"",pos.x,pos.y,84,84,DarkBrown,"researchSelect",i); node.GetComponent<Image>().sprite = circle;
            node.GetComponent<TabletControl>().selectOnHover = true;
            Icon(node,"ResearchPotato_"+i,"Tablet_UI_63",13,13,58,58,Brown);
            Icon(node,"ResearchSelected_"+i,"Tablet_UI_5",-17,-20,118,118,Cream);
            Text(node,"ResearchTier_"+i,cat.research[i].tier.ToString(),27,61,30,22,16,Tan,TextAlignmentOptions.Center);
        }
        var top = Box(p,"ResearchTop",0,0,1104,65,DarkBrown,false); Text(top,"ExperienceTitle","농사 경험 레벨",19,5,450,25,18,Cream);
        var track = Box(top,"ExperienceTrack",20,36,470,18,Brown); var fill = Box(track,"ResearchExperienceFill",0,0,470,18,Tan); fill.GetComponent<Image>().type = Image.Type.Filled; fill.GetComponent<Image>().fillMethod = Image.FillMethod.Horizontal;
        Text(top,"ResearchLevel","1",503,13,60,40,32,Cream); Text(top,"ResearchPoints","100 R",800,9,280,44,32,Cream,TextAlignmentOptions.Right);
        Button(p,"ResearchZoomOut","−",954,641,50,51,DarkBrown,"zoom",-1); Button(p,"ResearchZoomIn","+",1017,641,50,51,DarkBrown,"zoom",1);
        var tip = Box(p,"ResearchTooltip",430,264,413,260,DarkBrown);
        Text(tip,"ResearchTitle","감자 성장 속도 Ⅰ",19,16,370,43,28,Cream); Box(tip,"ResearchDivider",18,66,377,2,Tan,false);
        Text(tip,"ResearchDescription","",20,78,373,112,21,Cream,TextAlignmentOptions.TopLeft);
        Button(tip,"ResearchButton","",19,199,327,45,Brown,"research"); Text(tip,"ResearchButtonLabel","[E] 연구하기",23,200,319,43,24,Cream,TextAlignmentOptions.Center);
        Button(tip,"DismissResearch","×",354,202,41,40,Brown,"researchDismiss");
    }
    private static void Guide(RectTransform p)
    {
        string[] tabs = {"재배","가공","자동화"}; for(int i=0;i<3;i++) Button(p,"GuideTab_"+i,tabs[i],20+i*139,0,135,53,i==0?Brown:DarkBrown,"guideTab",i);
        var list = Scroll(p,"GuideScroll",20,77,329,615,false);
        for(int i=0;i<cat.guides.Length;i++) { var b = Button(list,"GuideItem_"+i,cat.guides[i].title,0,0,305,58,Tan,"guide",i); b.GetComponentInChildren<TMP_Text>().fontSize = 22; }
        Text(p,"GuideTitle","감자 재배 방법",385,78,678,56,36,Brown); Box(p,"GuideDivider",385,141,678,3,Brown,false);
        Text(p,"GuideIntro","",385,161,678,38,25,Brown);
        var picture = Box(p,"GuidePicture",385,216,650,243,Color.white,false); picture.GetComponent<Image>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Folder+"/GuideFarming.png"); picture.GetComponent<Image>().preserveAspect = true;
        Text(p,"GuideBody","",385,475,678,143,23,Brown,TextAlignmentOptions.TopLeft);
        Button(p,"GuidePrevious","◀  A",390,640,95,45,Brown,"guidePage",-1); Button(p,"GuideNext","D  ▶",965,640,95,45,Brown,"guidePage",1);
        for(int i=0;i<6;i++) { var d = Box(p,"GuideDot_"+i,601+i*38,654,15,15,Brown); d.GetComponent<Image>().sprite = circle; }
    }
    private static void Settings(RectTransform p)
    {
        string[] tabs = {"일반","그래픽","오디오"}; for(int i=0;i<3;i++) Button(p,"SettingsTab_"+i,tabs[i],20+i*139,0,135,53,i==0?Brown:DarkBrown,"settingsTab",i);
        for(int i=0;i<3;i++)
        {
            var page = Rect(p,"SettingsPage_"+i,0,62,1104,656);
            if(i==0) { Choice(page,"language","언어 설정",28,new[]{"한국어"}); Toggle(page,"CameraShake","카메라 흔들림",128,"shake","ShakeValue"); Slider(page,"cameraSensitivity","카메라 감도",228,.1f,4); Slider(page,"mouseSensitivity","마우스 감도",328,.1f,4); }
            if(i==1) { Choice(page,"window","화면 모드",20,new[]{"전체 화면","테두리 없는 창","창 모드"}); Choice(page,"resolution","해상도",118,new[]{"1920 × 1080"}); Choice(page,"fps","최대 프레임",216,new[]{"30","60","90","120","144","240","360"}); Toggle(page,"VerticalSync","수직 동기화",314,"vsync","VsyncValue"); Slider(page,"fov","카메라 시야각",412,45,100); }
            if(i==2) { Choice(page,"microphone","마이크 장치",8,new[]{"장치 없음"}); Choice(page,"microphoneMode","마이크 모드",88,new[]{"눌러서 말하기","항상 켜기","음소거"}); Slider(page,"microphoneVolume","마이크 볼륨",168,0,1); Slider(page,"master","마스터 볼륨",278,0,1); Slider(page,"music","음악 볼륨",358,0,1); Slider(page,"effects","효과음 볼륨",438,0,1); Slider(page,"ambience","환경음 볼륨",518,0,1); }
            page.gameObject.SetActive(i==0);
        }
    }
    private static void Choice(Transform p, string key, string title, float y, string[] options)
    {
        Text(p,"SettingLabel_"+key,title,35,y,430,55,29,Brown);
        var dropdown = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources()); dropdown.name = "Choice_"+key; dropdown.transform.SetParent(p,false); Position(dropdown.GetComponent<RectTransform>(),515,y,548,55);
        var d = dropdown.GetComponent<TMP_Dropdown>(); d.ClearOptions(); d.AddOptions(options.ToList()); d.captionText.font = font; d.captionText.fontSize = 25; d.captionText.color = Brown;
        foreach(var t in dropdown.GetComponentsInChildren<TMP_Text>(true)) {t.font=font;t.fontSize=24;t.color=Brown;}
        foreach(var im in dropdown.GetComponentsInChildren<Image>(true)) {im.sprite=rounded;im.type=Image.Type.Sliced;im.color=Tan;}
        var arrow = dropdown.transform.Find("Arrow");
        if (arrow) { var im = arrow.GetComponent<Image>(); im.sprite = cat.Art("Tablet_UI_79"); im.type = Image.Type.Simple; im.preserveAspect = true; im.color = Brown; }
        var item = d.template.Find("Viewport/Content/Item").GetComponent<RectTransform>(); item.sizeDelta = new Vector2(item.sizeDelta.x,48);
        d.template.sizeDelta = new Vector2(d.template.sizeDelta.x,240);
        var highlight = item.Find("Item Background"); if (highlight) highlight.GetComponent<Image>().color = Paper;
        if(d.itemText) d.itemText.rectTransform.sizeDelta = new Vector2(-30,0);
        d.captionText.textWrappingMode = TextWrappingModes.NoWrap; d.captionText.overflowMode = TextOverflowModes.Ellipsis;
        Bind(dropdown.GetComponent<RectTransform>(),key); if(key=="language") d.interactable=false;
    }
    private static void Toggle(Transform p,string name,string title,float y,string action,string valueLabel)
    { Text(p,name+"Title",title,35,y,430,55,29,Brown); var b=Button(p,name,"",515,y,548,55,Tan,action); Text(b,valueLabel,"ON",5,0,538,55,26,Brown,TextAlignmentOptions.Center); }
    private static void Slider(Transform p,string key,string title,float y,float min,float max)
    {
        Text(p,"SettingLabel_"+key,title,35,y,430,55,29,Brown); var r=Rect(p,"Setting_"+key,515,y+9,548,40); var s=r.gameObject.AddComponent<Slider>(); s.minValue=min;s.maxValue=max;s.navigation=new Navigation{mode=Navigation.Mode.None};
        Box(r,"SliderTrack_"+key,0,17,548,7,Tan); var fillArea=Rect(r,"SliderFillArea_"+key,0,17,548,7);var fill=Box(fillArea,"SliderFill_"+key,0,0,0,0,Brown);fill.anchorMin=Vector2.zero;fill.anchorMax=Vector2.one;fill.pivot=new Vector2(.5f,.5f);fill.anchoredPosition=Vector2.zero;s.fillRect=fill;
        var slide=Rect(r,"SliderArea_"+key,10,20,528,0); var handle=Box(slide,"SliderHandle_"+key,0,0,21,40,Brown); handle.anchorMin=handle.anchorMax=handle.pivot=new Vector2(.5f,.5f);handle.anchoredPosition=Vector2.zero;s.handleRect=handle;s.targetGraphic=handle.GetComponent<Image>();
        Text(p,"Value_"+key,"1.00",940,y+48,123,30,22,Brown,TextAlignmentOptions.Right); Bind(r,key);
    }
    private static void Position(RectTransform r,float x,float y,float w,float h) {r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);}
    private static Sprite Shape(string name,bool round)
    {
        string path=Folder+"/"+name+".asset"; var existing=AssetDatabase.LoadAssetAtPath<Sprite>(path); if(existing)return existing;
        var tex=new Texture2D(64,64,TextureFormat.RGBA32,false){name=name+"Texture",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
        var pixels=new Color[4096]; for(int y=0;y<64;y++)for(int x=0;x<64;x++){ float dx=round?x-31.5f:Mathf.Max(0,Mathf.Abs(x-31.5f)-19.5f),dy=round?y-31.5f:Mathf.Max(0,Mathf.Abs(y-31.5f)-19.5f);float radius=round?31:12;pixels[y*64+x]=new Color(1,1,1,Mathf.Clamp01(radius+.5f-Mathf.Sqrt(dx*dx+dy*dy)));} tex.SetPixels(pixels);tex.Apply();
        var sprite=Sprite.Create(tex,new Rect(0,0,64,64),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,round?Vector4.zero:new Vector4(15,15,15,15));sprite.name=name;AssetDatabase.CreateAsset(sprite,path);AssetDatabase.AddObjectToAsset(tex,sprite);return sprite;
    }
    private static Sprite Checker()
    {
        string path=Folder+"/Checker.asset";var existing=AssetDatabase.LoadAssetAtPath<Sprite>(path);if(existing)return existing;
        var tex=new Texture2D(64,64,TextureFormat.RGBA32,false){name="FarmChecker",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Repeat};var pixels=new Color[4096];for(int y=0;y<64;y++)for(int x=0;x<64;x++)pixels[y*64+x]=(x/32+y/32)%2==0?new Color32(76,94,56,255):new Color32(84,102,60,255);tex.SetPixels(pixels);tex.Apply();var sprite=Sprite.Create(tex,new Rect(0,0,64,64),Vector2.one*.5f,100);sprite.name="Checker";AssetDatabase.CreateAsset(sprite,path);AssetDatabase.AddObjectToAsset(tex,sprite);return sprite;
    }
}
