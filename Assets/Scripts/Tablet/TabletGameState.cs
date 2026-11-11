using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>Slot-owned farm profile, purchased stock and research; uses the existing inventory save section.</summary>
[DefaultExecutionOrder(-200)]
[DisallowMultipleComponent]
public sealed class TabletGameState : MonoBehaviour, IInventorySaveTarget
{
    [Serializable] public sealed class Stock { public string id; public int count; }
    [Serializable] public sealed class Profile
    {
        public string farmName = "", weather = "Sunny";
        public long money;
        public int researchPoints, experience, totalHarvested;
        public List<Stock> stock = new();
        public List<string> unlocked = new();
    }
    [SerializeField] private TabletCatalog catalog;
    private Profile data;
    private PotatoStorageSaveAdapter persistence;
    public static TabletGameState Local { get; private set; }
    public string OwnerId => "tablet:local-player";
    public TabletCatalog Catalog => catalog;
    public Profile Data { get { EnsureData(); return data; } }
    public int Level => 1 + Data.experience / 100;
    public event Action Changed;
    public Func<bool> SaveOverride { get; set; }

    private void EnsureData()
    {
        if (data != null) return;
        data = new Profile { money = catalog ? catalog.startingMoney : 1000, researchPoints = catalog ? catalog.startingResearchPoints : 100 };
        data.stock.Add(new Stock { id = "seed", count = catalog ? catalog.startingSeeds : 100 });
    }
    public void Configure(TabletCatalog value) { catalog = value; }
    private void Start()
    {
        var player = GetComponent<FirstPersonPlayerController>();
        if (!player || !player.IsLocallyControlled) { enabled = false; return; }
        Local = this;
        EnsureData();
        persistence = FindAnyObjectByType<PotatoStorageSaveAdapter>();
        persistence?.Register(this);
        ApplyResearch();
    }
    public int Owned(string id) => Data.stock.FirstOrDefault(s => s.id == id)?.count ?? 0;
    public int Tier(int branch) => catalog ? catalog.research.Where(r => r.branch == branch && Data.unlocked.Contains(r.id)).Select(r => r.tier).DefaultIfEmpty().Max() : 0;
    public int Price(TabletCatalog.Product item) => Mathf.Max(1, Mathf.CeilToInt(item.price * (1f - Tier(3) * 0.05f)));
    public bool IsUnlocked(int index) => catalog && index >= 0 && index < catalog.research.Length && Data.unlocked.Contains(catalog.research[index].id);
    public bool CanResearch(int index) => catalog && index >= 0 && index < catalog.research.Length &&
        !IsUnlocked(index) && Data.researchPoints >= catalog.research[index].cost &&
        (catalog.research[index].prerequisite < 0 || IsUnlocked(catalog.research[index].prerequisite));

