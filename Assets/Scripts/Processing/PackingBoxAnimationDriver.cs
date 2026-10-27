using System;
using UnityEngine;

/// <summary>Samples only authored channels so the packing pulse preserves the folded walls.</summary>
[DisallowMultipleComponent]
public sealed class PackingBoxAnimationDriver : MonoBehaviour
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
        public AnimationClip source;
        public TransformTrack[] tracks = Array.Empty<TransformTrack>();

        public void Apply(float time)
        {
            foreach (var track in tracks) track.Apply(time);
        }
    }

    [SerializeField] private Animator sourceAnimator;
    [SerializeField] private ClipPose folding = new ClipPose();
    [SerializeField] private ClipPose packing = new ClipPose();
    private float foldTime;
    private float packTime;

    public float FoldLength => folding.source ? folding.source.length : 0f;
    public float PackingLength => packing.source ? packing.source.length : 0f;

    public void Configure(Animator animator, ClipPose fold, ClipPose pack)
    {
        sourceAnimator = animator;
        folding = fold;
        packing = pack;
        if (sourceAnimator) sourceAnimator.enabled = false;
        SetPose(0f, 0f);
    }

    private void Awake()
    {
        if (sourceAnimator) sourceAnimator.enabled = false;
        SetPose(0f, 0f);
    }

    public void SetPose(float foldingSeconds, float packingSeconds)
    {
        foldTime = Mathf.Clamp(foldingSeconds, 0f, FoldLength);
        packTime = Mathf.Clamp(packingSeconds, 0f, PackingLength);
        ApplyPose();
    }

    private void ApplyPose()
    {
        folding.Apply(foldTime);
        packing.Apply(packTime);
    }

    private void LateUpdate() => ApplyPose();
}
