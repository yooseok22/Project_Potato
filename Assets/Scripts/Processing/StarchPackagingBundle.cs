using UnityEngine;

[DisallowMultipleComponent]
public sealed class StarchPackagingBundle : MonoBehaviour, IPlayerInteractable
{
    public string Prompt => "[E] 포장지 1개 들기";
    public bool RequiresHold => false;
    public float HoldDuration => 0f;

    public bool CanInteract(PlayerInteractionController player)
        => isActiveAndEnabled && StarchPackagingInventory.IsBareHanded(player) &&
           !(player.GetComponent<StarchPackagingInventory>()?.PackagingOwner);

    public void Interact(PlayerInteractionController player)
    {
        if (!CanInteract(player)) return;
        var inventory = player.GetComponent<StarchPackagingInventory>();
        if (!inventory) inventory = player.gameObject.AddComponent<StarchPackagingInventory>();
        var piece = transform.parent ? transform.parent.Find("FBX_Starch Packaging Piece") : null;
        inventory.TryTakeWrapper(player, piece ? piece.GetComponent<MeshFilter>() : null);
    }
}