    private bool Commit(Profile before, out string error)
    {
        error = null;
        bool saved = false;
        try { saved = SaveOverride != null ? SaveOverride() : persistence && persistence.IsReady && persistence.SaveActive(); }
        catch (Exception exception) { error = exception.Message; }
        if (!saved) { data = before; ApplyResearch(); error ??= "저장하지 못했습니다. 잠시 후 다시 시도해 주세요."; }
        Changed?.Invoke();
        return saved;
    }
    private Profile Snapshot() => JsonUtility.FromJson<Profile>(JsonUtility.ToJson(Data));
    private void Add(string id, int amount)
    {
        var entry = Data.stock.FirstOrDefault(s => s.id == id);
        if (entry == null) Data.stock.Add(new Stock { id = id, count = amount });
        else entry.count = checked(entry.count + amount);
    }
    public bool TryPurchase(IReadOnlyDictionary<int, int> cart, out string error)
    {
        error = null;
        if (!catalog || cart == null || cart.Count == 0) { error = "구매할 물품을 선택해 주세요."; return false; }
        long total = 0;
        foreach (var pair in cart)
        {
            if (pair.Key < 0 || pair.Key >= catalog.products.Length || pair.Value <= 0 || pair.Value > 999) { error = "주문 수량을 확인해 주세요."; return false; }
            total = checked(total + (long)Price(catalog.products[pair.Key]) * pair.Value);
        }
        if (total > Data.money) { error = "보유 금액이 부족합니다."; return false; }
        var before = Snapshot();
        data.money -= total;
        foreach (var pair in cart) { var item = catalog.products[pair.Key]; Add(item.grantId, pair.Value * item.grantQuantity); }
        return Commit(before, out error);
    }
    public bool TryResearch(int index, out string error)
    {
        error = null;
        if (!CanResearch(index)) { error = IsUnlocked(index) ? "이미 완료한 연구입니다." : "선행 연구와 연구 포인트를 확인해 주세요."; return false; }
        var before = Snapshot();
        data.researchPoints -= catalog.research[index].cost;
        data.unlocked.Add(catalog.research[index].id);
        ApplyResearch();
        return Commit(before, out error);
    }
    public bool Rename(string value, out string error)
    {
        value = (value ?? "").Trim();
        if (value.Length > 24) { error = "농장 이름은 24자까지 입력할 수 있습니다."; return false; }
        var before = Snapshot(); data.farmName = value;
        return Commit(before, out error);
    }
    public bool TryConsume(string id, int count, out string error)
    {
        error = null;
        if (count <= 0 || Owned(id) < count) { error = "보유 물품이 부족합니다. 태블릿 상점에서 구매해 주세요."; return false; }
        var before = Snapshot(); Add(id, -count);
        return Commit(before, out error);
    }
    public bool RecordHarvestAndSave(int count)
    {
        if (count < 0) return false;
        var before = Snapshot();
        data.totalHarvested += count; data.experience += count * 5; data.researchPoints += count;
        return Commit(before, out _);
    }
    public bool TryPlant(PotatoFarmingSystem farm, GroundGrid grid, Vector2Int cell, out string error)
    {
        error = null;
        if (Owned("seed") < 1) { error = "씨감자가 부족합니다. 태블릿 상점에서 구매해 주세요."; return false; }
        var section = new FarmingSaveSection();
        Action restore;
        try { restore = section.PrepareRestore(section.Version, section.Capture()); }
        catch (Exception e) { error = e.Message; return false; }
        var before = Snapshot();
        if (!farm.TryStartFarming(grid, cell)) return false;
        farm.GetComponent<PotatoHarvestCoordinator>()?.ResetCell(grid, cell);
        Add("seed", -1);
        if (Commit(before, out error)) return true;
        restore();
        return false;
    }
    public void ApplyResearch()
    {
        if (!Application.isPlaying || !catalog) return;
        foreach (var farm in FindObjectsByType<PotatoFarmingSystem>())
            farm.ApplyTabletResearch(1f + Tier(0) * 0.25f, 1f + Tier(1) * 0.1f, 1f - Tier(2) * 0.25f);
    }
    public InventoryData CaptureInventory()
    {
        EnsureData();
        var result = new InventoryData { ownerId = OwnerId };
        result.items.Add(new InventoryItemData { itemId = "tablet_profile", quantity = 1, propertiesVersion = 1, propertiesJson = JsonUtility.ToJson(data) });
        return result;
    }
    private Profile Read(InventoryData value)
    {
        if (value == null || value.ownerId != OwnerId || value.items == null || value.items.Count > 1) throw new InvalidDataException("Invalid tablet profile.");
        if (value.items.Count == 0) return null;
        var item = value.items[0];
        if (item.itemId != "tablet_profile" || item.quantity != 1 || item.propertiesVersion != 1) throw new InvalidDataException("Unsupported tablet profile.");
        var profile = JsonUtility.FromJson<Profile>(item.propertiesJson);
        if (profile == null || profile.money < 0 || profile.researchPoints < 0 || profile.experience < 0 || profile.totalHarvested < 0 ||
            profile.farmName == null || profile.farmName.Length > 24 || !new[]{"Sunny","Cloudy","Rainy","Hot","Thunder"}.Contains(profile.weather) || profile.stock == null || profile.unlocked == null ||
            profile.stock.Any(s => s == null || string.IsNullOrWhiteSpace(s.id) || s.count < 0) ||
            profile.stock.Select(s => s.id).Distinct().Count() != profile.stock.Count || profile.unlocked.Distinct().Count() != profile.unlocked.Count ||
            catalog && profile.unlocked.Any(id => !catalog.research.Any(r => r.id == id))) throw new InvalidDataException("Invalid tablet profile values.");
        return profile;
    }
    public void ValidateInventory(InventoryData value) => Read(value);
    public void RestoreInventory(InventoryData value) { data = Read(value); EnsureData(); ApplyResearch(); Changed?.Invoke(); }
    private void OnDestroy() { if (persistence) persistence.Unregister(this); if (Local == this) Local = null; }
}
