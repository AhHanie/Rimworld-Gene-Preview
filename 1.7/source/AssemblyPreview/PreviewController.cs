using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gene_Preview.AssemblyPreview
{
    internal sealed class PreviewController
    {
        private readonly List<Pawn> candidates = new List<Pawn>();
        private Pawn selectedPawn;
        private Pawn projectedPawn;
        private int lastSignature;
        private bool dirty = true;
        private int rotation;
        private float nextRosterRefresh;

        internal Pawn SelectedPawn => selectedPawn;
        internal Pawn ProjectedPawn => projectedPawn;
        internal Rot4 Rotation => new Rot4((2 + rotation) % 4);
        internal int CandidateCount => candidates.Count;

        internal void Invalidate() => dirty = true;

        internal void Refresh(Map preferredMap, bool first = false)
        {
            if (!first && Time.realtimeSinceStartup < nextRosterRefresh &&
                (selectedPawn == null || (!selectedPawn.Dead && selectedPawn.IsColonist))) return;
            nextRosterRefresh = Time.realtimeSinceStartup + 0.5f;
            Pawn previous = selectedPawn;
            int previousIndex = candidates.IndexOf(selectedPawn);
            candidates.Clear();
            candidates.AddRange(PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists);
            candidates.RemoveAll(p => p == null || p.Dead || !p.IsColonist || !p.RaceProps.Humanlike || p.IsSubhuman || p.genes == null || p.story == null || p.style == null);
            if (first)
            {
                selectedPawn = null;
                foreach (Pawn pawn in Find.Selector.SelectedPawns)
                    if (candidates.Contains(pawn) && pawn.MapHeld == preferredMap)
                    {
                        selectedPawn = pawn;
                        break;
                    }
            }
            if (selectedPawn == null || !candidates.Contains(selectedPawn))
                selectedPawn = candidates.Count == 0 ? null : candidates[Mathf.Clamp(previousIndex, 0, candidates.Count - 1)];
            if (selectedPawn != previous)
                dirty = true;
        }

        internal void SelectIfEligible(Pawn pawn)
        {
            if (!candidates.Contains(pawn)) return;
            if (selectedPawn != pawn)
            {
                selectedPawn = pawn;
                dirty = true;
            }
        }

        internal void Cycle(int step)
        {
            if (candidates.Count == 0) return;
            int index = candidates.IndexOf(selectedPawn);
            selectedPawn = candidates[(index + step + candidates.Count) % candidates.Count];
            dirty = true;
        }

        internal void Rotate() => rotation = (rotation + 1) % 4;

        internal void Update(IReadOnlyList<GeneDef> genes)
        {
            if (selectedPawn == null)
            {
                Release();
                return;
            }
            int signature = Signature(selectedPawn, genes);
            if (!dirty && signature == lastSignature && projectedPawn != null) return;
            Release();
            projectedPawn = PreviewPawnFactory.Create(selectedPawn, genes);
            lastSignature = signature;
            dirty = false;
        }

        internal void Release()
        {
            // The detached pawn is not registered with WorldPawns. Destroy() would
            // register it there; simply release it. PortraitsCache expires unused
            // entries after one second.
            projectedPawn = null;
        }

        private static int Signature(Pawn pawn, IReadOnlyList<GeneDef> genes)
        {
            unchecked
            {
                int h = pawn.thingIDNumber;
                h = h * 31 + pawn.ageTracker.CurLifeStageIndex;
                h = h * 31 + pawn.gender.GetHashCode();
                h = h * 31 + (pawn.story.bodyType?.shortHash ?? 0);
                h = h * 31 + (pawn.story.headType?.shortHash ?? 0);
                h = h * 31 + (pawn.story.hairDef?.shortHash ?? 0);
                h = h * 31 + (pawn.story.furDef?.shortHash ?? 0);
                h = h * 31 + pawn.story.HairColor.GetHashCode();
                h = h * 31 + PreviewPawnFactory.SourceSkinColorBase(pawn).GetHashCode();
                h = h * 31 + pawn.story.skinColorOverride.GetHashCode();
                h = h * 31 + (pawn.style.beardDef?.shortHash ?? 0);
                h = h * 31 + (pawn.style.FaceTattoo?.shortHash ?? 0);
                h = h * 31 + (pawn.style.BodyTattoo?.shortHash ?? 0);
                foreach (Gene gene in pawn.genes.GenesListForReading)
                    h = h * 31 + gene.def.shortHash + (pawn.genes.IsXenogene(gene) ? 1 : 0);
                for (int i = 0; i < genes.Count; i++)
                    h = h * 31 + genes[i].shortHash;
                return h;
            }
        }
    }
}
