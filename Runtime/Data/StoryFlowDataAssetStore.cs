using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
// The string ladder lives on the execution context (StoryFlow.Execution) and is reached from
// here for the .sfd read door's lookup. One assembly, and the precedent is StoryFlowComponent,
// which calls the same shared LookUpLocalizedIn from outside that namespace — the alternative
// would be a second, simpler lookup, which is exactly the drift the shared ladder prevents.
using StoryFlow.Execution;
using UnityEngine;

namespace StoryFlow.Data
{
    /// <summary>
    /// The two halves of the .sfd store as ONE non-owning reference, so callers that thread
    /// the pair (the execution context, and every signature below it) cannot end up holding
    /// a seed from one owner and an overlay from another. Both point at StoryFlowManager-owned
    /// dictionaries and share its lifetime; a null ref, or one with a null half, is the
    /// "no store" state every accessor checks.
    /// </summary>
    public sealed class StoryFlowDataAssetStoreRef
    {
        public Dictionary<string, StoryFlowDataAssetDef> Seed;
        public Dictionary<string, Dictionary<string, StoryFlowVariant>> Overlay;

        public bool IsValid => Seed != null && Overlay != null;
    }

    /// <summary>
    /// The character system's two tables as one non-owning reference, for the data-asset
    /// access layer's character branch (P4 characters contract §3): the id bridge and the
    /// runtime character table, both manager-owned, minted per call exactly like
    /// <see cref="StoryFlowDataAssetStoreRef"/> above so the pair can never come from two
    /// different owners. A null ref, or one with a null half, means "no character branch"
    /// and the access layer walks its ordinary seed path.
    /// </summary>
    public sealed class StoryFlowCharacterStoreRef
    {
        public Dictionary<string, string> Bridge;
        public Dictionary<string, StoryFlowCharacterData> Characters;

        public bool IsValid => Bridge != null && Characters != null;
    }

    /// <summary>
    /// What ONE chain walk found for an accessor's binding: either a usable value, or which
    /// rung of the degraded ladder (contract §6) the binding fell off.
    ///
    /// The three failure members are the three the walk itself can answer. The other two
    /// ladder reasons — no variableId on the node, and no pill wired to its dataAsset pin —
    /// are graph questions the caller settles before there is an assetId to walk from.
    /// </summary>
    public enum StoryFlowDataAssetBinding
    {
        /// <summary>The chain declares the id, the declaration still matches the snapshot.</summary>
        Ok,

        /// <summary>The seed carries no such asset (a deleted .sfd, or a pill left unbound).</summary>
        DeadRef,

        /// <summary>The asset is here, but no level of its chain declares the id.</summary>
        Missing,

        /// <summary>Declared, but the declaration no longer matches the spawn snapshot (§6.1).</summary>
        Changed,
    }

    /// <summary>
    /// WHAT ANSWERED a resolve — the PROVENANCE of the value, which the localization gate reads
    /// and nothing else does.
    ///
    /// THREE values and not two, because "not the declaration" has two different reasons and a
    /// reader who cannot tell them apart re-derives the gate wrongly. Both an ancestor's
    /// declaration and an ancestor's override look simply INHERITED from a descendant, and the
    /// reference implementation learned what that costs: its origin token folded the two into one
    /// `inherited` value, so its accessor door served the ancestor's translation for a text the
    /// descendant had deliberately replaced (fixed editor-side at b18c4de0). The failure is not a
    /// missing translation — it is a WRONG VALUE, and it is invisible in the source language.
    ///
    /// Recorded where the chain walk's branch already is, never re-derived afterwards: once an
    /// overlay entry and an override are both just a variant reference, nothing downstream can
    /// tell them apart.
    /// </summary>
    public enum StoryFlowDataAssetOrigin
    {
        /// <summary>
        /// The root-most declaration's own authored value (contract §4.3).
        /// </summary>
        Declaration,

        /// <summary>An authored <c>overrides</c> entry, keyed in localization version 2 exports.</summary>
        Override,

        /// <summary>An overlay entry: a write this session made. Live data, never content.</summary>
        SessionWrite,
    }

    /// <summary>
    /// The declared SHAPE an accessor node's pins were built from at spawn time: the four
    /// wire-type fields the exporter wrote onto the node, which travel together everywhere and
    /// mean nothing apart. §6.1 compares them against the seed's live declaration, and a
    /// mismatch degrades the node rather than coercing the value.
    ///
    /// WIRE STRINGS, not parsed types, because that is what the node data holds and parsing has
    /// to be able to fail: a type string the shared table does not know can never match, so a
    /// garbled snapshot degrades instead of resolving.
    /// </summary>
    public struct StoryFlowDataAssetPinShape
    {
        public string VariableType;
        public bool IsArray;

        /// <summary>Map declarations only — ignored for every other declared type.</summary>
        public string KeyType;
        public string ValueType;

        public StoryFlowDataAssetPinShape(
            string variableType, bool isArray, string keyType = "", string valueType = "")
        {
            VariableType = variableType;
            IsArray = isArray;
            KeyType = keyType;
            ValueType = valueType;
        }
    }

