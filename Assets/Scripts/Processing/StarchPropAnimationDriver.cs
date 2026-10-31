using System;
using UnityEngine;

public enum StarchAnimationId
{
    BlenderOpen, BlenderClose, BlenderStart, BlenderWorking, BlenderEnd,
    BasketFill, BasketWorking, TrayFill, HandleOne, HandleTwo, BasketDrop, DryBasketDrop
}

/// <summary>Evaluates the imported transform curves independently, without looping or pose blending.</summary>
[DisallowMultipleComponent]
public sealed class StarchPropAnimationDriver : MonoBehaviour
{
    [Serializable]
    public sealed class TransformTrack
    {
        public Transform target;
        public AnimationCurve px, py, pz, rx, ry, rz, rw, sx, sy, sz;

        private static float Value(AnimationCurve curve, float time, float fallback)
            => curve != null && curve.length > 0 ? curve.Evaluate(time) : fallback;

        public void Apply(float time)
        {
            if (!target) return;
            Vector3 p = target.localPosition, s = target.localScale;
            Quaternion r = target.localRotation;
            target.localPosition = new Vector3(Value(px, time, p.x), Value(py, time, p.y), Value(pz, time, p.z));
            target.localScale = new Vector3(Value(sx, time, s.x), Value(sy, time, s.y), Value(sz, time, s.z));
            if (rx != null && rx.length > 0)
                target.localRotation = new Quaternion(Value(rx, time, r.x), Value(ry, time, r.y),
                    Value(rz, time, r.z), Value(rw, time, r.w)).normalized;
        }
    }

    [Serializable]
    public sealed class ClipPose
    {
        public StarchAnimationId id;
        public AnimationClip source;
        public TransformTrack[] tracks;
    }

    [SerializeField] private Animator sourceAnimator;
    [SerializeField] private ClipPose[] clips = Array.Empty<ClipPose>();
    private readonly ClipPose[] heldClips = new ClipPose[8];
    private readonly float[] heldTimes = new float[8];

    public void Configure(Animator animator, ClipPose[] poses)
    {
        sourceAnimator = animator;
        clips = poses;
        if (sourceAnimator) sourceAnimator.enabled = false;
        ResetPose();
    }

    private void Awake()
    {
        if (sourceAnimator) sourceAnimator.enabled = false;
        ResetPose();
    }

    private ClipPose Find(StarchAnimationId id)
    {
        foreach (var clip in clips) if (clip.id == id) return clip;
        throw new InvalidOperationException($"Missing starch animation: {id} on {name}");
    }

    public float Length(StarchAnimationId id) => Find(id).source.length;

    private static int Channel(StarchAnimationId id) => id switch
    {
        StarchAnimationId.BlenderOpen or StarchAnimationId.BlenderClose => 0,
        StarchAnimationId.BlenderStart or StarchAnimationId.BlenderWorking or StarchAnimationId.BlenderEnd => 1,
        StarchAnimationId.BasketFill or StarchAnimationId.BasketWorking => 2,
        StarchAnimationId.TrayFill => 3,
        StarchAnimationId.HandleOne => 4,
        StarchAnimationId.HandleTwo => 5,
        StarchAnimationId.DryBasketDrop => 7,
        _ => 6
    };

    public void SetTime(StarchAnimationId id, double seconds, ProcessingCarryRig detachedRig = null)
    {
        var clip = Find(id);
        int channel = Channel(id);
        heldClips[channel] = clip;
        heldTimes[channel] = Mathf.Clamp((float)seconds, 0f, clip.source.length);
        Apply(clip, heldTimes[channel]);
        if (detachedRig)
            foreach (var track in clip.tracks) detachedRig.SyncPoseFromSource(track.target);
    }

    public float HeldTime(StarchAnimationId id)
    {
        int channel = Channel(id);
        return heldClips[channel] != null && heldClips[channel].id == id ? heldTimes[channel] : -1f;
    }

    public void ResetPose()
    {
        Array.Clear(heldClips, 0, heldClips.Length);
        foreach (var clip in clips)
        {
            if (clip.id == StarchAnimationId.BlenderClose || clip.id == StarchAnimationId.BlenderWorking ||
                clip.id == StarchAnimationId.BlenderEnd || clip.id == StarchAnimationId.BasketWorking) continue;
            // The dry-drop clip begins on the upper supports and ends in the lower slot.
            SetTime(clip.id, clip.id == StarchAnimationId.DryBasketDrop ? clip.source.length : 0d);
        }
    }

    private static void Apply(ClipPose clip, float time)
    {
        foreach (var track in clip.tracks) track.Apply(time);
    }

    private void LateUpdate()
    {
        for (int index = 0; index < heldClips.Length; index++)
            if (heldClips[index] != null) Apply(heldClips[index], heldTimes[index]);
    }
}
