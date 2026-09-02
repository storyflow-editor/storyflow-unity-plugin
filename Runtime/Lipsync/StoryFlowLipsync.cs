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

        private StoryFlowLipsyncDriver _driver;
        private readonly float[] _spectrum = new float[SpectrumBins];

        // One entry per renderer that owns at least one morph we drive. The FAN-OUT: on a Sidekick character
        // assembled from parts, `jawOpen` lives on head AND teeth AND tongue, and a driver that writes only the
        // head opens a jaw while the teeth stay put. A baked single-mesh character simply yields one entry.
        private readonly List<FaceTarget> _targets = new List<FaceTarget>();

        private AudioSource _speaking;
        private bool _lineIsMine;

        private sealed class FaceTarget
        {
            public SkinnedMeshRenderer Renderer;
            /// <summary>Morph name to blendshape index, resolved ONCE. Never look a name up per frame.</summary>
            public Dictionary<string, int> Indices;
        }

        private void OnEnable()
        {
            var table = VisemeMap != null ? VisemeMap.ToTable() : StoryFlowVisemeTable.Default();
            _driver = new StoryFlowLipsyncDriver(table);
            ResolveFace(table);

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

        private void Update()
        {
            if (_driver == null) return;
            _driver.Strength = Strength;
            _driver.Sensitivity = Sensitivity;
            _driver.JawBias = JawBias;
            _driver.Smooth = Smoothing;

            var dt = Time.deltaTime;
            if (_speaking != null && _speaking.isPlaying)
            {
                _speaking.GetSpectrumData(_spectrum, 0, FFTWindow.BlackmanHarris);
                _driver.AdvanceFromSpectrum(_spectrum, AudioSettings.outputSampleRate, dt);
            }
            else if (_lineIsMine && IdleMouthWithoutAudio)
            {
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

        /// <summary>Let the mouth close. Safe to call when nothing is playing.</summary>
        public void StopLipsync()
        {
            _speaking = null;
            _lineIsMine = false;
        }

        private void HandleDialogueUpdated(StoryFlowDialogueState state)
        {
            _lineIsMine = state != null && SpeakerIsMine(state);
            if (!_lineIsMine)
            {
                StopLipsync();
                return;
            }

            // The line's AudioSource is the one on the dialogue component already playing this clip. Asking the
            // scene rather than reaching into StoryFlowComponent's private field keeps this to the public
            // surface, and it works the same when a game overrides playback with its own source.
            _speaking = state.Audio != null ? FindSourcePlaying(state.Audio) : null;
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

        private AudioSource FindSourcePlaying(AudioClip clip)
        {
            if (Source == null) return null;
            var sources = Source.GetComponents<AudioSource>();
            foreach (var source in sources)
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

            if (_targets.Count == 0)
            {
                Debug.LogWarning($"[StoryFlow] Lipsync on '{name}' found no blendshapes under '{root.name}'. " +
                                 "Point FaceRoot at the character's face meshes.", this);
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
