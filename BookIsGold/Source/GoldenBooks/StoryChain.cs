using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace GoldenBooksMod
{
    // --- 剧情事件链状态（书灵纪）---
    public class StoryChain_GameComponent : GameComponent
    {
        public static StoryChain_GameComponent Get => Current.Game?.GetComponent<StoryChain_GameComponent>();

        public bool lecternLetterSent;      // 书页微光
        public bool executorArrived;        // 颜执中已现身
        public bool yanRuYuDeathSent;       // 手书已送达（解锁书蠹出没）
        public bool zhixiaArrived;          // 颜知夏已乱入
        public int dismantleCount;          // 拆书/书蠹累计
        public int executorArrivalTick = -1;

        public StoryChain_GameComponent(Game game) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref lecternLetterSent, "lecternLetterSent", false);
            Scribe_Values.Look(ref executorArrived, "executorArrived", false);
            Scribe_Values.Look(ref yanRuYuDeathSent, "yanRuYuDeathSent", false);
            Scribe_Values.Look(ref zhixiaArrived, "zhixiaArrived", false);
            Scribe_Values.Look(ref dismantleCount, "dismantleCount", 0);
            Scribe_Values.Look(ref executorArrivalTick, "executorArrivalTick", -1);
        }

        public static void Ensure()
        {
            Game game = Current.Game;
            if (game == null || game.components == null) return;
            if (game.GetComponent<StoryChain_GameComponent>() == null)
                game.components.Add(new StoryChain_GameComponent(game));
        }

        public override void GameComponentTick()
        {
            if (!GoldenBooksMod.settings.storyChainEnabled) return;
            if (Find.TickManager.TicksGame % 2000 != 0) return;

            // 兜底通道：执中入驻后超过设定天数，知夏必到
            if (executorArrived && !zhixiaArrived && executorArrivalTick >= 0)
            {
                float days = (Find.TickManager.TicksGame - executorArrivalTick) / 60000f;
                if (days >= GoldenBooksMod.settings.zhixiaFallbackDays) ArriveZhixia(true);
            }
        }

        // 事件2：劝学台首建 →「书页微光」
        public void OnLecternBuilt(Thing lectern)
        {
            if (!GoldenBooksMod.settings.storyChainEnabled || lecternLetterSent || executorArrived) return;
            lecternLetterSent = true;
            Find.LetterStack.ReceiveLetter("GoldenBooks_PageGlowLabel".Translate(),
                "GoldenBooks_PageGlowText".Translate(), LetterDefOf.NeutralEvent,
                new TargetInfo(lectern.Position, lectern.Map));
        }

        // 事件3：首次诵读 → 颜执中现身
        public void OnFirstSummon(Pawn trueSpirit)
        {
            if (!GoldenBooksMod.settings.storyChainEnabled || executorArrived) return;
            executorArrived = true;
            executorArrivalTick = Find.TickManager.TicksGame;

            Thing executor = TrySpawnExecutor(trueSpirit);
            if (executor != null)
            {
                Find.LetterStack.ReceiveLetter("GoldenBooks_ExecutorLabel".Translate(),
                    "GoldenBooks_ExecutorText".Translate(), LetterDefOf.PositiveEvent, executor);
            }
            else
            {
                Find.LetterStack.ReceiveLetter("GoldenBooks_ExecutorLabel".Translate(),
                    "GoldenBooks_ExecutorNoDefText".Translate(), LetterDefOf.PositiveEvent,
                    new TargetInfo(trueSpirit.Position, trueSpirit.Map));
            }
        }

        private Thing TrySpawnExecutor(Pawn near)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("GoldenBooks_BookSpirit_Yanzhongzhong");
            if (def == null || near == null || near.Map == null) return null;
            Thing spirit = ThingMaker.MakeThing(def);
            if (spirit is Pawn) return null;
            IntVec3 cell = CellFinder.RandomSpawnCellForPawnNear(near.Position, near.Map, 4);
            GenSpawn.Spawn(spirit, cell, near.Map);
            return spirit;
        }

        // 事件4：真·颜如玉死亡化书 →「颜执中的手书」+ 解锁书蠹出没
        public void OnYanRuYuDeath(Pawn pawn)
        {
            if (!GoldenBooksMod.settings.storyChainEnabled || yanRuYuDeathSent) return;
            yanRuYuDeathSent = true;
            if (pawn != null && pawn.MapHeld != null)
            {
                Find.LetterStack.ReceiveLetter("GoldenBooks_HandNoteLabel".Translate(),
                    "GoldenBooks_HandNoteText".Translate(), LetterDefOf.NeutralEvent,
                    new TargetInfo(pawn.PositionHeld, pawn.MapHeld));
            }
            else
            {
                Find.LetterStack.ReceiveLetter("GoldenBooks_HandNoteLabel".Translate(),
                    "GoldenBooks_HandNoteText".Translate(), LetterDefOf.NeutralEvent);
            }
        }

        // 知夏入场：主通道（阈值）+ 兜底（天数）
        public void RegisterDismantle()
        {
            GameComponent_BookWhispers.Get?.RecordDismantle();
            if (!GoldenBooksMod.settings.storyChainEnabled || zhixiaArrived || !executorArrived) return;
            dismantleCount++;
            if (dismantleCount >= GoldenBooksMod.settings.zhixiaThreshold) ArriveZhixia(false);
        }

        public void RegisterWorm()
        {
            RegisterDismantle();
            GameComponent_BookWhispers.Get?.RecordWorm();
        }

        private void ArriveZhixia(bool fallback)
        {
            if (zhixiaArrived || !executorArrived) return;
            zhixiaArrived = true;

            Map map = Find.AnyPlayerHomeMap;
            Thing zhixia = null;
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("GoldenBooks_BookSpirit_Zhixia");
            if (def != null && map != null)
            {
                IntVec3 cell = DropCellFinder.TradeDropSpot(map);
                zhixia = ThingMaker.MakeThing(def);
                if (zhixia is Pawn) zhixia = null;
                else GenSpawn.Spawn(zhixia, cell, map);
            }

            string key = fallback ? "GoldenBooks_ZhixiaFallbackText" : "GoldenBooks_ZhixiaText";
            LookTargets targets = zhixia != null ? new LookTargets(zhixia) : (map != null ? new LookTargets(new TargetInfo(map.Center, map)) : LookTargets.Invalid);
            Find.LetterStack.ReceiveLetter("GoldenBooks_ZhixiaLabel".Translate(),
                key.Translate(), LetterDefOf.PositiveEvent, targets);

            // 彩蛋：兜底入场附赠一小箱"顺来的书"
            if (fallback && map != null) SpawnBonusBooks(map);
        }

        public static void SpawnBonusBooks(Map map)
        {
            List<Thing> drops = new List<Thing>();
            List<ThingDef> pool = PlayableBookDefs();
            int n = Rand.RangeInclusive(3, 6);
            for (int i = 0; i < n && pool.Count > 0; i++)
            {
                Thing book = ThingMaker.MakeThing(pool.RandomElement());
                book.stackCount = 1;
                drops.Add(book);
            }
            if (Rand.Chance(0.5f))
            {
                Thing frag = ThingMaker.MakeThing(ThingDef.Named("GoldenBooks_BookFragment"));
                frag.stackCount = Rand.RangeInclusive(1, 3);
                drops.Add(frag);
            }
            if (drops.Count > 0)
                DropPodUtility.DropThingsNear(DropCellFinder.TradeDropSpot(map), map, drops);
        }

        public static List<ThingDef> PlayableBookDefs()
        {
            ThingCategoryDef booksCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Books");
            List<ThingDef> pool = new List<ThingDef>();
            foreach (ThingDef d in DefDatabase<ThingDef>.AllDefs)
            {
                if (d.defName == "GoldenBooks_QuanXueShi") continue;
                if (d.thingCategories == null) continue;
                if (booksCat != null)
                {
                    bool inBooks = d.thingCategories.Contains(booksCat);
                    if (!inBooks && d.thingCategories.Any(c => c != null && c.parent == booksCat)) inBooks = true;
                    if (!inBooks) continue;
                }
                pool.Add(d);
            }
            return pool;
        }
    }

    // --- 事件：落难书箱 ---
    public class IncidentWorker_LostBookBox : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            StoryChain_GameComponent s = StoryChain_GameComponent.Get;
            return s != null && s.executorArrived && GoldenBooksMod.settings.storyChainEnabled;
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = (Map)parms.target;
            List<Thing> drops = new List<Thing>();
            List<ThingDef> pool = StoryChain_GameComponent.PlayableBookDefs();
            int n = Rand.RangeInclusive(3, 8);
            for (int i = 0; i < n; i++)
            {
                ThingDef def = pool.Count > 0 ? pool.RandomElement() : ThingDef.Named("GoldenBooks_BookFragment");
                if (def == null) continue;
                Thing book = ThingMaker.MakeThing(def);
                book.stackCount = def.stackLimit > 1 ? Rand.RangeInclusive(1, Mathf.Min(5, def.stackLimit)) : 1;
                drops.Add(book);
            }
            if (Rand.Chance(0.35f))
            {
                Thing frag = ThingMaker.MakeThing(ThingDef.Named("GoldenBooks_BookFragment"));
                frag.stackCount = Rand.RangeInclusive(1, 2);
                drops.Add(frag);
            }
            if (drops.Count == 0) return false;

            IntVec3 cell = DropCellFinder.TradeDropSpot(map);
            DropPodUtility.DropThingsNear(cell, map, drops);
            Find.LetterStack.ReceiveLetter("GoldenBooks_BookBoxLabel".Translate(),
                "GoldenBooks_BookBoxText".Translate(), LetterDefOf.PositiveEvent,
                new TargetInfo(cell, map));
            return true;
        }
    }

    // --- 事件：书蠹出没（书蠹拖来书箱并发起袭击）---
    public class IncidentWorker_BookwormSwarm : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            StoryChain_GameComponent s = StoryChain_GameComponent.Get;
            return s != null && s.yanRuYuDeathSent && GoldenBooksMod.settings.storyChainEnabled;
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = (Map)parms.target;
            IntVec3 cell = CellFinder.RandomSpawnCellForPawnNear(map.Center, map, 12);

            // 书蠹"拖来"的书箱：先落书，再出虫
            List<Thing> drops = new List<Thing>();
            List<ThingDef> pool = StoryChain_GameComponent.PlayableBookDefs();
            int books = Rand.RangeInclusive(2, 5);
            for (int i = 0; i < books && pool.Count > 0; i++)
            {
                Thing book = ThingMaker.MakeThing(pool.RandomElement());
                book.stackCount = 1;
                drops.Add(book);
            }
            Thing fragDrop = ThingMaker.MakeThing(ThingDef.Named("GoldenBooks_BookFragment"));
            fragDrop.stackCount = Rand.RangeInclusive(1, 2);
            drops.Add(fragDrop);
            DropPodUtility.DropThingsNear(cell, map, drops);

            int worms = Rand.RangeInclusive(2, 4);
            for (int i = 0; i < worms; i++)
            {
                Pawn worm = PawnGenerator.GeneratePawn(PawnKindDefOf.Megascarab, Faction.OfInsects);
                GenSpawn.Spawn(worm, CellFinder.RandomSpawnCellForPawnNear(cell, map, 4), map);
                worm.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Manhunter);
            }

            Find.LetterStack.ReceiveLetter("GoldenBooks_WormSwarmLabel".Translate(),
                "GoldenBooks_WormSwarmText".Translate(), LetterDefOf.ThreatSmall,
                new TargetInfo(cell, map));
            return true;
        }
    }
}
