using System;
using System.Collections.Generic;

namespace StoryFlow.Lipsync
{
    /// <summary>
    /// Audio in, mouth pose out. The whole of Tier 1's judgement lives here, and NOTHING in this file touches
    /// Unity — it takes a spectrum array and a delta time and answers morph weights, so the numbers that decide
    /// whether a mouth reads as speech can be tested without an engine, a scene or a face.
    ///
    /// It is deliberately NOT phoneme recognition. Energy says how open the mouth is; the spectral centroid
    /// slides it along OO → OH → AA → EE. That reads convincingly against real speech and will not hit a
    /// specific consonant, which is the honest trade for zero baking and zero dependencies. The baked-track
    /// tier replaces the analysis and reuses everything else here.
    ///
    /// Every constant is pinned by the normative spec in LIPSYNC_DESIGN.md. See that document before changing
    /// one, because the Unreal arm implements the same numbers.
    /// </summary>
    public sealed class StoryFlowLipsyncDriver
    {
        /// <summary>The analysed band. Below is room rumble; above is sibilance that would open a jaw on an S.</summary>
        public const float MinHz = 90f;
        public const float MaxHz = 4200f;

        /// <summary>Speech never reaches the top of the band, so an unscaled centroid never leaves the OO end.</summary>
        private const float CentroidScale = 2.6f;

        /// <summary>Per-frame decay of the loudness peak, so a quiet take still reaches a full-open mouth.</summary>
        private const float PeakDecay = 0.9992f;
        private const float PeakFloor = 0.04f;
        private const float PeakInitial = 0.12f;

        private const float GateStart = 0.10f;
        private const float GateRange = 0.22f;
        private const float ClosingBreath = 0.45f;

        // Tunables. Defaults are the three.js build's, which is the point of them.
        public float Strength = 0.55f;
        public float Sensitivity = 1f;
        public float JawBias = 1f;
        public float Smooth = 16f;

        private readonly Dictionary<string, Dictionary<string, float>> _table;
        private readonly Dictionary<string, float> _current = new Dictionary<string, float>();
        private readonly Dictionary<string, float> _target = new Dictionary<string, float>();
        private float _peak = PeakInitial;

        // Idle mouth state — a line with no analysable audio still has to move.
        private readonly Random _random;
        private string _idlePose = "rest";
        private float _idleHold;

        public StoryFlowLipsyncDriver(Dictionary<string, Dictionary<string, float>> table, int randomSeed = 0)
        {
            _table = table ?? StoryFlowVisemeTable.Default();
            _random = randomSeed == 0 ? new Random() : new Random(randomSeed);
            foreach (var morph in StoryFlowVisemeTable.OwnedMorphs(_table)) _current[morph] = 0f;
        }

        /// <summary>The weights to write this frame. The same instance every call — do not hold on to it.</summary>
        public IReadOnlyDictionary<string, float> Current => _current;

        /// <summary>Loudness 0..1 after the peak follower, for a level meter. Not part of the pose.</summary>
        public float Level { get; private set; }

        /// <summary>
        /// Advance from a spectrum: magnitudes in 0..1, linear bins from 0 Hz to `sampleRate / 2`.
        ///
        /// The peak follower means the FIRST moments of a clip under-open the mouth while it learns the
        /// loudness. That is deliberate and matches the reference: resetting it per line is what makes a
        /// whisper after a shout look like a shout.
        /// </summary>
        public void AdvanceFromSpectrum(float[] spectrum, float sampleRate, float dt)
        {
            if (spectrum == null || spectrum.Length == 0 || sampleRate <= 0f)
            {
                AdvanceSilent(dt);
                return;
            }

            var binHz = (sampleRate * 0.5f) / spectrum.Length;
            var lo = Math.Max(1, (int)(MinHz / binHz));
            var hi = Math.Min(spectrum.Length, (int)(MaxHz / binHz));
            if (hi <= lo)
            {
                AdvanceSilent(dt);
                return;
            }

            float sum = 0f, weighted = 0f;
            for (var i = lo; i < hi; i++)
            {
                var a = spectrum[i];
                sum += a;
                weighted += a * i;
            }

            var energy = sum / (hi - lo);
            var centroid = 0f;
            if (sum > 0f)
            {
                centroid = Clamp01(((weighted / sum) - lo) / (hi - lo) * CentroidScale);
            }

            _peak = Math.Max(energy, _peak * PeakDecay);
            var norm = energy / Math.Max(PeakFloor, _peak);
            Level = Math.Min(1f, norm);

            var gate = Clamp01((norm - GateStart) / GateRange);
            var amp = Math.Min(1f, norm * 1.15f * Sensitivity) * gate;

            BuildAxisPose(centroid, amp, gate);
            Ease(dt);
        }

