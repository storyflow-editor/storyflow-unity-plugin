using System.Collections.Generic;
using StoryFlow.Data;
using UnityEngine;

namespace StoryFlow.Execution
{
    /// <summary>
    /// Where a resolved map input chain terminated (see <see cref="MapEvaluator.ResolveMapInputVariable"/>).
    /// ScriptVariable/GlobalVariable carry the terminal getMap/setMap node's bound variable —
    /// the LIVE storage mutators write through and setMap aliases. CharacterVariable and
    /// RunScriptOutput are READ-ONLY per the cross-runtime contract (the HTML runtime hands
    /// mutators a throwaway Map for those sources, and setMap SNAPSHOTS rather than aliases).
    /// </summary>
    internal enum MapSourceKind
    {
        Unresolved,
        ScriptVariable,
        GlobalVariable,
        CharacterVariable,
        RunScriptOutput,
    }

    /// <summary>
    /// Resolves map values from expression node chains.
    /// Maps alias: mutators (setMapValue/removeMapKey/clearMap) write through the resolved
    /// variable's live entry list, and every read of the same variable observes the change —
    /// so this evaluator hands out LIVE storage, never copies.
    /// </summary>
    internal static class MapEvaluator
    {
        /// <summary>
        /// Resolves the map input wired into a node and returns its LIVE entry list,
        /// or null when unresolved (callers treat null as an empty map: mutations no-op
        /// and reads return type defaults).
        /// </summary>
        internal static List<StoryFlowMapEntry> EvaluateMapInput(StoryFlowExecutionContext ctx, StoryFlowNode node, string optionId)
        {
            var variable = ResolveMapInputVariable(ctx, node, optionId, out _);
            return variable?.Value.GetMap();
        }

        /// <summary>
        /// Resolves the variable behind a node's map input. Map handles bake the key/value
        /// types into the handle ID ("target-{nodeId}-map-{keyType}-{valueType}-{optionId}");
        /// catalog map op nodes carry keyType/valueType in NODE DATA — without them the input
        /// handle cannot be built, so resolution fails to defaults (warn once per node).
        /// </summary>
        internal static StoryFlowVariable ResolveMapInputVariable(
            StoryFlowExecutionContext ctx, StoryFlowNode node, string optionId, out MapSourceKind sourceKind)
        {
            sourceKind = MapSourceKind.Unresolved;
            if (ctx?.CurrentScript == null || node == null) return null;

            var keyType = node.GetData("keyType");
            var valueType = node.GetData("valueType");
            if (string.IsNullOrEmpty(keyType) || string.IsNullOrEmpty(valueType))
            {
                ctx.MaybeWarnMissingMapTypes(node);
                return null;
            }

            return ResolveMapInputVariableByHandle(
                ctx, node, StoryFlowHandles.InMap(keyType, valueType, optionId), out sourceKind);
        }

