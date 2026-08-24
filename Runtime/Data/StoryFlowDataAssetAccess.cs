using System.Collections.Generic;
using UnityEngine;

namespace StoryFlow.Data
{
    /// <summary>
    /// THE host-facing .sfd accessor logic — name binding, the strict type gate, the read, the
    /// write and the latched refusal log — in ONE place, because two public surfaces expose it.
    ///
    /// StoryFlowManager carries it for game code that has no dialogue object (a shop, an
    /// inventory panel, a save menu): the store is manager-global, so needing a
    /// StoryFlowComponent to reach it would mean finding one as a proxy. StoryFlowComponent
    /// carries it too, because a script sitting on the dialogue object naturally asks the
    /// component it already has. The two must not be able to answer differently, which is why
    /// neither of them owns any of this: they own only what genuinely differs between them —
    /// which store to read through, and what to invalidate after a write.
    ///
    /// Game code holds an ASSET REFERENCE and the variable NAME it typed in the editor, where
    /// everything the exporter emits is keyed by id (ids survive a rename). The name resolves
    /// exactly once per call, here, root-most-wins like every other chain lookup
    /// (StoryFlowDataAssetStore.FindDeclarationByName).
    ///
    /// TYPED, where the character surface next door is not. GetCharacterVariable hands back a
    /// raw StoryFlowVariant and SetCharacterVariable takes one, which is a fine shape for a
    /// surface with no gate to enforce: a character variable coerces. A .sfd variable must not,
    /// because §6.1 refuses a read whose DECLARED type moved out from under it — and to refuse,
    /// this surface has to know which type the caller believes it is reading. A variant-in,
    /// variant-out pair carries no such belief (a String-tagged value is a legal thing to hand
    /// an enum declaration), so the gate would have nothing to check and the refusal §6.1
    /// requires would be unreachable from game code. The typed pairs make the caller state it
    /// once, in the method name. <see cref="GetVariant"/> is the untyped door for callers who
    /// genuinely want whatever is there — it reads only, so no belief about the declared type is
    /// needed and none is checked.
    ///
    /// READ ANY, WRITE SCALAR. Arrays and maps come out through <see cref="GetVariant"/> as a
    /// detached copy and have no setter: a container write from game code would have to rebuild
    /// the whole value with the right element typing to keep the save key stable, which is the
    /// Set node's job and not something a caller can be expected to get right.
    ///
    /// WRITES DO NOT INVALIDATE ANYTHING HERE. Every setter reports whether the overlay changed
    /// and stops; the caller decides what that means for its own memos, because the two callers
    /// have different answers (see StoryFlowComponent's setter and the manager's).
    /// </summary>
    internal static class StoryFlowDataAssetAccess
    {
        /// <summary>
        /// A refusal already reported, keyed (assetId, variableName, kind) — see
        /// <see cref="ShouldLog"/> for why the log is latched at all and why the key has three
        /// parts. Owned by StoryFlowManager so both public surfaces share one, and cleared
        /// wherever the seed is rebuilt.
        /// </summary>
        internal sealed class RefusalLatch
        {
            private readonly HashSet<(string AssetId, string VariableName, string Kind)> _seen = new();

            internal bool Claim(string assetId, string variableName, string kind)
            {
                return _seen.Add((assetId ?? "", variableName ?? "", kind));
            }

            internal void Clear()
            {
                _seen.Clear();
            }
        }

        /// <summary>
        /// What the string pair accepts, named the way every other refusal names what it wanted:
        /// DECLARED TYPE NAMES, so one message shape covers the whole surface. "a string" would
        /// have been a lie on this pair anyway — an Image declaration is reachable through it,
        /// and an author told "not a string" about their image variable learns the wrong thing.
        /// </summary>
        private const string StringFamilyNames = "String, Image, Audio or Character";

        // =====================================================================
        // Reads
        // =====================================================================

        internal static bool GetBool(
            StoryFlowDataAssetStoreRef store, RefusalLatch latch,
            StoryFlowDataAssetAsset asset, string variableName, out bool found)
        {
            var value = ReadScalar(store, latch, asset, variableName,
                StoryFlowVariableType.Boolean, out found);
            return found && value.GetBool();
        }

