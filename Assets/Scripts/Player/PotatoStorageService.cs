using System;
using UnityEngine;

/// <summary>One save commits both sides of a transfer, or the farm and the harvested reward together.</summary>
public static class PotatoStorageService
{
    private static readonly HarvestedPotatoCondition[] DebugConditions =
    {
        HarvestedPotatoCondition.Green, HarvestedPotatoCondition.BugEaten,
        HarvestedPotatoCondition.Injured, HarvestedPotatoCondition.Sprouted
    };

    /// <summary>Prototype shortcut: preserve existing potatoes and fill the empty basket spaces.</summary>
    public static bool TryFillDebugBasket(PotatoInventory basket, Func<bool> save, out int added, out string error)
    {
        added = 0;
        error = null;
        if (basket == null || save == null) { error = "바구니 저장 연결이 없습니다."; return false; }
        int empty = basket.Free;
        if (empty == 0) { error = "바구니가 이미 가득 찼습니다."; return false; }
        var before = basket.CaptureInventory();
        var randomState = UnityEngine.Random.state;
        try
        {
            for (int i = 0; i < empty; i++)
            {
                int kind = UnityEngine.Random.Range(0, 4);
                var grade = kind < 3 ? (PotatoHarvestGrade)(kind + 1) : PotatoHarvestGrade.None;
                var condition = kind < 3 ? HarvestedPotatoCondition.None : DebugConditions[UnityEngine.Random.Range(0, DebugConditions.Length)];
                if (!basket.TryAdd(grade, condition, 1)) throw new InvalidOperationException("랜덤 감자를 추가하지 못했습니다.");
            }
            if (save()) { added = empty; return true; }
        }
        catch (Exception failure) { Debug.LogWarning("[바구니 테스트] " + failure.Message); }
        basket.RestoreInventory(before);
        UnityEngine.Random.state = randomState;
        error = "저장에 실패하여 바구니를 이전 상태로 복구했습니다.";
        return false;
    }

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

    public static bool TryWithdraw(PotatoInventory shelf, PotatoInventory basket, long potatoId, Func<bool> save, out string error)
    {
        error = null;
        if (shelf == null || basket == null || ReferenceEquals(shelf, basket) || save == null)
        { error = "감자 저장 연결이 없습니다."; return false; }
        if (basket.Free == 0) { error = "바구니가 가득 찼습니다."; return false; }
        if (shelf.FindPotato(potatoId) == null) { error = "선택한 감자가 더 이상 선반에 없습니다."; return false; }
        if (shelf.TryTransferPotatoTo(potatoId, basket, save)) return true;
        error = "저장에 실패하여 감자를 원래 위치로 복구했습니다.";
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