        /// <summary>
        /// Same resolution as <see cref="ResolveMapInputVariable"/> but for an explicit
        /// input handle suffix. Walks upstream through chained mutators (a mutator mutates
        /// the origin variable's live storage in place, so its map output IS its own map
        /// input "2" — mirrors the HTML runtime's evaluateMapFromNode chain arm), then
        /// resolves the terminal node to a variable with live map storage established.
        /// </summary>
        internal static StoryFlowVariable ResolveMapInputVariableByHandle(
            StoryFlowExecutionContext ctx, StoryFlowNode node, string handleSuffix, out MapSourceKind sourceKind)
        {
            sourceKind = MapSourceKind.Unresolved;
            if (ctx?.CurrentScript == null || node == null) return null;

            var edge = ctx.CurrentScript.FindInputEdge(node.Id, handleSuffix);
            if (edge == null) return null;

            var sourceNode = ctx.CurrentScript.GetNode(edge.Source);

            // Walk upstream while the source is a mutator. Hop-bounded to defend against
            // cyclic graphs (the HTML recursion has no guard; we fail to unresolved).
            int hops = 0;
            while (sourceNode != null && IsMapMutatorNode(sourceNode.Type))
            {
                if (++hops > StoryFlowExecutionContext.MaxEvaluationDepth)
                {
                    Debug.LogWarning($"[StoryFlow] Map mutator chain too deep at node {sourceNode.Id} - possible cycle.");
                    return null;
                }

                var keyType = sourceNode.GetData("keyType");
                var valueType = sourceNode.GetData("valueType");
                if (string.IsNullOrEmpty(keyType) || string.IsNullOrEmpty(valueType))
                {
                    ctx.MaybeWarnMissingMapTypes(sourceNode);
                    return null;
                }

                var upstreamEdge = ctx.CurrentScript.FindInputEdge(
                    sourceNode.Id, StoryFlowHandles.InMap(keyType, valueType, "2"));
                if (upstreamEdge == null) return null;

                // Keep the terminal edge — its SourceHandle carries the runScript
                // "-out-" UUID the RunScript arm below parses.
                edge = upstreamEdge;
                sourceNode = ctx.CurrentScript.GetNode(upstreamEdge.Source);
            }

            if (sourceNode == null) return null;

            // Forward-compat: warn once and resolve to Unresolved when the terminal node
            // is a type the plugin does not understand.
            if (ctx.MaybeWarnUnknownNode(sourceNode)) return null;

            switch (sourceNode.Type)
            {
                case StoryFlowNodeType.GetMap:
                case StoryFlowNodeType.SetMap:
                {
                    // Resolve the bound variable (locals first, then globals) and return it
                    // with LIVE map storage established — never hand out a copy here.
                    var variable = ctx.FindVariable(sourceNode.GetData("variable"));
                    if (variable != null && variable.Type == StoryFlowVariableType.Map)
                    {
                        if (variable.Value.MapValue == null)
                        {
                            // Variable imported without an initial value — establish the map
                            // type so the returned storage is correctly typed for mutation.
                            variable.Value.SetMap(new List<StoryFlowMapEntry>());
                        }
                        sourceKind = ctx.LocalVariables.ContainsKey(variable.Id)
                            ? MapSourceKind.ScriptVariable
                            : MapSourceKind.GlobalVariable;
                        return variable;
                    }
                    return null;
                }

                case StoryFlowNodeType.GetCharacterVar:
                case StoryFlowNodeType.SetCharacterVar:
                {
                    // Map-typed character variables resolve to the character's LIVE runtime
                    // variable — fine for pure reads (zero-copy, observably identical to a
                    // copy). But the cross-runtime contract makes charvar-sourced chains
                    // READ-ONLY (HTML hands mutators a throwaway snapshot; setMap snapshots
                    // rather than aliases), so the CharacterVariable kind flags the arm and
                    // mutating/aliasing callers must honor it.
                    if (sourceNode.GetData("variableType") != "map") return null;

                    var charPath = EvaluatorHelpers.ResolveCharacterPath(ctx, sourceNode);
                    var characterData = ctx.FindCharacter(charPath);
                    var charVar = characterData?.FindVariableByName(sourceNode.GetData("variableName"));
                    if (charVar != null && charVar.Type == StoryFlowVariableType.Map)
                    {
                        if (charVar.Value.MapValue == null)
                        {
                            charVar.Value.SetMap(new List<StoryFlowMapEntry>());
                        }
                        sourceKind = MapSourceKind.CharacterVariable;
                        return charVar;
                    }
                    return null;
                }

                case StoryFlowNodeType.RunScript:
                {
                    // Map-typed runScript outputs. HandleEnd already stores each output
                    // variable's value as a DETACHED deep copy in the node state's
                    // OutputValues (the HTML runtime converts _outputValues entry arrays
                    // to a fresh Map at the read site — the call boundary is observably a
                    // snapshot both ways). Resolve via the terminal edge's "-out-{varId}"
                    // source handle (the ResolveRunScriptOutput precedent) and flag the
                    // chain READ-ONLY like charvar chains: mutators no-op, setMap
                    // snapshots. Missing or non-map output → Unresolved (reads return
                    // type defaults; setMap wipes to a fresh empty map — HTML's "missing
                    // _outputValues returns an empty Map" pin).
                    var outputVariant = EvaluatorHelpers.ResolveRunScriptOutputByHandle(
                        ctx, sourceNode, edge.SourceHandle);
                    if (outputVariant == null || outputVariant.Type != StoryFlowVariableType.Map)
                        return null;

                    sourceKind = MapSourceKind.RunScriptOutput;
                    // Synthetic wrapper: runScript outputs are variants, not variables.
                    // Callers only read Value through it (the read-only kind gates every
                    // mutating/aliasing path before scope checks touch Id).
                    return new StoryFlowVariable
                    {
                        Id = sourceNode.Id,
                        Name = sourceNode.Id,
                        Type = StoryFlowVariableType.Map,
                        Value = outputVariant,
                    };
                }

                default:
                    return null;
            }
        }

