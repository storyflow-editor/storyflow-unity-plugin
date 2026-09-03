using System.Collections.Generic;
using StoryFlow;
using StoryFlow.Data;
using UnityEngine;

namespace StoryFlow.Lipsync
{
    /// <summary>
    /// Audio-driven mouth movement for one character's face. Drop it on the actor, point it at the face, tell
    /// it which StoryFlow character it is, and it moves the mouth on that character's lines. No per-line wiring.
    ///
    /// A SEPARATE COMPONENT, not part of StoryFlowComponent, and deliberately so: StoryFlow is the dialogue
    /// brain and does not know the scene. Characters are ids and data; the GAME owns which actor wears which
    /// face. Lipsync is presentational, so it lives where the face lives, and a project with no 3D faces pays
    /// nothing for it.
    ///
    /// ANY ARKIT-NAMED RIG, not just Synty. The 52 ARKit face blendshape names are the de facto interchange
    /// format for facial animation — MetaHumans take them through the Live Link Face path, and VRM, Ready
    /// Player Me, Character Creator and Audio2Face all speak them. Synty's Sidekick rigs adopted the set and
    /// extended it. The built-in table is tuned on Sidekick, but nothing here is Sidekick-specific: point this
    /// at a MetaHuman or a VRM avatar and it drives the same names.
    ///
    /// The two exceptions are the TH and L poses, which use `tongueUp` and `tongueRaise` — Synty extensions,
    /// since ARKit itself has only `tongueOut`. A rig without them drops those two with a warning and keeps
    /// the jaw and lip half of the pose, which is the usual tongue-less approximation and is invisible at
    /// game camera distance.
    ///
    /// Tier 1 (this): live amplitude analysis, works on ANY audio with zero preparation, reads convincingly but
    /// will not hit a specific consonant. Tier 2 replaces the analysis with baked viseme tracks and reuses
    /// everything else. See LIPSYNC_DESIGN.md for both, and for the normative constants the driver implements.
    /// </summary>
    [AddComponentMenu("StoryFlow/StoryFlow Lipsync")]
    public class StoryFlowLipsync : MonoBehaviour
    {
        [Header("Who is speaking")]
        [Tooltip("The dialogue component to listen to. Empty: found in the scene on enable.")]
        public StoryFlowComponent Source;

        [Tooltip("This face's StoryFlow character id. Empty: move on EVERY line, which is right for a one-character scene.")]
        public string CharacterId;

        [Header("The face")]
        [Tooltip("Root of the face meshes. Empty: this GameObject. Every SkinnedMeshRenderer beneath it with ARKit-named blendshapes is driven.")]
        public Transform FaceRoot;

        [Tooltip("Optional per-rig mapping. Empty: the built-in ARKit table, tuned on Synty Sidekick.")]
        public StoryFlowVisemeMap VisemeMap;

        [Header("Feel")]
        [Range(0f, 1f)] public float Strength = 0.55f;
        [Range(0.1f, 3f)] public float Sensitivity = 1f;
        [Range(0f, 2f)] public float JawBias = 1f;
        [Range(1f, 40f)] public float Smoothing = 16f;

        [Tooltip("Move the mouth on lines whose audio cannot be analysed, instead of leaving a dead face.")]
        public bool IdleMouthWithoutAudio = true;

        /// <summary>Unity's spectrum size must be a power of two. 512 bins over 90–4200 Hz is ample for a mouth.</summary>
        private const int SpectrumBins = 512;

        /// <summary>How often to look again when no face was found at all — see RefreshFaceIfStale.</summary>
        private const float FaceRecheckSeconds = 1f;

        private StoryFlowLipsyncDriver _driver;
        private readonly float[] _spectrum = new float[SpectrumBins];

        // One entry per renderer that owns at least one morph we drive. The FAN-OUT: on a Sidekick character
        // assembled from parts, `jawOpen` lives on head AND teeth AND tongue, and a driver that writes only the
        // head opens a jaw while the teeth stay put. A baked single-mesh character simply yields one entry.
        private readonly List<FaceTarget> _targets = new List<FaceTarget>();

        /// <summary>Kept so a re-resolve costs nothing but the hierarchy walk.</summary>
        private Dictionary<string, Dictionary<string, float>> _resolvedTable;

        /// <summary>Throttles the "still no face" retry, and keeps its warning to one.</summary>
        private float _sinceFaceCheck;
        private bool _warnedNoFace;

        private AudioSource _speaking;
        private bool _lineIsMine;

        /// <summary>The LINE has audio, whether or not its source was found — see HandleDialogueUpdated.</summary>
        private bool _lineHasAudio;

        private sealed class FaceTarget
        {
            public SkinnedMeshRenderer Renderer;
            /// <summary>Morph name to blendshape index, resolved ONCE. Never look a name up per frame.</summary>
            public Dictionary<string, int> Indices;
        }

