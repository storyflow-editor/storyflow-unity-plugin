using System.Collections.Generic;
using StoryFlow.Data;
using UnityEngine;

namespace StoryFlow.Execution.NodeHandlers
{
    /// <summary>
    /// Handles map operations: SetMap, the mutators (SetMapValue/RemoveMapKey/ClearMap),
    /// and ForEachMap. GetMap, GetMapValue, HasMapKey, MapSize, MapKeys, and MapValues
    /// are evaluated lazily (no-ops in the dispatcher).
    /// </summary>
    public static class MapNodeHandler
    {
        // =====================================================================
        // SetMap — point the bound variable at the wired map
        // =====================================================================

        public static void HandleSetMap(StoryFlowComponent component, StoryFlowNode node)
        {
            var context = component.GetContext();

            var variableId = node.GetData("variable");
            var variable = context.FindVariable(variableId);
            if (variable == null || variable.Type != StoryFlowVariableType.Map)
            {
                // HTML returns early without trace/dispatch but still continues exec
                FollowFlowOrFallthrough(component, context, node);
                return;
            }

            // Missing K/V types: the map input handle cannot be built — HTML behaves as
            // disconnected (keeps the current value, still traces and dispatches).
            var keyType = node.GetData("keyType");
            var valueType = node.GetData("valueType");
            if (!string.IsNullOrEmpty(keyType) && !string.IsNullOrEmpty(valueType))
            {
                var handleSuffix = StoryFlowHandles.InMap(keyType, valueType, "2");
                if (context.CurrentScript.FindInputEdge(node.Id, handleSuffix) != null)
                {
                    var sourceVar = MapEvaluator.ResolveMapInputVariable(context, node, "2", out var sourceKind);
                    if (sourceVar != null)
                    {
                        if (sourceKind == MapSourceKind.CharacterVariable ||
                            sourceKind == MapSourceKind.RunScriptOutput)
                        {
                            // Read-only-terminal chain: HTML's setMap SNAPSHOTS the entries
                            // into fresh objects (charvars get a throwaway snapshot; runScript
                            // outputs are converted to a fresh Map at the read site). Neither
                            // aliases live storage.
                            variable.Value.SetMap(MapEvaluator.CopyEntries(sourceVar.Value.GetMap()));
                        }
                        else
                        {
                            // Wired and resolved: ALIAS the origin variable's live map storage
                            // (HTML assigns the _runtimeMap reference — see SetMapAlias).
                            variable.Value.SetMapAlias(sourceVar.Value);
                        }
                    }
                    else
                    {
                        // Wired but unresolved: HTML assigns a fresh empty Map
                        variable.Value.SetMap(new List<StoryFlowMapEntry>());
                    }
                }
                // No edge: keep the current value (maps have no inline fallback)
            }

            // Trace shape pinned by the cross-runtime fixture: size=, not value=
            bool isGlobal = !context.LocalVariables.ContainsKey(variable.Id);
            component.Trace($"VAR SET \"{variable.Name}\" global={isGlobal.ToString().ToLower()} size={variable.Value.GetMap().Count}");
            component.BroadcastVariableChanged(variable, isGlobal);

            FollowFlowOrFallthrough(component, context, node);
        }

        // =====================================================================
        // SetMapValue / RemoveMapKey / ClearMap — mutate the LIVE origin map
        // =====================================================================

        /// <summary>
        /// One handler, three ops (mirrors the HTML runtime's map mutator handlers).
        /// Mutates the ORIGIN variable's live map storage in place so every alias and
        /// downstream chained op observes the mutation, then fires the variable change
        /// notification for the origin variable (like the array write-back). NOTE: HTML
        /// mutators emit NO "VAR SET" trace line — only setMap does (see the
        /// map-trace-fixture snapshot) — so none is emitted here.
        /// </summary>
        public static void HandleMapModify(StoryFlowComponent component, StoryFlowNode node)
        {
            var context = component.GetContext();

            // Missing K/V types: HTML skips the mutation but still continues exec
            var keyType = node.GetData("keyType");
            var valueType = node.GetData("valueType");
            if (string.IsNullOrEmpty(keyType) || string.IsNullOrEmpty(valueType))
            {
                FollowFlowOrFallthrough(component, context, node);
                return;
            }

            // Resolve ALL non-map inputs FIRST (input-order parity with the HTML runtime):
            // key (input "3"), and for setMapValue the typed value (input "4")
            StoryFlowVariant key = null;
            if (node.Type != StoryFlowNodeType.ClearMap)
            {
                key = MapEvaluator.EvaluateMapOpKeyInput(context, node, "3");
            }

            StoryFlowVariant newValue = null;
            if (node.Type == StoryFlowNodeType.SetMapValue)
            {
                newValue = MapEvaluator.EvaluateMapOpValueInput(context, node, "4");
            }

            // THEN resolve the live map (input "2") and mutate immediately
            var variable = MapEvaluator.ResolveMapInputVariable(context, node, "2", out var sourceKind);
            if (variable == null)
            {
                // Unresolved map: HTML mutates a throwaway empty Map — no effect, no dispatch.
                LogVerbose($"[StoryFlow] Map mutator node {node.Id} could not resolve its map input - mutation skipped.");
                FollowFlowOrFallthrough(component, context, node);
                return;
            }

            if (sourceKind == MapSourceKind.CharacterVariable || sourceKind == MapSourceKind.RunScriptOutput)
            {
                // Read-only-terminal chain (charvar or runScript output): HTML hands the
                // mutator a THROWAWAY fresh Map — the stored variable is observably
                // unchanged and no variable-change dispatch fires. Observable no-op:
                // use setCharacterVar to write charvars.
                LogVerbose($"[StoryFlow] Map mutator node {node.Id} resolves to a read-only map source (character variable or runScript output) - mutation skipped.");
                FollowFlowOrFallthrough(component, context, node);
                return;
            }

            var map = variable.Value.GetMap();
            switch (node.Type)
            {
                case StoryFlowNodeType.SetMapValue:
                {
                    int entryIndex = MapEvaluator.FindMapEntryByKey(map, keyType, key);
                    if (entryIndex >= 0)
                    {
                        // Existing key: overwrite in place, keeping its position
                        map[entryIndex].Value = newValue;
                    }
                    else
                    {
                        // New key: append — JS Map insertion-order semantics
                        map.Add(new StoryFlowMapEntry { Key = key, Value = newValue });
                    }
                    break;
                }

                case StoryFlowNodeType.RemoveMapKey:
                {
                    int entryIndex = MapEvaluator.FindMapEntryByKey(map, keyType, key);
                    if (entryIndex >= 0)
                    {
                        // RemoveAt preserves the order of the remaining entries
                        map.RemoveAt(entryIndex);
                    }
                    break;
                }

                case StoryFlowNodeType.ClearMap:
                    // Clear() on the LIVE list so every alias observes (fixture pin) —
                    // never swap in a fresh list here
                    map.Clear();
                    break;
            }

            component.BroadcastVariableChanged(variable, sourceKind == MapSourceKind.GlobalVariable);
            FollowFlowOrFallthrough(component, context, node);
        }

