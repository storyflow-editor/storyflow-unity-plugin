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
    /// WRITES IN LateUpdate, on purpose: Unity's Animator evaluates after Update, so a facial clip touching a
    /// driven blendshape would overwrite everything this wrote and the lipsync would read as broken.
    ///
    /// IF THE MOUTH DOES NOT MOVE, read `Level` first. It is the analysed loudness the driver is seeing, and
    /// two settings on StoryFlowComponent can take it to zero without anything else looking wrong:
    /// `DialogueAudioMixerGroup` routes dialogue through a mixer, and on some Unity versions
    /// `GetSpectrumData` then reads silence from that source; `DialogueVolumeMultiplier` scales the audio and
    /// therefore scales the spectrum this analyses. Clear the mixer group and watch `Level` before suspecting
    /// the rig. There is no workaround in here for either, because both are the game's own settings.
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

        [Tooltip("The spectrum magnitude a full-scale sine produces at its own bin: the 0 dB reference the driver " +
                 "converts against. GetSpectrumData is normalised, so 1 is right on paper. If Level sits near 1 on " +
                 "every line and the mouth never closes between words, raise this; if quiet lines never open it, " +
                 "lower it. RawPeak shows what a loud line actually measures.")]
        [Min(0.001f)] public float AnalysisFullScale = 1f;

        /// <summary>Unity's spectrum size must be a power of two. 512 bins over 90–4200 Hz is ample for a mouth.</summary>
        private const int SpectrumBins = 512;

        /// <summary>How often to look again for a face, or for a dialogue component, that was not found.</summary>
        private const float FaceRecheckSeconds = 1f;

        /// <summary>Below this the mouth is shut for every practical purpose — Unity's scale makes it 0.01 of 100.</summary>
        private const float RestWeight = 1e-4f;

        private StoryFlowLipsyncDriver _driver;
        private readonly float[] _spectrum = new float[SpectrumBins];

        // One entry per renderer that owns at least one morph we drive. The FAN-OUT: on a Sidekick character
        // assembled from parts, `jawOpen` lives on head AND teeth AND tongue, and a driver that writes only the
        // head opens a jaw while the teeth stay put. A baked single-mesh character simply yields one entry.
        private readonly List<FaceTarget> _targets = new List<FaceTarget>();

        /// <summary>Throttles the retries for a missing face and a missing dialogue component.</summary>
        private float _sinceFaceCheck;
        private float _sinceSourceCheck;

        // Every diagnostic here fires ONCE. A per-frame retry that warns per attempt buries the console, and a
        // per-line one buries it more slowly.
        private bool _warnedNoFace;
        private bool _warnedNoDialogueComponent;
        private bool _warnedNoAudioSource;
        private bool _warnedMissingMorphs;
        private bool _warnedUnknownCharacter;
        private bool _saidEveryLine;

        private bool _subscribed;

        private AudioSource _speaking;
        private AudioClip _lineClip;
        private bool _lineIsMine;

        /// <summary>StartLipsyncFor was called by game code: it runs until StopLipsync, whatever the dialogue does.</summary>
        private bool _manual;

        /// <summary>The LINE has audio of its own, whether or not its source was found — see HandleDialogueUpdated.</summary>
        private bool _lineHasAudio;

        /// <summary>How long this line's AudioSource has been looked for. The search retries for a second before warning.</summary>
        private float _sinceAudioSearch;

        /// <summary>The node this face is already speaking. A repeat of it is a re-render, not a new line.</summary>
        private string _lineNodeId;

        /// <summary>True once the mouth is shut AND being asked to stay shut, so the write can stop.</summary>
        private bool _atRest;

        /// <summary>
        /// A renderer and the blendshapes on it this face drives, flattened: `Slots[i]` is an index into the
        /// driver's key array and `Shapes[i]` the blendshape it writes, so Apply is two array reads per morph
        /// and never a string lookup.
        /// </summary>
        private sealed class FaceTarget
        {
            public SkinnedMeshRenderer Renderer;

            /// <summary>The mesh the indices below were resolved against. A different one invalidates them.</summary>
            public Mesh Mesh;

            public int[] Slots;
            public int[] Shapes;
        }

        private void OnEnable()
        {
            var table = VisemeMap != null ? VisemeMap.ToTable() : StoryFlowVisemeTable.Default();

            // Seeded per component: Unity's Mono seeds a bare `new Random()` from TickCount, so a crowd built
            // in one frame would idle in lockstep.
            _driver = new StoryFlowLipsyncDriver(table, GetInstanceID());
            ResolveFace();
            BindSource();
        }

        private void OnDisable()
        {
            if (_subscribed && Source != null)
            {
                Source.OnDialogueUpdated -= HandleDialogueUpdated;
                Source.OnDialogueEnded -= HandleDialogueEnded;
            }
            _subscribed = false;
            StopLipsync();

            // Nothing else will run to ease this face shut, so a component disabled mid-vowel would leave the
            // mouth hanging open for as long as it stays disabled.
            ZeroOwnedShapes();
        }

        /// <summary>
        /// Find the dialogue component and subscribe, retried while it is missing.
        ///
        /// One-shot discovery on enable is wrong for the ordinary case of a character spawned before the
        /// dialogue component exists, or a scene where StoryFlow is created by the manager's own bootstrap:
        /// the face then never hears a line and nothing says why.
        /// </summary>
        private void BindSource()
        {
            if (_subscribed && Source != null) return;

            if (Source == null)
            {
                Source = FindFirstObjectByType<StoryFlowComponent>(FindObjectsInactive.Include);
                if (Source == null)
                {
                    if (!_warnedNoDialogueComponent)
                    {
                        _warnedNoDialogueComponent = true;
                        Debug.LogWarning($"[StoryFlow] Lipsync on '{name}' found no StoryFlowComponent in the scene. " +
                                         "It will keep looking; assign Source to point at one directly.", this);
                    }
                    return;
                }
            }

            Source.OnDialogueUpdated += HandleDialogueUpdated;
            Source.OnDialogueEnded += HandleDialogueEnded;
            _subscribed = true;

            // A line may already be on screen — this component was enabled mid-dialogue, or its actor was
            // spawned by the line itself. Without this it waits for the next one with a dead face.
            if (Source.IsDialogueActive())
            {
                var showing = Source.GetCurrentDialogue();
                if (showing != null) HandleDialogueUpdated(showing);
            }
        }

        /// <summary>
        /// Re-resolve the face when what was cached has gone, or when the mesh under it changed.
        ///
        /// The case that forced this is Unreal's: Synty's Sidekick tool rebuilds a character's part meshes
        /// whenever the outfit changes at runtime, so targets resolved once point at destroyed objects and
        /// the mouth quietly stops moving. Unity's equivalent is worse than a null: an outfit or LOD swap
        /// assigns a new `sharedMesh` to the SAME renderer, and the cached blendshape indices then address a
        /// different shape list — fewer shapes errors every frame, a different order moves the wrong shapes.
        /// So the mesh asset is cached per target and a change counts as stale.
        ///
        /// Having found NOTHING is different: re-walking the hierarchy every frame would cost something on
        /// every character that legitimately has no face, so that retry is throttled and its warning kept to one.
        /// </summary>
        private void RefreshFaceIfStale(float dt)
        {
            var anyStale = false;
            foreach (var target in _targets)
            {
                if (!IsLive(target)) anyStale = true;
            }

            if (!anyStale && _targets.Count > 0)
            {
                _sinceFaceCheck = 0f;
                return;
            }

            _sinceFaceCheck += dt;
            if (!anyStale && _sinceFaceCheck < FaceRecheckSeconds) return;

            _sinceFaceCheck = 0f;
            ResolveFace();
        }

        /// <summary>
        /// The Animator writes blendshapes after Update, so anything written there is overwritten before it is
        /// ever drawn. Everything this component does happens here instead.
        /// </summary>
        private void LateUpdate()
        {
            if (_driver == null) return;
            _driver.Strength = Strength;
            _driver.Sensitivity = Sensitivity;
            _driver.JawBias = JawBias;
            _driver.Smooth = Smoothing;
            _driver.FullScale = Mathf.Max(AnalysisFullScale, 0.001f);

            // The audio clock is not dilated, so the mouth must not be either: at timeScale 0.2 a scaled delta
            // would ease the face at a fifth of the speech it is following.
            var dt = Time.unscaledDeltaTime;

            // The dialogue component can go away and come back (a scene load under a persistent character). Its
            // events died with it, so discovery has to be re-armed or this face is deaf with nothing said.
            if (_subscribed && Source == null) _subscribed = false;

            if (!_subscribed)
            {
                _sinceSourceCheck += dt;
                if (_sinceSourceCheck >= FaceRecheckSeconds)
                {
                    _sinceSourceCheck = 0f;
                    BindSource();
                }
            }

            RefreshFaceIfStale(dt);
            RetryAudioSource(dt);

            var silent = false;
            if (IsAnalysable())
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
                silent = true;

                // A tail the mouth was following has ended and no line owns the source any more: let go of
                // it, or the same clip replayed later through that source (a media node) would move this
                // mouth with no line on screen.
                if (!_lineIsMine && !_manual && _speaking != null)
                {
                    _speaking = null;
                    _lineClip = null;
                }
            }

            Apply(silent);
        }

        /// <summary>
        /// A line with audio drives the mouth only while THAT audio is playing.
        ///
        /// The clip check is not belt and braces: the plugin plays dialogue through one AudioSource on the
        /// StoryFlowComponent, and MediaNodeHandler plays through the same one. Without it, the next thing the
        /// story plays through that source — music, a sound effect — drives this character's mouth. Game code
        /// that handed over a source explicitly is trusted with whatever that source plays.
        /// </summary>
        private bool IsAnalysable()
        {
            return _speaking != null && _speaking.isPlaying && (_manual || _speaking.clip == _lineClip);
        }

        /// <summary>
        /// A game that starts dialogue audio inside its OWN OnDialogueUpdated handler, registered after this
        /// one, has not started it yet when this component looks. So the search keeps looking for a second
        /// before it gives up and says so, the way the face and the dialogue component searches do.
        /// </summary>
        private void RetryAudioSource(float dt)
        {
            if (!_lineIsMine || !_lineHasAudio || _speaking != null) return;

            _sinceAudioSearch += dt;
            if (_sinceAudioSearch < FaceRecheckSeconds)
            {
                _speaking = FindSourcePlaying(_lineClip);
                if (_speaking != null) _driver.ResetLevel();
            }
            else if (!_warnedNoAudioSource)
            {
                _warnedNoAudioSource = true;
                Debug.LogWarning($"[StoryFlow] Lipsync on '{name}' could not find the AudioSource playing " +
                                 $"'{_lineClip.name}'. The mouth will stay closed for this line. " +
                                 "If your game plays dialogue audio itself, call StartLipsyncFor with that source.", this);
            }
        }

        /// <summary>
        /// Drive the mouth from any AudioSource: a cutscene line, a bark, a radio. Nothing to bake and nothing
        /// to author — the same quality the automatic path gives, on audio StoryFlow knows nothing about.
        /// It runs until StopLipsync: other characters' lines and the dialogue ending do not touch it, and a
        /// line of THIS face's takes over from it.
        /// </summary>
        public void StartLipsyncFor(AudioSource source)
        {
            _manual = source != null;
            _lineIsMine = false;
            _lineHasAudio = false;
            _lineNodeId = null;
            _speaking = source;
            _lineClip = source != null ? source.clip : null;
            _driver?.ResetLevel();
        }

        /// <summary>Is this face being driven right now — by a line of its own, or by StartLipsyncFor from game code?</summary>
        public bool IsLipsyncActive => _manual || _lineIsMine;

        /// <summary>
        /// Loudness 0..1 the driver is currently seeing, after the peak follower. For a debug meter while
        /// tuning Sensitivity — the three.js studio this table came from had one, and picking a sensitivity
        /// by watching a number beats picking it by watching a mouth.
        ///
        /// It is also the first thing to read when a mouth will not move. A line that is audible but reads 0
        /// here is not reaching the analyser: `DialogueAudioMixerGroup` on StoryFlowComponent can make
        /// `GetSpectrumData` return silence on some Unity versions, and `DialogueVolumeMultiplier` scales what
        /// this sees along with what the player hears.
        /// </summary>
        public float Level => _driver?.Level ?? 0f;

        /// <summary>
        /// The loudest RAW spectrum magnitude seen since this line started, before the driver's conversion to
        /// the reference domain. Unity's `GetSpectrumData` is already normalised so nothing needs calibrating
        /// here; this is the readout that says so.
        /// </summary>
        public float RawPeak => _driver?.RawPeak ?? 0f;

        /// <summary>
        /// Where the analysed audio sits on the vowel axis, 0 (OO) to 1 (EE). Read it with Level when a mouth
        /// opens but looks wrong: pinned at 1 is a stretched grin, pinned at 0 a permanent pucker.
        /// </summary>
        public float Centroid => _driver?.Centroid ?? 0f;

        /// <summary>Let the mouth close. Safe to call when nothing is playing.</summary>
        public void StopLipsync()
        {
            _manual = false;
            _speaking = null;
            _lineClip = null;
            _lineIsMine = false;
            _lineHasAudio = false;
            _lineNodeId = null;
        }

        private void HandleDialogueUpdated(StoryFlowDialogueState state)
        {
            if (state == null)
            {
                StopLipsync();
                return;
            }

            if (string.IsNullOrEmpty(CharacterId) && !_saidEveryLine)
            {
                _saidEveryLine = true;
                Debug.Log($"[StoryFlow] Lipsync on '{name}' has no CharacterId, so this face moves on every " +
                          "line. Set CharacterId if more than one character speaks in this scene.", this);
            }

            if (!SpeakerIsMine(state))
            {
                // Someone else's line. Game-code lipsync (StartLipsyncFor) promised to run until StopLipsync,
                // and another character talking is not that.
                if (!_manual) StopLipsync();
                return;
            }

            // A RE-RENDER, not a new line. The same node is broadcast again on a variable change, on
            // ResumeDialogue and on a dead-end redraw; treating those as line starts resets the peak
            // follower, searches for the AudioSource again and repeats every warning — and a per-frame
            // variable write would then pin the follower at its initial value and hold the mouth wide open.
            if (_lineIsMine && state.NodeId == _lineNodeId) return;

            _lineNodeId = state.NodeId;
            _lineIsMine = true;
            _manual = false;
            _lineHasAudio = state.Audio != null;

            if (_lineHasAudio)
            {
                // The AudioSource playing this line, wherever it lives. `_lineHasAudio` records that the LINE
                // has audio at all, which is a different question from whether we found its source: it is
                // what stops the idle mouth flapping over speech that is playing somewhere we could not see.
                // Not found yet is not a failure: RetryAudioSource keeps looking for a second.
                _lineClip = state.Audio;
                _speaking = FindSourcePlaying(state.Audio);
                _sinceAudioSearch = 0f;
                _driver?.ResetLevel();
            }
            else if (IsAnalysable())
            {
                // A text-only line, but the previous line's sound is still playing (audioReset is off by
                // default): keep following it, and idle only once it stops. Idling over audible speech is the
                // one thing the idle mouth must never do.
            }
            else
            {
                _speaking = null;
                _lineClip = null;
            }
        }

        /// <summary>
        /// The line is over, but the audio may not be: with StopAudioOnDialogueEnd off the tail keeps playing,
        /// and a mouth that snaps shut over audible speech reads worse than one that closes when the sound
        /// does. So the source is kept while it is still playing this line's clip and the silent path closes
        /// the mouth the frame it stops; only the line bookkeeping is cleared, so nothing idles and the next
        /// line is a fresh start. Game-code lipsync is not the dialogue's to end. The Unreal arm follows the
        /// same rule.
        /// </summary>
        private void HandleDialogueEnded()
        {
            if (_manual) return;

            if (!IsAnalysable())
            {
                StopLipsync();
                return;
            }

            _lineIsMine = false;
            _lineHasAudio = false;
            _lineNodeId = null;
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
            if (Source == null) return false;

            var path = Source.GetCharacterPathById(CharacterId, out var found);
            if (!found || string.IsNullOrEmpty(path))
            {
                // A typo, a wrong case, or a project imported before character ids existed. Without this the
                // face is simply never anyone's and says nothing about it, while a missing morph warns.
                if (!_warnedUnknownCharacter)
                {
                    _warnedUnknownCharacter = true;
                    Debug.LogWarning($"[StoryFlow] Lipsync on '{name}': no character with id '{CharacterId}' in " +
                                     "this project, so this face will never speak. Check the id in the editor's " +
                                     "character list, or clear it to move on every line.", this);
                }
                return false;
            }

            if (state.Character == null) return false;
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
        /// than the alternative, which is a mouth flapping at random over speech it never found. The sweep
        /// prefers a source that is actually PLAYING the clip: several may hold it, and only one is the line.
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

            AudioSource holding = null;
            foreach (var source in FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (source == null || source.clip != clip) continue;
                if (source.isPlaying) return source;
                if (holding == null) holding = source;
            }
            return holding;
        }

        /// <summary>
        /// Resolve every driven morph to a blendshape index, once. Names are matched leniently because the same
        /// shape is spelled differently by engine: Unity keeps the FBX group prefix (`MESHBlends.jawOpen`) while
        /// the source rig and Unreal call it `jawOpen`. A name no renderer owns is simply absent, and the rest of
        /// the pose still plays — a missing `tongueOut` must not take the jaw down with it.
        /// </summary>
        private void ResolveFace()
        {
            _targets.Clear();
            _atRest = false;
            var root = FaceRoot != null ? FaceRoot : transform;
            var keys = _driver.Keys;
            var style = VisemeMap != null ? VisemeMap.NameStyle : StoryFlowMorphNameStyle.UnityMeshBlendsPrefix;

            var missing = new HashSet<string>();
            for (var k = 0; k < keys.Count; k++) missing.Add(keys[k]);

            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = renderer != null ? renderer.sharedMesh : null;
                if (mesh == null || mesh.blendShapeCount == 0) continue;

                var byName = new Dictionary<string, int>();
                for (var i = 0; i < mesh.blendShapeCount; i++) byName[mesh.GetBlendShapeName(i)] = i;

                var slots = new List<int>();
                var shapes = new List<int>();
                for (var k = 0; k < keys.Count; k++)
                {
                    if (!TryResolve(byName, keys[k], style, out var index)) continue;
                    slots.Add(k);
                    shapes.Add(index);
                    missing.Remove(keys[k]);
                }
                if (slots.Count > 0)
                {
                    _targets.Add(new FaceTarget
                    {
                        Renderer = renderer,
                        Mesh = mesh,
                        Slots = slots.ToArray(),
                        Shapes = shapes.ToArray(),
                    });
                }
            }

            // A face was found, so the next disappearance is worth reporting again.
            _warnedNoFace = _warnedNoFace && _targets.Count == 0;
            _warnedMissingMorphs = _warnedMissingMorphs && _targets.Count > 0;

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
            else if (missing.Count > 0 && !_warnedMissingMorphs)
            {
                // Once: on a plain ARKit rig this names the tongue shapes, and a re-resolve happens on every
                // mesh swap, which is the ordinary outfit-change case rather than a reason to say it again.
                _warnedMissingMorphs = true;
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

        /// <summary>A target still addressable: its renderer alive and still wearing the mesh we resolved against.</summary>
        private static bool IsLive(FaceTarget target)
        {
            return target.Renderer != null && target.Renderer.sharedMesh == target.Mesh;
        }

        /// <summary>
        /// Write the driver's weights. Unity's blendshape range is 0..100, the driver's is 0..1.
        ///
        /// A face doing nothing must STOP writing. `SetBlendShapeWeight` is an assignment, not a contribution,
        /// so an idle component stamping ~0 into eighteen shapes every frame flattens anything else that poses
        /// this mouth — an expression, a chew cycle, a hand-authored clip on shapes the driver happens to own.
        /// The zeros land once as the mouth settles, and then this stands aside until it has something to say.
        /// </summary>
        private void Apply(bool silent)
        {
            if (silent && MouthIsShut())
            {
                if (_atRest) return;

                // The last frame before the write stops: leave the face exactly closed rather than at the
                // sliver of weight the ease was still on.
                _atRest = true;
                ZeroOwnedShapes();
                return;
            }

            _atRest = false;
            foreach (var target in _targets)
            {
                var slots = target.Slots;
                var shapes = target.Shapes;
                for (var i = 0; i < slots.Length; i++)
                {
                    target.Renderer.SetBlendShapeWeight(shapes[i], _driver.GetWeight(slots[i]) * 100f);
                }
            }
        }

        private bool MouthIsShut()
        {
            for (var i = 0; i < _driver.Keys.Count; i++)
            {
                if (_driver.GetWeight(i) >= RestWeight) return false;
            }
            return true;
        }

        private void ZeroOwnedShapes()
        {
            foreach (var target in _targets)
            {
                if (!IsLive(target)) continue;
                for (var i = 0; i < target.Shapes.Length; i++) target.Renderer.SetBlendShapeWeight(target.Shapes[i], 0f);
            }
        }
    }
}
