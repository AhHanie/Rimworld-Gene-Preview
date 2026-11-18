using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gene_Preview.AssemblyPreview
{
    internal sealed class AssemblyPreviewState
    {
        internal readonly PreviewController Controller = new PreviewController();
        internal readonly List<GeneDef> Genes = new List<GeneDef>();
        internal Pawn PreferredPawn;
        internal PreviewPopup Popup;
        internal float ExtraWidth;
        internal bool Failed;
    }

    internal static class AssemblyPreviewPatches
    {
        private const float PanelWidth = 300f;
        private static readonly ConditionalWeakTable<Dialog_CreateXenogerm, AssemblyPreviewState> States = new ConditionalWeakTable<Dialog_CreateXenogerm, AssemblyPreviewState>();
        private static readonly FieldInfo SelectedPacksField = AccessTools.Field(typeof(Dialog_CreateXenogerm), "selectedGenepacks");
        private static readonly FieldInfo AssemblerField = AccessTools.Field(typeof(Dialog_CreateXenogerm), "geneAssembler");
        private static bool loggedMissingField;
        private static readonly Texture2D RotateIcon = ContentFinder<Texture2D>.Get("GenePreview/Rotate");

        private static bool Available
        {
            get
            {
                if (SelectedPacksField != null && AssemblerField != null) return true;
                if (!loggedMissingField)
                {
                    Log.Error("[Gene Preview] RimWorld 1.7 assembly fields could not be found; the assembly preview is disabled.");
                    loggedMissingField = true;
                }
                return false;
            }
        }

        internal static void InitialSizePostfix(Dialog_CreateXenogerm __instance, ref Vector2 __result)
        {
            if (!Available) return;
            __result.x = Mathf.Min(__result.x, Mathf.Max(800f, UI.screenWidth - 32f));
            float extra = Mathf.Clamp(UI.screenWidth - 32f - __result.x, 0f, PanelWidth);
            // A cramped side panel hides most of the portrait; offer a pop-up instead.
            if (extra < 230f) extra = 0f;
            States.GetValue(__instance, _ => new AssemblyPreviewState()).ExtraWidth = extra;
            __result.x += extra;
        }

        internal static void PreOpenPrefix(Dialog_CreateXenogerm __instance)
        {
            if (!Available) return;
            AssemblyPreviewState state = States.GetValue(__instance, _ => new AssemblyPreviewState());
            foreach (Pawn pawn in Find.Selector.SelectedPawns)
            {
                state.PreferredPawn = pawn;
                break;
            }
        }

        internal static void PreOpenPostfix(Dialog_CreateXenogerm __instance)
        {
            if (!Available) return;
            AssemblyPreviewState state = States.GetValue(__instance, _ => new AssemblyPreviewState());
            try
            {
                Building_GeneAssembler assembler = AssemblerField.GetValue(__instance) as Building_GeneAssembler;
                state.Controller.Refresh(assembler?.MapHeld, first: true);
                if (state.PreferredPawn != null && state.PreferredPawn.MapHeld == assembler?.MapHeld)
                    state.Controller.SelectIfEligible(state.PreferredPawn);
            }
            catch (Exception ex)
            {
                state.Failed = true;
                Log.Error("[Gene Preview] Assembly preview setup failed; vanilla assembly remains available: " + ex);
            }
        }

        internal static void PostClosePrefix(Dialog_CreateXenogerm __instance)
        {
            if (States.TryGetValue(__instance, out AssemblyPreviewState state))
            {
                if (state.Popup?.IsOpen == true) state.Popup.Close(doCloseSound: false);
                state.Controller.Release();
                States.Remove(__instance);
            }
        }

        internal static void GenesChangedPostfix(GeneCreationDialogBase __instance)
        {
            if (__instance is Dialog_CreateXenogerm dialog && States.TryGetValue(dialog, out AssemblyPreviewState state) && !state.Failed)
            {
                try
                {
                    PopulateGenes(dialog, state.Genes);
                    state.Controller.Invalidate();
                }
                catch (Exception ex)
                {
                    state.Failed = true;
                    Log.Error("[Gene Preview] Assembly preview gene update failed: " + ex);
                }
            }
        }

        internal static void ContentsPrefix(GeneCreationDialogBase __instance, ref Rect rect, out Rect __state)
        {
            __state = rect;
            if (__instance is Dialog_CreateXenogerm dialog && States.TryGetValue(dialog, out AssemblyPreviewState state) && !state.Failed)
                rect.width -= state.ExtraWidth;
        }

        internal static void ContentsPostfix(GeneCreationDialogBase __instance, Rect __state)
        {
            if (!Available || __instance is not Dialog_CreateXenogerm dialog || !States.TryGetValue(dialog, out AssemblyPreviewState state) || state.Failed) return;
            try
            {
                if (Event.current.type == EventType.Repaint)
                    state.Controller.Refresh(null);
                if (state.ExtraWidth > 0f)
                {
                    Rect panel = new Rect(__state.xMax - state.ExtraWidth + 8f, __state.y + 40f, state.ExtraWidth - 8f, __state.height - 86f);
                    DrawPanel(panel, state.Controller, state.Genes);
                }
                else if (Widgets.ButtonText(new Rect(__state.x + 210f, __state.y + 7f, 100f, 28f), "GenePreview.Open".Translate()))
                {
                    if (state.Popup == null || !state.Popup.IsOpen)
                    {
                        state.Popup = new PreviewPopup(state, dialog);
                        Find.WindowStack.Add(state.Popup);
                    }
                }
            }
            catch (Exception ex)
            {
                state.Failed = true;
                state.Controller.Release();
                Log.Error("[Gene Preview] Assembly preview disabled for this dialog: " + ex);
            }
        }

        internal static void BottomButtonsPrefix(Dialog_CreateXenogerm __instance, ref Rect rect)
        {
            if (States.TryGetValue(__instance, out AssemblyPreviewState state) && !state.Failed)
                rect.width += state.ExtraWidth;
        }

        internal static void PopulateGenes(Dialog_CreateXenogerm dialog, List<GeneDef> result)
        {
            result.Clear();
            if (!Available) return;
            if (SelectedPacksField.GetValue(dialog) is List<Genepack> packs)
                foreach (Genepack pack in packs)
                    if (pack?.GeneSet != null)
                        result.AddRange(pack.GeneSet.GenesListForReading);
        }

        internal static void DrawPanel(Rect rect, PreviewController controller, List<GeneDef> genes)
        {
            Widgets.DrawMenuSection(rect);
            float x = rect.x + 12f;
            float width = rect.width - 24f;
            Widgets.Label(new Rect(x, rect.y + 10f, width, 26f), "GenePreview.Title".Translate());
            if (controller.SelectedPawn == null)
            {
                Widgets.Label(new Rect(x, rect.y + 55f, width, 70f), "GenePreview.NoColonists".Translate());
                return;
            }

            if (Event.current.type == EventType.Repaint)
                controller.Update(genes);
            Pawn projected = controller.ProjectedPawn;
            Rect portrait = new Rect(x, rect.y + 43f, width, Mathf.Min(width * 1.2f, rect.height - 195f));
            Widgets.DrawBoxSolid(portrait, new Color(0.12f, 0.13f, 0.15f));
            if (projected != null && Event.current.type == EventType.Repaint)
            {
                RenderTexture texture = PortraitsCache.Get(projected, new Vector2(256f, 320f), controller.Rotation,
                    renderHeadgear: false, renderClothes: false, healthStateOverride: PawnHealthState.Mobile);
                GUI.DrawTexture(portrait, texture, ScaleMode.ScaleToFit);
            }
            Widgets.DrawBox(portrait);

            float y = portrait.yMax + 6f;
            TextAnchor oldAnchor = Text.Anchor;
            try
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(x + 32f, y, width - 64f, 28f), controller.SelectedPawn.LabelCap);
            }
            finally { Text.Anchor = oldAnchor; }
            if (Widgets.ButtonText(new Rect(x, y, 30f, 28f), "<")) controller.Cycle(-1);
            if (Widgets.ButtonText(new Rect(x + width - 30f, y, 30f, 28f), ">")) controller.Cycle(1);
            TooltipHandler.TipRegion(new Rect(x, y, 30f, 28f), "GenePreview.PreviousTip".Translate());
            TooltipHandler.TipRegion(new Rect(x + width - 30f, y, 30f, 28f), "GenePreview.NextTip".Translate());
            y += 34f;
            if (Widgets.ButtonImage(new Rect(x, y, 30f, 30f), RotateIcon)) controller.Rotate();
            TooltipHandler.TipRegion(new Rect(x, y, 30f, 30f), "GenePreview.RotateTip".Translate());
            Widgets.Label(new Rect(x + 40f, y + 3f, width - 40f, 28f), genes.Count == 0 ? "GenePreview.Current".Translate() : "GenePreview.Projected".Translate());
            y += 37f;
            Widgets.Label(new Rect(x, y, width, 55f), "GenePreview.ClothingHidden".Translate());
        }
    }

    internal sealed class PreviewPopup : Window
    {
        private readonly AssemblyPreviewState state;
        private readonly Dialog_CreateXenogerm dialog;
        public override Vector2 InitialSize => new Vector2(340f, 510f);
        internal PreviewPopup(AssemblyPreviewState state, Dialog_CreateXenogerm dialog)
        {
            this.state = state;
            this.dialog = dialog;
            doCloseX = true;
        }
        public override void DoWindowContents(Rect inRect)
        {
            if (!dialog.IsOpen) { Close(doCloseSound: false); return; }
            if (Event.current.type == EventType.Repaint)
                state.Controller.Refresh(null);
            AssemblyPreviewPatches.DrawPanel(inRect, state.Controller, state.Genes);
        }
    }

    [HarmonyPatch(typeof(Dialog_CreateXenogerm), "get_InitialSize")]
    internal static class Patch_AssemblyInitialSize
    {
        [HarmonyPostfix]
        private static void Postfix(Dialog_CreateXenogerm __instance, ref Vector2 __result) => AssemblyPreviewPatches.InitialSizePostfix(__instance, ref __result);
    }

    [HarmonyPatch(typeof(GeneCreationDialogBase), "PreOpen")]
    internal static class Patch_AssemblyPreOpen
    {
        [HarmonyPrefix]
        private static void Prefix(GeneCreationDialogBase __instance)
        {
            if (__instance is Dialog_CreateXenogerm dialog)
                AssemblyPreviewPatches.PreOpenPrefix(dialog);
        }
        [HarmonyPostfix]
        private static void Postfix(GeneCreationDialogBase __instance)
        {
            if (__instance is Dialog_CreateXenogerm dialog)
                AssemblyPreviewPatches.PreOpenPostfix(dialog);
        }
    }

    [HarmonyPatch(typeof(Window), "PostClose")]
    internal static class Patch_AssemblyPostClose
    {
        [HarmonyPrefix]
        private static void Prefix(Window __instance)
        {
            if (__instance is Dialog_CreateXenogerm dialog)
                AssemblyPreviewPatches.PostClosePrefix(dialog);
        }
    }

    [HarmonyPatch(typeof(GeneCreationDialogBase), "OnGenesChanged")]
    internal static class Patch_AssemblyGenesChanged
    {
        [HarmonyPostfix]
        private static void Postfix(GeneCreationDialogBase __instance) => AssemblyPreviewPatches.GenesChangedPostfix(__instance);
    }

    [HarmonyPatch(typeof(GeneCreationDialogBase), "DoWindowContents")]
    internal static class Patch_AssemblyContents
    {
        [HarmonyPrefix]
        private static void Prefix(GeneCreationDialogBase __instance, ref Rect rect, out Rect __state) => AssemblyPreviewPatches.ContentsPrefix(__instance, ref rect, out __state);
        [HarmonyPostfix]
        private static void Postfix(GeneCreationDialogBase __instance, Rect __state) => AssemblyPreviewPatches.ContentsPostfix(__instance, __state);
    }

    [HarmonyPatch(typeof(Dialog_CreateXenogerm), "DoBottomButtons")]
    internal static class Patch_AssemblyBottomButtons
    {
        [HarmonyPrefix]
        private static void Prefix(Dialog_CreateXenogerm __instance, ref Rect rect) => AssemblyPreviewPatches.BottomButtonsPrefix(__instance, ref rect);
    }
}