        // =====================================================================
        // ForEachMap — iterate map entries (key + value) in insertion order
        // =====================================================================

        /// <summary>
        /// Mirrors the HTML runtime's processForEachMap. Entries are SNAPSHOT once at loop
        /// init — body mutations (even removeMapKey of the current key) land on the live
        /// map but neither skip, repeat, nor extend iteration. ContinueForEachLoop re-enters
        /// here via the dispatch table (it reuses LoopIndex/LoopMapEntries).
        /// </summary>
        public static void HandleForEachMap(StoryFlowComponent component, StoryFlowNode node)
        {
            var context = component.GetContext();

            // Missing K/V types: the map input handle cannot be built — HTML follows
            // "completed" immediately with zero iterations.
            var keyType = node.GetData("keyType");
            var valueType = node.GetData("valueType");
            if (string.IsNullOrEmpty(keyType) || string.IsNullOrEmpty(valueType))
            {
                component.ProcessNextNodeFromSource(node.Id, StoryFlowHandles.Out_LoopCompleted);
                return;
            }

            var runtimeState = context.GetNodeRuntimeState(node.Id);

            // Initialize loop on first entry: resolve the live map (input "map") and
            // snapshot its entries immediately. The snapshot deep-copies so setMapValue's
            // in-place entry overwrite on the live map can't retroactively change an
            // iteration that was already scheduled.
            if (runtimeState.LoopMapEntries == null)
            {
                var liveMap = MapEvaluator.EvaluateMapInput(context, node, "map");
                runtimeState.LoopMapEntries = MapEvaluator.CopyEntries(liveMap);
                runtimeState.LoopIndex = 0;
            }

            if (runtimeState.LoopIndex < runtimeState.LoopMapEntries.Count)
            {
                // Clear evaluation caches from previous iteration so chains re-evaluate.
                // LoopMapEntries/LoopKey/LoopValue survive this clear by design — see
                // NodeRuntimeState.ClearCache.
                context.ClearNodeRuntimeStates();

                // Expose the current entry's key/value (read by the typed evaluators
                // via the "-key"/"-value" source handle suffixes)
                var entry = runtimeState.LoopMapEntries[runtimeState.LoopIndex];
                runtimeState.LoopKey = entry.Key;
                runtimeState.LoopValue = entry.Value;

                // Push loop context for this iteration
                context.PushLoop(new LoopContext(node.Id, runtimeState.LoopIndex, "map"));

                component.Trace($"LOOP {node.Id} index={runtimeState.LoopIndex} key={entry.Key} value={entry.Value}");

                // Execute loop body
                component.ProcessNextNodeFromSource(node.Id, StoryFlowHandles.Out_LoopBody);
            }
            else
            {
                // Loop completed — clean up
                runtimeState.LoopMapEntries = null;
                runtimeState.LoopKey = null;
                runtimeState.LoopValue = null;
                runtimeState.LoopIndex = 0;
                runtimeState.CachedOutput = null;

                // Only pop if the top frame belongs to this loop
                var top = context.PeekLoop();
                if (top != null && top.NodeId == node.Id)
                    context.PopLoop();

                // Follow completed edge
                component.ProcessNextNodeFromSource(node.Id, StoryFlowHandles.Out_LoopCompleted);
            }
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        /// <summary>
        /// Follows the flow output edge, falling back to loop continuation / dialogue
        /// re-render (same continuation logic as the scalar Set* handlers).
        /// </summary>
        private static void FollowFlowOrFallthrough(StoryFlowComponent component, StoryFlowExecutionContext context, StoryFlowNode node)
        {
            var flowHandle = StoryFlowHandles.Source(node.Id, StoryFlowHandles.Out_Flow);
            var flowEdge = context.CurrentScript.FindEdgeBySourceHandle(flowHandle);
            if (flowEdge != null)
            {
                component.ProcessNextNode(flowHandle);
                return;
            }

            BooleanNodeHandler.SetNodeFallthrough(component, context, node);
        }

        /// <summary>
        /// Logs a diagnostic that is silent by default (HTML parity: these paths are
        /// silent no-ops) but visible with verbose logging enabled in StoryFlow settings.
        /// </summary>
        private static void LogVerbose(string message)
        {
            var settings = StoryFlowSettings.Instance;
            if (settings != null && settings.VerboseLogging)
                Debug.Log(message);
        }
    }
}
