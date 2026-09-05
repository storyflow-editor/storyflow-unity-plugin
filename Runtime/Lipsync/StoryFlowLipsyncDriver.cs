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
    /// Every constant is pinned by the normative v2 update rule in LIPSYNC_DESIGN.md. See that document before
    /// changing one, because the Unreal arm implements the same numbers in the same order.
    ///
    /// THE REFERENCE DOMAIN is the thing to understand before touching any of it. The constants come from a
    /// three.js build that never saw linear FFT magnitudes: it read `getByteFrequencyData()/255`, which is Web
    /// Audio's DECIBEL mapping (-100 dB → 0, -30 dB → 1) after the analyser's own per-bin smoothing. On that
    /// scale speech sits at 0.2–0.6, which is where the peak floor and the gate were placed. Linear magnitudes
    /// from an engine sit twenty times lower and the gate never opens, so step 1 below converts every bin back
    /// into the reference's domain and step 2 reproduces its smoothing. Then every later constant means what
    /// it meant.
    /// </summary>
    public sealed class StoryFlowLipsyncDriver
    {
        /// <summary>The analysed band. Below is room rumble; above is sibilance that would open a jaw on an S.</summary>
        public const float MinHz = 90f;
        public const float MaxHz = 4200f;

        /// <summary>Speech never reaches the top of the band, so an unscaled centroid never leaves the OO end.</summary>
        private const float CentroidScale = 2.6f;

        /// <summary>
        /// Decay of the loudness peak, so a quiet take still reaches a full-open mouth. Written per SECOND:
        /// the reference decayed by this much per frame at roughly 60 Hz, so the exponent carries `dt * 60`.
        /// Per-frame decay would give a 42 s time constant at 30 fps and 9 s at 144.
        /// </summary>
        private const float PeakDecayPer60Hz = 0.9992f;
        private const float PeakFloor = 0.04f;
        private const float PeakInitial = 0.12f;

        /// <summary>The reference analyser's per-bin temporal smoothing (`smoothingTimeConstant = .55`).</summary>
        private const float SpectralKeep = 0.55f;
        private const float SpectralTake = 0.45f;

        private const float GateStart = 0.10f;
        private const float GateRange = 0.22f;
        private const float ClosingBreath = 0.45f;

        // Tunables. Defaults are the three.js build's, which is the point of them.
        public float Strength = 0.55f;
        public float Sensitivity = 1f;
        public float JawBias = 1f;
        public float Smooth = 16f;

        /// <summary>
        /// The raw magnitude a full-scale sine produces at its own bin in the engine feeding this driver, i.e.
        /// the 0 dB reference for step 1. Unity's `GetSpectrumData` is normalised, so 1 is right there; an
        /// engine whose FFT is scaled differently sets this instead of rescaling the constants.
        /// </summary>
        public float FullScale = 1f;

        private readonly Dictionary<string, Dictionary<string, float>> _table;

        // The owned morphs, flattened. `_keys` is fixed at construction and `_weights` / `_target` are parallel
        // to it, so nothing on the per-frame path allocates or looks a name up: the component resolves each key
        // INDEX to a blendshape index once and then reads by index forever.
        private readonly string[] _keys;
        private readonly Dictionary<string, int> _slots;
        private readonly float[] _weights;
        private readonly float[] _target;
        private readonly Dictionary<string, float> _current = new Dictionary<string, float>();

        /// <summary>Where the closing breath goes, or -1 when no pose in the table owns `mouthClose`.</summary>
        private readonly int _mouthCloseSlot;

        private float _peak = PeakInitial;

        /// <summary>Per-bin state for the reference analyser's smoothing. Re-made when the bin count changes.</summary>
        private float[] _smoothed;

        // Idle mouth state — a line with no analysable audio still has to move.
        private readonly Random _random;
        private readonly string[] _idlePool;
        private readonly bool _idleHasMM;
        private string _idlePose = "rest";
        private float _idleHold;

        public StoryFlowLipsyncDriver(Dictionary<string, Dictionary<string, float>> table, int randomSeed = 0)
        {
            _table = table ?? StoryFlowVisemeTable.Default();
            _random = randomSeed == 0 ? new Random() : new Random(randomSeed);

            var owned = new List<string>(StoryFlowVisemeTable.OwnedMorphs(_table));
            owned.Sort(StringComparer.Ordinal);
            _keys = owned.ToArray();
            _weights = new float[_keys.Length];
            _target = new float[_keys.Length];
            _slots = new Dictionary<string, int>(_keys.Length);
            _mouthCloseSlot = -1;
            for (var i = 0; i < _keys.Length; i++)
            {
                _slots[_keys[i]] = i;
                _current[_keys[i]] = 0f;
                if (_keys[i] == "mouthClose") _mouthCloseSlot = i;
            }

            // The idle pool is THIS table's poses, not the built-in names: a custom map with four poses must
            // idle on those four, not spend five picks in nine on names it has never heard of. Sorted
            // ordinally so both engine arms walk the same list.
            var pool = new List<string>();
            foreach (var pose in _table.Keys)
            {
                if (pose != "rest") pool.Add(pose);
            }
            pool.Sort(StringComparer.Ordinal);
            _idlePool = pool.ToArray();
            _idleHasMM = _table.ContainsKey("MM");
        }

        /// <summary>
        /// The weights to write this frame, by name. Built on demand for tests and tooling; the per-frame
        /// path reads by index through <see cref="Keys"/> and <see cref="GetWeight"/> and never touches this.
        /// The same instance every call — do not hold on to it.
        /// </summary>
        public IReadOnlyDictionary<string, float> Current
        {
            get
            {
                for (var i = 0; i < _keys.Length; i++) _current[_keys[i]] = _weights[i];
                return _current;
            }
        }

        /// <summary>Every morph this driver writes, in a fixed order. Resolve these to engine handles ONCE.</summary>
        public IReadOnlyList<string> Keys => _keys;

        /// <summary>This frame's weight for `Keys[index]`. The per-frame read: no strings, no lookups.</summary>
        public float GetWeight(int index)
        {
            return index >= 0 && index < _weights.Length ? _weights[index] : 0f;
        }

        /// <summary>Loudness 0..1 after the peak follower, for a level meter. Not part of the pose.</summary>
        public float Level { get; private set; }

        /// <summary>
        /// Where the last analysed frame sat on the vowel axis, 0 (OO) to 1 (EE). The second meter: a mouth
        /// that opens but looks wrong is usually a centroid pinned at one end, and this says which.
        /// </summary>
        public float Centroid { get; private set; }

        /// <summary>
        /// The loudest RAW magnitude seen in the band since ResetLevel, before step 1's transform. This is the
        /// calibration instrument: an engine whose FullScale is unknown reads it off a full-volume line.
        /// </summary>
        public float RawPeak { get; private set; }

        /// <summary>
        /// Advance from a spectrum: linear magnitudes, bins from 0 Hz to `sampleRate / 2`.
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

            var n = hi - lo;
            if (_smoothed == null || _smoothed.Length != n) _smoothed = new float[n];

            // 2's coefficient, PER SECOND at the reference's ~60 Hz like the peak follower below, so the
            // analyser's lag does not double at 30 fps and halve at 120. A zero delta keeps every bin as it was.
            var keep = dt > 0f ? (float)Math.Pow(SpectralKeep, dt * 60f) : 1f;

            float sum = 0f, weighted = 0f, raw = 0f;
            for (var j = 0; j < n; j++)
            {
                // A NaN or infinite sample would sit in the smoothing state and in the peak and turn every
                // weight after it into NaN for the rest of the line. Read it as silence instead.
                var m = spectrum[lo + j];
                if (!(m > 0f) || float.IsInfinity(m)) m = 0f;
                if (m > raw) raw = m;

                // 1. reference domain, per bin, and 2. the reference analyser's smoothing.
                var a = Clamp01((20f * (float)Math.Log10(Math.Max(m, 1e-9f) / FullScale) + 100f) / 70f);
                var s = keep * _smoothed[j] + (1f - keep) * a;
                _smoothed[j] = s;

                sum += s;
                weighted += s * j;
            }
            if (raw > RawPeak) RawPeak = raw;

            // 3. energy and centroid, over the SMOOTHED reference-domain bins.
            var energy = sum / n;
            var centroid = 0f;
            if (sum > 0f && n > 1)
            {
                centroid = Clamp01((weighted / sum) / (n - 1) * CentroidScale);
            }
            Centroid = centroid;

            // 4. peak follower, per second.
            _peak = Math.Max(energy, _peak * (float)Math.Pow(PeakDecayPer60Hz, dt * 60f));
            var norm = energy / Math.Max(PeakFloor, _peak);
            Level = Math.Min(1f, norm);

            // 5-6. gate and amplitude.
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
            // The meters report ANALYSED audio and nothing else; an idle mouth is not loudness.
            Level = 0f;
            Centroid = 0f;

            _idleHold -= dt;
            if (_idleHold <= 0f)
            {
                if (_idlePool.Length == 0 || _random.NextDouble() < 0.20)
                {
                    // The gap. `MM` is a closed mouth only if this table has one — never write a pose name
                    // the table cannot answer, because that is a dead hold with no diagnostic.
                    _idlePose = _idleHasMM && _random.NextDouble() < 0.5 ? "MM" : "rest";
                    _idleHold = 0.14f + (float)_random.NextDouble() * 0.22f;
                }
                else
                {
                    _idlePose = _idlePool[_random.Next(_idlePool.Length)];
                    _idleHold = 0.12f + (float)_random.NextDouble() * 0.13f;
                }
            }

            ClearTarget();
            if (_table.TryGetValue(_idlePose, out var pose))
            {
                foreach (var kvp in pose)
                {
                    if (!_slots.TryGetValue(kvp.Key, out var slot)) continue;
                    _target[slot] = kvp.Value * Strength * (kvp.Key == "jawOpen" ? JawBias : 1f);
                }
            }
            Ease(dt);
        }

        /// <summary>Close the mouth: what a component does when its line ends, rather than freezing mid-vowel.</summary>
        public void AdvanceSilent(float dt)
        {
            Level = 0f;
            Centroid = 0f;
            ClearTarget();
            Ease(dt);
        }

        /// <summary>Forget the loudness history. Call at the START of a line so takes do not scale each other.</summary>
        public void ResetLevel()
        {
            _peak = PeakInitial;
            Level = 0f;
            RawPeak = 0f;
            if (_smoothed != null) Array.Clear(_smoothed, 0, _smoothed.Length);
        }

        // 7. the vowel axis, and 8. the closing breath.
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
            // Only where a pose owns `mouthClose` — on a table without it the key would be written and never
            // read, which is the step silently skipped rather than a mouth that closes.
            if (gate < 1f && _mouthCloseSlot >= 0)
            {
                _target[_mouthCloseSlot] += (1f - gate) * ClosingBreath * Strength;
            }
        }

        private void AccumulateBlend(Dictionary<string, float> pose, float share, float amp)
        {
            if (pose == null || share <= 0f) return;
            foreach (var kvp in pose)
            {
                if (!_slots.TryGetValue(kvp.Key, out var slot)) continue;
                var scale = amp * Strength * (kvp.Key == "jawOpen" ? JawBias : 1f);
                _target[slot] += kvp.Value * share * scale;
            }
        }

        private void ClearTarget()
        {
            Array.Clear(_target, 0, _target.Length);
        }

        /// <summary>
        /// 9. Frame-rate independent easing. A raw per-frame lerp constant would make the mouth snappier at
        /// 144 Hz than at 30, which is precisely the kind of thing nobody notices until the tuning refuses to
        /// hold. A dt of zero HOLDS: a paused game keeps the face it had rather than snapping to the target,
        /// which is what a k of 1 would do on every still frame.
        /// </summary>
        private void Ease(float dt)
        {
            var k = dt <= 0f ? 0f : 1f - (float)Math.Exp(-Smooth * dt);
            for (var i = 0; i < _weights.Length; i++)
            {
                // The clamp is not decoration: JawBias 2 asks for 1.7 on an AA, and both engines extrapolate
                // a shape past 1 rather than ignoring it.
                _weights[i] = Clamp01(_weights[i] + (_target[i] - _weights[i]) * k);
            }
        }

        /// <summary>NaN-safe: a NaN fails both comparisons in the naive form and would pass through as NaN.</summary>
        private static float Clamp01(float v)
        {
            if (!(v > 0f)) return 0f;
            return v > 1f ? 1f : v;
        }
    }
}
