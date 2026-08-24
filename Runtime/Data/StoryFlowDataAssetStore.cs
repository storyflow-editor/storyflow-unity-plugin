using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
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
    /// </summary>
    public static class StoryFlowDataAssetStore
    {
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
        /// variables do: data-assets.json carries no strings table (the exporter writes .sfd
        /// values verbatim), so a .sfd string value is a literal, and running the lookup over
        /// it would replace every literal with a failed lookup.
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
        /// THE chain walk, leaf -> root, shared by every function in this file so none of
        /// them can disagree about chain order, the depth cap or the cycle guard. Calls
        /// <paramref name="visit"/> per level and stops early when it returns false.
        ///
        /// Mirrors runtime-data-assets.js's `depth++ &lt;= MAX_DEPTH` boundary exactly: the
        /// counter is tested BEFORE it is incremented, so MaxChainDepth ancestors plus the
        /// starting level — 65 levels — are visited (contract §4.4). An absent parent, a
        /// cycle, or the cap ends the walk silently, and callers answer with whatever they
        /// collected so far.
        /// </summary>
        private static void WalkChain(
            Dictionary<string, StoryFlowDataAssetDef> seed, string assetId,
            Func<StoryFlowDataAssetDef, bool> visit)
        {
            if (seed == null || string.IsNullOrEmpty(assetId)) return;

            var visited = new HashSet<string>();
            int depth = 0;
            seed.TryGetValue(assetId, out var level);
            while (level != null && depth++ <= MaxChainDepth && !visited.Contains(level.Id))
            {
                visited.Add(level.Id);
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
        /// answers has no better one. Logged, because from the caller's chair it looks like
        /// the setter wrote to the wrong variable.
        /// </summary>
        public static StoryFlowVariable FindDeclarationByName(
            Dictionary<string, StoryFlowDataAssetDef> seed, string assetId, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            StoryFlowVariable declared = null;
            WalkChain(seed, assetId, level =>
            {
                var decl = FindDeclaredOnLevelByName(level, name);
                if (decl != null)
                {
                    if (declared != null && declared.Id != decl.Id)
                    {
                        Debug.Log($"[StoryFlow] Data Asset '{assetId}' has the name '{name}' on two " +
                                  $"different variables ('{decl.Id}' and '{declared.Id}') - the root-most " +
                                  "one wins and the other cannot be reached by name.");
                    }
                    declared = decl;
                }
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
        /// </summary>
        public static bool TryResolve(
            Dictionary<string, StoryFlowDataAssetDef> seed,
            Dictionary<string, Dictionary<string, StoryFlowVariant>> overlay,
            string assetId, string variableId, out StoryFlowVariant value)
        {
            value = null;
            if (string.IsNullOrEmpty(variableId)) return false;

            // resolveEntry's two accumulators, kept apart on purpose:
            //  - nearest: the FIRST overlay-or-override hit leaf -> root (§4.1/§4.2).
            //  - declaration: the ROOT-MOST DECLARATION (§4.3), which is why a declaration
            //    must NOT stop the walk.
            // Returning early on an override would resurrect ORPHAN overrides on other
            // levels; an override counts only where the chain still declares the id, and the
            // declaration that proves it may be further up than the override is.
            //
            // The second accumulator holds the DECLARATION, not its value, because "did any
            // level declare this id?" and "does that declaration carry a value?" are
            // different questions (§4.3 asks only the first). Keying success on the value
            // reports a valueless declaration as UNDECLARED — the same answer a deleted
            // variable gets — so an accessor would take the degraded path instead of reading
            // its type default. Nothing BuildSeed produces has a null value, but the seed is
            // a plain dictionary any caller can assemble, and this function should not
            // depend on a repair that happens in another one.
            StoryFlowVariant nearest = null;
            StoryFlowVariable declaration = null;

            WalkChain(seed, assetId, level =>
            {
                if (nearest == null)
                {
                    if (overlay != null &&
                        overlay.TryGetValue(level.Id, out var levelOverlay) &&
                        levelOverlay.TryGetValue(variableId, out var written))
                    {
                        nearest = written;
                    }
                    else if (level.Overrides.TryGetValue(variableId, out var overridden))
                    {
                        nearest = overridden;
                    }
                }

                var decl = FindDeclaredOnLevel(level, variableId);
                if (decl != null) declaration = decl;
                return true;
            });

            if (declaration == null) return false;

            // A declaration carrying no value at all resolves to its TYPE DEFAULT rather
            // than to nothing. The reference implementation reaches the same place by a
            // different road — JS `declared = decl.value` can be undefined and resolve()
            // still reports found — and no seed the exporter writes has the shape, so this
            // is the translation that keeps "resolved" meaning the same thing in a language
            // where the caller gets a typed object instead of undefined.
            value = new StoryFlowVariant(
                nearest ?? declaration.Value ?? new StoryFlowVariant { Type = declaration.Type });
            return true;
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
            return true;
        }

        /// <summary>Drops every session write (game restart / new game). The seed is untouched.</summary>
        public static void ResetOverlay(Dictionary<string, Dictionary<string, StoryFlowVariant>> overlay)
        {
            overlay?.Clear();
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
        /// Takes the snapshot as the WIRE STRINGS the exporter wrote, because that is what the
        /// node data holds; they convert through the one shared table (StoryFlowWireTypes), and
        /// a type string that table does not know can never match, so a garbled snapshot
        /// degrades instead of resolving.
        ///
        /// keyType/valueType are compared for MAPS ONLY; isArray always, since an array pin and
        /// a scalar pin of the same type are different pins.
        /// </summary>
        public static bool DeclMatches(
            StoryFlowVariable declaration, string variableType, bool isArray,
            string keyType, string valueType)
        {
            if (declaration == null) return false;
            if (!StoryFlowWireTypes.TryParseWireType(variableType, out var type)) return false;
            if (declaration.Type != type) return false;
            if (declaration.IsArray != isArray) return false;

            if (type == StoryFlowVariableType.Map)
            {
                if (!StoryFlowWireTypes.TryParseWireType(keyType, out var parsedKey)) return false;
                if (declaration.KeyType != parsedKey) return false;
                if (!StoryFlowWireTypes.TryParseWireType(valueType, out var parsedValue)) return false;
                if (declaration.ValueType != parsedValue) return false;
            }

            return true;
        }
    }
}
