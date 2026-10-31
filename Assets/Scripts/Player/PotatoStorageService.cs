using System;
using UnityEngine;

/// <summary>One save commits both sides of a transfer, or the farm and the harvested reward together.</summary>
public static class PotatoStorageService
{
    public static bool TryDeposit(PotatoInventory basket, PotatoInventory shelf, Func<bool> save, out int moved)
    {
        moved = 0;
        if (basket == null || shelf == null || save == null || ReferenceEquals(basket, shelf) || basket.Count == 0 || shelf.Free == 0) return false;
        var beforeBasket = basket.CaptureInventory();
        var beforeShelf = shelf.CaptureInventory();
        try
        {
            moved = basket.TransferTo(shelf);
            if (moved > 0 && save()) return true;
        }
        catch (Exception error) { Debug.LogWarning("[Potato Storage] " + error.Message); }
        basket.RestoreInventory(beforeBasket);
        shelf.RestoreInventory(beforeShelf);
        moved = 0;
        return false;
    }

    public static bool TryHarvest(PotatoInventory basket, PotatoHarvestCoordinator harvest, GroundGrid ground,
        Vector2Int cell, Func<bool> save, out string error, ISaveSection farmingSection = null)
    {
        error = null;
        var farm = harvest ? harvest.GetComponent<PotatoFarmingSystem>() : null;
        if (basket == null || !farm || !ground || save == null || !harvest.CanHarvest(ground, cell) ||
            !farm.TryGetFarmingTile(ground, cell, out var crop)) return false;
        PotatoHarvestRewardRules.GetCountRange(crop.CurrentHealth, out _, out int maximum);
        if (basket.Free < maximum)
        {
            error = $"바구니 공간 부족: 최대 {maximum}개를 담을 빈자리가 필요합니다. ({basket.Count}/{basket.Capacity})";
            return false;
        }
        var section = farmingSection ?? new FarmingSaveSection();
        Action restoreFarm;
        try { restoreFarm = section.PrepareRestore(section.Version, section.Capture()); }
        catch (Exception failure) { error = failure.Message; return false; }
        var beforeBasket = basket.CaptureInventory();
        var randomState = UnityEngine.Random.state;
        try
        {
            if (harvest.TryHarvest(ground, cell, out var result) && basket.TryAddHarvest(result) && save()) return true;
            error = "수확 저장 실패: 작물과 바구니를 이전 상태로 복구했습니다.";
        }
        catch (Exception failure) { error = failure.Message; }
        restoreFarm();
        basket.RestoreInventory(beforeBasket);
        UnityEngine.Random.state = randomState;
        return false;
    }
}