    /// <summary>
    /// The .sfd Data Asset STORE (engine contract §3) and its chain RESOLVER (§4).
    ///
    /// NORMATIVE SOURCE: the HTML runtime's src/renderer/runtime/runtime-data-assets.js — its
    /// resolveEntry() is the chain walk every function here mirrors, isDeclaredOnChain() the
    /// write guard, declaration() the root-most declaration lookup. Where this file and that
    /// one disagree, that one is right. The shared golden fixtures in the Unreal package's
    /// TestContent/engine-contract/data-assets-*.json are generated from it and pin the
    /// agreement (DataAssetTests.GoldenResolutionFixtureMatches).
    ///
    /// The store is two halves:
    ///  - the SEED: the imported table, keyed by assetId. Read-only, forever.
    ///  - the OVERLAY: this session's script writes, keyed (assetId -> variableId -> value).
    ///    Cleared on a game reset, persisted sparsely in saves (§7).
    /// Both are owned by StoryFlowManager and passed in, so tests can drive the resolver
    /// against a seed built straight from the fixture JSON.
    ///
    /// FOUR DOORS IN, one walk behind all of them:
    ///  - TryRead     — value with the LOCALIZATION GATE applied: the door every surface that
    ///    hands a .sfd value to GAME CODE goes through (both host mirrors, via
    ///    StoryFlowDataAssetAccess).
    ///  - TryResolve  — the CHAIN RULE ALONE, no string lookup anywhere in it: saves, the
    ///    fixture harnesses and any caller that wants the bytes the store actually holds.
    ///  - ReadBound   — TryRead plus the §6.1 ladder answer: what a bound accessor NODE reads
    ///    through, since a node's pins can be stale in a way an id cannot.
    ///  - CheckBound  — the ladder answer alone, no overlay and no copy-out: the write path,
    ///    which needs to know the binding is sound and nothing else.
    /// TryRead and ReadBound share ONE gate (see <see cref="ReadOut"/>) rather than carrying a
    /// copy each: a rule that held on the host mirrors and not at the node arms would be a bug
    /// no single-surface test could see.
    ///
    /// ONE HOLE IN "the seed is never mutated": FindDeclaration and FindDeclarationByName
    /// hand back a StoryFlowVariable BY REFERENCE into the seed, and C# has no const to stop
    /// a caller writing through it. Everything else here copies — TryResolve deep-copies out,
    /// TrySet deep-copies in — so these two are the only way to reach seed storage, and a
    /// caller assigning to declaration.Value would corrupt every descendant that inherits it
    /// for the rest of the session, silently and permanently. Read declarations, never write
    /// to them, and do not hold one across a re-seed. If Task N3's public API ends up holding
    /// declarations rather than consuming them at the boundary, revisit this and hand back a
    /// readonly declaration-info struct instead.
    /// </summary>
    public static class StoryFlowDataAssetStore
    {
        // The generation follows the shared overlay identity without retaining dead stores.
        private sealed class Revision { public long Value; }
        private static readonly ConditionalWeakTable<object, Revision> Revisions = new();
        internal static void NotifyChanged(object overlay) { if (overlay != null) Revisions.GetOrCreateValue(overlay).Value++; }
        internal static long GetRevision(object overlay) => overlay == null ? 0 : Revisions.GetOrCreateValue(overlay).Value;

        /// <summary>
        /// Chain depth cap, matching the reference implementation's MAX_DEPTH (contract §4.4):
        /// the walk admits MaxChainDepth ancestors PLUS the starting level, so 65 levels are
        /// visited before a malformed chain is abandoned. A cycle is caught earlier by the
        /// visited set.
        ///
        /// NOTE: deliberately NOT StoryFlowEvaluator's MaxEvaluationDepth (100), despite the
        /// family resemblance. That one is a local stack-safety limit this plugin chose for
        /// itself; this one is a CONTRACT value shared by all four runtimes, and moving it
        /// would make Unity resolve a chain the other three abandon (or the reverse).
        /// </summary>
        public const int MaxChainDepth = 64;

        // =====================================================================
        // Seed construction
        // =====================================================================

        /// <summary>
        /// Builds the runtime seed from a project's imported Data Assets (contract §3 init).
        /// Replaces <paramref name="outSeed"/> wholesale; the caller clears the overlay,
        /// because a fresh seed is a fresh session and the two belong together.
        ///
        /// TWO PASSES, and they cannot be merged. Pass 1 builds every level with its
        /// declarations rehydrated and NO overrides. Pass 2 types each stored override
        /// against the declaration that owns its id somewhere on the chain — an ancestor
        /// that pass 1 may not have reached yet, because assets arrive keyed by id in
        /// dictionary order and nothing orders them leaf-to-root.
        ///
        /// Deliberately does NOT resolve string-table keys the way character and global
        /// variables do, and the REASON CHANGED with localization spec §2's amendment of
        /// 2026-08-27 (which supersedes engine-contract 2.1's literal-value posture):
        /// data-assets.json now DOES carry a strings table, and a declared .sfd string value is
        /// a table key like any other artifact's. It is still not resolved here, because the
        /// seed is the store's read-only half and a bake would (a) freeze the authored text in
        /// whatever language happened to be current at SetProject, and (b) destroy the one thing
        /// the localization gate needs — the difference between a value that came from the seed
        /// and one a script wrote. Resolution happens at the READ DOOR instead: see
        /// <see cref="TryRead"/>.
        /// </summary>
        public static void BuildSeed(
            StoryFlowProjectAsset project, Dictionary<string, StoryFlowDataAssetDef> outSeed)
        {
            if (outSeed == null) return;
            outSeed.Clear();
            if (project == null) return;

            // --- Pass 1: levels and declarations, overrides left empty ---
            foreach (var pair in project.DataAssets)
            {
                var asset = pair.Value;
                if (asset == null) continue;

                var def = new StoryFlowDataAssetDef
                {
                    // The MAP KEY is the authoritative assetId — it is what the pills, the
                    // resolver and the save key all use; the asset's own Id field carries the
                    // same value for inspection.
                    Id = pair.Key,
                    Name = asset.DisplayName,
                    ParentId = asset.ParentId ?? string.Empty
                };

                foreach (var declared in asset.Variables)
                {
                    if (declared == null) continue;
                    def.Variables.Add(RehydrateDeclaration(declared));
                }

                outSeed[pair.Key] = def;
            }

            // --- Pass 2: overrides, typed against the chain's declaration ---
            foreach (var pair in project.DataAssets)
            {
                var asset = pair.Value;
                if (asset == null) continue;
                if (!outSeed.TryGetValue(pair.Key, out var def)) continue;

                foreach (var entry in asset.Overrides)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.VariableId)) continue;

                    var declaration = FindDeclaration(outSeed, pair.Key, entry.VariableId);
                    if (declaration == null)
                    {
                        // ORPHAN: the base variable this override shadowed was deleted. The
                        // resolver would ignore it anyway (§4.3 honours a value only where the
                        // chain still declares the id), and a typed runtime has nothing to type
                        // it against, so it never enters the seed at all.
                        Debug.Log($"[StoryFlow] Data Asset '{pair.Key}' overrides '{entry.VariableId}', " +
                                  "which no level of its chain declares - dropping the override.");
                        continue;
                    }

