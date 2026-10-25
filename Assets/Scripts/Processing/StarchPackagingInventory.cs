using UnityEngine;

/// <summary>Per-player test counts until the inventory system provides these operations.</summary>
[DisallowMultipleComponent]
public sealed class StarchPackagingInventory : MonoBehaviour
{
    public int WrapperCount { get; private set; }
    public int StarchPackCount { get; private set; }
    public PotatoStarchProcessor PackagingOwner { get; private set; }

    public static bool IsBareHanded(PlayerInteractionController player)
    {
        var items = player ? player.GetComponent<PlayerItemController>() : null;
        return player && player.isActiveAndEnabled && items &&
            items.SelectedSlot == PlayerItemSlot.Hands && !items.HasItem(PlayerItemSlot.Hands);
    }

    public void AddWrapper()
    {
        WrapperCount++;
        Debug.Log($"[PotatoStarch] 포장지 1개 획득(테스트). 보유 포장지={WrapperCount}개. 실제 인벤토리 지급 없음", this);
    }

    public bool TryBeginPackaging(PotatoStarchProcessor owner)
    {
        if (!owner || PackagingOwner || WrapperCount <= 0) return false;
        WrapperCount--;
        PackagingOwner = owner;
        return true;
    }

    public void CompletePackaging(PotatoStarchProcessor owner)
    {
        if (!owner || PackagingOwner != owner) return;
        PackagingOwner = null;
        StarchPackCount++;
        Debug.Log($"[PotatoStarch] Strach Pack 1개 획득(테스트). 누적={StarchPackCount}개. 실제 인벤토리 지급 없음", this);
    }

    public void CancelPackaging(PotatoStarchProcessor owner)
    {
        if (!owner || PackagingOwner != owner) return;
        PackagingOwner = null;
        WrapperCount++;
        Debug.Log($"[PotatoStarch] 포장 취소. 포장지 1개 반환. 보유={WrapperCount}개", this);
    }
}
