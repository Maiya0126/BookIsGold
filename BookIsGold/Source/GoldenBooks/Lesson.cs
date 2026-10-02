using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace GoldenBooksMod
{
    // --- 阶段六：颜氏课业（成长任务线） ---

    public class GameComponent_Kewei : GameComponent
    {
        public static GameComponent_Kewei Get => Current.Game?.GetComponent<GameComponent_Kewei>();

        public bool started;          // 执中入驻后开启
        public bool beginLetterSent;

        // 主线计数
        public int booksRead, milletCrafts, wealthCrafts, raids, classicsCrafted;
        public bool mortalSummoned;
        // 支线计数
        public int milletGrains, caravans, weddings;

        public int mainStage = 0;     // 已完成的主线课业数 0..6
        public bool sideGrains, sideBedrooms, sideCaravans, sideWeddings;
        public bool auraUpgraded;
        public bool allDone;

        public GameComponent_Kewei(Game game) { }

        public static void Ensure()
        {
            Game game = Current.Game;
            if (game == null || game.components == null) return;
            if (game.GetComponent<GameComponent_Kewei>() == null)
                game.components.Add(new GameComponent_Kewei(game));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref started, "started", false);
            Scribe_Values.Look(ref beginLetterSent, "beginLetterSent", false);
            Scribe_Values.Look(ref booksRead, "booksRead", 0);
            Scribe_Values.Look(ref milletCrafts, "milletCrafts", 0);
            Scribe_Values.Look(ref wealthCrafts, "wealthCrafts", 0);
            Scribe_Values.Look(ref raids, "raids", 0);
            Scribe_Values.Look(ref classicsCrafted, "classicsCrafted", 0);
            Scribe_Values.Look(ref mortalSummoned, "mortalSummoned", false);
            Scribe_Values.Look(ref milletGrains, "milletGrains", 0);
            Scribe_Values.Look(ref caravans, "caravans", 0);
            Scribe_Values.Look(ref weddings, "weddings", 0);
            Scribe_Values.Look(ref mainStage, "mainStage", 0);
            Scribe_Values.Look(ref sideGrains, "sideGrains", false);
            Scribe_Values.Look(ref sideBedrooms, "sideBedrooms", false);
            Scribe_Values.Look(ref sideCaravans, "sideCaravans", false);
            Scribe_Values.Look(ref sideWeddings, "sideWeddings", false);
            Scribe_Values.Look(ref auraUpgraded, "auraUpgraded", false);
            Scribe_Values.Look(ref allDone, "allDone", false);
        }

        // 执中入驻 → 开课（发布置信）
        public void Begin()
        {
            if (!GoldenBooksMod.settings.storyChainEnabled || started) return;
            started = true;
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            if (!started || !GoldenBooksMod.settings.storyChainEnabled) return;
            if (Find.TickManager.TicksGame % 2000 != 0) return;

            // 开课信延迟一日送达
            if (!beginLetterSent)
            {
                beginLetterSent = true;
                Find.LetterStack.ReceiveLetter("GoldenBooks_KwBeginLabel".Translate(),
                    "GoldenBooks_KwBeginText".Translate(), LetterDefOf.NeutralEvent);
            }

            CheckMain();
            CheckSides();
        }

        // --- 记录 ---
        public void RecordBookRead() { if (started) { booksRead++; CheckMain(); } }
        public void RecordMillet(int grains) { if (started) { milletCrafts++; milletGrains += grains; CheckMain(); CheckSides(); } }
        public void RecordWealth() { if (started) { wealthCrafts++; CheckMain(); } }
        public void RecordRaid() { if (started) { raids++; CheckMain(); } }
        public void RecordMortal() { if (started) { mortalSummoned = true; CheckMain(); } }
        public void RecordClassic() { if (started) { classicsCrafted++; CheckMain(); } }
        public void RecordCaravan() { if (started) { caravans++; CheckSides(); } }
        public void RecordWedding() { if (started) { weddings++; CheckSides(); } }

        // --- 主线 ---
        private void CheckMain()
        {
            var s = GoldenBooksMod.settings;
            bool advanced = false;
            while (mainStage < 6)
            {
                bool done =
                    mainStage == 0 ? booksRead >= s.keweiBooks :
                    mainStage == 1 ? milletCrafts >= s.keweiMillet :
                    mainStage == 2 ? wealthCrafts >= s.keweiWealth :
                    mainStage == 3 ? raids >= s.keweiRaids :
                    mainStage == 4 ? MortalGoalDone() :
                    classicsCrafted >= s.keweiClassics;
                if (!done) break;
                mainStage++;
                GiveMainReward(mainStage);
                advanced = true;
            }

            if (mainStage >= 6 && !allDone && SideAllDone())
            {
                allDone = true;
                GiveFinalReward();
            }
            if (advanced) RefreshAuras();
        }

        private bool MortalGoalDone()
        {
            if (GoldenBooksMod.settings.yanRuYuChance <= 0f)
                return wealthCrafts >= 8; // 概率为0时替换目标
            return mortalSummoned;
        }

        private bool SideAllDone() => sideGrains && sideBedrooms && sideCaravans && sideWeddings;

        private void CheckSides()
        {
            var s = GoldenBooksMod.settings;
            if (!sideGrains && milletGrains >= s.keweiGrains) { sideGrains = true; DeliverGift(SideRewardGrains()); SideLetter("GoldenBooks_KwSide1".Translate()); }
            if (!sideBedrooms && CountImpressiveBedrooms() >= s.keweiBedrooms) { sideBedrooms = true; DeliverGift(SideRewardBedrooms()); SideLetter("GoldenBooks_KwSide2".Translate()); }
            if (!sideCaravans && caravans >= s.keweiCaravans) { sideCaravans = true; DeliverGift(SideRewardCaravans()); SideLetter("GoldenBooks_KwSide3".Translate()); }
            if (!sideWeddings && weddings >= s.keweiWeddings) { sideWeddings = true; DeliverGift(SideRewardWeddings()); SideLetter("GoldenBooks_KwSide4".Translate()); }

            if (mainStage >= 6 && !allDone && SideAllDone())
            {
                allDone = true;
                GiveFinalReward();
            }
        }

        // --- 奖励 ---
        private List<ThingDefCountClass> MainReward(int stage)
        {
            var list = new List<ThingDefCountClass>();
            switch (stage)
            {
                case 1:
                    list.Add(new ThingDefCountClass(ThingDef.Named("GoldenBooks_GoldenMillet"), 30));
                    UnlockHandbookRecipe();
                    break;
                case 2:
                    list.Add(new ThingDefCountClass(ThingDefOf.Gold, 30));
                    list.Add(new ThingDefCountClass(ThingDefOf.Jade, 30));
                    break;
                case 3:
                    list.Add(new ThingDefCountClass(ThingDef.Named("GoldenBooks_GodSpeedTalisman"), 2));
                    break;
                case 4:
                    list.Add(new ThingDefCountClass(ThingDef.Named("GoldenBooks_BookFragment"), 10));
                    break;
                case 5:
                    list.Add(new ThingDefCountClass(ThingDef.Named("GoldenBooks_FiveClassics"), 1));
                    list.Add(new ThingDefCountClass(ThingDef.Named("GoldenBooks_BookFragment"), 5));
                    break;
                case 6:
                    list.Add(new ThingDefCountClass(ThingDef.Named("GoldenBooks_GoldenMillet"), 200));
                    list.Add(new ThingDefCountClass(ThingDefOf.Gold, 50));
                    list.Add(new ThingDefCountClass(ThingDefOf.Jade, 50));
                    list.Add(new ThingDefCountClass(ThingDef.Named("GoldenBooks_GodSpeedTalisman"), 3));
                    break;
            }
            return list;
        }

        private List<ThingDefCountClass> SideRewardGrains()
        {
            return new List<ThingDefCountClass> {
                new ThingDefCountClass(ThingDefOf.Gold, 20),
                new ThingDefCountClass(ThingDefOf.Jade, 20) };
        }

        private List<ThingDefCountClass> SideRewardBedrooms()
        {
            return new List<ThingDefCountClass> {
                new ThingDefCountClass(ThingDefOf.Gold, 30),
                new ThingDefCountClass(ThingDefOf.WoodLog, 100) };
        }

        private List<ThingDefCountClass> SideRewardCaravans()
        {
            return new List<ThingDefCountClass> {
                new ThingDefCountClass(ThingDef.Named("GoldenBooks_BookFragment"), 5),
                new ThingDefCountClass(ThingDef.Named("GoldenBooks_GodSpeedTalisman"), 1) };
        }

        private List<ThingDefCountClass> SideRewardWeddings()
        {
            return new List<ThingDefCountClass> {
                new ThingDefCountClass(ThingDefOf.Gold, 20),
                new ThingDefCountClass(ThingDef.Named("GoldenBooks_GoldenMillet"), 50) };
        }

        private void GiveMainReward(int stage)
        {
            List<ThingDefCountClass> rewards = MainReward(stage);
            DeliverGift(rewards);
            string stageName = StageName(stage);
            string rewardDesc = DescribeRewards(rewards);
            Find.LetterStack.ReceiveLetter(
                "GoldenBooks_KwDoneLabel".Translate(stageName),
                "GoldenBooks_KwDoneText".Translate(stageName, rewardDesc),
                LetterDefOf.PositiveEvent);

            if (stage == 6) auraUpgraded = true;
        }

        private void SideLetter(TaggedString stageName)
        {
            Find.LetterStack.ReceiveLetter(
                "GoldenBooks_KwDoneLabel".Translate(stageName),
                "GoldenBooks_KwSideDoneText".Translate(stageName),
                LetterDefOf.PositiveEvent);
        }

        private void GiveFinalReward()
        {
            RefreshAuras();
            Find.LetterStack.ReceiveLetter("GoldenBooks_KwFinalLabel".Translate(),
                "GoldenBooks_KwFinalText".Translate(), LetterDefOf.PositiveEvent);
        }

        private void DeliverGift(List<ThingDefCountClass> rewards)
        {
            Map map = Find.AnyPlayerHomeMap;
            if (map == null || rewards == null) return;
            List<Thing> things = new List<Thing>();
            foreach (ThingDefCountClass r in rewards)
            {
                if (r.thingDef == null || r.count <= 0) continue;
                int remaining = r.count;
                while (remaining > 0)
                {
                    int take = Math.Min(remaining, r.thingDef.stackLimit > 0 ? r.thingDef.stackLimit : 1);
                    Thing piece = ThingMaker.MakeThing(r.thingDef);
                    piece.stackCount = take;
                    things.Add(piece);
                    remaining -= take;
                }
            }
            if (things.Count > 0)
                DropPodUtility.DropThingsNear(DropCellFinder.TradeDropSpot(map), map, things);
        }

        private static string DescribeRewards(List<ThingDefCountClass> rewards)
        {
            if (rewards == null || rewards.Count == 0) return "";
            return string.Join("、", rewards.Where(r => r.thingDef != null).Select(r => r.thingDef.LabelCap + "×" + r.count).ToArray());
        }

        private static string StageName(int stage)
        {
            bool zh = GameComponent_BookWhispers.IsChinese;
            switch (stage)
            {
                case 1: return zh ? "识字·开蒙" : "First Steps";
                case 2: return zh ? "千钟粟" : "Bushels of Millet";
                case 3: return zh ? "黄金屋" : "House of Gold";
                case 4: return zh ? "车马多如簇" : "Chariots and Steeds";
                case 5: return zh ? "颜如玉" : "Beauty as Jade";
                case 6: return zh ? "五经勤读·大成" : "Five Classics, Mastery";
                default: return "?";
            }
        }

        public static string StageNameCurrent(int stageIndex)
        {
            bool zh = GameComponent_BookWhispers.IsChinese;
            switch (stageIndex)
            {
                case 0: return zh ? "识字·开蒙（研读书籍）" : "First Steps (read books)";
                case 1: return zh ? "千钟粟（成功产出千钟粟）" : "Bushels of Millet (craft millet)";
                case 2: return zh ? "黄金屋（读出金玉）" : "House of Gold (extract gold/jade)";
                case 3: return zh ? "车马多如簇（击退袭击）" : "Chariots and Steeds (repel raids)";
                case 4: return zh ? "颜如玉（迎来凡人·颜如玉）" : "Beauty as Jade (welcome a mortal Yan Ru Yu)";
                case 5: return zh ? "五经勤读·大成（编纂五经）" : "Five Classics (compile classics)";
                default: return zh ? "六课皆成" : "All lessons complete";
            }
        }

        // 主线当前进度 (current, target)
        public Vector2Int MainProgressNow()
        {
            var s = GoldenBooksMod.settings;
            switch (mainStage)
            {
                case 0: return new Vector2Int(Mathf.Min(booksRead, s.keweiBooks), s.keweiBooks);
                case 1: return new Vector2Int(Mathf.Min(milletCrafts, s.keweiMillet), s.keweiMillet);
                case 2: return new Vector2Int(Mathf.Min(wealthCrafts, s.keweiWealth), s.keweiWealth);
                case 3: return new Vector2Int(Mathf.Min(raids, s.keweiRaids), s.keweiRaids);
                case 4: return new Vector2Int(MortalGoalDone() ? 1 : 0, 1);
                case 5: return new Vector2Int(Mathf.Min(classicsCrafted, s.keweiClassics), s.keweiClassics);
                default: return new Vector2Int(6, 6);
            }
        }

        // --- 手札配方解锁 ---
        private void UnlockHandbookRecipe()
        {
            try
            {
                ThingDef lectern = DefDatabase<ThingDef>.GetNamedSilentFail("GoldenBooks_ArcaneLectern");
                RecipeDef recipe = DefDatabase<RecipeDef>.GetNamedSilentFail("GoldenBooks_Bind_Handbook");
                if (lectern == null || recipe == null) return;
                if (lectern.recipes == null) lectern.recipes = new List<RecipeDef>();
                if (!lectern.recipes.Contains(recipe)) lectern.recipes.Add(recipe);
                // 清缓存，使台子账单立即出现新配方
                var field = typeof(ThingDef).GetField("allRecipesCached", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (field != null) field.SetValue(lectern, null);
            }
            catch (Exception ex)
            {
                Log.Warning("[GoldenBooks] 手札配方解锁失败: " + ex.Message);
            }
        }

        // --- 光环升级 ---
        public void RefreshAuras()
        {
            foreach (Map m in Find.Maps)
            {
                foreach (Pawn p in m.mapPawns.AllPawnsSpawned)
                {
                    if (p.Dead || p.health == null) continue;
                    Hediff h = p.health.hediffSet.GetFirstHediffOfDef(HediffDef.Named("GoldenBooks_LectureAura"));
                    if (h != null) h.Severity = auraUpgraded ? 1.01f : 1f;
                    h = p.health.hediffSet.GetFirstHediffOfDef(HediffDef.Named("GoldenBooks_NoiseStudyAura"));
                    if (h != null) h.Severity = auraUpgraded ? 1.01f : 1f;
                }
            }
        }

        // --- 卧室统计 ---
        public static int CountImpressiveBedrooms()
        {
            int count = 0;
            foreach (Map m in Find.Maps)
            {
                foreach (Pawn p in m.mapPawns.FreeColonistsSpawned)
                {
                    Room room = p.ownership?.OwnedRoom;
                    if (room == null) continue;
                    if (room.Role != RoomRoleDefOf.Bedroom && room.Role != RoomRoleDefOf.PrisonCell) continue;
                    if (room.GetStat(RoomStatDefOf.Impressiveness) >= GoldenBooksMod.settings.keweiBedImpressive) count++;
                }
            }
            return count;
        }

        // 供 Comp_BookSpirit 光环新贴身对象读取
        public static float AuraSeverity() => Get?.auraUpgraded == true ? 1.01f : 1f;
    }

    // --- 钩子 ---
    // 1.6 中 ExitMapAndCreateCaravan 有两个重载，必须显式指定参数类型，否则 Harmony 报 AmbiguousMatchException
    [HarmonyPatch(typeof(CaravanExitMapUtility), nameof(CaravanExitMapUtility.ExitMapAndCreateCaravan),
        new[] { typeof(IEnumerable<Pawn>), typeof(Faction), typeof(PlanetTile), typeof(PlanetTile), typeof(PlanetTile), typeof(bool) })]
    public static class Patch_CaravanFormed
    {
        static void Postfix(IEnumerable<Pawn> pawns)
        {
            try
            {
                if (pawns != null && pawns.Any(t => t.Faction == Faction.OfPlayer))
                    GameComponent_Kewei.Get?.RecordCaravan();
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(MarriageCeremonyUtility), "Married")]
    public static class Patch_Wedding
    {
        static void Postfix(Pawn firstPawn, Pawn secondPawn)
        {
            try
            {
                if (firstPawn?.Faction == Faction.OfPlayer || secondPawn?.Faction == Faction.OfPlayer)
                    GameComponent_Kewei.Get?.RecordWedding();
            }
            catch { }
        }
    }
}