        private void OnEnable()
        {
            _resolvedTable = VisemeMap != null ? VisemeMap.ToTable() : StoryFlowVisemeTable.Default();
            _driver = new StoryFlowLipsyncDriver(_resolvedTable);
            ResolveFace(_resolvedTable);

            if (Source == null) Source = FindObjectOfType<StoryFlowComponent>();
            if (Source != null)
            {
                Source.OnDialogueUpdated += HandleDialogueUpdated;
                Source.OnDialogueEnded += HandleDialogueEnded;
            }
        }

        private void OnDisable()
        {
            if (Source != null)
            {
                Source.OnDialogueUpdated -= HandleDialogueUpdated;
                Source.OnDialogueEnded -= HandleDialogueEnded;
            }
            StopLipsync();
        }


        /// <summary>
        /// Re-resolve the face when what was cached has gone.
        ///
        /// The case that forced this is Unreal's: Synty's Sidekick tool rebuilds a character's part meshes
        /// whenever the outfit changes at runtime, so targets resolved once point at destroyed objects and
        /// the mouth quietly stops moving. Unity's baked characters do not do that, but swapping a face mesh
        /// at runtime has the same effect, and the two arms should not behave differently here.
        ///
        /// A stale target is free to detect. Having found NOTHING is different: re-walking the hierarchy every
        /// frame would cost something on every character that legitimately has no face, so that retry is
        /// throttled and its warning kept to one.
        /// </summary>
        private void RefreshFaceIfStale(float dt)
        {
            var anyStale = false;
            foreach (var target in _targets)
            {
                if (target.Renderer == null) anyStale = true;
            }

            if (!anyStale && _targets.Count > 0)
            {
                _sinceFaceCheck = 0f;
                return;
            }

            _sinceFaceCheck += dt;
            if (!anyStale && _sinceFaceCheck < FaceRecheckSeconds) return;

            _sinceFaceCheck = 0f;
            ResolveFace(_resolvedTable);
        }

        private void Update()
        {
            if (_driver == null) return;
            _driver.Strength = Strength;
            _driver.Sensitivity = Sensitivity;
            _driver.JawBias = JawBias;
            _driver.Smooth = Smoothing;

            var dt = Time.deltaTime;
            RefreshFaceIfStale(dt);

            if (_speaking != null && _speaking.isPlaying)
            {
                _speaking.GetSpectrumData(_spectrum, 0, FFTWindow.BlackmanHarris);
                _driver.AdvanceFromSpectrum(_spectrum, AudioSettings.outputSampleRate, dt);
            }
            else if (_lineIsMine && !_lineHasAudio && IdleMouthWithoutAudio)
            {
                // The idle mouth is for a line that HAS no audio — a subtitled game, where a still face
                // reads as broken. A line that has audio we could not find is the opposite case: flapping
                // at random over real speech looks worse than not moving, so that one holds still.
                _driver.AdvanceIdle(dt);
            }
            else
            {
                _driver.AdvanceSilent(dt);
            }

            Apply();
        }

        /// <summary>
        /// Drive the mouth from any AudioSource: a cutscene line, a bark, a radio. Nothing to bake and nothing
        /// to author — the same quality the automatic path gives, on audio StoryFlow knows nothing about.
        /// </summary>
        public void StartLipsyncFor(AudioSource source)
        {
            _speaking = source;
            _driver?.ResetLevel();
        }


        /// <summary>
        /// Loudness 0..1 the driver is currently seeing, after the peak follower. For a debug meter while
        /// tuning Sensitivity — the three.js studio this table came from had one, and picking a sensitivity
        /// by watching a number beats picking it by watching a mouth.
        /// </summary>
        public float Level => _driver?.Level ?? 0f;

        /// <summary>Let the mouth close. Safe to call when nothing is playing.</summary>
        public void StopLipsync()
        {
            _speaking = null;
            _lineIsMine = false;
            _lineHasAudio = false;
        }

        private void HandleDialogueUpdated(StoryFlowDialogueState state)
        {
            _lineIsMine = state != null && SpeakerIsMine(state);
            if (!_lineIsMine)
            {
                StopLipsync();
                return;
            }

            // The AudioSource playing this line, wherever it lives. `_lineHasAudio` records that the LINE
            // has audio at all, which is a different question from whether we found its source: it is what
            // stops the idle mouth flapping over speech that is playing somewhere we could not see.
            _lineHasAudio = state.Audio != null;
            _speaking = _lineHasAudio ? FindSourcePlaying(state.Audio) : null;
            if (_lineHasAudio && _speaking == null)
            {
                Debug.LogWarning($"[StoryFlow] Lipsync on '{name}' could not find the AudioSource playing " +
                                 $"'{state.Audio.name}'. The mouth will stay closed for this line. " +
                                 "If your game plays dialogue audio itself, call StartLipsyncFor with that source.", this);
            }
            _driver?.ResetLevel();
        }

        private void HandleDialogueEnded()
        {
            StopLipsync();
        }

