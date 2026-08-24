using System.Collections.Generic;
using StoryFlow.Data;
using UnityEngine;

namespace StoryFlow.Execution.NodeHandlers
{
    /// <summary>
    /// Handles SetDataAssetVariable nodes — the only .sfd node type that EXECUTES. The
    /// reference pill (getDataAsset) and the read accessor (getDataAssetVariable) are
    /// evaluated lazily, so the dispatcher maps both to NoOp.
    ///
    /// Also owns the array-op routing both array mutators share (see
    /// <see cref="TryRouteArrayOpToDataAsset"/>): an add/remove/set-element/clear wired to a
    /// .sfd accessor writes INTO the session overlay rather than into a script variable.
    /// </summary>
    public static class DataAssetNodeHandler
    {
        public static void HandleSetDataAssetVariable(StoryFlowComponent component, StoryFlowNode node)
        {
            var context = component.GetContext();

            // THE LADDER FIRST (contract §6): an unwired / dead / stale binding is a NO-OP with
            // a once-per-node warning already latched by the ladder, and exec still continues.
            if (!context.TryResolveDataAssetBinding(node, out var assetId))
            {
                FollowFlowOrFallthrough(component, context, node);
                return;
            }

            // NO INLINE-VALUE FALLBACK, ever (contract §5). The editor persists no literal on
            // this node — its face is the binding, not an editor — so an unwired value pin has
            // nothing to offer and must REFUSE rather than write a type zero over the declared
            // default. This is the trap the character Set arm walks into: its
            // Evaluate*WithDefault helpers substitute 0 / "" / false for an unwired pin, which
            // is why TryReadSetInput checks the edge itself on EVERY branch instead of trusting
            // an evaluator's fallback.
            if (!TryReadSetInput(context, node, out var newValue))
            {
                // NOT latched, unlike the ladder's reasons: this names a wiring mistake on an
                // EXEC node the author just ran, and an exec node fires far less often than a
                // condition re-evaluates (contract §6, the value-refusal row).
                Debug.LogWarning(
                    "[StoryFlow] Set Data Asset Variable refused an undefined value " +
                    $"(nothing wired to its value pin): node {node.Id}");
                FollowFlowOrFallthrough(component, context, node);
                return;
            }

            var variableId = node.GetData("variableId");

            // Trace BEFORE the write, matching the reference arm's order (trace lines are
            // parity artifacts — never reorder trace-vs-write). Containers trace by size, the
            // way the map pins on the character Set arm do.
            if (newValue.Type == StoryFlowVariableType.Map)
                component.Trace($"DA SET \"{assetId}.{variableId}\" size={newValue.GetMap().Count}");
            else if (node.GetDataBool("isArray"))
                component.Trace($"DA SET \"{assetId}.{variableId}\" size={newValue.GetArray().Count}");
            else
                component.Trace($"DA SET \"{assetId}.{variableId}\" value={newValue}");

            // The store deep-copies on the way in and REPLACES a map's whole value; the write
            // lands at the WIRED asset's own level, always (contract §5). The ladder already
            // proved the asset and the declaration, so a false here would be a store bug.
            context.TrySetDataAsset(assetId, variableId, newValue);

            InvalidateCachedConditions(context);

            FollowFlowOrFallthrough(component, context, node);
        }

        /// <summary>
        /// The value a Set node is writing, or false when its value pin is unwired.
        ///
        /// The three pin shapes are the editor's, read off the node's own snapshot: a map pin
        /// bakes K/V into its handle id, an array pin is "{type}-array-2", a scalar pin is
        /// "{type}-2". The ladder has already proved declMatches, so the snapshot IS the
        /// chain's declared shape and the value written carries the declared type.
        /// </summary>
        private static bool TryReadSetInput(
            StoryFlowExecutionContext context, StoryFlowNode node, out StoryFlowVariant value)
        {
            value = null;
            var variableType = node.GetData("variableType");

            if (variableType == "map")
            {
                // A snapshot missing either half cannot even NAME its pin — treat that as
                // unwired rather than guessing a shape.
                string keyType = node.GetData("keyType");
                string valueType = node.GetData("valueType");
                if (string.IsNullOrEmpty(keyType) || string.IsNullOrEmpty(valueType)) return false;

                var mapSuffix = StoryFlowHandles.InMap(keyType, valueType, StoryFlowHandles.DataAssetValueOptionId);
                if (context.CurrentScript.FindInputEdge(node.Id, mapSuffix) == null) return false;

                // CopyEntries, not the resolved list: a map input resolves to LIVE variable
                // storage, and the overlay must never share it (the store copies again on the
                // way in — this one keeps the read side honest as well).
                value = new StoryFlowVariant();
                value.SetMap(MapEvaluator.CopyEntries(
                    MapEvaluator.EvaluateMapInput(context, node, StoryFlowHandles.DataAssetValueOptionId)));
                return true;
            }

            if (node.GetDataBool("isArray"))
            {
                var arraySuffix = StoryFlowHandles.InDataAssetArrayValue(variableType);
                if (context.CurrentScript.FindInputEdge(node.Id, arraySuffix) == null) return false;

                var evaluated = StoryFlowEvaluator.EvaluateTypedArray(
                    context, node.Id, arraySuffix, variableType);

                // The element type is STATED, not inferred: an empty wired array has no
                // element to read one off, and this node is the LAST writer before the store,
                // so an untyped value here is what the save key would serialize.
                value = new StoryFlowVariant
                {
                    Type = ParseElementType(variableType),
                    ArrayValue = evaluated?.ArrayValue != null
                        ? new List<StoryFlowVariant>(evaluated.ArrayValue)
                        : new List<StoryFlowVariant>(),
                };
                RetagEnumElements(value, variableType);
                return true;
            }

            var scalarSuffix = StoryFlowHandles.InDataAssetValue(variableType);
            if (context.CurrentScript.FindInputEdge(node.Id, scalarSuffix) == null) return false;

            // EvaluateTyped is the shared typed reader: enum comes back Enum-tagged (its
            // storage is EnumValue), image / audio / character come back String-tagged, which
            // is how the whole string family already travels through this plugin and matches
            // what the sibling Unreal port writes.
            value = StoryFlowEvaluator.EvaluateTyped(context, node.Id, scalarSuffix, variableType);
            return value != null;
        }