        internal static int GetInt(
            StoryFlowDataAssetStoreRef store, RefusalLatch latch,
            StoryFlowDataAssetAsset asset, string variableName, out bool found)
        {
            var value = ReadScalar(store, latch, asset, variableName,
                StoryFlowVariableType.Integer, out found);
            return found ? value.GetInt() : 0;
        }

        internal static float GetFloat(
            StoryFlowDataAssetStoreRef store, RefusalLatch latch,
            StoryFlowDataAssetAsset asset, string variableName, out bool found)
        {
            var value = ReadScalar(store, latch, asset, variableName,
                StoryFlowVariableType.Float, out found);
            return found ? value.GetFloat() : 0f;
        }

        internal static string GetEnum(
            StoryFlowDataAssetStoreRef store, RefusalLatch latch,
            StoryFlowDataAssetAsset asset, string variableName, out bool found)
        {
            var value = ReadScalar(store, latch, asset, variableName,
                StoryFlowVariableType.Enum, out found);
            return found ? value.GetEnum() : "";
        }

        /// <summary>
        /// The string FAMILY read: string, image, audio and character all store their text in
        /// the same place and travel as bare paths / keys, so one pair covers all four.
        ///
        /// ENUM IS NOT REACHABLE HERE, deliberately, even though an enum also reads as text. An
        /// enum keeps its own storage and its own type tag, and a string write onto an enum
        /// declaration would put a String-tagged value in the overlay where the save key expects
        /// an enum — the one place the asymmetry is observable.
        /// </summary>
        internal static string GetString(
            StoryFlowDataAssetStoreRef store, RefusalLatch latch,
            StoryFlowDataAssetAsset asset, string variableName, out bool found)
        {
            var declaration = FindDeclaration(store, latch, asset, variableName, out var assetId);
            if (declaration == null || !IsStringFamilyScalar(declaration))
            {
                LogRefusal(latch, asset, variableName, declaration, StringFamilyNames);
                found = false;
                return "";
            }
            found = TryResolveValue(store, assetId, declaration, out var value);
            return found ? value.GetString() : "";
        }

        /// <summary>
        /// Reads ANY .sfd variable, including the arrays and maps the typed getters do not
        /// cover, as a DETACHED copy — mutating what comes back cannot reach the store. Returns
        /// null (with <paramref name="found"/> false) when nothing resolves.
        /// </summary>
        internal static StoryFlowVariant GetVariant(
            StoryFlowDataAssetStoreRef store, RefusalLatch latch,
            StoryFlowDataAssetAsset asset, string variableName, out bool found)
        {
            var declaration = FindDeclaration(store, latch, asset, variableName, out var assetId);
            if (declaration == null)
            {
                found = false;
                return null;
            }
            found = TryResolveValue(store, assetId, declaration, out var value);
            return found ? value : null;
        }

        // =====================================================================
        // Writes
        // =====================================================================

        internal static bool SetBool(
            StoryFlowDataAssetStoreRef store, RefusalLatch latch,
            StoryFlowDataAssetAsset asset, string variableName, bool value)
        {
            return WriteScalar(store, latch, asset, variableName,
                StoryFlowVariableType.Boolean, StoryFlowVariant.Bool(value));
        }

        internal static bool SetInt(
            StoryFlowDataAssetStoreRef store, RefusalLatch latch,
            StoryFlowDataAssetAsset asset, string variableName, int value)
        {
            return WriteScalar(store, latch, asset, variableName,
                StoryFlowVariableType.Integer, StoryFlowVariant.Int(value));
        }

        internal static bool SetFloat(
            StoryFlowDataAssetStoreRef store, RefusalLatch latch,
            StoryFlowDataAssetAsset asset, string variableName, float value)
        {
            return WriteScalar(store, latch, asset, variableName,
                StoryFlowVariableType.Float, StoryFlowVariant.Float(value));
        }

