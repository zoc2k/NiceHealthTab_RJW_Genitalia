using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace NHTRJWGenitalia
{
    /// <summary>
    /// What a gonad hediff is, read from the gonad mod's own data.
    ///
    /// 'RJW Gonadz Reloaded' (the continuation of 'RJW Now with balls!') marks every gonad with
    /// a <c>Gonadz.GonadExtension</c> mod extension that says which kind it is and whether it
    /// sits inside the body. Its own words on the matter: "Gonads are found by what their def
    /// says, never by their name" (API.md). Reading that mark lets us draw gonads another mod
    /// adds under a name of its own, and keeps internal testicles off the skin.
    ///
    /// Only the **data** on the def is read - a field of a mod extension, the same way the RJW
    /// genital family is read in <see cref="DollPartFormDef.GenitalFamilyOf"/>. None of that
    /// mod's code is called, so nothing can run while we draw.
    ///
    /// Without the extension (the older mod, or a plain RJW hediff) the kind is "" and the form
    /// Defs fall back to matching the hediff name.
    /// </summary>
    internal static class GonadzParts
    {
        private const string ExtensionTypeName = "Gonadz.GonadExtension";
        private const string KindFieldName = "kind";
        private const string InternalFieldName = "isInternal";

        /// <summary>Marks an internal gonad in the older mod, which has no extension to read.
        /// Its three internal defs are InternalTesticles, GenericInternalTesticles and
        /// InternalOvaries.</summary>
        private const string InternalNameMark = "Internal";

        private static bool resolved;
        private static Type extensionType;
        private static FieldInfo kindField;
        private static FieldInfo internalField;

        private static readonly Dictionary<HediffDef, string> kindCache =
            new Dictionary<HediffDef, string>();
        private static readonly Dictionary<HediffDef, bool> internalCache =
            new Dictionary<HediffDef, bool>();

        /// <summary>True once a gonad mod that marks its parts is here.</summary>
        internal static bool Available
        {
            get
            {
                Resolve();
                return kindField != null;
            }
        }

        private static void Resolve()
        {
            if (resolved)
            {
                return;
            }
            resolved = true;
            extensionType = GenTypes.GetTypeInAnyAssembly(ExtensionTypeName);
            if (extensionType == null)
            {
                return;         // The older mod, or no gonad mod at all.
            }
            kindField = extensionType.GetField(KindFieldName,
                                               BindingFlags.Instance | BindingFlags.Public);
            internalField = extensionType.GetField(InternalFieldName,
                                                   BindingFlags.Instance | BindingFlags.Public);
        }

        /// <summary>
        /// The gonad kind of this hediff - "Testicles", "Ovaries", "OvipositorMale" or
        /// "OvipositorFemale" - or "" when the def carries no gonad mark.
        ///
        /// The field is an enum of theirs, so it is turned into its name rather than compared as
        /// a number: the numbers could be reordered, the names are what their API documents.
        /// </summary>
        internal static string KindOf(HediffDef def)
        {
            if (def == null)
            {
                return "";
            }
            string found;
            if (kindCache.TryGetValue(def, out found))
            {
                return found;
            }
            found = ReadKind(def);
            kindCache[def] = found;
            return found;
        }

        private static string ReadKind(HediffDef def)
        {
            Resolve();
            if (kindField == null || def.modExtensions == null)
            {
                return "";
            }
            try
            {
                for (int i = 0; i < def.modExtensions.Count; i++)
                {
                    DefModExtension ext = def.modExtensions[i];
                    if (ext == null || !extensionType.IsInstanceOfType(ext))
                    {
                        continue;
                    }
                    object kind = kindField.GetValue(ext);
                    return (kind == null) ? "" : kind.ToString();
                }
            }
            catch (Exception ex)
            {
                kindField = null;       // Shape changed - stop asking, fall back to names.
                Log.Warning(Bootstrap.Prefix + "reading the gonad kind failed: " + ex);
            }
            return "";
        }

        /// <summary>
        /// Whether this gonad sits inside the body. Internal testicles are drawn in the organ
        /// layer but never on the skin.
        ///
        /// Read from the mark when there is one; otherwise from the name, which carries it in
        /// both mods (InternalTesticles, GenericInternalTesticles, InternalOvaries).
        /// </summary>
        internal static bool IsInternal(HediffDef def)
        {
            if (def == null)
            {
                return false;
            }
            bool found;
            if (internalCache.TryGetValue(def, out found))
            {
                return found;
            }
            found = ReadInternal(def);
            internalCache[def] = found;
            return found;
        }

        private static bool ReadInternal(HediffDef def)
        {
            Resolve();
            if (internalField != null && def.modExtensions != null)
            {
                try
                {
                    for (int i = 0; i < def.modExtensions.Count; i++)
                    {
                        DefModExtension ext = def.modExtensions[i];
                        if (ext != null && extensionType.IsInstanceOfType(ext))
                        {
                            return (bool)internalField.GetValue(ext);
                        }
                    }
                }
                catch (Exception ex)
                {
                    internalField = null;
                    Log.Warning(Bootstrap.Prefix + "reading the gonad placement failed: " + ex);
                }
            }
            return def.defName.IndexOf(InternalNameMark, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