        /// <summary>
        /// Advance with no analysable audio: the idle mouth. Random poses held briefly, with occasional gaps,
        /// so a subtitled line does not sit there with a dead face.
        /// </summary>
        public void AdvanceIdle(float dt)
        {
            _idleHold -= dt;
            if (_idleHold <= 0f)
            {
                var gap = _random.NextDouble() < 0.20;
                if (gap)
                {
                    _idlePose = _random.NextDouble() < 0.5 ? "MM" : "rest";
                    _idleHold = 0.14f + (float)_random.NextDouble() * 0.22f;
                }
                else
                {
                    var names = StoryFlowVisemeTable.PoseNames;
                    _idlePose = names[_random.Next(1, names.Length)];
                    _idleHold = 0.12f + (float)_random.NextDouble() * 0.13f;
                }
            }

            ClearTarget();
            if (_table.TryGetValue(_idlePose, out var pose))
            {
                foreach (var kvp in pose)
                {
                    _target[kvp.Key] = kvp.Value * Strength * (kvp.Key == "jawOpen" ? JawBias : 1f);
                }
            }
            Ease(dt);
        }

        /// <summary>Close the mouth: what a component does when its line ends, rather than freezing mid-vowel.</summary>
        public void AdvanceSilent(float dt)
        {
            Level = 0f;
            ClearTarget();
            Ease(dt);
        }

        /// <summary>Forget the loudness history. Call at the START of a line so takes do not scale each other.</summary>
        public void ResetLevel()
        {
            _peak = PeakInitial;
            Level = 0f;
        }

        private void BuildAxisPose(float centroid, float amp, float gate)
        {
            ClearTarget();

            var axis = StoryFlowVisemeTable.Axis;
            var f = centroid * (axis.Length - 1);
            var i0 = (int)Math.Floor(f);
            if (i0 < 0) i0 = 0;
            if (i0 > axis.Length - 1) i0 = axis.Length - 1;
            var i1 = Math.Min(axis.Length - 1, i0 + 1);
            var t = f - i0;

            _table.TryGetValue(axis[i0], out var a);
            _table.TryGetValue(axis[i1], out var b);

            AccumulateBlend(a, 1f - t, amp);
            AccumulateBlend(b, t, amp);

            // The closing breath: as the gate shuts, the lips come together instead of hanging half-open.
            if (gate < 1f)
            {
                _target.TryGetValue("mouthClose", out var closed);
                _target["mouthClose"] = closed + (1f - gate) * ClosingBreath * Strength;
            }
        }

        private void AccumulateBlend(Dictionary<string, float> pose, float share, float amp)
        {
            if (pose == null || share <= 0f) return;
            foreach (var kvp in pose)
            {
                var scale = amp * Strength * (kvp.Key == "jawOpen" ? JawBias : 1f);
                _target.TryGetValue(kvp.Key, out var have);
                _target[kvp.Key] = have + kvp.Value * share * scale;
            }
        }

        private void ClearTarget()
        {
            foreach (var morph in _current.Keys) _target[morph] = 0f;
        }

        /// <summary>
        /// Frame-rate independent easing. A raw per-frame lerp constant would make the mouth snappier at 144 Hz
        /// than at 30, which is precisely the kind of thing nobody notices until the tuning refuses to hold.
        /// </summary>
        private void Ease(float dt)
        {
            var k = dt <= 0f ? 1f : 1f - (float)Math.Exp(-Smooth * dt);
            foreach (var morph in _keys(_current))
            {
                _target.TryGetValue(morph, out var want);
                _current[morph] += (want - _current[morph]) * k;
            }
        }

        // Iterating a dictionary while assigning into it is legal for existing keys, but only through a
        // snapshot of the keys — assigning during enumeration of .Keys itself throws.
        private static List<string> _keys(Dictionary<string, float> from)
        {
            return new List<string>(from.Keys);
        }

        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            return v > 1f ? 1f : v;
        }
    }
}