        /// <summary>
        /// Is this line mine? An empty CharacterId means every line is.
        ///
        /// StoryFlowDialogueState carries the resolved character DATA but no id or path, so there is nothing on
        /// the event to compare an id against. Rather than widen the dialogue state — a cross-engine contract
        /// change for a presentational feature — we resolve our id to a path and compare the state's character
        /// BY REFERENCE against the manager's runtime record: the dialogue handler assigns that very instance.
        /// </summary>
        private bool SpeakerIsMine(StoryFlowDialogueState state)
        {
            if (string.IsNullOrEmpty(CharacterId)) return true;
            if (state.Character == null || Source == null) return false;

            var path = Source.GetCharacterPathById(CharacterId, out var found);
            if (!found || string.IsNullOrEmpty(path)) return false;

            var manager = StoryFlowManager.Instance;
            if (manager == null) return false;
            return manager.RuntimeCharacters.TryGetValue(path, out var mine) && ReferenceEquals(mine, state.Character);
        }

        /// <summary>
        /// The AudioSource actually playing this line.
        ///
        /// The plugin's own playback puts it on the StoryFlow component's GameObject, so that is looked at
        /// first and is the answer almost always. A game that overrides PlayDialogueAudio can play the clip
        /// anywhere, though, and a scene sweep is cheap when it happens at most once per line — far cheaper
        /// than the alternative, which is a mouth flapping at random over speech it never found.
        /// </summary>
        private AudioSource FindSourcePlaying(AudioClip clip)
        {
            if (Source != null)
            {
                foreach (var source in Source.GetComponents<AudioSource>())
                {
                    if (source != null && source.clip == clip) return source;
                }
            }

            foreach (var source in FindObjectsOfType<AudioSource>())
            {
                if (source != null && source.clip == clip) return source;
            }
            return null;
        }

        /// <summary>
        /// Resolve every driven morph to a blendshape index, once. Names are matched leniently because the same
        /// shape is spelled differently by engine: Unity keeps the FBX group prefix (`MESHBlends.jawOpen`) while
        /// the source rig and Unreal call it `jawOpen`. A name no renderer owns is simply absent, and the rest of
        /// the pose still plays — a missing `tongueOut` must not take the jaw down with it.
        /// </summary>
        private void ResolveFace(Dictionary<string, Dictionary<string, float>> table)
        {
            _targets.Clear();
            var root = FaceRoot != null ? FaceRoot : transform;
            var owned = StoryFlowVisemeTable.OwnedMorphs(table);
            var style = VisemeMap != null ? VisemeMap.NameStyle : StoryFlowMorphNameStyle.UnityMeshBlendsPrefix;

            var missing = new HashSet<string>(owned);
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = renderer != null ? renderer.sharedMesh : null;
                if (mesh == null || mesh.blendShapeCount == 0) continue;

                var byName = new Dictionary<string, int>();
                for (var i = 0; i < mesh.blendShapeCount; i++) byName[mesh.GetBlendShapeName(i)] = i;

                var indices = new Dictionary<string, int>();
                foreach (var morph in owned)
                {
                    if (TryResolve(byName, morph, style, out var index))
                    {
                        indices[morph] = index;
                        missing.Remove(morph);
                    }
                }
                if (indices.Count > 0) _targets.Add(new FaceTarget { Renderer = renderer, Indices = indices });
            }

            // A face was found, so the next disappearance is worth reporting again.
            _warnedNoFace = _warnedNoFace && _targets.Count == 0;

            if (_targets.Count == 0)
            {
                // Once, not once per retry: RefreshFaceIfStale comes back every second while a face is missing.
                if (!_warnedNoFace)
                {
                    _warnedNoFace = true;
                    Debug.LogWarning($"[StoryFlow] Lipsync on '{name}' found no blendshapes under '{root.name}'. " +
                                     "Point FaceRoot at the character's face meshes.", this);
                }
            }
            else if (missing.Count > 0)
            {
                Debug.LogWarning($"[StoryFlow] Lipsync on '{name}': the rig has no {string.Join(", ", missing)}. " +
                                 "Those parts of each pose are skipped; the rest still plays.", this);
            }
        }

        private static bool TryResolve(Dictionary<string, int> byName, string morph, StoryFlowMorphNameStyle style, out int index)
        {
            if (byName.TryGetValue(morph, out index)) return true;
            if (style == StoryFlowMorphNameStyle.UnityMeshBlendsPrefix && byName.TryGetValue("MESHBlends." + morph, out index)) return true;

            // Last resort: any group prefix at all. Synty spell it MESHBlends today; the source FBXs carry
            // HEADBlends / TETHBlends, and a re-export could land on something else again.
            foreach (var kvp in byName)
            {
                var dot = kvp.Key.LastIndexOf('.');
                if (dot >= 0 && string.CompareOrdinal(kvp.Key, dot + 1, morph, 0, morph.Length) == 0 && kvp.Key.Length - dot - 1 == morph.Length)
                {
                    index = kvp.Value;
                    return true;
                }
            }

            index = -1;
            return false;
        }

        /// <summary>Write the driver's weights. Unity's blendshape range is 0..100, the driver's is 0..1.</summary>
        private void Apply()
        {
            foreach (var target in _targets)
            {
                foreach (var kvp in target.Indices)
                {
                    if (!_driver.Current.TryGetValue(kvp.Key, out var weight)) continue;
                    target.Renderer.SetBlendShapeWeight(kvp.Value, weight * 100f);
                }
            }
        }
    }
}