        /// <summary>
        /// An enum array's elements are ENUM-tagged, not String-tagged. They travel on a
        /// string pin, so the typed array reader hands them back as the source stored them —
        /// but the importer types an enum array's elements Enum and a save restores them Enum
        /// from the declaration, so a write that left them String would be the only one of the
        /// three writers disagreeing. Invisible today (every reader answers GetString for the
        /// string family) and exactly the kind of thing that stops being invisible the moment
        /// something switches on the element type.
        /// </summary>
        private static void RetagEnumElements(StoryFlowVariant array, string variableType)
        {
            if (variableType != "enum" || array.ArrayValue == null) return;
            foreach (var element in array.ArrayValue)
            {
                if (element.Type != StoryFlowVariableType.Enum)
                    element.SetEnum(element.GetString());
            }
        }

        private static StoryFlowVariableType ParseElementType(string variableType)
        {
            return StoryFlowWireTypes.TryParseWireType(variableType, out var parsed)
                ? parsed
                : StoryFlowVariableType.String;
        }

        /// <summary>
        /// Routes an array op whose array input comes from a .sfd accessor into the session
        /// overlay, returning true when it did (written OR refused — either way the caller
        /// must not also write a script variable).
        ///
        /// Checked BEFORE any name-based lookup, including the op node's own "variable" field:
        /// an accessor carries no isGlobal and its "variable" data is the .sfd variable's
        /// display NAME, so falling through would ask FindVariable a question about the wrong
        /// namespace — and could clobber a same-named script array.
        ///
        /// A bound-but-not-array accessor keeps a warn-once refusal: its pins could not have
        /// fed the op an array, and writing one over a scalar the declaration promises is
        /// exactly what the store's callers must never do.
        /// </summary>
        /// <param name="opNode">
        /// The op node whose output stamp must survive the cache sweep, or null for a caller
        /// that stamps in its own tail AFTER this returns (HandleClearArray) and so would only
        /// be overwriting the stamp a moment later.
        /// </param>
        internal static bool TryRouteArrayOpToDataAsset(
            StoryFlowComponent component, StoryFlowExecutionContext context,
            StoryFlowNode opNode, StoryFlowNode sourceNode, List<StoryFlowVariant> newArray)
        {
            if (sourceNode == null || !EvaluatorHelpers.IsDataAssetAccessor(sourceNode.Type))
                return false;

            // Both refusals below answer TRUE — handled, as in "the caller must not also write
            // a script variable" — while deliberately stamping NOTHING and invalidating
            // nothing: the op did nothing, so there is no result to publish and no cached
            // condition whose answer changed.
            if (!sourceNode.GetDataBool("isArray"))
            {
                context.MaybeWarnDataAsset(sourceNode.Id, "arrayop",
                    $"Data Asset array op refused: node {sourceNode.Id} is not bound to an array variable");
                return true;
            }

            if (!context.TryResolveDataAssetBinding(sourceNode, out var assetId))
                return true;

            var variableId = sourceNode.GetData("variableId");
            var variableType = sourceNode.GetData("variableType");
            var value = new StoryFlowVariant
            {
                Type = ParseElementType(variableType),
                ArrayValue = newArray ?? new List<StoryFlowVariant>(),
            };
            RetagEnumElements(value, variableType);

            component.Trace($"DA SET \"{assetId}.{variableId}\" size={value.GetArray().Count}");
            context.TrySetDataAsset(assetId, variableId, value);

            // ORDERING: the sweep below is context-wide, and the array ops stamp their own
            // output array on the op node's runtime state (downstream consumers read the
            // result from there) BEFORE handing it to a writer. Clearing without re-stamping
            // would leave the op observably outputless — so re-stamp it after the sweep.
            InvalidateCachedConditions(context);
            if (opNode != null)
            {
                // Type comes from `value`, not from the parameterless ctor (which would make
                // this Boolean): the stamp is read back as an array by downstream consumers,
                // and it should state the same element type the write just did.
                context.GetNodeRuntimeState(opNode.Id).CachedOutput =
                    new StoryFlowVariant { Type = value.Type, ArrayValue = value.ArrayValue };
            }
            return true;
        }

        /// <summary>
        /// Drops memoized evaluation results after a .sfd write, so option conditions
        /// re-evaluate against the new value (contract §5).
        ///
        /// The accessors themselves are cache-exempt (EvaluatorHelpers.IsDataAssetAccessor),
        /// but a notBool / comparison ABOVE one is not, and only the boolean-chain pre-pass
        /// clears those — a value input read through Evaluate*WithDefault (every Set node's
        /// value pin) gets no pre-pass at all. That is the stale-parent hole this closes, and
        /// it is the same sweep the array and map mutators already run after a write.
        /// </summary>
        private static void InvalidateCachedConditions(StoryFlowExecutionContext context)
        {
            context.ClearNodeRuntimeStates();
        }

        private static void FollowFlowOrFallthrough(
            StoryFlowComponent component, StoryFlowExecutionContext context, StoryFlowNode node)
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
    }
}
