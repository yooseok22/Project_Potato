using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Project Potato/Tablet Catalog")]
public sealed class TabletCatalog : ScriptableObject
{
    [Serializable] public sealed class Product
    {
        public string id, title, description, grantId;
        public int price = 100, grantQuantity = 1;
        public bool machine;
        public int machineKind;
    }
    [Serializable] public sealed class Research
    {
        public string id, title, description;
        public int branch, tier, cost = 20, prerequisite = -1;
        public Vector2 position;
    }
    [Serializable] public sealed class Guide
    {
        public string title;
        public int category;
        [TextArea] public string[] pages;
    }
    public long startingMoney = 1000;
    public int startingResearchPoints = 100, startingSeeds = 100;
    public Product[] products;
    public Research[] research;
    public Guide[] guides;
    public Sprite[] artwork;
    public Sprite roundedPanel, roundIcon, checker;

    public Sprite Art(string name)
    {
        if (artwork != null) foreach (var sprite in artwork) if (sprite && sprite.name == name) return sprite;
        return null;
    }

    public static void SetDefaults(TabletCatalog catalog)
    {
        var names = new[] { "씨감자", "포장지", "씨감자 2개", "씨감자 3개", "포장지 2개", "포장지 3개", "씨감자 5개", "씨감자 10개", "씨감자 20개", "씨감자 50개", "포장지 5개", "포장지 10개" };
        var ids = new[] { "seed", "wrapper", "seed-2", "seed-3", "wrapper-2", "wrapper-3", "seed-5", "seed-10", "seed-20", "seed-50", "wrapper-5", "wrapper-10" };
        catalog.products = new Product[19];
        for (int i = 0; i < names.Length; i++)
        {
            int quantity = new[] { 1, 1, 2, 3, 2, 3, 5, 10, 20, 50, 5, 10 }[i];
            string grant = ids[i].StartsWith("seed") ? "seed" : "wrapper";
            catalog.products[i] = new Product { id = ids[i], title = names[i], grantId = grant, grantQuantity = quantity,
                price = quantity * (grant == "wrapper" ? 20 : 100), description = grant == "seed" ? "‘씨감자’는 감자의 씨앗으로, 밭에 심으면 감자가 자란다." :
                    "전분 포장에 사용하는 포장지입니다. 작업대의 포장지 묶음에서 [E]로 한 장씩 꺼낼 수 있습니다." };
        }
        var machines = new[] { "발전기", "물탱크", "스프링클러", "작물 이동 레일", "파종 로봇 충전소", "방제 드론 충전소", "멀칭 로봇 충전소" };
        for (int i = 0; i < machines.Length; i++) catalog.products[12 + i] = new Product {
            id = "machine-" + i, grantId = "machine-" + i, title = machines[i], price = 100 + i * 50,
            machine = true, machineKind = i, description = machines[i] + "입니다. [B] 농장 자동화에서 보유 대수만큼 설치할 수 있습니다. 철거하면 다시 설치할 수 있습니다." };
        string[] branches = { "감자 성장 속도", "작물 생명력", "벌레 예방", "상점 할인", "수확량 증가" };
        catalog.research = new Research[15];
        Vector2[] positions = { new(65, 160), new(245, 75), new(300, 270), new(130, 400), new(400, 135),
            new(485, 280), new(650, 95), new(710, 270), new(870, 160), new(920, 355), new(780, 510),
            new(955, 615), new(560, 580), new(335, 510), new(160, 640) };
        for (int i = 0; i < 15; i++)
        {
            int branch = i / 3, tier = i % 3 + 1;
            string roman = tier == 1 ? "Ⅰ" : tier == 2 ? "Ⅱ" : "Ⅲ";
            string description = branch switch {
                0 => $"감자 성장 속도를 {1f + tier * 0.25f:0.##}배 상승시킨다.",
                1 => $"새로 심는 감자의 최대 체력을 {100 + tier * 10}으로 높인다.",
                2 => $"벌레 발생 확률을 기본 확률의 {100 - tier * 25}%로 낮춘다.",
                3 => $"상점의 모든 상품 가격을 {tier * 5}% 할인한다.",
                _ => $"살아 있는 작물의 수확 감자를 {tier}개 추가한다." };
            catalog.research[i] = new Research { id = "research-" + i, title = branches[branch] + " " + roman,
                description = description, branch = branch, tier = tier, cost = tier * 20,
                prerequisite = tier == 1 ? -1 : i - 1, position = new Vector2(positions[i].x, positions[i].y * .76f + 12f) };
        }
        catalog.guides = new[] {
            G("감자 재배 방법",0,"감자는 다음과 같이 재배할 수 있다.\n\n1. 괭이로 일반 땅을 밭 땅, 구멍 땅으로 변경한다.","2. 씨감자를 준비하고 구멍을 조준한 뒤 맨손으로 [E]를 누른다.","3. 물뿌리개로 물을 주면 성장이 시작된다.","4. 큰 잎이 자라면 [E]로 흙을 덮는다. 벌레가 생겼다면 [E]로 쫓는다.","5. 싱싱한 큰 잎 6개가 되면 낫으로 잎을 벤다.","6. 감자 끌개로 감자를 드러내고 맨손 [E]로 바구니에 담는다."),
            G("상처난 감자 해결방법",0,"감자가 드러난 뒤 끌개를 다시 사용하면 상처가 생긴다.\n\n드러난 감자는 맨손 [E]로 수확하자. 추가 끌개질을 반복하면 감자가 폐기된다."),
            G("녹색 감자가 되는 이유",0,"정해진 성장 시점까지 흙을 덮지 않으면 감자가 녹색이 된다. 큰 잎이 자랐을 때 맨손 [E]로 흙을 덮거나, 초기 단계에서 멀칭을 적용하자."),
            G("감자 수확 적기는?",0,"싱싱한 큰 잎 6개가 적기 수확이다.\n\n큰 잎 6개는 이른 수확, 시든 잎 6개는 늦은 수확으로 체력 피해가 발생한다."),
            G("감자 등급이란?",0,"정상 감자는 1성·2성·3성으로 나뉜다. 체력이 높으면 수확량과 좋은 등급의 확률이 높아진다.\n\n벌레 먹음·녹색·상처·싹남 감자는 상태이상 감자로 분류된다."),
            G("감자 보관 방법",0,"바구니에는 감자 10개를 담을 수 있다.\n\n창고의 선반에서 [Shift+E]로 감자를 보관하고, [E]로 원하는 감자를 다시 꺼낸다."),
            G("감자 전분 만드는 법",1,"믹서기를 [E]로 열고 정상 감자 4개를 투입한다. 다시 [E]로 분쇄한다.","분쇄한 믹서기를 들어 빨간 통에 5회 투입한다. 전분이 아래 틀로 배출될 때까지 기다린다.","전분 틀을 들고 손잡이를 조작한 뒤 위 건조대에 놓는다. 건조가 끝나면 포장지를 들고 전분을 포장한다."),
            G("가공할 수 있는 감자",1,"상태이상이 없는 1~3성 감자만 믹서기에 넣을 수 있다.\n\n벌레 먹음·녹색·상처·싹남·썩은 감자는 가공할 수 없다."),
            G("전기와 물 연결",2,"[B]로 농장 자동화를 연다. [Z/X]로 기계를 고르고 [R]로 회전한다.\n\n상점에서 구매한 기계를 비어 있는 곳에 설치한다.","발전기나 물탱크에 [E]로 코드·호스를 꺼낸다. [우클릭]으로 경로를 고정하고 목적 기계에 [E]로 연결한다."),
            G("농사 로봇 설정",2,"충전소에 로봇이 돌아왔을 때 [E]로 작업 구역을 설정한다. 파종·방제·멀칭 로봇을 사용할 수 있다.\n\n전력이 연결되어야 정상 동작한다.") };
    }
    private static Guide G(string title, int category, params string[] pages) => new() { title = title, category = category, pages = pages };
}
