using System;
using System.IO;
using UnityEngine;

// Use the existing inventory save contract so older saves without a processor record still load.
public sealed partial class PotatoStarchProcessor : IInventorySaveTarget
{
    [Serializable]
    private sealed class SavedState
    {
        public StarchBlenderState blender;
        public StarchSieveState sieve;
        public StarchTrayState tray;
        public StarchWaterBasketState water;
        public int loaded, fill, remaining, lastProduced, totalProduced;
        public double blenderElapsed, makerElapsed, dryingRemaining;
    }

    private PotatoStorageSaveAdapter storagePersistence;
    private string lastCheckpoint;
    private string lastSaveAttempt;
    public string OwnerId => "processor:" + SaveIdentity.Key(this);

    private void BindPersistence()
    {
        if (!storagePersistence) storagePersistence = FindAnyObjectByType<PotatoStorageSaveAdapter>();
        if (!storagePersistence) return;
        storagePersistence.Register(this);
        MarkPersistenceCheckpoint();
    }

    internal void MarkPersistenceCheckpoint()
    {
        lastCheckpoint = Fingerprint();
        lastSaveAttempt = null;
    }

    private string Fingerprint() => $"{BlenderState}|{SieveState}|{TrayState}|{WaterBasketState}|{LoadedPotatoCount}|{SieveFillCount}|{RemainingPackCount}|{TotalProducedPackCount}";

    private void LateUpdate()
    {
        if (!storagePersistence || !storagePersistence.IsReady || !storagePersistence.IsOffline) return;
        string fingerprint = Fingerprint();
        if (fingerprint == lastCheckpoint || fingerprint == lastSaveAttempt) return;
        lastSaveAttempt = fingerprint;
        if (storagePersistence.SaveActive()) MarkPersistenceCheckpoint();
    }

    public InventoryData CaptureInventory()
    {
        EnsureInitialized();
        var state = new SavedState
        {
            blender = BlenderState, sieve = SieveState, tray = TrayState, water = WaterBasketState,
            loaded = LoadedPotatoCount, fill = SieveFillCount, remaining = RemainingPackCount,
            lastProduced = LastProducedPackCount, totalProduced = TotalProducedPackCount,
            blenderElapsed = Math.Max(0d, Now - blenderStartedAt),
            makerElapsed = Math.Max(0d, Now - makerStartedAt),
            dryingRemaining = TrayState == StarchTrayState.Drying ? Math.Max(0d, dryingEndsAt - Now) : 0d
        };
        var data = new InventoryData { ownerId = OwnerId };
        data.items.Add(new InventoryItemData { itemId = "starch_processor_state", quantity = 1,
            propertiesVersion = 1, propertiesJson = JsonUtility.ToJson(state) });
        return data;
    }

    public void ValidateInventory(InventoryData data) => ReadSavedState(data);

    private SavedState ReadSavedState(InventoryData data)
    {
        if (data == null || data.ownerId != OwnerId || data.items == null || data.items.Count > 1)
            throw new InvalidDataException("Invalid starch processor record.");
        if (data.items.Count == 0) return new SavedState(); // Original saves have no processor state.
        var item = data.items[0];
        if (item == null || item.itemId != "starch_processor_state" || item.quantity != 1 ||
            item.propertiesVersion != 1 || item.slot != -1 || !string.IsNullOrEmpty(item.instanceId) || string.IsNullOrWhiteSpace(item.propertiesJson))
            throw new InvalidDataException("Unsupported starch processor record.");
        var state = JsonUtility.FromJson<SavedState>(item.propertiesJson);
        if (state == null || !Enum.IsDefined(typeof(StarchBlenderState), state.blender) ||
            !Enum.IsDefined(typeof(StarchSieveState), state.sieve) || !Enum.IsDefined(typeof(StarchTrayState), state.tray) ||
            !Enum.IsDefined(typeof(StarchWaterBasketState), state.water) ||
            state.loaded < 0 || state.loaded > potatoesPerBatch || state.fill < 0 || state.fill > Capacity ||
            state.remaining < 0 || state.remaining > starchPacksPerCycle || state.lastProduced < 0 || state.totalProduced < 0 ||
            !FiniteNonnegative(state.blenderElapsed) || !FiniteNonnegative(state.makerElapsed) || !FiniteNonnegative(state.dryingRemaining))
            throw new InvalidDataException("Invalid starch processor state.");
        return state;
    }

