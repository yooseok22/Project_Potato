using UnityEngine;

/// <summary>Per-player test counts until the inventory system provides these operations.</summary>
[DisallowMultipleComponent]
public sealed class StarchPackagingInventory : MonoBehaviour
{
    public int WrapperCount { get; private set; }
    public int StarchPackCount { get; private set; }
    public PotatoStarchProcessor PackagingOwner { get; private set; }
    private ProcessingCarryItem heldWrapper;

    public static bool IsBareHanded(PlayerInteractionController player)
    {
        var items = player ? player.GetComponent<PlayerItemController>() : null;
        return player && player.isActiveAndEnabled && items &&
            items.SelectedSlot == PlayerItemSlot.Hands && !items.HasItem(PlayerItemSlot.Hands);
    }

    public bool IsHoldingWrapper(PlayerInteractionController player)
    {
        var items = player ? player.GetComponent<PlayerItemController>() : null;
        return player && player.isActiveAndEnabled && items && heldWrapper &&
            heldWrapper.IsCarriedBy(player) && items.SelectedSlot == PlayerItemSlot.Hands;
    }

    public bool TryTakeWrapper(PlayerInteractionController player, MeshFilter source)
    {
        if (PackagingOwner || heldWrapper || !IsBareHanded(player) || !source || !source.sharedMesh) return false;
        var root = new GameObject("StarchPackaging_Hand");
        root.transform.SetParent(transform, false);
        var visual = new GameObject("PackagingPiece", typeof(MeshFilter), typeof(MeshRenderer));
        visual.transform.SetParent(root.transform, false);
        visual.GetComponent<MeshFilter>().sharedMesh = source.sharedMesh;
        visual.GetComponent<MeshRenderer>().sharedMaterials = source.GetComponent<MeshRenderer>().sharedMaterials;
        var bounds = source.sharedMesh.bounds;
        visual.transform.localPosition = -bounds.center;
        heldWrapper = root.AddComponent<ProcessingCarryItem>();
        float scale = 0.35f / Mathf.Max(0.01f, bounds.size.x, bounds.size.y, bounds.size.z);
        heldWrapper.ConfigureHeldPose(new Vector3(-0.12f, -0.08f, 0.28f), new Vector3(-25f, 0f, 0f), Vector3.one * scale);
        if (!heldWrapper.TryAttach(player))
        {
            ClearHeldWrapper();
            return false;
        }
        WrapperCount = 1;
        Debug.Log("[PotatoStarch] 포장지 1개를 손에 들었습니다. 위 전분 틀에 [E]로 포장하세요.", this);
        return true;
    }

    public bool TryBeginPackaging(PotatoStarchProcessor owner)
    {
        if (!owner || PackagingOwner || WrapperCount <= 0 || !IsHoldingWrapper(GetComponent<PlayerInteractionController>())) return false;
        WrapperCount--;
        PackagingOwner = owner;
        return true;
    }

    public void CompletePackaging(PotatoStarchProcessor owner)
    {
        if (!owner || PackagingOwner != owner) return;
        PackagingOwner = null;
        ClearHeldWrapper();
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

    private void ClearHeldWrapper()
    {
        if (!heldWrapper) return;
        heldWrapper.ResetHome();
        var wrapperObject = heldWrapper.gameObject;
        heldWrapper = null;
        wrapperObject.SetActive(false);
        if (Application.isPlaying) Destroy(wrapperObject);
        else DestroyImmediate(wrapperObject);
    }

    private void OnDestroy() => ClearHeldWrapper();
}