        internal static bool SetEnum(
            StoryFlowDataAssetStoreRef store, RefusalLatch latch,
            StoryFlowDataAssetAsset asset, string variableName, string value)
        {
            return WriteScalar(store, latch, asset, variableName,
                StoryFlowVariableType.Enum, StoryFlowVariant.Enum(value));
        }

        /// <summary>
        /// Writes a string-family value. It goes in String-TAGGED, which is what the Set node's
        /// own writer produces for the whole family, so the save key does not depend on which
        /// writer wrote it (a read answers GetString for either tag).
        /// </summary>
        internal static bool SetString(
            StoryFlowDataAssetStoreRef store, RefusalLatch latch,
            StoryFlowDataAssetAsset asset, string variableName, string value)
        {
            var declaration = FindDeclaration(store, latch, asset, variableName, out var assetId);
            if (declaration == null || !IsStringFamilyScalar(declaration))
            {
                LogRefusal(latch, asset, variableName, declaration, StringFamilyNames);
                return false;
            }
            return CommitWrite(store, latch, assetId, declaration.Id, StoryFlowVariant.String(value));
        }

        // =====================================================================
        // The shared middle
        // =====================================================================

        /// <summary>
        /// The declaration a (asset, name) pair binds to, plus the assetId writes land at — the
        /// REFERENCED asset's own level, never the declaring ancestor (contract §5). Null, with
        /// one latched log line, for a null asset, a store that does not exist yet, or a name
        /// nothing on the chain declares.
        /// </summary>
        private static StoryFlowVariable FindDeclaration(
            StoryFlowDataAssetStoreRef store, RefusalLatch latch,
            StoryFlowDataAssetAsset asset, string variableName, out string assetId)
        {
            assetId = asset != null ? asset.Id : "";
            if (asset == null || string.IsNullOrEmpty(assetId))
            {
                if (ShouldLog(latch, "", variableName, "noasset"))
                {
                    Debug.Log("[StoryFlow] Data Asset access refused: no asset supplied " +
                              $"(variable \"{variableName}\").");
                }
                return null;
            }

            if (store == null || !store.IsValid)
            {
                if (ShouldLog(latch, assetId, variableName, "nostore"))
                {
                    Debug.Log("[StoryFlow] Data Asset access refused: no Data Asset store yet " +
                              $"(\"{assetId}.{variableName}\").");
                }
                return null;
            }

            var declaration = StoryFlowDataAssetStore.FindDeclarationByName(store.Seed, assetId, variableName);
            if (declaration == null && ShouldLog(latch, assetId, variableName, "undeclared"))
            {
                Debug.Log($"[StoryFlow] Data Asset variable \"{variableName}\" is not declared on " +
                          $"\"{assetId}\" or any of its parents.");
            }
            return declaration;
        }

        /// <summary>True for the four types that share <c>StringValue</c>. Enum is NOT one.</summary>
        private static bool IsStringFamilyScalar(StoryFlowVariable declaration)
        {
            if (declaration.IsArray) return false;
            return declaration.Type == StoryFlowVariableType.String ||
                   declaration.Type == StoryFlowVariableType.Image ||
                   declaration.Type == StoryFlowVariableType.Audio ||
                   declaration.Type == StoryFlowVariableType.Character;
        }

        /// <summary>
        /// The STRICT type gate every typed accessor passes through: the declaration must be a
        /// SCALAR of exactly the asked-for type. No coercion, for the same reason §6.1 refuses it
        /// at the node arms — a value in this engine carries no evidence of what declared it, so
        /// a gate that guessed would write a differently shaped save key than the Set node writes
        /// for the same variable.
        /// </summary>
        private static StoryFlowVariant ReadScalar(
            StoryFlowDataAssetStoreRef store, RefusalLatch latch,
            StoryFlowDataAssetAsset asset, string variableName, StoryFlowVariableType type,
            out bool found)
        {
            var declaration = FindDeclaration(store, latch, asset, variableName, out var assetId);
            if (declaration == null || declaration.IsArray || declaration.Type != type)
            {
                LogRefusal(latch, asset, variableName, declaration, type.ToString());
                found = false;
                return null;
            }
            found = TryResolveValue(store, assetId, declaration, out var value);
            return value;
        }

