using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace NHTRJWGenitalia
{
    /// <summary>
    /// Splits 'RJW Now with balls!' gonad harvesting into **remove testicles** and
    /// **remove ovaries**.
    ///
    /// What goes wrong (confirmed by decompiling AutoOrganAdder.dll)
    ///
    ///   Their harvesting is a single recipe (<c>HarvestGonads</c>, worker
    ///   <c>Recipe_HarvestGonads</c>). For a pawn with both testicles and ovaries it offers the
    ///   same part twice and swaps the label per row through a global counter
    ///   ("neuter (testicles)", "spay (ovaries)"). But:
    ///
    ///     1. <b>You cannot choose what gets removed.</b> Whichever row you click, ApplyOnPawn
    ///        removes the natural testicles first and only falls back to the ovaries when there
    ///        are none. While testicles are present there is no way to harvest ovaries alone -
    ///        the same in the vanilla surgery tab. Artificial testicles
    ///        (Wood/Steel/Archotech) are never removed, so for a pawn with artificial testicles
    ///        plus ovaries the label reads "neuter (archotech testicles)" while the
    ///        **ovaries** are what actually come out.
    ///     2. NHT's doll menu builds one row per recipe, and walks GetPartsToApplyOn again
    ///        beforehand, which resets the counter to 0 - so the label is always the first one.
    ///     3. The recipe is registered both in XML (recipeUsers) and in C# (race.recipes), and
    ///        vanilla <c>ThingDef.AllRecipes</c> does not deduplicate, so one row appears
    ///        twice. The same goes for their install recipes.
    ///
    ///   One recipe cannot solve this: a bill remembers only (recipe, part), and both rows
    ///   carry the same pair. So we split the recipe in two.
    ///
    /// What it does
    ///
    ///   We ship two recipes, <c>NHTRJW_RemoveTesticles</c> and <c>NHTRJW_RemoveOvaries</c>
    ///   (Defs/BallsSurgery, loaded only with the balls mod), and attach them instead of
    ///   <c>HarvestGonads</c> to whichever races had it. <c>HarvestGonads</c> is only dropped
    ///   from the lists - its Def stays, so a bill queued in an older save still finishes their
    ///   way.
    ///
    ///   The removal itself is a **verbatim** port of one branch of their ApplyOnPawn: the
    ///   organ item that drops (a per-size ThingDef plus the original owner's details), the
    ///   Neutered hediff, no surgery failure and no violation - all the same. One thing
    ///   differs: the recipe decides what comes out. Testicle gear is removed
    ///   (RemoveTesticleGear) only when testicles are harvested; there is no reason to strip
    ///   gear off testicles that are still attached when only ovaries come out.
    ///
    ///   Their assembly is never hard-referenced. If even one required member cannot be found,
    ///   <see cref="Ready"/> stays false and nothing is changed: our two recipes match no part
    ///   and stay invisible, and their harvesting surgery remains as it was.
    /// </summary>
    internal static class GonadSurgery
    {
        private const string SpeciesTypeName = "Ballz.GonadSpeciesDef";
        private const string AdderTypeName = "Ballz.AutoOrganAdder";
        private const string OwnerCompTypeName = "Ballz.CompGonadOriginalOwner";
        private const string HarvestRecipeName = "HarvestGonads";
        private const string NeuteredHediffName = "Neutered";
        private const string GonadsGroupName = "GonadsBPG";

        internal const string RemoveTesticlesName = "NHTRJW_RemoveTesticles";
        internal const string RemoveOvariesName = "NHTRJW_RemoveOvaries";

        // Size names from their install recipes. Used when SizeNames cannot be read (same values).
        private static readonly string[] FallbackSizeNames =
            { "Micro", "Tiny", "Average", "Large", "Huge", "Ginormous" };

        private static readonly FieldInfo AllRecipesCache =
            AccessTools.Field(typeof(ThingDef), "allRecipesCached");

        private static Type speciesType;
        private static FieldInfo fExcluded;
        private static FieldInfo fTesticles;
        private static FieldInfo fOvaries;
        private static string[] sizeNames;
        private static MethodInfo removeTesticleGear;
        private static Type ownerCompType;
        private static FieldInfo fOwnerName;
        private static FieldInfo fOwnerGender;
        private static FieldInfo fOwnerRace;
        private static FieldInfo fOwnerSize;

        private static RecipeDef harvest;
        private static RecipeDef removeTesticles;
        private static RecipeDef removeOvaries;
        private static HediffDef neutered;
        private static BodyPartGroupDef gonadsGroup;

        /// <summary>True once the split is in place. Our recipe workers check it before offering
        /// a part.</summary>
        internal static bool Ready { get; private set; }

        /// <summary>True when installed. Quietly false when the balls mod is absent or shaped
        /// differently.</summary>
        internal static bool Install()
        {
            if (!ModDeps.Balls)
            {
                return false;
            }

            // --- Our recipes (Defs/BallsSurgery - loaded only with the balls mod) ---
            removeTesticles = DefDatabase<RecipeDef>.GetNamedSilentFail(RemoveTesticlesName);
            removeOvaries = DefDatabase<RecipeDef>.GetNamedSilentFail(RemoveOvariesName);
            harvest = DefDatabase<RecipeDef>.GetNamedSilentFail(HarvestRecipeName);
            gonadsGroup = DefDatabase<BodyPartGroupDef>.GetNamedSilentFail(GonadsGroupName);
            neutered = DefDatabase<HediffDef>.GetNamedSilentFail(NeuteredHediffName);
            if (removeTesticles == null || removeOvaries == null || harvest == null
                || gonadsGroup == null)
            {
                return Refuse("recipes or the gonads group are missing");
            }

            // --- Their species definitions: what counts as natural testicles / ovaries
            speciesType = GenTypes.GetTypeInAnyAssembly(SpeciesTypeName);
            fExcluded = Field(speciesType, "excluded", typeof(bool));
            fTesticles = Field(speciesType, "testiclesDefName", typeof(string));
            fOvaries = Field(speciesType, "ovariesDefName", typeof(string));
            if (speciesType == null || fExcluded == null || fTesticles == null || fOvaries == null)
            {
                return Refuse(SpeciesTypeName + " no longer matches");
            }

            // --- Optional pieces: if one is missing, only that part is skipped -------
            Type adder = GenTypes.GetTypeInAnyAssembly(AdderTypeName);
            FieldInfo fSizes = (adder == null)
                ? null
                : adder.GetField("SizeNames", BindingFlags.Static | BindingFlags.Public);
            sizeNames = (fSizes == null) ? null : fSizes.GetValue(null) as string[];
            if (sizeNames == null || sizeNames.Length == 0)
            {
                sizeNames = FallbackSizeNames;
            }
            removeTesticleGear = (adder == null)
                ? null
                : AccessTools.Method(adder, "RemoveTesticleGear", new[] { typeof(Pawn) });

            ownerCompType = GenTypes.GetTypeInAnyAssembly(OwnerCompTypeName);
            fOwnerName = Field(ownerCompType, "originalPawnName", typeof(string));
            fOwnerGender = Field(ownerCompType, "originalGender", typeof(Gender));
            fOwnerRace = Field(ownerCompType, "originalRace", typeof(string));
            fOwnerSize = Field(ownerCompType, "gonadSize", typeof(string));

            Ready = true;

            // --- Swapping the recipe lists -------------------------------------------
            // They add their recipes to the race lists through
            // LongEventHandler.ExecuteWhenFinished, which runs **after** every static
            // constructor, so right now they may not be in yet. We also run after that method
            // and do it again; repeating it changes nothing.
            Relist();
            MethodInfo addRecipes = (adder == null)
                ? null
                : AccessTools.Method(adder, "AddRecipesToAnimals");
            if (addRecipes != null)
            {
                new Harmony(Bootstrap.HarmonyId).Patch(
                    addRecipes,
                    postfix: new HarmonyMethod(typeof(GonadSurgery).GetMethod(
                        "AddRecipesPostfix", BindingFlags.Static | BindingFlags.Public)));
            }
            else
            {
                // The name changed. Queue ourselves so our turn still comes after theirs.
                LongEventHandler.ExecuteWhenFinished(Relist);
            }
            return true;
        }

        public static void AddRecipesPostfix()
        {
            Relist();
        }

        private static bool Refuse(string why)
        {
            Log.Message(Bootstrap.Prefix + "gonad surgery split not installed - " + why + ".");
            return false;
        }

        private static FieldInfo Field(Type t, string name, Type fieldType)
        {
            if (t == null)
            {
                return null;
            }
            FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public);
            return (f != null && f.FieldType == fieldType) ? f : null;
        }

        // -------------------------------------------------------------------------
        // Lists
        // -------------------------------------------------------------------------

        /// <summary>
        /// Drops <c>HarvestGonads</c> from every race that had it and attaches our two instead.
        /// Also clears the duplicates left by their double registration. Idempotent.
        /// </summary>
        internal static void Relist()
        {
            if (!Ready)
            {
                return;
            }
            try
            {
                List<ThingDef> things = DefDatabase<ThingDef>.AllDefsListForReading;
                for (int i = 0; i < things.Count; i++)
                {
                    ThingDef td = things[i];
                    if (td == null || td.race == null)
                    {
                        continue;
                    }
                    bool changed = false;

                    bool had = (td.recipes != null && td.recipes.Contains(harvest))
                               || (harvest.recipeUsers != null && harvest.recipeUsers.Contains(td));
                    if (had)
                    {
                        if (td.recipes != null && td.recipes.RemoveAll(r => r == harvest) > 0)
                        {
                            changed = true;
                        }
                        if (harvest.recipeUsers != null && harvest.recipeUsers.Remove(td))
                        {
                            changed = true;
                        }
                        changed |= Attach(td, removeTesticles);
                        changed |= Attach(td, removeOvaries);
                    }

                    // Double registration: they add it through XML (recipeUsers) and through C#
                    // (race.recipes). The recipeUsers side stays, so dropping the recipes side
                    // does not make the recipe disappear.
                    if (td.recipes != null)
                    {
                        for (int k = td.recipes.Count - 1; k >= 0; k--)
                        {
                            RecipeDef r = td.recipes[k];
                            if (IsBallsRecipe(r) && r.recipeUsers != null
                                && r.recipeUsers.Contains(td))
                            {
                                td.recipes.RemoveAt(k);
                                changed = true;
                            }
                        }
                    }

                    if (changed && AllRecipesCache != null)
                    {
                        AllRecipesCache.SetValue(td, null);     // Rebuilt on the next read
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(Bootstrap.Prefix + "gonad surgery relist failed: " + ex);
            }
        }

        /// <summary>Left alone when it is already attached through recipeUsers - adding it again
        /// would duplicate it.</summary>
        private static bool Attach(ThingDef td, RecipeDef recipe)
        {
            if (recipe.recipeUsers != null && recipe.recipeUsers.Contains(td))
            {
                return false;
            }
            if (td.recipes == null)
            {
                td.recipes = new List<RecipeDef>();
            }
            if (td.recipes.Contains(recipe))
            {
                return false;
            }
            td.recipes.Add(recipe);
            return true;
        }

        /// <summary>The same set their AddRecipesToAnimals adds.</summary>
        private static bool IsBallsRecipe(RecipeDef r)
        {
            if (r == null || r.defName == null)
            {
                return false;
            }
            string n = r.defName;
            return n == HarvestRecipeName || n == "Elastrate" || n == "RemoveElastrationBand"
                   || (n.StartsWith("Install", StringComparison.Ordinal)
                       && (n.Contains("Testicles") || n.Contains("Ovaries")));
        }

        // -------------------------------------------------------------------------
        // Finding the target and removing it
        // -------------------------------------------------------------------------

        /// <summary>
        /// The natural testicles (or ovaries) to remove from this pawn, or null.
        /// Searched in **the same order** as their ApplyOnPawn: by species definition, skipping
        /// excluded species. Artificial testicles and ovaries are not in those definitions, so
        /// they never match here (their code does not remove them either).
        /// </summary>
        internal static Hediff FindTarget(Pawn pawn, bool testicles)
        {
            if (!Ready || pawn == null || pawn.health == null || pawn.health.hediffSet == null)
            {
                return null;
            }
            FieldInfo nameField = testicles ? fTesticles : fOvaries;
            foreach (Def species in GenDefDatabase.GetAllDefsInDatabaseForDef(speciesType))
            {
                if ((bool)fExcluded.GetValue(species))
                {
                    continue;
                }
                string name = nameField.GetValue(species) as string;
                if (name.NullOrEmpty())
                {
                    continue;
                }
                HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(name);
                if (def == null)
                {
                    continue;
                }
                Hediff h = pawn.health.hediffSet.GetFirstHediffOfDef(def);
                if (h != null)
                {
                    return h;
                }
            }
            return null;
        }

        /// <summary>The pawn's gonad part: the first part of the GonadsBPG group, as on their
        /// side.</summary>
        internal static BodyPartRecord GonadPart(Pawn pawn)
        {
            if (gonadsGroup == null || pawn == null || pawn.RaceProps == null
                || pawn.RaceProps.body == null)
            {
                return null;
            }
            List<BodyPartRecord> parts = pawn.RaceProps.body.AllParts;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].groups != null && parts[i].groups.Contains(gonadsGroup))
                {
                    return parts[i];
                }
            }
            return null;
        }

        /// <summary>The label shown in the menu, shaped like theirs: "name (size)".</summary>
        internal static string LabelOf(Hediff h)
        {
            string label = h.def.label;
            List<HediffStage> stages = h.def.stages;
            if (stages != null && stages.Count > 1)
            {
                string stage = stages[Mathf.Clamp(h.CurStageIndex, 0, stages.Count - 1)].label;
                if (!stage.NullOrEmpty())
                {
                    label = label + " (" + stage + ")";
                }
            }
            return label;
        }

        /// <summary>
        /// A verbatim port of one branch of their ApplyOnPawn. Only the choice of what to remove
        /// comes from the recipe.
        /// </summary>
        internal static void Remove(Pawn pawn, BodyPartRecord part, Pawn billDoer, bool testicles)
        {
            Hediff target = FindTarget(pawn, testicles);
            if (target == null)
            {
                return;             // Already gone since the bill was queued; no Neutered
                                    // hediff either.
            }

            string defName = target.def.defName;
            string size = sizeNames[Mathf.Clamp(target.CurStageIndex, 0, sizeNames.Length - 1)];
            pawn.health.RemoveHediff(target);

            // The organ item: "<hediff>_<size>" when that exists, otherwise "<hediff>".
            ThingDef itemDef = DefDatabase<ThingDef>.GetNamedSilentFail(defName + "_" + size)
                               ?? DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (itemDef != null && billDoer != null && billDoer.Map != null)
            {
                Thing item = ThingMaker.MakeThing(itemDef);
                SetOriginalOwner(item, pawn, size);
                GenPlace.TryPlaceThing(item, billDoer.Position, billDoer.Map, ThingPlaceMode.Near);
            }

            // The Neutered hediff: they add it on every harvest (even with one gonad left).
            // We do the same.
            if (neutered != null && part != null)
            {
                Hediff n = HediffMaker.MakeHediff(neutered, pawn, part);
                n.Severity = 0.01f;
                pawn.health.AddHediff(n);
                if (testicles && removeTesticleGear != null)
                {
                    removeTesticleGear.Invoke(null, new object[] { pawn });
                }
            }
        }

        /// <summary>Records the original owner on the organ item; their code reads it when the
        /// organ is implanted again.</summary>
        private static void SetOriginalOwner(Thing item, Pawn pawn, string size)
        {
            ThingWithComps withComps = item as ThingWithComps;
            if (ownerCompType == null || withComps == null)
            {
                return;
            }
            List<ThingComp> comps = withComps.AllComps;
            for (int i = 0; i < comps.Count; i++)
            {
                if (!ownerCompType.IsInstanceOfType(comps[i]))
                {
                    continue;
                }
                if (fOwnerName != null) fOwnerName.SetValue(comps[i], pawn.LabelShort);
                if (fOwnerGender != null) fOwnerGender.SetValue(comps[i], pawn.gender);
                if (fOwnerRace != null) fOwnerRace.SetValue(comps[i], pawn.def.label);
                if (fOwnerSize != null) fOwnerSize.SetValue(comps[i], size);
                return;
            }
        }
    }

    /// <summary>
    /// The harvesting recipe worker. Like their <c>Recipe_HarvestGonads</c> it cannot fail and
    /// is not a violation.
    /// </summary>
    public abstract class Recipe_RemoveGonadBase : Recipe_Surgery
    {
        /// <summary>True for testicles, false for ovaries.</summary>
        protected abstract bool Testicles { get; }

        public override bool IsViolationOnPawn(Pawn pawn, BodyPartRecord part, Faction billDoerFaction)
        {
            return false;
        }

        public override IEnumerable<BodyPartRecord> GetPartsToApplyOn(Pawn pawn, RecipeDef recipe)
        {
            if (!GonadSurgery.Ready || GonadSurgery.FindTarget(pawn, Testicles) == null)
            {
                yield break;
            }
            BodyPartRecord part = GonadSurgery.GonadPart(pawn);
            if (part != null && !pawn.health.hediffSet.PartIsMissing(part))
            {
                yield return part;
            }
        }

        public override string GetLabelWhenUsedOn(Pawn pawn, BodyPartRecord part)
        {
            Hediff target = GonadSurgery.FindTarget(pawn, Testicles);
            return (target == null)
                ? recipe.label
                : recipe.label + " (" + GonadSurgery.LabelOf(target) + ")";
        }

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer,
                                         List<Thing> ingredients, Bill bill)
        {
            if (pawn == null || pawn.health == null || pawn.health.hediffSet == null)
            {
                return;
            }
            GonadSurgery.Remove(pawn, part, billDoer, Testicles);
        }
    }

    /// <summary>Testicle harvesting.</summary>
    public class Recipe_RemoveTesticles : Recipe_RemoveGonadBase
    {
        protected override bool Testicles
        {
            get { return true; }
        }
    }

    /// <summary>Ovary harvesting.</summary>
    public class Recipe_RemoveOvaries : Recipe_RemoveGonadBase
    {
        protected override bool Testicles
        {
            get { return false; }
        }
    }
}