        /// <summary>
        /// Evaluates a map op node's key input: wired "{keyType}-{optionId}" edge first,
        /// else the inline node-data "key" fallback. Inline keys stay RAW — map keys are
        /// raw identifiers, never strings-table keys (wired chains go through the normal
        /// typed evaluators, which resolve string variable VALUES as usual).
        /// </summary>
        internal static StoryFlowVariant EvaluateMapOpKeyInput(StoryFlowExecutionContext ctx, StoryFlowNode node, string optionId)
        {
            var key = new StoryFlowVariant();
            if (ctx?.CurrentScript == null || node == null) return key;

            var keyType = node.GetData("keyType");
            var handleSuffix = keyType + "-" + optionId;

            if (keyType == "integer")
            {
                key.SetInt(StoryFlowEvaluator.EvaluateIntegerWithDefault(
                    ctx, node.Id, handleSuffix, node.GetDataInt("key")));
                return key;
            }

            var edge = ctx.CurrentScript.FindInputEdge(node.Id, handleSuffix);
            var sourceNode = edge != null ? ctx.CurrentScript.GetNode(edge.Source) : null;
            if (sourceNode != null)
            {
                // Propagate the edge's source handle (save/restore, mirroring the typed
                // Evaluate entry points) so multi-output sources — forEachMap
                // "-key"/"-value", getMapValue "-isValid" — discriminate correctly.
                var prevHandle = ctx.LastSourceHandle;
                ctx.LastSourceHandle = edge.SourceHandle;
                if (keyType == "enum")
                    key.SetEnum(EnumEvaluator.EvaluateFromNode(ctx, sourceNode));
                else
                    key.SetString(StringEvaluator.EvaluateFromNode(ctx, sourceNode));
                ctx.LastSourceHandle = prevHandle;
            }
            else if (keyType == "enum")
            {
                key.SetEnum(node.GetData("key"));
            }
            else
            {
                key.SetString(node.GetData("key"));
            }
            return key;
        }

        /// <summary>
        /// Evaluates a setMapValue node's value input: wired "{valueType}-{optionId}" edge
        /// first, else the inline node-data "value" fallback (typed off the declared
        /// valueType). String-family inline fallbacks resolve through the strings table,
        /// matching the scalar Set* handler precedent.
        /// </summary>
        internal static StoryFlowVariant EvaluateMapOpValueInput(StoryFlowExecutionContext ctx, StoryFlowNode node, string optionId)
        {
            var value = new StoryFlowVariant();
            if (ctx?.CurrentScript == null || node == null) return value;

            var valueType = node.GetData("valueType");
            var handleSuffix = valueType + "-" + optionId;
            switch (valueType)
            {
                case "boolean":
                    value.SetBool(StoryFlowEvaluator.EvaluateBooleanWithDefault(
                        ctx, node.Id, handleSuffix, node.GetDataBool("value")));
                    break;
                case "integer":
                    value.SetInt(StoryFlowEvaluator.EvaluateIntegerWithDefault(
                        ctx, node.Id, handleSuffix, node.GetDataInt("value")));
                    break;
                case "float":
                    value.SetFloat(StoryFlowEvaluator.EvaluateFloatWithDefault(
                        ctx, node.Id, handleSuffix, node.GetDataFloat("value")));
                    break;
                case "enum":
                {
                    var edge = ctx.CurrentScript.FindInputEdge(node.Id, handleSuffix);
                    var sourceNode = edge != null ? ctx.CurrentScript.GetNode(edge.Source) : null;
                    if (sourceNode != null)
                    {
                        // Same source-handle propagation as EvaluateMapOpKeyInput
                        var prevHandle = ctx.LastSourceHandle;
                        ctx.LastSourceHandle = edge.SourceHandle;
                        value.SetEnum(EnumEvaluator.EvaluateFromNode(ctx, sourceNode));
                        ctx.LastSourceHandle = prevHandle;
                    }
                    else
                    {
                        value.SetEnum(node.GetData("value"));
                    }
                    break;
                }
                case "image":
                case "character":
                case "audio":
                {
                    // Asset-key values flow through the string evaluator but keep their
                    // declared type (matches ArrayNodeHandler.EvaluateElementValue).
                    var val = StoryFlowEvaluator.EvaluateStringWithDefault(
                        ctx, node.Id, handleSuffix, node.GetData("value"));
                    value.Type = valueType == "image" ? StoryFlowVariableType.Image
                        : valueType == "character" ? StoryFlowVariableType.Character
                        : StoryFlowVariableType.Audio;
                    value.StringValue = val ?? "";
                    break;
                }
                default: // string
                    value.SetString(StoryFlowEvaluator.EvaluateStringWithDefault(
                        ctx, node.Id, handleSuffix, node.GetData("value")));
                    break;
            }
            return value;
        }

