using System;

/// <summary>The basket removal and processor insertion are one persisted operation.</summary>
public static class PotatoProcessingService
{
    public static bool TryInsert(PotatoInventory basket, long potatoId, PotatoStarchProcessor processor,
        Func<bool> save, out string error)
    {
        error = null;
        var potato = basket?.FindPotato(potatoId);
        if (potato == null || !processor || save == null) { error = "선택한 감자를 찾을 수 없습니다."; return false; }
        if (!potato.CanProcess)
        {
            error = potato.Conditions != HarvestedPotatoCondition.None
                ? "상태이상 감자는 넣을 수 없습니다." : "1성, 2성, 3성 감자만 넣을 수 있습니다.";
            return false;
        }
        if (!processor.CanReceivePotato) { error = "믹서기가 감자 투입 대기 상태가 아닙니다."; return false; }
        int before = processor.LoadedPotatoCount;
        bool success = basket.TryConsume(potatoId, () => processor.AcceptPotato(potato) && save());
        if (success) { processor.MarkPersistenceCheckpoint(); return true; }
        processor.RestoreLoadingCount(before);
        error = "투입 저장에 실패하여 감자와 믹서기를 복구했습니다.";
        return false;
    }
}
