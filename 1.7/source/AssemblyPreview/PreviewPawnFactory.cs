using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gene_Preview.AssemblyPreview
{
    internal static class PreviewPawnFactory
    {
        private static readonly AccessTools.FieldRef<Pawn_StoryTracker, Color?> SkinColorBaseField =
            AccessTools.FieldRefAccess<Pawn_StoryTracker, Color?>("skinColorBase");

        internal static Color SourceSkinColorBase(Pawn source)
        {
            Color? stored = SkinColorBaseField(source.story);
            if (stored.HasValue) return stored.Value;
            GeneDef melanin = source.genes.GetMelaninGene();
            return melanin?.skinColorBase ?? Color.white;
        }

        internal static Pawn Create(Pawn source, IReadOnlyList<GeneDef> selected)
        {
            Pawn fake = null;
            Rand.PushState();
            try
            {
                Rand.Seed = Seed(source, selected);
                fake = (Pawn)ThingMaker.MakeThing(ThingDefOf.Human);
                PawnComponentsUtility.CreateInitialComponents(fake);
                fake.kindDef = source.kindDef ?? PawnKindDefOf.Colonist;
                fake.Name = source.Name;
                fake.gender = source.gender;
                fake.ageTracker.AgeBiologicalTicks = source.ageTracker.AgeBiologicalTicks;
                fake.ageTracker.AgeChronologicalTicks = source.ageTracker.AgeChronologicalTicks;
                fake.ageTracker.LockCurrentLifeStageIndex(source.ageTracker.CurLifeStageIndex);

                // Use separate Gene instances. AddGene also runs the game's cosmetic
                // conflict resolution and render-node invalidation on this detached pawn.
                foreach (Gene gene in source.genes.Endogenes)
                    fake.genes.AddGene(gene.def, xenogene: false);
                foreach (Gene gene in source.genes.Xenogenes)
                    fake.genes.AddGene(gene.def, xenogene: true);

                // Gene initialization may randomize these values. The source pawn's
                // current appearance is the starting point for implantation.
                fake.story.bodyType = source.story.bodyType;
                fake.story.headType = source.story.headType;
                fake.story.hairDef = source.story.hairDef;
                fake.story.HairColor = source.story.HairColor;
                fake.story.SkinColorBase = SourceSkinColorBase(source);
                fake.story.skinColorOverride = source.story.skinColorOverride;
                fake.story.furDef = source.story.furDef;
                fake.style.beardDef = source.style.beardDef;
                fake.style.FaceTattoo = source.style.FaceTattoo;
                fake.style.BodyTattoo = source.style.BodyTattoo;

                if (selected.Count > 0)
                {
                    fake.genes.SetXenotype(XenotypeDefOf.Baseliner);
                    HashSet<GeneDef> added = new HashSet<GeneDef>();
                    for (int i = 0; i < selected.Count; i++)
                        if (added.Add(selected[i]))
                            fake.genes.AddGene(selected[i], xenogene: true);
                }

                fake.Drawer.renderer.SetAllGraphicsDirty();
                return fake;
            }
            catch
            {
                throw;
            }
            finally
            {
                Rand.PopState();
            }
        }

        private static int Seed(Pawn source, IReadOnlyList<GeneDef> selected)
        {
            unchecked
            {
                int hash = source.thingIDNumber;
                for (int i = 0; i < selected.Count; i++)
                    hash = hash * 31 + selected[i].shortHash;
                return hash;
            }
        }
    }
}