        /// <summary>
        /// Finds the index of the entry matching the given key, or -1. Integer keys compare
        /// numerically; string/enum keys compare ordinal exact (case-sensitive).
        /// Deliberately a linear scan with NO key index: story maps are small (tens of
        /// entries), op handlers do a single lookup per execution, and the HTML/Unreal
        /// runtimes scan the same way — revisit only if profiling ever flags it.
        /// </summary>
        internal static int FindMapEntryByKey(List<StoryFlowMapEntry> entries, string keyType, StoryFlowVariant key)
        {
            if (entries == null || key == null) return -1;

            if (keyType == "integer")
            {
                int keyInt = key.GetInt();
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i].Key != null && entries[i].Key.GetInt() == keyInt) return i;
                }
                return -1;
            }

            string keyString = AsText(key);
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Key != null && AsText(entries[i].Key) == keyString) return i;
            }
            return -1;
        }

        /// <summary>
        /// Computes a getMapValue lookup: key (input "2" / inline fallback) FIRST, then the
        /// map (input "1"). The key-first order is the Unreal port's pointer-lifetime rule
        /// (resolve non-map inputs before taking the live map reference); the HTML runtime
        /// resolves map-first — observably equivalent, since neither input evaluation can
        /// mutate the other.
        /// Returns true and sets <paramref name="value"/> on a hit; false on miss/unresolved
        /// (the IsValid output) leaving <paramref name="value"/> null.
        /// </summary>
        internal static bool ComputeGetMapValue(StoryFlowExecutionContext ctx, StoryFlowNode node, out StoryFlowVariant value)
        {
            value = null;
            if (ctx == null || node == null) return false;

            var key = EvaluateMapOpKeyInput(ctx, node, "2");
            var map = EvaluateMapInput(ctx, node, "1");
            if (map == null) return false;

            int entryIndex = FindMapEntryByKey(map, node.GetData("keyType"), key);
            if (entryIndex < 0) return false;

            value = map[entryIndex].Value;
            return true;
        }

        /// <summary>
        /// Deep-copies an entry list into fresh storage (fresh entries AND fresh key/value
        /// variants). Used for boundary snapshots: setMap from read-only sources and the
        /// forEachMap iteration snapshot.
        /// </summary>
        internal static List<StoryFlowMapEntry> CopyEntries(List<StoryFlowMapEntry> source)
        {
            var result = new List<StoryFlowMapEntry>(source?.Count ?? 0);
            if (source == null) return result;
            foreach (var entry in source)
            {
                if (entry == null) continue;
                result.Add(new StoryFlowMapEntry
                {
                    Key = entry.Key != null ? new StoryFlowVariant(entry.Key) : new StoryFlowVariant(),
                    Value = entry.Value != null ? new StoryFlowVariant(entry.Value) : new StoryFlowVariant(),
                });
            }
            return result;
        }

        /// <summary>
        /// Renders a string-family variant (map key or value) as plain text. Enum variants
        /// store their value in EnumValue, everything else in StringValue (GetString is
        /// type-gated), so this covers key comparison and string-family value reads uniformly.
        /// </summary>
        internal static string AsText(StoryFlowVariant variant)
        {
            return variant.Type == StoryFlowVariableType.Enum ? variant.GetEnum() : variant.GetString();
        }

        /// <summary>
        /// Returns true if the node type is a map mutator (its map output is its own
        /// mutated map input — the chain-walk hop in <see cref="ResolveMapInputVariableByHandle"/>).
        /// </summary>
        internal static bool IsMapMutatorNode(StoryFlowNodeType type)
        {
            switch (type)
            {
                case StoryFlowNodeType.SetMapValue:
                case StoryFlowNodeType.RemoveMapKey:
                case StoryFlowNodeType.ClearMap:
                    return true;
                default:
                    return false;
            }
        }
    }
}
