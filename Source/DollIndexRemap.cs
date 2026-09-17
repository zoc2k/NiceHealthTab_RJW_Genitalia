using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace NHTRJWGenitalia
{
    /// <summary>
    /// Answers NHT's body part remap for the parts it does not know about - ours.
    ///
    /// NHT does not read <c>DollBodyPartDef.bodyPartId</c> as an index into the pawn's body. It
    /// runs it through a **remap** first, in <c>DollPartRenderer.DrawPart</c>:
    ///
    ///     body part record = pawn body.GetPartAtIndex(remap.Get(part.bodyPartId))
    ///
    /// For a body with no remap of its own the default is the identity (<c>Get(i) = i</c>), which
    /// is why writing the real index into our Defs works on a plain RJW game.
    ///
    /// Two things break that.
    ///
    ///   * **Another body.** NHT's ModCompat races (Ratkin, Kurin, ABF Synstruct) carry a remap
    ///     from NHT's own part list to theirs. Our indices are not in it, so <c>Get</c> answers
    ///     -1 and every RJW part is dropped.
    ///   * **A mod that adds body parts to the human body** (More Injuries, for one). It inserts
    ///     parts in the middle of the body, shifting the indices after them, so NHT's own doll
    ///     parts land on the wrong records and it colours the wrong part. NHT's cure is "auto
    ///     assign" in its Body parts settings, which builds a remap for that body by matching part
    ///     names - but it only knows **its own** parts
    ///     (<c>NiceHediffTabSettings.BodyPartsOriginal</c>: the 64 vanilla human parts, keys
    ///     0..63). The RJW parts, which RJW appends to the body after those, get no key at all, so
    ///     <c>Get</c> answers -1 and the whole RJW set vanishes from the doll. That is the
    ///     compatibility report this class answers: "auto assign fixes NHT and loses RJW".
    ///
    /// The cure is a Harmony postfix on <c>BodyPartIndexesRemap.Get</c>. For an index of ours it
    /// answers with the index of that same body part **in the body of the pawn being drawn**,
    /// looked up by part name. For the human body that is the index the Def already carries; for
    /// another race it is wherever that race keeps the part; for a body without the part the
    /// answer is -1 and the part stays hidden, as it should.
    ///
    ///   * Only indices above NHT's own highest key are ours, so nothing of NHT's is touched.
    ///   * The identity remap is patched as well - it is the one a race with no remap of its own
    ///     gets, and there our human index would otherwise land on a different part of that
    ///     race's body, or past the end of it. That is why nothing but the nipples (which this
    ///     mod draws itself) showed on Ratkin and Kurin pawns.
    ///   * **Only a key nobody else has claimed.** An answer that is neither -1 nor the key itself
    ///     means some remap really does map that key, and it is not ours to take over. Nice Health
    ///     Tab - Anatomy Editor hands out keys from 64 upwards for the parts a race adds (a tail,
    ///     wings, a reproduction part), which is the very range our parts live in, and its dolls
    ///     were drawing our body parts on those slots.
    ///   * Until a pawn has been noted we change nothing.
    ///
    /// **Why nothing is written into the remap.** Earlier this was done by adding
    /// "our index -> that body's index" entries to every remap at startup (Bootstrap.PatchRemaps).
    /// Two faults: the remap "auto assign" builds is created while the game runs - long after
    /// startup, and <c>AutoRemap</c> clears the remap first - so the entries were never there when
    /// they were needed; and a key of ours has no row in NHT's table, which its Body parts screen
    /// looks up for every key it finds (<c>GetInverted</c> -> <c>GetDefaultRowByIndex</c> ->
    /// <c>BodyPartsOriginal[row]</c>), so a row of -1 would throw inside NHT's own settings
    /// window. Keeping the mapping on our side avoids both. <see cref="DropOldEntries"/> clears
    /// out what older versions of this mod wrote.
    ///
    /// Everything here is reflection plus one Harmony postfix; no NHT assembly is referenced. If
    /// the type or the method cannot be found, nothing is patched and the parts fall back to the
    /// raw body index, which is right whenever no remap is in play.
    /// </summary>
    internal static class DollIndexRemap
    {
        private const string RemapTypeName = "NiceHealthTab.BodyPartIndexesRemap";
        private const string IdentityTypeName = "NiceHealthTab.BodyPartIndexesRemapDefaultHuman";
        /// <summary>
        /// Where the keys we add to a **transient** remap start. High enough to miss every key NHT
        /// or another framework hands out, and the same between sessions. See <see cref="KeyFor"/>.
        /// </summary>
        private const int LentKeyBase = 9000;
        private const string SettingsTypeName = "NiceHealthTab.NiceHediffTabSettings";

        /// <summary>Highest key NHT itself uses when its table cannot be read (0..63 vanilla).</summary>
        private const int DefaultMaxKey = 63;

        /// <summary>Our doll parts' body part name, keyed by the index our Defs carry.</summary>
        private static readonly Dictionary<int, string> ourParts = new Dictionary<int, string>();

        /// <summary>That, resolved per body: our index -> the index in that body.</summary>
        private static readonly Dictionary<BodyDef, Dictionary<int, int>> byBody =
            new Dictionary<BodyDef, Dictionary<int, int>>();

        private static int maxNhtKey = DefaultMaxKey;
        private static Dictionary<int, int> current;
        private static object currentRemap;
        private static bool currentTransient;
        private static bool patched;
        private static MethodInfo solveMethod;
        private static MethodInfo getMethod;
        private static MethodInfo invertMethod;
        private static MethodInfo setMethod;
        private static bool resolvedMethods;

        /// <summary>
        /// The parts we put on the doll: body part defName by the index our Defs carry (the index
        /// in the human body, looked up at startup, so it already accounts for whatever other mods
        /// added there).
        /// </summary>
        internal static void Init(Dictionary<string, int> canonical)
        {
            ourParts.Clear();
            byBody.Clear();
            current = null;
            ReadNhtKeys();
            if (canonical == null)
            {
                return;
            }
            foreach (KeyValuePair<string, int> pair in canonical)
            {
                // A part absent from the human body has no index of ours to answer for, and an
                // index NHT uses itself is none of our business.
                if (pair.Value > maxNhtKey && !pair.Key.NullOrEmpty())
                {
                    ourParts[pair.Value] = pair.Key;
                }
            }
        }

        /// <summary>
        /// The pawn whose doll is about to be drawn: our answers are about that pawn's body.
        /// </summary>
        internal static void Note(Pawn pawn)
        {
            RaceProperties race = (pawn == null) ? null : pawn.RaceProps;
            BodyDef body = (race == null) ? null : race.body;
            current = (body == null) ? null : MapFor(body);
            currentRemap = null;
            currentTransient = false;
            if (pawn == null || ourParts.Count == 0)
            {
                return;
            }
            ResolveMethods();
            if (solveMethod == null)
            {
                return;
            }
            try
            {
                currentRemap = solveMethod.Invoke(null, new object[] { pawn });
                currentTransient = currentRemap != null && !KnownToNht(currentRemap);
            }
            catch (Exception ex)
            {
                currentRemap = null;
                Log.Warning(Bootstrap.Prefix + "reading the body part remap failed: " + ex);
            }
        }

        /// <summary>
        /// The doll index our part should carry for the pawn being drawn.
        ///
        /// Normally that is the real index, which GetPostfix answers for. But another mod remap may
        /// already use that number as a key of its own - Nice Health Tab - Anatomy Editor hands out
        /// keys from 64 up for the parts a race adds, and ours live in the same range. Then we look
        /// for a key that already leads to our part, and failing that add one: only to a
        /// **transient** remap, one that is not in NHT own tables, so nothing is written into what
        /// NHT saves and shows in its Body parts screen.
        /// </summary>
        internal static int KeyFor(int realIndex)
        {
            int target;
            if (currentRemap == null || current == null || getMethod == null
                || !current.TryGetValue(realIndex, out target))
            {
                return realIndex;
            }
            try
            {
                int at = (int)getMethod.Invoke(currentRemap, new object[] { realIndex });
                if (at == target || at < 0 || at == realIndex)
                {
                    return realIndex;      // Right already, or a key nobody claimed
                }
                if (invertMethod != null)
                {
                    int key = (int)invertMethod.Invoke(currentRemap, new object[] { target });
                    if (key >= 0)
                    {
                        return key;        // Some key already leads to our part
                    }
                }
                if (currentTransient && setMethod != null)
                {
                    int lent = LentKeyBase + realIndex;
                    setMethod.Invoke(currentRemap, new object[] { lent, target, false });
                    if ((int)getMethod.Invoke(currentRemap, new object[] { lent }) == target)
                    {
                        return lent;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(Bootstrap.Prefix + "picking a doll index failed: " + ex);
            }
            return realIndex;
        }

        /// <summary>Whether NHT itself keeps this remap (then we never write into it).</summary>
        private static bool KnownToNht(object remap)
        {
            Type remapType = GenTypes.GetTypeInAnyAssembly(RemapTypeName);
            if (remapType == null)
            {
                return true;        // Unknown shape: treat it as NHT and leave it alone
            }
            string[] dictFields = { "BodyPartsRemaper", "BodyPartsOverride" };
            for (int i = 0; i < dictFields.Length; i++)
            {
                FieldInfo field = remapType.GetField(dictFields[i],
                                                     BindingFlags.Static | BindingFlags.Public);
                IDictionary dict = (field == null) ? null : field.GetValue(null) as IDictionary;
                if (dict == null)
                {
                    continue;
                }
                foreach (DictionaryEntry e in dict)
                {
                    if (ReferenceEquals(e.Value, remap))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static void ResolveMethods()
        {
            if (resolvedMethods)
            {
                return;
            }
            resolvedMethods = true;
            Type remapType = GenTypes.GetTypeInAnyAssembly(RemapTypeName);
            if (remapType == null)
            {
                return;
            }
            solveMethod = remapType.GetMethod("Solve", BindingFlags.Static | BindingFlags.Public);
            getMethod = remapType.GetMethod("Get", BindingFlags.Instance | BindingFlags.Public,
                                            null, new[] { typeof(int) }, null);
            invertMethod = remapType.GetMethod("GetInverted",
                                               BindingFlags.Instance | BindingFlags.Public,
                                               null, new[] { typeof(int) }, null);
            setMethod = remapType.GetMethod("Set", BindingFlags.Instance | BindingFlags.Public,
                                            null, new[] { typeof(int), typeof(int), typeof(bool) },
                                            null);
        }

        /// <summary>
        /// Where one of our parts sits in the body of this pawn, or -1. The index our Defs carry
        /// is the human one, so anything that looks a part up in the pawn's own body has to go
        /// through here.
        /// </summary>
        internal static int IndexIn(Pawn pawn, int ourIndex)
        {
            RaceProperties race = (pawn == null) ? null : pawn.RaceProps;
            BodyDef body = (race == null) ? null : race.body;
            if (body == null || ourIndex < 0)
            {
                return -1;
            }
            if (!ourParts.ContainsKey(ourIndex))
            {
                return ourIndex;        // Not one of ours - nothing to translate
            }
            int real;
            return MapFor(body).TryGetValue(ourIndex, out real) ? real : -1;
        }

        /// <summary>Our index -> that body's index, worked out once per body.</summary>
        private static Dictionary<int, int> MapFor(BodyDef body)
        {
            Dictionary<int, int> map;
            if (byBody.TryGetValue(body, out map))
            {
                return map;
            }
            map = new Dictionary<int, int>();
            List<BodyPartRecord> all = body.AllParts;
            if (all != null)
            {
                foreach (KeyValuePair<int, string> pair in ourParts)
                {
                    for (int i = 0; i < all.Count; i++)
                    {
                        BodyPartRecord rec = all[i];
                        if (rec != null && rec.def != null && rec.def.defName == pair.Value)
                        {
                            map[pair.Key] = i;
                            break;
                        }
                    }
                }
            }
            byBody[body] = map;
            return map;
        }

        /// <summary>
        /// Patches the remap. True when the patch is in place; without it our parts still work on
        /// a body that has no remap of its own.
        /// </summary>
        internal static bool Install()
        {
            if (patched || ourParts.Count == 0)
            {
                return patched;
            }
            // Both the remap itself and the identity one a body without a remap gets. The
            // second is an override, so patching the first does not cover it.
            List<MethodInfo> targets = new List<MethodInfo>();
            MethodInfo get = GetMethod(RemapTypeName);
            if (get != null)
            {
                targets.Add(get);
            }
            MethodInfo identity = GetMethod(IdentityTypeName);
            if (identity != null && identity != get)
            {
                targets.Add(identity);
            }
            if (targets.Count == 0)
            {
                Log.Message(Bootstrap.Prefix + "body part remap not found - "
                            + "doll parts use raw body indices.");
                return false;
            }
            try
            {
                Harmony harmony = new Harmony(Bootstrap.HarmonyId);
                HarmonyMethod postfix = new HarmonyMethod(typeof(DollIndexRemap).GetMethod(
                    "GetPostfix", BindingFlags.NonPublic | BindingFlags.Static));
                for (int i = 0; i < targets.Count; i++)
                {
                    harmony.Patch(targets[i], null, postfix);
                }
                patched = true;
            }
            catch (Exception ex)
            {
                Log.Warning(Bootstrap.Prefix + "could not patch the body part remap: " + ex);
            }
            return patched;
        }

        /// <summary>
        /// Removes the entries older versions of this mod added to NHT's remaps (keys of ours,
        /// which its Body parts screen cannot look up). Returns how many were dropped. Quiet and
        /// harmless when there are none, or when the field is shaped differently.
        /// </summary>
        internal static int DropOldEntries()
        {
            Type remapType = GenTypes.GetTypeInAnyAssembly(RemapTypeName);
            if (remapType == null || ourParts.Count == 0)
            {
                return 0;
            }
            FieldInfo partsRemap = remapType.GetField(
                "PartsRemap", BindingFlags.Instance | BindingFlags.NonPublic);
            if (partsRemap == null)
            {
                return 0;
            }
            int dropped = 0;
            string[] dictFields = { "BodyPartsRemaper", "BodyPartsOverride" };
            foreach (string fieldName in dictFields)
            {
                FieldInfo field = remapType.GetField(fieldName,
                                                     BindingFlags.Static | BindingFlags.Public);
                IDictionary dict = (field == null) ? null : field.GetValue(null) as IDictionary;
                if (dict == null)
                {
                    continue;
                }
                foreach (DictionaryEntry entry in dict)
                {
                    if (entry.Value == null)
                    {
                        continue;
                    }
                    IDictionary map;
                    try
                    {
                        map = partsRemap.GetValue(entry.Value) as IDictionary;
                    }
                    catch (Exception)
                    {
                        continue;       // The identity default has no dictionary of its own
                    }
                    if (map == null)
                    {
                        continue;
                    }
                    List<object> stale = new List<object>();
                    foreach (DictionaryEntry pair in map)
                    {
                        if (pair.Key is int && ourParts.ContainsKey((int)pair.Key))
                        {
                            stale.Add(pair.Key);
                        }
                    }
                    for (int i = 0; i < stale.Count; i++)
                    {
                        map.Remove(stale[i]);
                        dropped++;
                    }
                }
            }
            return dropped;
        }

        private static MethodInfo GetMethod(string typeName)
        {
            Type type = GenTypes.GetTypeInAnyAssembly(typeName);
            return (type == null)
                ? null
                : type.GetMethod("Get",
                                 BindingFlags.Instance | BindingFlags.Public
                                 | BindingFlags.DeclaredOnly,
                                 null, new[] { typeof(int) }, null);
        }

        /// <summary>
        /// NHT's own highest remap key, read from its settings table. Anything above it belongs to
        /// parts another mod added to the body.
        /// </summary>
        private static void ReadNhtKeys()
        {
            Type settings = GenTypes.GetTypeInAnyAssembly(SettingsTypeName);
            FieldInfo field = (settings == null)
                ? null
                : settings.GetField("BodyPartsOriginal", BindingFlags.Static | BindingFlags.Public);
            IEnumerable rows = (field == null) ? null : field.GetValue(null) as IEnumerable;
            if (rows == null)
            {
                return;
            }
            int max = -1;
            try
            {
                foreach (object row in rows)
                {
                    // ValueTuple<string, string, int> - the key is the third item.
                    FieldInfo item3 = row.GetType().GetField("Item3");
                    if (item3 == null)
                    {
                        return;     // Not the shape we expect; keep the built-in bound.
                    }
                    int key = (int)item3.GetValue(row);
                    if (key > max)
                    {
                        max = key;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(Bootstrap.Prefix + "could not read NHT's body part table: " + ex);
                return;
            }
            if (max >= 0)
            {
                maxNhtKey = max;
            }
        }

        /// <summary>
        /// Harmony postfix for <c>BodyPartIndexesRemap.Get</c>: an index of ours is answered from
        /// our own table, for the body of the pawn being drawn. A part that body does not have
        /// answers -1, which is how NHT hides a part.
        /// </summary>
        private static void GetPostfix(int index, ref int __result)
        {
            if (current == null || !ourParts.ContainsKey(index))
            {
                return;         // Not one of ours, or no pawn to answer for yet
            }
            if (__result >= 0 && __result != index)
            {
                return;         // Somebody's remap really maps this key - leave it alone
            }
            int real;
            __result = current.TryGetValue(index, out real) ? real : -1;
        }
    }
}