        private static bool WriteScalar(
            StoryFlowDataAssetStoreRef store, RefusalLatch latch,
            StoryFlowDataAssetAsset asset, string variableName, StoryFlowVariableType type,
            StoryFlowVariant value)
        {
            var declaration = FindDeclaration(store, latch, asset, variableName, out var assetId);
            if (declaration == null || declaration.IsArray || declaration.Type != type)
            {
                LogRefusal(latch, asset, variableName, declaration, type.ToString());
                return false;
            }
            return CommitWrite(store, latch, assetId, declaration.Id, value);
        }

        /// <summary>The read behind every getter, on the store the declaration lookup settled.</summary>
        private static bool TryResolveValue(
            StoryFlowDataAssetStoreRef store, string assetId, StoryFlowVariable declaration,
            out StoryFlowVariant value)
        {
            return StoryFlowDataAssetStore.TryResolve(
                store.Seed, store.Overlay, assetId, declaration.Id, out value);
        }

        /// <summary>
        /// Records the write, reporting whether the overlay changed. INVALIDATES NOTHING — the
        /// caller owns that, because the right answer differs per surface.
        /// </summary>
        private static bool CommitWrite(
            StoryFlowDataAssetStoreRef store, RefusalLatch latch,
            string assetId, string variableId, StoryFlowVariant value)
        {
            if (StoryFlowDataAssetStore.TrySet(store.Seed, store.Overlay, assetId, variableId, value))
            {
                return true;
            }

            if (ShouldLog(latch, assetId, variableId, "refused"))
            {
                Debug.Log($"[StoryFlow] Data Asset write refused: \"{assetId}.{variableId}\".");
            }
            return false;
        }

        /// <summary>
        /// ONE refusal shape for every typed accessor: <c>is declared {actual}, not {wanted}</c>,
        /// with both sides named in DECLARED TYPE NAMES (the string pair passes
        /// <see cref="StringFamilyNames"/>, which is the same vocabulary, just four of them).
        /// Mixing type names on one accessor with prose on another makes two refusals of the same
        /// kind read as two different problems.
        /// </summary>
        private static void LogRefusal(
            RefusalLatch latch, StoryFlowDataAssetAsset asset, string variableName,
            StoryFlowVariable declaration, string wanted)
        {
            // A null declaration was already reported by the lookup — this only names the
            // mismatches, which the lookup cannot see.
            if (declaration == null) return;

            string assetId = asset != null ? asset.Id : "";
            if (!ShouldLog(latch, assetId, variableName, "mismatch")) return;

            string actual = declaration.IsArray
                ? declaration.Type + " array"
                : declaration.Type.ToString();
            Debug.Log($"[StoryFlow] Data Asset variable \"{assetId}.{variableName}\" " +
                      $"is declared {actual}, not {wanted}.");
        }

        /// <summary>
        /// LATCHED, unlike the character surface next door, because this one is shaped for
        /// per-frame calls. A shop panel polling a price in Update() with a variable name that
        /// was renamed in the editor logs a line per frame — in the editor that is a managed
        /// stack trace each time, and in a build it is Player.log growing until the disk cares.
        /// The refusal itself is never suppressed: `found` / a false return say so on EVERY call,
        /// every time. Only the console line is once.
        ///
        /// The key has three parts because they answer different questions and a caller can hit
        /// several at once: a typo'd name and a genuine type mismatch on the SAME variable are
        /// two different fixes, and the same mistake against two assets is two bugs.
        ///
        /// A NULL latch logs every time. That is a caller with no manager to own one — reachable
        /// only from a component holding a hand-built store, which is a test shape, never a
        /// running game (a game's store comes from the manager in the first place). Failing open
        /// keeps a diagnostic surface from going quiet just because the singleton is missing.
        /// </summary>
        private static bool ShouldLog(
            RefusalLatch latch, string assetId, string variableName, string kind)
        {
            return latch == null || latch.Claim(assetId, variableName, kind);
        }
    }
}
