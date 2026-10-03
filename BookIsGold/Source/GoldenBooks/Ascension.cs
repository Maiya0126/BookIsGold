using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace GoldenBooksMod
{
    // --- 化灵之仪：高智识殖民者化灵为书灵（课业大成后解锁） ---

    public class GoldenBooksCompProperties_SpiritBookData : CompProperties
    {
        public GoldenBooksCompProperties_SpiritBookData() { this.compClass = typeof(GoldenBooksComp_SpiritBookData); }
    }

    // 书灵之书数据：保存化灵书灵的名字与 RimTalk 人格（轻量，序列化 pawn 不做）
    public class GoldenBooksComp_SpiritBookData : ThingComp
    {
        public string spiritName = "";
        public string persona = "";

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref spiritName, "spiritName", "");
            Scribe_Values.Look(ref persona, "persona", "");
        }
    }

    public static class SpiritAscension
    {
        public static int AscendedCount()
        {
            int n = 0;
            if (Current.Game == null) return 0;
            foreach (Map m in Find.Maps)
                n += m.mapPawns.AllPawnsSpawned.Count(p => p.def.defName == "GoldenBooks_BookSpirit_Colonist");
            return n;
        }

        public static string AscendBlockReason(Pawn p)
        {
            var s = GoldenBooksMod.settings;
            if (GameComponent_Kewei.Get == null || GameComponent_Kewei.Get.mainStage < 6)
                return "GoldenBooks_AscReasonKewei".Translate();
            SkillRecord skill = p.skills?.GetSkill(SkillDefOf.Intellectual);
            if (skill == null || skill.TotallyDisabled || skill.Level < s.spiritAscendIntellect)
                return "GoldenBooks_AscReasonIntellect".Translate(s.spiritAscendIntellect);
            if (AscendedCount() >= s.colonistSpiritLimit)
                return "GoldenBooks_AscReasonLimit".Translate(s.colonistSpiritLimit);
            if (!HasMaterial(p.MapHeld)) return "GoldenBooks_AscReasonMaterials".Translate(s.spiritAscendFragments, s.spiritAscendClassics);
            return null;
        }

        private static bool HasMaterial(Map map)
        {
            var s = GoldenBooksMod.settings;
            if (map == null) return false;
            int frag = 0, classics = 0;
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableAlways))
            {
                if (t.def.defName == "GoldenBooks_BookFragment") frag += t.stackCount;
                else if (t.def.defName == "GoldenBooks_FiveClassics") classics += t.stackCount;
            }
            return frag >= s.spiritAscendFragments && classics >= s.spiritAscendClassics;
        }

        private static void ConsumeMaterials(Map map)
        {
            var s = GoldenBooksMod.settings;
            ConsumeDef(map, "GoldenBooks_BookFragment", s.spiritAscendFragments);
            ConsumeDef(map, "GoldenBooks_FiveClassics", s.spiritAscendClassics);
        }

        private static void ConsumeDef(Map map, string defName, int need)
        {
            var s = GoldenBooksMod.settings;
            int left = need;
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableAlways).ToList())
            {
                if (left <= 0) break;
                if (t.def.defName != defName) continue;
                int take = Math.Min(left, t.stackCount);
                t.SplitOff(take).Destroy();
                left -= take;
            }
        }

        public static void DoAscension(Pawn p)
        {
            Map map = p.MapHeld;
            IntVec3 pos = p.PositionHeld;
            string reason = AscendBlockReason(p);
            if (reason != null) { Messages.Message("无法化灵：" + reason, MessageTypeDefOf.RejectInput); return; }
            if (map == null) return;

            ConsumeMaterials(map);

            string oldPersona = WhisperPersonaHelper.ExtractPersonaFrom(p);
            string oldShort = p.LabelShort;
            Name oldName = p.Name;

            // 人格延续：原人格 + 书灵状态说明
            string suffix = GameComponent_BookWhispers.IsChinese
                ? "你已完成化灵之仪，从人化为了书灵——浅金册页中的学者。你不再需要饮食与睡眠，以读书人的心念为生。"
                : "You have completed the Rite of Ascension — you are now a book spirit, a scholar within pale-golden pages. You no longer eat or sleep; readers' devotion sustains you.";
            p.Destroy();

            Pawn spirit = GoldenBooksUtils.SpawnBookSpirit("GoldenBooks_BookSpirit_Colonist_Kind", null);
            if (spirit == null)
            {
                if (map != null)
                {
                    Pawn gen = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                        DefDatabase<PawnKindDef>.GetNamedSilentFail("GoldenBooks_BookSpirit_Colonist_Kind"),
                        Faction.OfPlayer, forceGenerateNewPawn: true));
                    GenSpawn.Spawn(gen, pos.IsValid ? pos : DropCellFinder.TradeDropSpot(map), map);
                    spirit = gen;
                }
            }
            if (spirit == null) return;

            if (oldName != null) spirit.Name = oldName;
            if (map != null && spirit.Spawned)
            {
                IntVec3 near = CellFinder.RandomSpawnCellForPawnNear(pos.IsValid ? pos : spirit.Position, map, 3);
                spirit.Position = near;
            }
            string basePersona = string.IsNullOrEmpty(oldPersona)
                ? WhisperPersonaHelper.PersonaColonist(spirit.LabelShort)
                : oldPersona;
            WhisperPersonaHelper.CopyPersonaToPawn(spirit, basePersona + "\n" + suffix);

            Find.LetterStack.ReceiveLetter("GoldenBooks_AscendLabel".Translate(),
                "GoldenBooks_AscendText".Translate(oldShort, spirit.LabelShort), LetterDefOf.PositiveEvent, spirit);
        }

        private static string ExtractPersona(Pawn p)
        {
            try
            {
                HediffDef pd = DefDatabase<HediffDef>.GetNamedSilentFail("RimTalk_PersonaData");
                if (pd == null || p?.health?.hediffSet == null) return null;
                Hediff h = p.health.hediffSet.GetFirstHediffOfDef(pd);
                if (h == null) return null;
                var f = h.GetType().GetField("Personality", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                return f?.GetValue(h) as string;
            }
            catch { return null; }
        }
    }

    // 选中殖民者时显示化灵 Gizmo
    [HarmonyPatch(typeof(Pawn), "GetGizmos")]
    public static class Patch_PawnGizmos_SpiritAscension
    {
        static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            try
            {
                if (__instance == null || !__instance.Spawned) return;
                if (!__instance.RaceProps.Humanlike || __instance.Faction != Faction.OfPlayer) return;
                if (__instance.IsPrisoner || __instance.IsSlave) return;
                if (__instance.def.defName == "GoldenBooks_BookSpirit_Yanzhongzhong" ||
                    __instance.def.defName == "GoldenBooks_BookSpirit_Zhixia" ||
                    __instance.def.defName == "GoldenBooks_BookSpirit_Colonist") return;
                if (Find.TickManager.TicksGame % 2 == 1) return; // 降低开销

                var s = GoldenBooksMod.settings;
                string reason = SpiritAscension.AscendBlockReason(__instance);
                // 显示策略：智识达标才显示；书灵满员时仍显示（置灰）提示上限存在
                SkillRecord iSkill = __instance.skills?.GetSkill(SkillDefOf.Intellectual);
                bool intellectOk = iSkill != null && !iSkill.TotallyDisabled && iSkill.Level >= s.spiritAscendIntellect;
                if (!intellectOk) return;
                Command_Action g = new Command_Action
                {
                    defaultLabel = "GoldenBooks_AscGizmoLabel".Translate(),
                    defaultDesc = string.IsNullOrEmpty(reason)
                        ? "GoldenBooks_AscGizmoDesc".Translate(s.spiritAscendFragments, s.spiritAscendClassics)
                        : "GoldenBooks_AscGizmoBlocked".Translate(reason),
                    icon = Icon(),
                    action = delegate
                    {
                        string r2 = SpiritAscension.AscendBlockReason(__instance);
                        if (r2 != null) { Messages.Message("无法化灵：" + r2, MessageTypeDefOf.RejectInput); return; }
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                            "化灵之仪（不可逆）\n\n" + __instance.LabelShort + " 将失去人形，化为书灵——不食不眠，以心念为生，死亡后化作书灵之书可召还。\n\n确定进行吗？",
                            delegate { SpiritAscension.DoAscension(__instance); },
                            destructive: false));
                    }
                };
                if (reason != null) { g.Disabled = true; g.disabledReason = reason; }
                __result = __result.Concat(new Gizmo[] { g });
            }
            catch { }
        }

        private static Texture2D _icon;
        private static Texture2D Icon()
        {
            if (_icon == null)
                _icon = ContentFinder<Texture2D>.Get("UI/Buttons/OpenCodex", false)
                     ?? ContentFinder<Texture2D>.Get("UI/Commands/DesirePower", false);
            return _icon;
        }
    }
}
