using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace NHTRJWGenitalia
{
    /// <summary>
    /// Writes the current pawn's state into our Defs right before the health tab is drawn.
    ///
    ///  1) Child body type - with the setting off, every one of our parts on the Kid doll gets
    ///     <c>bodyPartId</c> -1 so the NHT renderer skips it quietly. We invalidate the index
    ///     rather than remove the Def, so there is no log line and no side effect.
    ///  2) Form and size - the hediffs on the part decide the form (penis / vagina, testicles /
    ///     ovaries, breasts / chest), the severity picks the tier, and we set <c>tex</c>,
    ///     <c>position</c>, <c>width</c> and <c>height</c> accordingly. When no form matches we
    ///     restore the defaults from the Def XML.
    ///
    /// Nice Health Tab draws only one health card at a time, which is what makes borrowing the
    /// Def's global fields like this safe.
    /// </summary>
    public static class DollStateApplier
    {
        private static bool failed;
        private static int failures;

        /// <summary>Give up after this many failures in a row, so one pawn cannot cost the whole
        /// playthrough.</summary>
        private const int FailureLimit = 8;

        /// <summary>Harmony prefix. It returns nothing, so it does not interfere with the
        /// original call flow.</summary>
        public static void Prefix(Pawn pawn)
        {
            if (!Bootstrap.Ready)
            {
                return;
            }
            if (failed)
            {
                // Simply returning once switched off would **leave the last pawn's state
                // behind** - the cause of the womb fluid that once stuck to every pawn.
                HideAll();
                return;
            }
            try
            {
                Apply(pawn);
                failures = 0;
            }
            catch (Exception ex)
            {
                // This runs every frame, so only the first failure is logged.
                if (failures == 0)
                {
                    Log.Warning(Bootstrap.Prefix + "per-pawn state failed: " + ex);
                }
                if (++failures >= FailureLimit)
                {
                    failed = true;
                    Log.Warning(Bootstrap.Prefix + "per-pawn state disabled after "
                                + FailureLimit + " consecutive failures.");
                }
                // Never leave it half applied: no picture beats a wrong picture.
                HideAll();
            }
        }

        /// <summary>
        /// Hides everything we put on the doll. This is the path taken after a failure, so
        /// **it must not throw again**.
        /// </summary>
        private static void HideAll()
        {
            try
            {
                NippleAppearance.Clear();
                WombFluidAppearance.Clear();
                AnusWindow.Clear();
                if (Bootstrap.BodyPartIdField == null)
                {
                    return;
                }
                List<BoundPart> parts = Bootstrap.BoundParts;
                for (int i = 0; i < parts.Count; i++)
                {
                    parts[i].currentForm = null;
                    Bootstrap.BodyPartIdField.SetValue(parts[i].def, -1);
                }
            }
            catch
            {
                // Nothing can be done here. Move on quietly.
            }
        }

        /// <summary>The doll name NHT uses for children.</summary>
        private const string KidDollName = "Kid";

        private static void Apply(Pawn pawn)
        {
            bool show = pawn != null;
            Dictionary<string, List<Hediff>> byPart = show ? CollectHediffs(pawn) : null;
            NHTRJWSettings cfg = NHTRJWSettings.Current;
            bool nipples = show && cfg.showNipples;
            // Gonads are always drawn (the old "show gonads" option was dropped).
            // The chest (breasts plus nipples) on pawns whose gender is male. RJW gives every
            // pawn a Breasts hediff, so without this switch it shows on males too.
            bool chest = show && (cfg.showMaleChest || pawn.gender != Gender.Male);
            // Testicles on futa. The ovaries ignore this switch - off leaves only them.
            bool testicles = show && (cfg.showFutaTesticles || !IsFuta(byPart));
            // The womb layers only make sense over a vagina. The hediffs they read sit on the
            // whole body (cum inflation) or the torso (pregnancy), so they outlive the vagina
            // they came from - RJW only wipes the hediffs **on** a part it removes. A male pawn
            // carrying such a leftover used to get a womb full of cum in the panel (user report).
            bool hasVagina = show && HasFamily(byPart, VaginaFamily);
            // Child body type - decided by **which doll is drawn**, not by age. NHT draws one
            // doll at a time, so with this off we hide all of our parts on the Kid doll.
            bool kidDoll = cfg.showKidGenitals;
            // Whose doll is about to be drawn: our remap fallback only answers for pawns of the
            // body our part indices were measured in (DollIndexRemap).
            DollIndexRemap.Note(pawn);
            NippleAppearance.Clear();
            WombFluidAppearance.Clear();
            AnusWindow.Clear();

            List<BoundPart> parts = Bootstrap.BoundParts;
            for (int i = 0; i < parts.Count; i++)
            {
                BoundPart p = parts[i];
                bool here = show && (kidDoll || p.dollName != KidDollName);
                bool isNipple = p.slot == Bootstrap.NippleSlot;
                // The nipples are a layer on top of the breasts, so they go with the chest.
                bool isChest = isNipple || p.slot == Bootstrap.ChestSlot;
                // Both testicle slots (the organ and the surface copy). The futa switch applies
                // only here.
                bool isTesticles = p.slot == Bootstrap.GonadsSlot
                                   || p.slot == Bootstrap.OuterGonadsSlot;
                // The womb and the fluid inside it: a display layer over the vagina.
                bool isWomb = p.slot == Bootstrap.WombSlot
                              || p.slot == Bootstrap.WombFluidSlot;

                // Pick the form first: whether the part shows at all depends on the result.
                DollPartFormDef form = null;
                Hediff match = null;
                float severity = float.NaN;
                List<Hediff> hediffs = null;
                if (here)
                {
                    byPart.TryGetValue(p.partDefName, out hediffs);
                    hediffs = WithExtraParts(byPart, p.slot, hediffs);
                    SelectForm(p, pawn, hediffs, out form, out match, out severity);
                }

                // We invalidate the index rather than remove the part (NHT then skips it quietly).
                //   nipples  - can be switched off, and are not drawn when the chest is missing.
                //   chest    - can be switched off for male pawns (off by default).
                //   testicles- can be switched off for futa (off by default). Ovaries stay.
                //   others   - no matching form means there is nothing to draw.
                //
                // A missing form happens for two reasons. Either way, nothing is drawn.
                //   (1) the pawn never had that part (a vulva on a penis-only pawn, say)
                //   (2) the part was destroyed or harvested - the moment RJW attaches
                //       Hediff_MissingPart it **wipes every other hediff** on that part
                //       (patch_MissingBodyPart.cs), so there is no way to know what was there.
                bool visible = here
                               && (!isNipple || (nipples && !PartMissing(pawn, p)))
                               && (!isChest || chest)
                               && (!isTesticles || testicles)
                               && (!isWomb || hasVagina)
                               && form != null;

                // The anus window shows even without an anus, so we keep the part index alive
                // (NHT has to give this part its turn for the window to be laid) and hide only
                // the anus glyph. TintedPartRenderer does the hiding (AnusWindow.ShowGlyph).
                bool windowOnly = here && !visible && p.slot == Bootstrap.AnusSlot;

                // The index the doll carries is not always the real one: another mod remap may
                // use that number as a key of its own (DollIndexRemap.KeyFor).
                Bootstrap.BodyPartIdField.SetValue(
                    p.def, (visible || windowOnly) ? DollIndexRemap.KeyFor(p.realIndex) : -1);
                // Keep the chosen form so the RJW panel can read its panel coordinates.
                p.currentForm = visible ? form : null;
                p.placementForm = null;     // set below once the picture is chosen
                if (here && p.slot == Bootstrap.AnusSlot)
                {
                    AnusWindow.Prepare(visible);
                }
                if (!visible)
                {
                    if (windowOnly)
                    {
                        RestoreDefaults(p);
                    }
                    continue;
                }

                if (isNipple)
                {
                    // The nipple colour comes from what the pawn actually has, not from its
                    // health status. Settled once here, right before drawing, for the renderer.
                    NippleAppearance.Prepare(pawn, match);
                }
                else if (p.slot == Bootstrap.WombFluidSlot)
                {
                    // The fluid colour depends on whether menstrual blood is mixed in. The comp
                    // that holds it sits on the **vagina hediff**, but the hediff that picked the
                    // form may differ (inflation is picked by the Cumflation hediff), so we walk
                    // the list to find the comp.
                    WombFluidAppearance.Prepare(hediffs);
                }

                if (form == null)
                {
                    RestoreDefaults(p);
                    continue;
                }

                // The kind of penis picks the art set (and, for testicles, the pawn's penis
                // does), the way Sized Apparel does. It also picks the kind's own hit area.
                string kind = null;
                string kindVariation = null;
                if (form.HasKinds)
                {
                    Hediff source = form.VariantFromPenis ? DollPartFormDef.PenisOf(pawn) : match;
                    if (source != null && source.def != null)
                    {
                        kind = source.def.defName;
                        kindVariation = DollPartFormDef.VariationOf(source);
                    }
                }

                Texture2D tex = null;
                DollPartFormDef placement = form;   // whose position, scale and panel placement
                if (form.tiers != null && form.tiers.Length > 0)
                {
                    // Cycle forms take their tier from the value we read, not from a hediff
                    // severity. A pinned tier wins over both - that is the case for the ovaries
                    // without the balls mod: a vagina's size says nothing about ovary size, so we
                    // pin one reference picture (Ovaries_5).
                    int tier = (form.fixedTier >= 0) ? form.fixedTier
                               : !float.IsNaN(severity) ? form.TierFor(severity)
                               : (match == null) ? 0 : form.TierFor(match.Severity);
                    // With a multiple pregnancy, the multiplet picture of the same tier - only
                    // when one exists.
                    // Kind testicle art sits on that kind's penis canvas, so it is drawn where the
                    // penis is drawn. Its size: with the balls mod the testicles keep their own size;
                    // without it there is no testicle size at all, so they follow the penis.
                    if (form.variantOnPenisCanvas && kind != null)
                    {
                        Hediff penis = DollPartFormDef.PenisOf(pawn);
                        DollPartFormDef penisForm = PenisFormFor(p.dollName);
                        if (penis != null && penisForm != null)
                        {
                            int artTier = ModDeps.Balls ? tier : penisForm.TierFor(penis.Severity);
                            if (form.KindHasOwnArt(kind, kindVariation, artTier))
                            {
                                tier = artTier;
                                placement = penisForm;
                            }
                        }
                    }
                    int babies = (form.multipletTiers == null) ? 1 : FormStateReader.BabyCount(match);
                    tex = form.TextureFor(tier, babies, kind, kindVariation);
                }

                if (tex != null && Bootstrap.TexField != null)
                {
                    Bootstrap.TexField.SetValue(p.def, tex);
                }
                else
                {
                    RestoreTexture(p);
                }

                p.placementForm = placement;
                // The anus is drawn inside a window at a fixed place in the doll column, so where
                // it goes depends on the doll's bounding box. On another mod's doll that has to be
                // worked out again (ForeignDolls); on ours the Def already has it.
                Vector2 pos = placement.position;
                float sc = placement.scale;
                if (p.slot == Bootstrap.AnusSlot || p.slot == Bootstrap.OuterAnusSlot)
                {
                    Vector2 fp;
                    float fs;
                    if (ForeignDolls.TryAnusPlacement(pawn, placement, out fp, out fs))
                    {
                        pos = fp;
                        sc = fs;
                    }
                }
                else
                {
                    // A doll another mod built may lay the body somewhere else than we do; then
                    // our parts move with it (ForeignDolls). The anus is not in that number - its
                    // window is at a fixed place in the doll column, not on the body.
                    pos += ForeignDolls.OffsetFor(pawn);
                }
                if (Bootstrap.PositionField != null)
                {
                    Bootstrap.PositionField.SetValue(p.def, pos);
                }
                if (Bootstrap.WidthField != null)
                {
                    Bootstrap.WidthField.SetValue(p.def, sc);
                    Bootstrap.HeightField.SetValue(p.def, Mathf.Abs(sc));
                }
                if (Bootstrap.HitboxField != null)
                {
                    Bootstrap.HitboxField.SetValue(p.def, form.HitboxFor(kind));
                }
            }
        }

        private static readonly Dictionary<string, DollPartFormDef> penisForms =
            new Dictionary<string, DollPartFormDef>();

        /// <summary>The penis form of a doll (the outer genitals slot), or null. Cached.</summary>
        private static DollPartFormDef PenisFormFor(string dollName)
        {
            DollPartFormDef found;
            if (dollName == null)
            {
                return null;
            }
            if (penisForms.TryGetValue(dollName, out found))
            {
                return found;
            }
            List<BoundPart> parts = Bootstrap.BoundParts;
            for (int i = 0; i < parts.Count && found == null; i++)
            {
                BoundPart bp = parts[i];
                if (bp.dollName != dollName || bp.slot != Bootstrap.OuterGenitalsSlot || bp.forms == null)
                {
                    continue;
                }
                for (int k = 0; k < bp.forms.Count; k++)
                {
                    if (bp.forms[k].form == "Penis")
                    {
                        found = bp.forms[k];
                        break;
                    }
                }
            }
            penisForms[dollName] = found;
            return found;
        }

        /// <summary>
        /// Picks which form this part has.
        /// Forms matched by a hediff come first; otherwise the fallback form is used.
        /// </summary>
        private static void SelectForm(BoundPart p, Pawn pawn, List<Hediff> hediffs,
                                       out DollPartFormDef form, out Hediff match,
                                       out float severity)
        {
            form = null;
            match = null;
            severity = float.NaN;       // NaN = use the hediff's own severity
            if (p.forms == null)
            {
                return;
            }

            DollPartFormDef fallback = null;

            for (int i = 0; i < p.forms.Count; i++)
            {
                DollPartFormDef f = p.forms[i];      // sorted by descending priority
                if (!f.MatchesGender(pawn))
                {
                    continue;
                }
                if (f.hideWhileFetus && FormStateReader.FetusShowing(pawn))
                {
                    continue;       // Nothing is laid over a womb that shows a fetus.
                }
                if (!f.hiddenKindsKeepOrgan && f.HidesKindOf(pawn))
                {
                    continue;       // This kind of penis comes without this part (no testicles).
                }

                // The belly reads every hediff of the pawn and adds them up, so it does not walk
                // the slot's list.
                if (f.stateSource == FormStateReader.Belly)
                {
                    float belly;
                    Hediff first;
                    if (FormStateReader.TryBelly(pawn, f, out belly, out first))
                    {
                        form = f;
                        match = first;
                        severity = belly;
                        return;
                    }
                    continue;
                }

                // Forms that read their tier from outside a severity ignore the hediff rules.
                // We do not know which hediff carries the value, so we walk the list and ask
                // (menstruation and fluid live on the vagina hediff's comp, the implantation
                // count on the pregnancy hediff).
                if (!f.stateSource.NullOrEmpty())
                {
                    if (hediffs != null)
                    {
                        for (int h = 0; h < hediffs.Count; h++)
                        {
                            float v;
                            if (FormStateReader.TryRead(f.stateSource,
                                                        hediffs[h], out v))
                            {
                                form = f;
                                match = hediffs[h];
                                severity = v;
                                return;
                            }
                        }
                    }
                    continue;
                }

                if (hediffs != null)
                {
                    for (int h = 0; h < hediffs.Count; h++)
                    {
                        if (f.MatchesHediff(hediffs[h]))
                        {
                            form = f;
                            match = hediffs[h];
                            return;
                        }
                    }
                }

                if (fallback == null && f.fallback)
                {
                    fallback = f;
                }
            }

            form = fallback;
        }

        private static void RestoreDefaults(BoundPart p)
        {
            RestoreTexture(p);
            if (Bootstrap.PositionField != null)
            {
                Bootstrap.PositionField.SetValue(p.def, p.defaultPosition);
            }
            if (Bootstrap.WidthField != null)
            {
                Bootstrap.WidthField.SetValue(p.def, p.defaultWidth);
                Bootstrap.HeightField.SetValue(p.def, p.defaultHeight);
            }
            if (Bootstrap.HitboxField != null)
            {
                Bootstrap.HitboxField.SetValue(p.def, p.defaultHitbox);
            }
        }

        private static void RestoreTexture(BoundPart p)
        {
            if (Bootstrap.TexField != null && p.defaultTex != null)
            {
                Bootstrap.TexField.SetValue(p.def, p.defaultTex);
            }
        }

        /// <summary>
        /// Groups the hediffs on the parts we track, by part.
        /// It reads hediffSet directly, regardless of NHT's IgnoreHediffs, so size hediffs left
        /// out of the doll's condition check are still available for form and size matching.
        /// </summary>
        /// <summary>
        /// Does the pawn have **both** a penis and a vagina?
        ///
        /// Checked the same way forms are matched: the <c>genitalFamily</c> of the hediffs on the
        /// genitals part. It looks at what is actually there rather than at the gender, so
        /// transgender pawns come out right too.
        /// </summary>
        private static bool IsFuta(Dictionary<string, List<Hediff>> byPart)
        {
            return HasFamily(byPart, PenisFamily) && HasFamily(byPart, VaginaFamily);
        }

        private const string PenisFamily = "Penis";
        private const string VaginaFamily = "Vagina";

        /// <summary>
        /// Does the pawn have a sex part of this family on the genitals?
        ///
        /// Checked the same way forms are matched: the <c>genitalFamily</c> of the hediffs on the
        /// genitals part. Hediffs that are not sex parts (wounds and so on) have no such field and
        /// give "", so they never count.
        /// </summary>
        private static bool HasFamily(Dictionary<string, List<Hediff>> byPart, string family)
        {
            List<Hediff> list;
            if (byPart == null
                || !byPart.TryGetValue(Bootstrap.GenitalsSlot, out list) || list == null)
            {
                return false;
            }
            for (int i = 0; i < list.Count; i++)
            {
                Hediff h = list[i];
                if (h == null || h.def == null)
                {
                    continue;
                }
                if (string.Equals(DollPartFormDef.GenitalFamilyOf(h.def), family,
                                  StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Has this part gone from the pawn entirely (amputated or harvested)?</summary>
        private static bool PartMissing(Pawn pawn, BoundPart p)
        {
            if (p.realIndex < 0 || pawn == null || pawn.health == null
                || pawn.health.hediffSet == null || pawn.RaceProps == null
                || pawn.RaceProps.body == null)
            {
                return false;
            }
            // Our Defs carry the index in the human body; on another race the same part sits
            // elsewhere (DollIndexRemap).
            int index = DollIndexRemap.IndexIn(pawn, p.realIndex);
            if (index < 0)
            {
                return false;
            }
            BodyPartRecord record = pawn.RaceProps.body.GetPartAtIndex(index);
            return record != null && pawn.health.hediffSet.PartIsMissing(record);
        }

        /// <summary>
        /// When a slot has to look at hediffs beyond its own part, returns them merged.
        /// With nothing to merge it returns the original list, as most slots do.
        /// </summary>
        private static List<Hediff> WithExtraParts(Dictionary<string, List<Hediff>> byPart,
                                                   string slot, List<Hediff> own)
        {
            string[] extra;
            if (byPart == null || slot == null
                || !Bootstrap.SlotExtraHediffParts.TryGetValue(slot, out extra))
            {
                return own;
            }

            List<Hediff> merged = null;
            for (int i = 0; i < extra.Length; i++)
            {
                List<Hediff> bucket;
                if (!byPart.TryGetValue(extra[i], out bucket) || bucket.Count == 0)
                {
                    continue;
                }
                if (merged == null)
                {
                    // Priority order is decided on the form side, so order does not matter much
                    // here. Our own part's hediffs go first so they win at equal priority.
                    merged = new List<Hediff>(own ?? EmptyHediffs);
                }
                merged.AddRange(bucket);
            }
            return merged ?? own;
        }

        private static readonly List<Hediff> EmptyHediffs = new List<Hediff>();

        private static Dictionary<string, List<Hediff>> CollectHediffs(Pawn pawn)
        {
            Dictionary<string, List<Hediff>> result = new Dictionary<string, List<Hediff>>();
            if (pawn == null || pawn.health == null || pawn.health.hediffSet == null)
            {
                return result;
            }

            List<Hediff> all = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < all.Count; i++)
            {
                Hediff h = all[i];
                if (h == null)
                {
                    continue;
                }
                // Hediffs without a part are kept as well - womb inflation, for one, attaches
                // without a part. They are stored aside and only the slots that need them look.
                string partName = (h.Part == null || h.Part.def == null)
                                  ? Bootstrap.WholeBodyKey : h.Part.def.defName;
                List<Hediff> bucket;
                if (!result.TryGetValue(partName, out bucket))
                {
                    bucket = new List<Hediff>(2);
                    result[partName] = bucket;
                }
                bucket.Add(h);
            }
            return result;
        }
    }
}