                    if (!TryTypeOverride(declaration, entry.ValueJson, out var value))
                    {
                        Debug.Log($"[StoryFlow] Data Asset '{pair.Key}' has an override of " +
                                  $"'{declaration.Name}' that does not fit its declaration " +
                                  $"(JSON: {entry.ValueJson}) - dropping the override.");
                        continue;
                    }

                    def.Overrides[entry.VariableId] = value;
                }
            }
        }

        /// <summary>
        /// A deep copy of one declaration with its containers rehydrated. ArrayValue and
        /// MapValue are [NonSerialized] on StoryFlowVariant, so a declaration loaded from a
        /// .asset comes back with empty container storage and its JSON beside it — the same
        /// dance StoryFlowCharacterAsset.CreateRuntimeData does for character variables.
        /// </summary>
        private static StoryFlowVariable RehydrateDeclaration(StoryFlowVariable declared)
        {
            var copy = new StoryFlowVariable(declared);
            if (copy.Value == null) copy.Value = new StoryFlowVariant { Type = copy.Type };

            if (copy.Type == StoryFlowVariableType.Map && copy.Value.MapValue == null)
            {
                copy.Value = StoryFlowVariant.DeserializeMapFromJson(
                    copy.KeyType, copy.ValueType, copy.DefaultValueJson);
            }
            else if (copy.IsArray && copy.Value.ArrayValue == null)
            {
                copy.Value = StoryFlowVariant.DeserializeArrayFromJson(copy.Type, copy.DefaultValueJson);
            }

            return copy;
        }

        /// <summary>
        /// Types one stored override's raw JSON against the declaration that owns its id.
        /// Returns false when the JSON cannot produce a value that fits — which is the whole
        /// point of dropping here rather than at read time: TryResolve answering true must
        /// always mean the caller has a usable value of the declared shape.
        /// </summary>
        private static bool TryTypeOverride(
            StoryFlowVariable declaration, string valueJson, out StoryFlowVariant value)
        {
            value = null;
            if (string.IsNullOrEmpty(valueJson)) return false;

            JToken token;
            try
            {
                token = JToken.Parse(valueJson);
            }
            catch (Exception)
            {
                return false;
            }

            if (declaration.Type == StoryFlowVariableType.Map)
            {
                // Map values are ORDERED ENTRY LISTS (§2.1), never JSON objects. Anything
                // else cannot be read as a map, so it is not stored as one either.
                if (!(token is JArray)) return false;
                value = StoryFlowVariant.DeserializeMapFromJson(
                    declaration.KeyType, declaration.ValueType, token.ToString(Newtonsoft.Json.Formatting.None));
                return true;
            }

            if (declaration.IsArray)
            {
                if (!(token is JArray)) return false;
                // Elements carry the DECLARED type, so an enum array's elements come out
                // Enum-tagged rather than String-tagged. An empty array stays array-shaped.
                value = StoryFlowVariant.DeserializeArrayFromJson(
                    declaration.Type, token.ToString(Newtonsoft.Json.Formatting.None));
                return true;
            }

            if (token is JArray || token is JObject) return false;

            // Scalars keep the DECLARED type tag, so image/audio/character values stay
            // distinguishable from plain strings (all four read back through GetString,
            // StoryFlowVariant:111-119) and enums land in EnumValue rather than StringValue.
            value = StoryFlowVariant.DeserializeFromJson(declaration.Type, TokenText(token));
            return true;
        }

        /// <summary>
        /// A JSON token as the plain text StoryFlowVariant.DeserializeFromJson expects, with
        /// invariant culture for numerics so a value reads the same under every process
        /// culture. Booleans render "True"/"False", which that parser accepts.
        /// </summary>
        private static string TokenText(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return "";
            return token is JValue jValue && jValue.Value is IFormattable formattable
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : token.ToString();
        }

        // =====================================================================
        // The chain walk
        // =====================================================================

        /// <summary>
        /// ONE resolution's three answers, which are one answer: the nearest hit, the root-most
        /// declaration, and WHICH tier the nearest came from. <see cref="WalkForValue"/> produces
        /// all three together and every consumer needs all three, so they travel together rather
        /// than as a positional list a later door could assemble by hand from a different walk —
        /// and an Origin paired with somebody else's Declaration is exactly the mis-gate
        /// <see cref="StoryFlowDataAssetOrigin"/> exists to prevent.
        ///
        /// <c>Declaration == null</c> is the "nothing on the chain declares this id" answer every
        /// caller tests first; the other two mean nothing until it is non-null.
        /// </summary>
        private readonly struct Resolution
        {
            /// <summary>The FIRST overlay-or-override hit leaf -> root (§4.1/§4.2), or null.</summary>
            public readonly StoryFlowVariant Nearest;

            /// <summary>The ROOT-MOST declaration (§4.3), or null when nothing declares the id.</summary>
            public readonly StoryFlowVariable Declaration;

            /// <summary>Which tier <see cref="Nearest"/> came from — Declaration when it is null.</summary>
            public readonly StoryFlowDataAssetOrigin Origin;

            public Resolution(
                StoryFlowVariant nearest, StoryFlowVariable declaration, StoryFlowDataAssetOrigin origin)
            {
                Nearest = nearest;
                Declaration = declaration;
                Origin = origin;
            }
        }

        /// <summary>
        /// THE chain walk, leaf -> root, shared by every function in this file so none of
        /// them can disagree about chain order, the depth cap or the cycle guard. Calls
        /// <paramref name="visit"/> per level and stops early when it returns false.
        ///
        /// Mirrors runtime-data-assets.js's `depth++ &lt;= MAX_DEPTH` boundary exactly: the
        /// counter is tested BEFORE it is incremented, so MaxChainDepth ancestors plus the
        /// starting level — 65 levels — are visited (contract §4.4). An absent parent, a
        /// cycle, or the cap ends the walk silently, and callers answer with whatever they
        /// collected so far.
        ///
        /// The visited set is allocated only on the SECOND hop. Real chains are one to three
        /// levels, so the walk a running game does constantly — option conditions re-resolve
        /// on every render — usually never needs a set at all, and a root asset never does.
        /// (The Func closure per walk is left alone: a generic struct visitor would remove it
        /// too, but that is a measurement Task N2 should take with the node arms in place,
        /// not a shape guessed at now.)
        /// </summary>
        private static void WalkChain(
            Dictionary<string, StoryFlowDataAssetDef> seed, string assetId,
            Func<StoryFlowDataAssetDef, bool> visit)
        {
            if (seed == null || string.IsNullOrEmpty(assetId)) return;

            string firstId = null;
            HashSet<string> visited = null;
            int depth = 0;
            seed.TryGetValue(assetId, out var level);
            while (level != null && depth++ <= MaxChainDepth)
            {
                // depth is 1 on the first level — it was incremented by the test above — and
                // that is what identifies it, rather than a null check on firstId, which a
                // level carrying no id would defeat.
                if (depth == 1)
                {
                    firstId = level.Id;
                }
                else
                {
                    if (visited == null) visited = new HashSet<string> { firstId };
                    if (!visited.Add(level.Id)) return;
                }

                if (!visit(level)) return;

                if (string.IsNullOrEmpty(level.ParentId)) return;
                if (!seed.TryGetValue(level.ParentId, out level)) return;
            }
        }

        /// <summary>The declaration of <paramref name="variableId"/> on ONE level, or null.</summary>
        private static StoryFlowVariable FindDeclaredOnLevel(StoryFlowDataAssetDef level, string variableId)
        {
            foreach (var variable in level.Variables)
            {
                if (variable != null && variable.Id == variableId) return variable;
            }
            return null;
        }

        private static StoryFlowVariable FindDeclaredOnLevelByName(StoryFlowDataAssetDef level, string name)
        {
            // FIRST DECLARED WINS within a level, which only matters because names, unlike
            // ids, are not unique by construction: the editor keeps them unique per asset,
            // but nothing in the seed format enforces it. Between LEVELS the root-most
            // declaration still wins — that rule lives in the walk, not here.
            foreach (var variable in level.Variables)
            {
                if (variable != null && variable.Name == name) return variable;
            }
            return null;
        }

        // =====================================================================
        // Reads
        // =====================================================================

        /// <summary>
        /// Is <paramref name="assetId"/> carried by the seed at all? Tells a DEAD REFERENCE
        /// (a pill pointing at an asset this build does not carry) apart from a STALE BINDING
        /// (the asset is here, the variable is not), which FindDeclaration alone cannot.
        /// </summary>
        public static bool HasAsset(Dictionary<string, StoryFlowDataAssetDef> seed, string assetId)
        {
            return seed != null && !string.IsNullOrEmpty(assetId) && seed.ContainsKey(assetId);
        }

        /// <summary>
        /// The declaration <paramref name="variableId"/> resolves to on the asset's chain, or
        /// null when the asset is unknown or no level declares the id. ROOT-MOST declaration
        /// wins (contract §4.3): a descendant re-declaring an inherited id does not shadow the
        /// ancestor's definition.
        ///
        /// Returned BY REFERENCE into the seed — read it, never write through it.
        /// </summary>
        public static StoryFlowVariable FindDeclaration(
            Dictionary<string, StoryFlowDataAssetDef> seed, string assetId, string variableId)
        {
            if (string.IsNullOrEmpty(variableId)) return null;

            // Keep walking past a hit: each ancestor's declaration overwrites the
            // descendant's, so the walk ends holding the root-most one.
            StoryFlowVariable declared = null;
            WalkChain(seed, assetId, level =>
            {
                var decl = FindDeclaredOnLevel(level, variableId);
                if (decl != null) declared = decl;
                return true;
            });
            return declared;
        }

        /// <summary>
        /// FindDeclaration's twin for the public API, matching on the display NAME instead of
        /// the id, with the same root-most-wins rule.
        ///
        /// Two lookups exist because two audiences do: everything the exporter emits is keyed
        /// by id (ids survive a rename), while a game programmer holds an asset reference and
        /// the name they typed in the editor. The declaration handed back carries the id, so a
        /// name is resolved exactly once, at the boundary.
        ///
        /// The SAME NAME ON TWO DIFFERENT IDS across levels leaves the descendant's variable
        /// unreachable by name — one rule, no special case, and a name lookup with two right
        /// answers has no better one.
        ///
        /// SILENT, deliberately: this answers root-most and says nothing, matching Unreal and
        /// matching the rule TrySet's own doc states — the CALLER owns the warning. This is a
        /// per-call path (Task N3's typed getters and setters run it on every access, which a
        /// game can do per frame), so a diagnostic here would be an unlatched log in a hot
        /// loop. Duplicate-name diagnostics belong to the public API, which can decide once
        /// when a caller binds a name rather than every time it reads one.
        /// </summary>
        public static StoryFlowVariable FindDeclarationByName(
            Dictionary<string, StoryFlowDataAssetDef> seed, string assetId, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            StoryFlowVariable declared = null;
            WalkChain(seed, assetId, level =>
            {
                var decl = FindDeclaredOnLevelByName(level, name);
                if (decl != null) declared = decl;
                return true;
            });
            return declared;
        }

        /// <summary>
        /// Effective value of <paramref name="variableId"/> as seen by <paramref name="assetId"/>
        /// (contract §4), mirroring runtime-data-assets.js resolveEntry. Walks leaf -> root
        /// taking, per level and in order, the overlay entry, else that level's override;
        /// first hit wins, so an ancestor's entry cascades to every descendant that does not
        /// shadow it. With no such hit the ROOT-MOST declaration's own value answers.
        ///
        /// Returns false (leaving <paramref name="value"/> null) for an unknown asset or an id
        /// nothing declares. The value is COPIED OUT (contract §3: graph code must not be able
        /// to mutate the seed or the overlay through a read).
        ///
        /// THIS IS THE CHAIN RULE AND NOTHING MORE: it answers the value the contract says the
        /// chain holds, with no string-table lookup anywhere in it. Game-facing reads go through
        /// <see cref="TryRead"/>, which layers the localization gate on top. Saves and the
        /// fixture harnesses want this one — a persisted overlay entry must be the bytes the
        /// game wrote.
        /// </summary>
        public static bool TryResolve(
            Dictionary<string, StoryFlowDataAssetDef> seed,
            Dictionary<string, Dictionary<string, StoryFlowVariant>> overlay,
            string assetId, string variableId, out StoryFlowVariant value)
        {
            value = null;
            if (string.IsNullOrEmpty(variableId)) return false;

            var walk = WalkForValue(seed, overlay, assetId, variableId);
            if (walk.Declaration == null) return false;

            value = CopyOut(walk);
            return true;
        }

        /// <summary>
        /// THE READ DOOR: <see cref="TryResolve"/> plus the localization gate, and the ONE
        /// function every surface that hands a .sfd value to GAME CODE calls (both host mirrors,
        /// through StoryFlowDataAssetAccess; the node arms reach the same gate through
        /// <see cref="ReadBound"/>, which needs the §6.1 ladder answer as well).
        ///
        /// Localization spec §2's amendment of 2026-08-27 — which SUPERSEDES engine-contract
        /// 2.1's "a .sfd value is a literal, never look it up" — makes a Data Asset's declared
        /// string values player-facing prose that ships as stable table keys in
        /// data-assets.json's own <c>strings.en</c>, resolving through the very ladder every
        /// other artifact's strings already use (the importer merges that table into the project
        /// globals characters.json's strings feed).
        ///
        /// WHAT LOCALIZES, including the version 2 authored-override contract:
        ///
        ///  - DECLARATIONS AND VERSION 2 AUTHORED OVERRIDES. Older exports keep their
        ///    overrides literal. The root-most declaration's Localizable flag applies to both
        ///    tiers, including an override inherited through another asset.
        ///  - A WRITTEN VALUE NEVER LOCALIZES, including after a save/load, because the save
        ///    carries the overlay and a restored write was never content. The gate is WHERE THE
        ///    VALUE CAME FROM and never whether it LOOKS like a key: a write that happened to
        ///    equal a key would otherwise be translated into a string the game has since
        ///    redefined, and that failure is invisible in the source language.
        ///  - STRING-TYPED PROSE ONLY, decided by the DECLARED type — see
        ///    <see cref="LocalizeDeclaredValue"/>.
        ///
        /// The exporter builds declaration keys as <c>&lt;variableId&gt;.value</c> and override
        /// keys as <c>data.&lt;authoringAssetId&gt;.&lt;variableId&gt;.value</c>, with array indices
        /// or opaque map keys appended. This door resolves the bytes the seed carries, so an
        /// inherited override retains the key of the asset that authored it.
        ///
        /// RESOLUTION IS AT THIS DOOR, not baked into the seed, so a mid-session SetLanguage
        /// lands on the very next .sfd read. That is the same read-time posture this engine
        /// already has for every other string it holds (StoryFlowManager.SetLanguage's "what
        /// moves, and when"), and here it is forced rather than chosen: the seed is read-only
        /// forever and a baked value could no longer be told apart from a write.
        ///
        /// <paramref name="project"/> may be null (a store with no project localizes nothing and
        /// every value passes through), and an empty <paramref name="languageCode"/> reads as
        /// the source language, since no language table is keyed by it and the artifact's own
        /// source table answers.
        /// </summary>
        public static bool TryRead(
            Dictionary<string, StoryFlowDataAssetDef> seed,
            Dictionary<string, Dictionary<string, StoryFlowVariant>> overlay,
            StoryFlowProjectAsset project, string languageCode,
            string assetId, string variableId, out StoryFlowVariant value)
        {
            value = null;
            if (string.IsNullOrEmpty(variableId)) return false;

            var walk = WalkForValue(seed, overlay, assetId, variableId);
            if (walk.Declaration == null) return false;

            value = ReadOut(walk, project, languageCode);
            return true;
        }

        /// <summary>
        /// resolveEntry's ONE walk, with its two accumulators kept apart on purpose:
        ///  - <paramref name="nearest"/>: the FIRST overlay-or-override hit leaf -> root
        ///    (§4.1/§4.2).
        ///  - <paramref name="declaration"/>: the ROOT-MOST DECLARATION (§4.3), which is why
        ///    a declaration must NOT stop the walk.
        /// Returning early on an override would resurrect ORPHAN overrides on other levels;
        /// an override counts only where the chain still declares the id, and the declaration
        /// that proves it may be further up than the override is.
        ///
        /// The second accumulator holds the DECLARATION, not its value, because "did any
        /// level declare this id?" and "does that declaration carry a value?" are different
        /// questions (§4.3 asks only the first). Keying success on the value reports a
        /// valueless declaration as UNDECLARED — the same answer a deleted variable gets — so
        /// an accessor would take the degraded path instead of reading its type default.
        /// Nothing BuildSeed produces has a null value, but the seed is a plain dictionary any
        /// caller can assemble, and this walk should not depend on a repair that happens
        /// somewhere else.
        ///
        /// A NULL overlay skips the session lookups entirely — that is the write path, which
        /// needs the declaration and nothing else.
        ///
        /// <see cref="Resolution.Origin"/> reports WHICH of the three tiers answered, recorded
        /// at the branch that already knows rather than re-derived by a caller that no longer can
        /// (see <see cref="StoryFlowDataAssetOrigin"/>). With no nearest hit the declaration
        /// answered. The localization gate also accepts authored overrides in version 2 exports.
        ///
        /// The three travel back as ONE <see cref="Resolution"/> because they are one answer:
        /// they mean nothing apart, and every consumer below needs all three of THIS walk's.
        /// </summary>
        private static Resolution WalkForValue(
            Dictionary<string, StoryFlowDataAssetDef> seed,
            Dictionary<string, Dictionary<string, StoryFlowVariant>> overlay,
            string assetId, string variableId)
        {
            StoryFlowVariant foundValue = null;
            StoryFlowVariable foundDecl = null;
            var foundFrom = StoryFlowDataAssetOrigin.Override;

            WalkChain(seed, assetId, level =>
            {
                if (foundValue == null)
                {
                    if (overlay != null &&
                        overlay.TryGetValue(level.Id, out var levelOverlay) &&
                        levelOverlay.TryGetValue(variableId, out var written))
                    {
                        foundValue = written;
                        foundFrom = StoryFlowDataAssetOrigin.SessionWrite;
                    }
                    else if (level.Overrides.TryGetValue(variableId, out var overridden))
                    {
                        foundValue = overridden;
                        foundFrom = StoryFlowDataAssetOrigin.Override;
                    }
                }

                var decl = FindDeclaredOnLevel(level, variableId);
                if (decl != null) foundDecl = decl;
                return true;
            });

            return new Resolution(foundValue, foundDecl,
                foundValue == null ? StoryFlowDataAssetOrigin.Declaration : foundFrom);
        }

        /// <summary>
        /// The value a completed walk hands OUT: a deep copy, always (contract §3 — graph code
        /// must not be able to mutate the seed or the overlay through a read).
        ///
        /// A declaration carrying no value at all copies out as its TYPE DEFAULT rather than
        /// as nothing. The reference implementation reaches the same place by a different road
        /// — JS `declared = decl.value` can be undefined and resolve() still reports found —
        /// and no seed the exporter writes has the shape, so this is the translation that
        /// keeps "resolved" meaning the same thing in a language where the caller gets a typed
        /// object instead of undefined.
        /// </summary>
        private static StoryFlowVariant CopyOut(Resolution walk)
        {
            return new StoryFlowVariant(
                walk.Nearest ?? walk.Declaration.Value
                    ?? new StoryFlowVariant { Type = walk.Declaration.Type });
        }

        /// <summary>
        /// THE ONE LOCALIZATION GATE this plugin has, and the tail of every game-facing read:
        /// <see cref="CopyOut"/> plus the decision of whether the value is CONTENT.
        /// <see cref="TryRead"/> (the host mirrors) and <see cref="ReadBound"/> (the node arms
        /// and the degraded ladder) both end here, so the rule cannot hold at one surface and
        /// not the other — which is the failure a single-surface test cannot see.
        ///
        /// GATED ON PROVENANCE, never on the value's shape. Every rule behind that sentence is
        /// written out on <see cref="TryRead"/>; this is only where it is enforced.
        ///
        /// A null project is "no project to look anything up in", which is a hand-built store in
        /// a test and the write path's own CheckBound — both of which want the bytes.
        /// </summary>
        private static StoryFlowVariant ReadOut(
            Resolution walk, StoryFlowProjectAsset project, string languageCode)
        {
            var value = CopyOut(walk);
            if (project != null && walk.Declaration.Localizable &&
                (walk.Origin == StoryFlowDataAssetOrigin.Declaration ||
                 (walk.Origin == StoryFlowDataAssetOrigin.Override && project.DataAssetLocalizationVersion == 2)))
            {
                LocalizeDeclaredValue(walk.Declaration, project, languageCode, value);
            }
            // This door returns finished text (including deliberately literal overrides).
            // Downstream array/map consumers must not reinterpret it as a fresh authored key.
            if (walk.Declaration.IsArray && walk.Declaration.Type == StoryFlowVariableType.String && value.ArrayValue != null)
                foreach (var element in value.ArrayValue)
                    if (element != null) element.IsLiteralString = true;
            if (walk.Declaration.Type == StoryFlowVariableType.Map &&
                walk.Declaration.ValueType == StoryFlowVariableType.String && value.MapValue != null)
                foreach (var entry in value.MapValue)
                    if (entry?.Value != null) entry.Value.IsLiteralString = true;
            return value;
        }

        /// <summary>
        /// An authored value with its string-table keys resolved according to its declaration,
        /// IN PLACE on the copy the read is about to hand out.
        ///
        /// THE TYPE GATE IS THE EXPORTER'S, transcribed (json-export-strategy.ts
        /// keyDataAssetDeclaration): a string scalar, the elements of a string ARRAY, and the
        /// values of a map whose ValueType is String. Everything else — enum, image, audio,
        /// character, and every number and boolean — passes through untouched even when its
        /// value is a string, and a map's KEYS are identifiers that never resolve whatever their
        /// KeyType is. A gate that drifted from the exporter's would look up an id nothing keyed,
        /// or hand back a key.
        ///
        /// "an absent valueType is a string map" arrives here already settled: the importer
        /// defaults both map sides to "string" (ParseMapTypeInfo), which it must, because
        /// default(StoryFlowVariableType) in this engine is Boolean rather than String.
        /// </summary>
        private static void LocalizeDeclaredValue(
            StoryFlowVariable declaration, StoryFlowProjectAsset project, string languageCode,
            StoryFlowVariant value)
        {
            if (declaration.Type == StoryFlowVariableType.Map)
            {
                if (declaration.ValueType != StoryFlowVariableType.String || value.MapValue == null) return;
                foreach (var entry in value.MapValue)
                {
                    if (entry != null) LocalizeString(project, languageCode, entry.Value);
                }
                return;
            }

            if (declaration.Type != StoryFlowVariableType.String) return;

            if (declaration.IsArray)
            {
                if (value.ArrayValue == null) return;
                foreach (var element in value.ArrayValue) LocalizeString(project, languageCode, element);
                return;
            }

            LocalizeString(project, languageCode, value);
        }

        /// <summary>
        /// One string through the project's string ladder, left alone when it is not prose.
        ///
        /// THE SHARED LADDER, with NO current script: a .sfd id is keyed by data-assets.json,
        /// which the importer merges into the project globals, so passing a script would only
        /// let a script table shadow it. Reusing LookUpLocalizedIn rather than reaching into
        /// GetGlobalString is what keeps the overlay tier and the source-language fall-through
        /// the same here as everywhere else in the plugin — a second, simpler lookup would be
        /// the fourth ladder and would drift.
        ///
        /// PROSE means non-blank after trimming, exactly as the editor's keying pass decides it:
        /// a whitespace-only value keys nothing there, so looking one up here would probe an id
        /// no translator can ever reach. A miss answers with the key itself, which is the raw
        /// fallback tier and is why an unkeyed literal survives this untouched.
        /// </summary>
        private static void LocalizeString(
            StoryFlowProjectAsset project, string languageCode, StoryFlowVariant value)
        {
            if (value == null) return;
            string key = value.GetString();
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(key.Trim())) return;

            string resolved = StoryFlowExecutionContext.LookUpLocalizedIn(project, null, key, languageCode);
            if (resolved != null) value.SetString(resolved);
        }

        /// <summary>
        /// ONE WALK for a bound accessor's read: resolve the value AND settle which degraded
        /// rung (if any) the binding is on, without handing the declaration back.
        ///
        /// The pair this replaces — FindDeclaration for the ladder, then TryResolve for the
        /// value — walked the same chain twice on every single read, and option conditions
        /// re-resolve on every render. Splitting them also let the two disagree in principle
        /// (declMatches checked against one walk's declaration, the value taken from
        /// another's), which is a class of bug this shape cannot have.
        ///
        /// The DECLARATION deliberately does not come back out. Past a Ok result the caller's
        /// own snapshot (variableType / isArray / keyType / valueType) IS the chain's declared
        /// shape, so it already holds everything a declaration would tell it — and a
        /// declaration is a live reference into the seed (see this class's header).
        ///
        /// THE VALUE COMES OUT OF THE SAME GATE <see cref="TryRead"/> uses (<see cref="ReadOut"/>):
        /// a node arm is game code reading a .sfd value exactly as a host API call is, so a
        /// declared string resolves on both surfaces and a written one stays verbatim on both.
        /// Every localization rule lives on TryRead.
        /// </summary>
        public static StoryFlowDataAssetBinding ReadBound(
            Dictionary<string, StoryFlowDataAssetDef> seed,
            Dictionary<string, Dictionary<string, StoryFlowVariant>> overlay,
            StoryFlowProjectAsset project, string languageCode,
            string assetId, string variableId, StoryFlowDataAssetPinShape pins,
            out StoryFlowVariant value)
        {
            value = null;

            var status = CheckBoundInternal(
                seed, overlay, assetId, variableId, pins, out var walk);
            if (status != StoryFlowDataAssetBinding.Ok) return status;

            value = ReadOut(walk, project, languageCode);
            return StoryFlowDataAssetBinding.Ok;
        }

        /// <summary>
        /// <see cref="ReadBound"/> for the WRITE path: the same one walk and the same ladder
        /// answer, minus the overlay lookups and the copy-out that only a reader needs.
        /// </summary>
        public static StoryFlowDataAssetBinding CheckBound(
            Dictionary<string, StoryFlowDataAssetDef> seed,
            string assetId, string variableId, StoryFlowDataAssetPinShape pins)
        {
            return CheckBoundInternal(seed, null, assetId, variableId, pins, out _);
        }

        private static StoryFlowDataAssetBinding CheckBoundInternal(
            Dictionary<string, StoryFlowDataAssetDef> seed,
            Dictionary<string, Dictionary<string, StoryFlowVariant>> overlay,
            string assetId, string variableId, StoryFlowDataAssetPinShape pins,
            out Resolution walk)
        {
            walk = default;

            // Dead REFERENCE vs stale BINDING, told apart BEFORE the walk: FindDeclaration
            // answers "no" to both, and the two have different fixes (rebind the pill vs
            // rebind the accessor), so the caller gets to name the right one.
            if (!HasAsset(seed, assetId)) return StoryFlowDataAssetBinding.DeadRef;
            if (string.IsNullOrEmpty(variableId)) return StoryFlowDataAssetBinding.Missing;

            walk = WalkForValue(seed, overlay, assetId, variableId);
            if (walk.Declaration == null) return StoryFlowDataAssetBinding.Missing;

            // §6.1: the declaration moved under a live node. Treated as MISSING by every
            // caller, never coerced — within the string family a value carries no evidence of
            // its declared type, which is exactly why the check is on the DECLARATION.
            return DeclMatches(walk.Declaration, pins)
                ? StoryFlowDataAssetBinding.Ok
                : StoryFlowDataAssetBinding.Changed;
        }

        /// <summary>
        /// The NAMES of every variable the asset's chain DECLARES (contract §11.1) — the
        /// Get Variable Names node's whole answer, mirroring runtime-data-assets.js
        /// variableNames (a consumer of the same eachDeclaration walk its debugger table
        /// uses; here the walk is <see cref="WalkChain"/>, THE walk every resolver door
        /// shares, not a second one).
        ///
        /// ORDER is the editor's: chain ROOT-FIRST, each level's variables in file order.
        /// WalkChain enumerates leaf -> root, so the levels are collected and then
        /// enumerated backwards — that reversal is the only thing making first-wins below
        /// mean root-most-wins, exactly as the reference implementation's own loop notes.
        ///
        /// DECLARATIONS ONLY. Overrides are never visited: an override re-states a value
        /// for a variable the chain already declares, so it can neither add a name nor
        /// duplicate one — and an ORPHAN override, whose id nothing on the chain declares,
        /// adds nothing either. BuildSeed drops orphans on the way in, but the walk does
        /// not lean on that repair: a seed assembled by hand carries them and the list
        /// must not change.
        ///
        /// SHADOWING is the resolver's: where two levels declare the same ID, the
        /// root-most declaration keeps its slot (§4.3's rule, the one FindDeclaration
        /// reaches by walking to the end of the chain). Where two levels declare the same
        /// NAME under different ids, the name is stated once, at the root-most position —
        /// a by-name reader (<see cref="FindDeclarationByName"/>) can only ever reach one
        /// of them. Empty names are skipped.
        ///
        /// CATEGORY rows never appear, because they never enter this seed at all: the
        /// importer drops them (the §2.1 category-drop ruling — the reference
        /// implementation instead skips them mid-walk while letting them claim their id
        /// slot, a skew the drop already sanctioned for the resolver).
        ///
        /// An unknown asset — which is every degraded shape the node can meet — answers an
        /// EMPTY list, never null and never a throw.
        /// </summary>
        public static List<string> VariableNames(
            Dictionary<string, StoryFlowDataAssetDef> seed, string assetId)
        {
            var names = new List<string>();
            if (!HasAsset(seed, assetId)) return names;

            var chain = new List<StoryFlowDataAssetDef>();
            WalkChain(seed, assetId, level =>
            {
                chain.Add(level);
                return true;
            });

            var claimedIds = new HashSet<string>();
            var claimedNames = new HashSet<string>();
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                foreach (var decl in chain[i].Variables)
                {
                    // FIRST-WINS on the id — root-most-wins, per the reverse enumeration.
                    if (decl == null || !claimedIds.Add(decl.Id ?? "")) continue;

                    string name = decl.Name ?? "";
                    if (name.Length == 0 || !claimedNames.Add(name)) continue;
                    names.Add(name);
                }
            }
            return names;
        }

        /// <summary>
        /// TryResolve's convenience twin: the resolved value, or null when nothing resolves
        /// (contract §4.5 / §9.1 — the fixtures' "resolved": false). Callers that must tell
        /// "unset" apart from a legitimately empty value want TryResolve instead.
        /// </summary>
        public static StoryFlowVariant Resolve(
            Dictionary<string, StoryFlowDataAssetDef> seed,
            Dictionary<string, Dictionary<string, StoryFlowVariant>> overlay,
            string assetId, string variableId)
        {
            return TryResolve(seed, overlay, assetId, variableId, out var value) ? value : null;
        }

        /// <summary>
        /// True when any level of the asset's chain declares the id (what a write validates
        /// against). Unlike FindDeclaration this stops at the first hit — any declaration
        /// answers the question.
        /// </summary>
        public static bool IsDeclaredOnChain(
            Dictionary<string, StoryFlowDataAssetDef> seed, string assetId, string variableId)
        {
            if (string.IsNullOrEmpty(variableId)) return false;

            bool declared = false;
            WalkChain(seed, assetId, level =>
            {
                if (FindDeclaredOnLevel(level, variableId) == null) return true;
                declared = true;
                return false;
            });
            return declared;
        }

        // =====================================================================
        // Writes
        // =====================================================================

        /// <summary>
        /// Records a session write in the overlay (contract §5), reporting whether it landed.
        /// Writes go to THE REFERENCED ASSET'S OWN LEVEL, always — never to the declaring
        /// ancestor: setting via a child overrides for that child's subtree, setting via the
        /// base cascades to every descendant that does not shadow it. There is no
        /// "write to base" switch.
        ///
        /// Refuses (returning false, no write) an unknown asset or an id no chain level
        /// declares. Map writes REPLACE the whole value, and the value is deep-copied on the
        /// way in so a caller mutating its own container afterwards cannot reach into the
        /// store.
        ///
        /// The CALLER owns the warning: the node arms have a per-node warn latch (contract §6)
        /// and the public API surface does not, so this reports the refusal rather than
        /// logging it.
        /// </summary>
        public static bool TrySet(
            Dictionary<string, StoryFlowDataAssetDef> seed,
            Dictionary<string, Dictionary<string, StoryFlowVariant>> overlay,
            string assetId, string variableId, StoryFlowVariant value)
        {
            if (overlay == null || value == null) return false;
            // Two guards, and the first is NOT redundant despite answering the same way as
            // the second for an absent asset today (the declaration walk starts at
            // seed[assetId], so it also refuses one). They ask different questions —
            // "is this asset here at all?" vs "does its chain declare this id?" — which is
            // the line the degraded ladder draws between a dead reference and a stale
            // binding (§6), and the read side draws it with HasAsset too. Both are checked
            // BEFORE the per-asset table is minted, so a refused write leaves no empty
            // table behind to ride every later save.
            if (!HasAsset(seed, assetId)) return false;
            if (!IsDeclaredOnChain(seed, assetId, variableId)) return false;

            if (!overlay.TryGetValue(assetId, out var levelOverlay))
            {
                levelOverlay = new Dictionary<string, StoryFlowVariant>();
                overlay[assetId] = levelOverlay;
            }
            levelOverlay[variableId] = new StoryFlowVariant(value);
            Revisions.GetOrCreateValue(overlay).Value++;
            return true;
        }

        /// <summary>Drops every session write (game restart / new game). The seed is untouched.</summary>
        public static void ResetOverlay(Dictionary<string, Dictionary<string, StoryFlowVariant>> overlay)
        {
            overlay?.Clear();
            if (overlay != null) Revisions.GetOrCreateValue(overlay).Value++;
        }

        // =====================================================================
        // The snapshot match rule
        // =====================================================================

        /// <summary>
        /// Does the seed's declaration still match the spawn-time snapshot an accessor node's
        /// pins were built from (contract §6.1, mirroring runtime-data-assets.js declMatches)?
        /// Stale is treated as MISSING — no silent coercion, ever, because within the string
        /// family a value carries no evidence of which type declared it.
        ///
        /// Takes the snapshot as <see cref="StoryFlowDataAssetPinShape"/> — the WIRE STRINGS the
        /// exporter wrote, because that is what the node data holds; they convert through the
        /// one shared table (StoryFlowWireTypes), and a type string that table does not know can
        /// never match, so a garbled snapshot degrades instead of resolving.
        ///
        /// KeyType/ValueType are compared for MAPS ONLY; IsArray always, since an array pin and
        /// a scalar pin of the same type are different pins.
        /// </summary>
        public static bool DeclMatches(StoryFlowVariable declaration, StoryFlowDataAssetPinShape pins)
        {
            if (declaration == null) return false;
            if (!StoryFlowWireTypes.TryParseWireType(pins.VariableType, out var type)) return false;
            if (declaration.Type != type) return false;
            if (declaration.IsArray != pins.IsArray) return false;

            if (type == StoryFlowVariableType.Map)
            {
                if (!StoryFlowWireTypes.TryParseWireType(pins.KeyType, out var parsedKey)) return false;
                if (declaration.KeyType != parsedKey) return false;
                if (!StoryFlowWireTypes.TryParseWireType(pins.ValueType, out var parsedValue)) return false;
                if (declaration.ValueType != parsedValue) return false;
            }

            return true;
        }
    }
}