    private static bool FiniteNonnegative(double value) => value >= 0d && !double.IsInfinity(value) && !double.IsNaN(value);

    public void RestoreInventory(InventoryData data)
    {
        var saved = ReadSavedState(data);
        ResetCycle();
        initialized = true;
        LoadedPotatoCount = saved.loaded;
        BlenderState = saved.blender;
        SieveState = saved.sieve;
        TrayState = saved.tray;
        WaterBasketState = saved.water;
        SieveFillCount = saved.fill;
        RemainingPackCount = saved.remaining;
        LastProducedPackCount = saved.lastProduced;
        TotalProducedPackCount = saved.totalProduced;
        blenderStartedAt = Now - saved.blenderElapsed;
        makerStartedAt = Now - saved.makerElapsed;
        dryingEndsAt = Now + saved.dryingRemaining;
        nextDryingLogAt = Math.Min(dryingEndsAt, Now + dryingLogIntervalSeconds);

        // Held objects return to a safe pickup point; no runtime player/hand reference is serialized.
        if (BlenderState == StarchBlenderState.CarriedFull) BlenderState = StarchBlenderState.GroundReady;
        if (BlenderState == StarchBlenderState.CarriedEmpty)
        { BlenderState = StarchBlenderState.DockedEmpty; LoadedPotatoCount = 0; }
        if (TrayState == StarchTrayState.Packing) TrayState = StarchTrayState.ReadyToPack;
        if (TrayState == StarchTrayState.CarriedWet)
        {
            SieveState = StarchSieveState.WetStarchReady;
            TrayState = StarchTrayState.InstalledWet;
            WaterBasketState = StarchWaterBasketState.Docked;
        }
        if (SieveState == StarchSieveState.DryDropping || SieveState == StarchSieveState.WaitingForBasketReturn)
        {
            ResetCycle();
            LastProducedPackCount = saved.lastProduced;
            TotalProducedPackCount = saved.totalProduced;
        }
        else if (SieveState == StarchSieveState.DryBasketCarried || SieveState == StarchSieveState.DryHandlesMoving)
        {
            SieveState = StarchSieveState.ReadyToPack;
            WaterBasketState = StarchWaterBasketState.Lowered;
        }
        RestoreSavedPose();
        EvaluateTimers();
        MarkPersistenceCheckpoint();
    }

    private void RestoreSavedPose()
    {
        if (BlenderState == StarchBlenderState.AwaitingPotatoes)
            blenderAnimation.SetTime(StarchAnimationId.BlenderOpen, BlenderLength(StarchAnimationId.BlenderOpen));
        else if (BlenderState != StarchBlenderState.DockedEmpty && BlenderState != StarchBlenderState.LidOpening)
        {
            blenderAnimation.SetTime(StarchAnimationId.BlenderClose, BlenderLength(StarchAnimationId.BlenderClose));
            if (BlenderState == StarchBlenderState.GroundReady)
                blenderAnimation.SetTime(StarchAnimationId.BlenderEnd, BlenderLength(StarchAnimationId.BlenderEnd));
        }
        makerAnimation.SetTime(StarchAnimationId.BasketFill, MakerLength(StarchAnimationId.BasketFill) * SieveFillCount / Capacity);
        if (SieveState >= StarchSieveState.WetStarchReady)
            makerAnimation.SetTime(StarchAnimationId.TrayFill, MakerLength(StarchAnimationId.TrayFill));
        if (WaterBasketState == StarchWaterBasketState.Lowered)
            makerAnimation.SetTime(StarchAnimationId.BasketDrop, MakerLength(StarchAnimationId.BasketDrop));
        if (TrayState == StarchTrayState.Drying || TrayState == StarchTrayState.ReadyToPack || TrayState == StarchTrayState.EmptyOnTop)
        {
            makerAnimation.SetTime(StarchAnimationId.TrayFill, MakerLength(StarchAnimationId.TrayFill) *
                (TrayState == StarchTrayState.Drying ? 1f : (float)RemainingPackCount / starchPacksPerCycle));
            dryingTray.CarryItem.RestorePlacedAt(dryingAnchor);
        }
        waterBasket.RefreshCollider();
    }

    private void OnDestroy()
    {
        if (storagePersistence) storagePersistence.Unregister(this);
    }
}
