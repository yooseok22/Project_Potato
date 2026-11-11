using UnityEngine;

[DisallowMultipleComponent]
public sealed class BasketFillHud : MonoBehaviour
{
    [SerializeField] private UnityEngine.UI.Image basketIcon;
    [SerializeField] private Sprite[] fillSprites = new Sprite[4];
    private PlayerBasketInventory basket;
    private int previousCount = -1;
    public int DisplayedStage { get; private set; }

    public static int Stage(int count, int capacity) => count >= capacity ? 3 :
        count * 10 >= capacity * 7 ? 2 : count * 2 >= capacity ? 1 : 0;

    public void Configure(UnityEngine.UI.Image icon, Sprite[] sprites)
    {
        basketIcon = icon;
        fillSprites = sprites;
        Refresh(0, PlayerBasketInventory.BasketCapacity);
    }

    private void LateUpdate()
    {
        if (!basket)
            foreach (var candidate in FindObjectsByType<PlayerBasketInventory>())
                if (candidate.IsLocalPlayer) { basket = candidate; break; }
        if (!basket || basket.Inventory.Count == previousCount) return;
        Refresh(basket.Inventory.Count, basket.Inventory.Capacity);
    }

    public void Refresh(int count, int capacity)
    {
        previousCount = count;
        DisplayedStage = Stage(Mathf.Max(0, count), Mathf.Max(1, capacity));
        if (basketIcon && fillSprites != null && fillSprites.Length == 4)
        {
            basketIcon.sprite = fillSprites[DisplayedStage];
            basketIcon.preserveAspect = true;
        }
    }
}
