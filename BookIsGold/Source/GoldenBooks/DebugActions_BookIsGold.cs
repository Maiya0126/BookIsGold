using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;

namespace GoldenBooksMod
{
    // --- 开发者模式调试按钮（书灵物语） ---
    public static class DebugActions_BookIsGold
    {
        [DebugAction("书中自有黄金屋 (Golden Books)", "重置许愿冷却 (Reset Wish Cooldown)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DebugResetWishCooldown()
        {
            GameComponent_WhisperWish.ResetCooldown();
            Log.Message("[GoldenBooks] Debug: 许愿冷却已重置");
        }

        [DebugAction("书中自有黄金屋 (Golden Books)", "击杀书灵测试 (Kill Spirit Test)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DebugKillSpiritTest()
        {
            Map map = Find.CurrentMap;
            HediffDef vit = DefDatabase<HediffDef>.GetNamedSilentFail("GoldenBooks_SpiritVitality");
            if (vit == null) { Log.Error("[GoldenBooks] 缺少 GoldenBooks_SpiritVitality"); return; }
            Pawn target = map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => p.health?.hediffSet != null && p.health.hediffSet.HasHediff(vit));
            if (target == null) { Messages.Message("地图上没有书灵（请先用调试按钮生成一个）。", MessageTypeDefOf.RejectInput); return; }
            target.Kill(null, null);
            Log.Message("[GoldenBooks] Debug: 已击杀书灵 " + target.LabelShort + "，验证化书流程");
        }

        [DebugAction("书中自有黄金屋 (Golden Books)", "生成殖民地书灵 (Spawn Colony Book Spirit)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DebugSpawnColonistSpirit()
        {
            Map map = Find.CurrentMap;
            Pawn spirit = GoldenBooksUtils.SpawnBookSpirit(GoldenBooksUtils.SpiritColonist, null, 20f, null, "书灵·测试学者");
            if (spirit == null) { Log.Error("[GoldenBooks] 殖民地书灵生成失败"); return; }
            // 挪到鼠标处（统一入口默认落在地图空投点）
            IntVec3 cell = UI.MouseCell();
            if (cell.Standable(map)) spirit.Position = cell;
            Log.Message("[GoldenBooks] Debug: 已生成 殖民地书灵（种族同设置白名单）");
        }

        [DebugAction("书中自有黄金屋 (Golden Books)", "书灵·颜执中现身 (Spawn Yan Zhongzhong)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DebugSpawnExecutor()
        {
            Map map = Find.CurrentMap;
            Pawn pawn = Find.CurrentMap.mapPawns.FreeColonistsSpawned.FirstOrDefault();
            Pawn spirit = GoldenBooksUtils.SpawnBookSpirit(GoldenBooksUtils.SpiritYan, pawn, 67f, GameComponent_BookWhispers.PersonaYan());
            if (spirit == null) { Log.Error("[GoldenBooks] 颜执中生成失败"); return; }
            IntVec3 cell = UI.MouseCell();
            if (cell.Standable(map)) spirit.Position = cell;
            Log.Message("[GoldenBooks] Debug: 已生成 书灵·颜执中（种族同设置白名单）");
        }

        [DebugAction("书中自有黄金屋 (Golden Books)", "书灵·颜知夏乱入 (Spawn Yan Zhixia)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DebugSpawnZhixia()
        {
            Map map = Find.CurrentMap;
            Pawn pawn = Find.CurrentMap.mapPawns.FreeColonistsSpawned.FirstOrDefault();
            Pawn spirit = GoldenBooksUtils.SpawnBookSpirit(GoldenBooksUtils.SpiritMo, pawn, 19f, GameComponent_BookWhispers.PersonaMo());
            if (spirit == null) { Log.Error("[GoldenBooks] 颜知夏生成失败"); return; }
            IntVec3 cell = UI.MouseCell();
            if (cell.Standable(map)) spirit.Position = cell;
            Log.Message("[GoldenBooks] Debug: 已生成 书灵·颜知夏（种族同设置白名单）");
        }

        [DebugAction("书中自有黄金屋 (Golden Books)", "获得颜氏手札 (Get Handbook)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DebugGetHandbook()
        {
            Map map = Find.CurrentMap;
            ThingDef handbook = DefDatabase<ThingDef>.GetNamedSilentFail("GoldenBooks_Handbook");
            if (handbook == null) { Log.Error("[GoldenBooks] 缺少 GoldenBooks_Handbook"); return; }
            Thing h = ThingMaker.MakeThing(handbook);
            h.stackCount = 1;
            IntVec3 cell = UI.MouseCell();
            if (!cell.Standable(map)) cell = DropCellFinder.TradeDropSpot(map);
            GenSpawn.Spawn(h, cell, map);
            Log.Message("[GoldenBooks] Debug: 手札已生成");
        }

        [DebugAction("书中自有黄金屋 (Golden Books)", "触发事件：落难书箱 (Lost Book Box)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DebugBookBox()
        {
            Map map = Find.CurrentMap;
            List<Thing> drops = new List<Thing>();
            List<ThingDef> pool = StoryChain_GameComponent.PlayableBookDefs();
            int n = Rand.RangeInclusive(3, 8);
            for (int i = 0; i < n; i++)
            {
                if (pool.Count == 0) break;
                Thing book = ThingMaker.MakeThing(pool.RandomElement());
                book.stackCount = 1;
                drops.Add(book);
            }
            IntVec3 cell = DropCellFinder.TradeDropSpot(map);
            DropPodUtility.DropThingsNear(cell, map, drops);
            Find.LetterStack.ReceiveLetter("GoldenBooks_BookBoxLabel".Translate(),
                "GoldenBooks_BookBoxText".Translate(), LetterDefOf.PositiveEvent, new TargetInfo(cell, map));
        }

        [DebugAction("书中自有黄金屋 (Golden Books)", "触发事件：书蠹出没 (Bookworm Swarm)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DebugWormSwarm()
        {
            Map map = Find.CurrentMap;
            IntVec3 cell = CellFinder.RandomSpawnCellForPawnNear(map.Center, map, 10);
            for (int i = 0; i < 3; i++)
            {
                Pawn worm = PawnGenerator.GeneratePawn(PawnKindDefOf.Megascarab, Faction.OfInsects);
                GenSpawn.Spawn(worm, CellFinder.RandomSpawnCellForPawnNear(cell, map, 3), map);
                worm.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Manhunter);
            }
            Find.LetterStack.ReceiveLetter("GoldenBooks_WormSwarmLabel".Translate(),
                "GoldenBooks_WormSwarmText".Translate(), LetterDefOf.ThreatSmall, new TargetInfo(cell, map));
        }

        [DebugAction("书中自有黄金屋 (Golden Books)", "立即生成一篇书语 (Write a Whisper Now)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DebugWhisperNow()
        {
            GameComponent_BookWhispers comp = GameComponent_BookWhispers.Get;
            if (comp == null) return;
            bool isYan = Rand.Bool;
            int day = Find.TickManager.TicksGame / 60000;
            string text = isYan
                ? "（试笔）是岁，灯下无事，先记一笔。颜氏记之。——执中"
                : "（试笔）【殖民地日报·试刊号】大家好呀！！这里还什么都没发生，但本姑娘已经就位了！(๑•̀ㅂ•́)و✧——颜知夏";
            comp.entries.Add(new WhisperEntry { day = day, author = isYan ? "Yan" : "Mo", text = text });
            Find.LetterStack.ReceiveLetter(
                (isYan ? "GoldenBooks_WspLetterLabel_Yan" : "GoldenBooks_WspLetterLabel_Mo").Translate(day),
                text, LetterDefOf.NeutralEvent);
        }

        [DebugAction("书中自有黄金屋 (Golden Books)", "注入RimTalk人格 (Inject RimTalk Personas)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DebugInjectPersonas()
        {
            GameComponent_BookWhispers.TryInjectRimTalkPersonas();
            Log.Message("[GoldenBooks] Debug: RimTalk 人格注入已执行（详见上方日志）");
        }

        [DebugAction("书中自有黄金屋 (Golden Books)", "重置剧情链状态 (Reset Story Chain)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DebugResetStoryChain()
        {
            StoryChain_GameComponent s = StoryChain_GameComponent.Get;
            if (s == null) return;
            s.lecternLetterSent = false;
            s.executorArrived = false;
            s.yanRuYuDeathSent = false;
            s.zhixiaArrived = false;
            s.dismantleCount = 0;
            s.executorArrivalTick = -1;
            Log.Message("[GoldenBooks] Debug: 剧情链状态已重置");
        }

        [DebugAction("书中自有黄金屋 (Golden Books)", "召唤真·颜如玉 (Summon True Yan Ru Yu)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DebugSummonTrueSpirit()
        {
            Map map = Find.CurrentMap;
            Pawn spirit = GoldenBooksUtils.GenerateYanRuYu(true);
            if (spirit == null) return;
            GoldenBooksUtils.DressUpYanRuYu(spirit);
            GoldenBooksUtils.EnsureBeautifulHair(spirit);
            IntVec3 cell = UI.MouseCell();
            if (!cell.Standable(map)) cell = CellFinder.RandomSpawnCellForPawnNear(UI.MouseCell(), map, 5);
            GenSpawn.Spawn(spirit, cell, map);
            GameComponent_BookWhispers.TryInjectRimTalkPersonas();
            Find.LetterStack.ReceiveLetter("GoldenBooks_TrueSpiritLabel".Translate(),
                "GoldenBooks_TrueSpiritText".Translate(spirit.Name.ToStringShort, spirit.def.LabelCap),
                LetterDefOf.PositiveEvent, spirit);
        }
    }
}
