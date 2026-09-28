using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace GoldenBooksMod
{
    // --- 1. Mod 设置 ---
    public class GoldenBooksSettings : ModSettings
    {
        public float resourceYieldPct = 1.0f;
        public float yanRuYuChance = 0.05f;
        public List<string> allowedDefNames = new List<string>();
        public Dictionary<string, bool> bookWhitelist = new Dictionary<string, bool>();

        // 书灵纪剧情链
        public bool storyChainEnabled = true;
        public int zhixiaThreshold = 5;
        public int zhixiaFallbackDays = 6;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref resourceYieldPct, "resourceYieldPct", 1.0f);
            Scribe_Values.Look(ref yanRuYuChance, "yanRuYuChance", 0.05f);
            Scribe_Collections.Look(ref allowedDefNames, "allowedDefNames", LookMode.Value);
            Scribe_Collections.Look(ref bookWhitelist, "bookWhitelist", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref storyChainEnabled, "storyChainEnabled", true);
            Scribe_Values.Look(ref zhixiaThreshold, "zhixiaThreshold", 5);
            Scribe_Values.Look(ref zhixiaFallbackDays, "zhixiaFallbackDays", 6);
            base.ExposeData();
        }
    }

    // --- 2. 核心功能组件 ---
    public class HediffCompProperties_RedSleevesAura : HediffCompProperties
    {
        public float radius = 9.9f;
        public HediffCompProperties_RedSleevesAura() { this.compClass = typeof(HediffComp_RedSleevesAura); }
    }

    public class HediffComp_RedSleevesAura : HediffComp
    {
        public HediffCompProperties_RedSleevesAura Props => (HediffCompProperties_RedSleevesAura)props;

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            // 每 120 Tick (约2秒) 检查一次，减少性能消耗
            if (Pawn.IsHashIntervalTick(120))
            {
                Pawn owner = parent.pawn;
                if (owner.Map == null || !owner.Spawned || owner.Dead || owner.Downed) return;

                // 【核心修复】将 CurLevel 改为 CurLevelPercentage
                // 这样无论种族上限是 1.0 还是 100.0，都会被正确锁定在 100%
                if (owner.needs != null)
                {
                    if (owner.needs.food != null) owner.needs.food.CurLevelPercentage = 1.0f;
                    if (owner.needs.rest != null) owner.needs.rest.CurLevelPercentage = 1.0f;
                    if (owner.needs.joy != null) owner.needs.joy.CurLevelPercentage = 1.0f;
                    if (owner.needs.mood != null) owner.needs.mood.CurLevelPercentage = 1.0f; // 心情也锁满
                }

                // 特效部分保持不变
                if (Rand.Chance(0.15f)) MoteMaker.ThrowText(owner.DrawPos, owner.Map, "✨", Color.yellow);

                IReadOnlyList<Pawn> pawns = owner.Map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn target = pawns[i];
                    if (target == owner) continue;
                    if (target.Faction == owner.Faction && !target.IsPrisoner && target.RaceProps.Humanlike)
                    {
                        if (target.Position.InHorDistOf(owner.Position, Props.radius))
                        {
                            bool applied = ApplyBuff(target);
                            if (Rand.Chance(0.2f)) MoteMaker.ThrowText(target.DrawPos, target.Map, "❤", Color.red);
                        }
                    }
                }
            }
        }

        // ... ApplyBuff 方法不用动 ...
        private bool ApplyBuff(Pawn target)
        {
            HediffDef buffDef = HediffDef.Named("GoldenBooks_RedSleevesBuff");
            Hediff buff = target.health.hediffSet.GetFirstHediffOfDef(buffDef);
            if (buff == null) { buff = target.health.AddHediff(buffDef); buff.Severity = 1.0f; return true; }
            HediffComp_Disappears disappears = buff.TryGetComp<HediffComp_Disappears>();
            if (disappears != null) disappears.ticksToDisappear = 240;
            return false;
        }
    }

    public class CompUseEffect_GodSpeed : CompUseEffect
    {
        public override void DoEffect(Pawn usedBy)
        {
            HediffDef buffDef = HediffDef.Named("GoldenBooks_GodSpeedBuff");
            usedBy.health.AddHediff(buffDef);
            SoundDefOf.PsychicPulseGlobal.PlayOneShot(usedBy);
            Messages.Message("GoldenBooks_MessageGodSpeed".Translate(usedBy.LabelShort), usedBy, MessageTypeDefOf.PositiveEvent);
            if (parent.stackCount > 1) parent.SplitOff(1).Destroy(); else parent.Destroy();
        }
    }

    public class CompUseEffect_GainInspiration : CompUseEffect
    {
        public override void DoEffect(Pawn usedBy)
        {
            InspirationDef randomInspiration = DefDatabase<InspirationDef>.AllDefsListForReading.Where(i => i.Worker.InspirationCanOccur(usedBy)).RandomElementWithFallback();
            if (randomInspiration != null)
            {
                usedBy.mindState.inspirationHandler.TryStartInspiration(randomInspiration, "阅读五经有感", true);
                SoundDefOf.TechprintApplied.PlayOneShot(usedBy);
                Messages.Message("GoldenBooks_MessageInspirationSuccess".Translate(usedBy.LabelShort, randomInspiration.LabelCap), usedBy, MessageTypeDefOf.PositiveEvent);
            }
            else Messages.Message("GoldenBooks_MessageInspirationFail".Translate(usedBy.LabelShort), usedBy, MessageTypeDefOf.NeutralEvent);
            if (parent.stackCount > 1) parent.SplitOff(1).Destroy(); else parent.Destroy();
        }
    }

    public class CompUseEffect_ReadQuanXueShi : CompUseEffect
    {
        public override void DoEffect(Pawn usedBy)
        {
            base.DoEffect(usedBy);
            SoundDefOf.TechprintApplied.PlayOneShot(usedBy);
            if (usedBy.Map != null)
            {
                IntVec3 pos = usedBy.Position;
                Map map = usedBy.Map;
                Thing gold = ThingMaker.MakeThing(ThingDefOf.Gold); gold.stackCount = 20; GenSpawn.Spawn(gold, pos, map);
                Thing wood = ThingMaker.MakeThing(ThingDefOf.WoodLog); wood.stackCount = 50; GenSpawn.Spawn(wood, pos, map);
                Messages.Message("GoldenBooks_GiftText".Translate(), new TargetInfo(pos, map), MessageTypeDefOf.PositiveEvent);
            }
        }
    }

    // --- 3. 工具类 ---
    public static class GoldenBooksUtils
    {
        public static Pawn GenerateYanRuYu(bool isTrueSpirit)
        {
            List<string> allowed = GoldenBooksMod.settings.allowedDefNames;
            if (allowed == null || allowed.Count == 0) allowed = new List<string> { XenotypeDefOf.Baseliner.defName };
            string picked = allowed.RandomElement();
            PawnKindDef kind = PawnKindDefOf.Colonist;
            XenotypeDef xeno = XenotypeDefOf.Baseliner;
            bool isXeno = false;
            XenotypeDef xenoDef = DefDatabase<XenotypeDef>.GetNamedSilentFail(picked);
            if (xenoDef != null) { xeno = xenoDef; isXeno = true; }
            else
            {
                ThingDef race = DefDatabase<ThingDef>.GetNamedSilentFail(picked);
                if (race != null)
                {
                    PawnKindDef foundKind = DefDatabase<PawnKindDef>.AllDefsListForReading.FirstOrDefault(k => k.race == race && !IsCreepJoiner(k) && (k.defName.Contains("Colonist") || k.defName.Contains("Villager") || k.defName.Contains("Tribesperson") || k.defName.Contains("Player")));
                    if (foundKind == null) foundKind = DefDatabase<PawnKindDef>.AllDefsListForReading.FirstOrDefault(k => k.race == race && !IsCreepJoiner(k) && !k.defName.Contains("Ancient"));
                    if (foundKind != null) kind = foundKind;
                }
            }
            PawnGenerationRequest request = new PawnGenerationRequest(kind, Faction.OfPlayer, forceGenerateNewPawn: true, forcedXenotype: isXeno ? xeno : null, fixedGender: Gender.Female, fixedBiologicalAge: 20, fixedChronologicalAge: 20);
            Pawn pawn = PawnGenerator.GeneratePawn(request);
            PurifyTraits(pawn);
            string baseName = "GoldenBooks_YanRuYuName".Translate();
            if (baseName.Contains("GoldenBooks_")) baseName = "Yan Ru Yu";
            string nickName = baseName;
            if (isTrueSpirit) { if (LanguageDatabase.activeLanguage.LegacyFolderName == "ChineseSimplified") nickName = "真·" + baseName; else nickName = "True " + baseName; }
            string finalName = nickName;
            int count = 0;
            foreach (var map in Find.Maps) count += map.mapPawns.FreeColonists.Count(p => p.Name != null && p.Name.ToStringShort.Contains(nickName));
            if (count > 0) finalName = $"{nickName} {ToRoman(count + 1)}";
            pawn.Name = new NameTriple("如玉", finalName, "颜");
            TraitDef beauty = TraitDef.Named("Beauty");
            if (pawn.story.traits.HasTrait(beauty)) pawn.story.traits.RemoveTrait(pawn.story.traits.GetTrait(beauty));
            pawn.story.traits.GainTrait(new Trait(beauty, 2));
            int minLevel = isTrueSpirit ? 15 : 8;
            int craftLevel = isTrueSpirit ? 10 : 5;
            EnhanceSkill(pawn, SkillDefOf.Cooking, craftLevel);
            EnhanceSkill(pawn, SkillDefOf.Crafting, craftLevel);
            EnhanceSkill(pawn, SkillDefOf.Intellectual, minLevel);
            if (isTrueSpirit) { HediffDef spiritDef = DefDatabase<HediffDef>.GetNamedSilentFail("GoldenBooks_BookSpiritEssence"); if (spiritDef != null) pawn.health.AddHediff(spiritDef); }
            return pawn;
        }
        private static void PurifyTraits(Pawn pawn)
        {
            if (pawn.story == null || pawn.story.traits == null) return;
            for (int i = pawn.story.traits.allTraits.Count - 1; i >= 0; i--)
            {
                Trait trait = pawn.story.traits.allTraits[i];
                string d = trait.def.defName;
                if (trait.def.disabledWorkTags != WorkTags.None) { pawn.story.traits.RemoveTrait(trait); continue; }
                bool isBad = d == "Wimp" || d == "Pyromaniac" || d == "Gourmand" || d == "BodyPurist" || d == "Transhumanist" || d == "Misandrist" || d == "Misogynist" || d == "AnnoyingVoice" || d == "CreepyBreathing" || d == "Pessimist" || d == "Depressive" || d == "Lazy" || d == "Slothful";
                bool isGenericBad = trait.CurrentData.marketValueFactorOffset < 0;
                if (isBad || isGenericBad) pawn.story.traits.RemoveTrait(trait);
            }
        }
        private static void EnhanceSkill(Pawn pawn, SkillDef skill, int minLevel)
        {
            SkillRecord record = pawn.skills.GetSkill(skill);
            if (record != null) { record.passion = Passion.Major; if (record.Level < minLevel) { record.Level = minLevel; record.xpSinceLastLevel = 0; } }
        }
        public static void EnsureBeautifulHair(Pawn pawn)
        {
            if (pawn == null || pawn.story == null) return;
            if (pawn.genes != null) { List<Gene> baldGenes = pawn.genes.GenesListForReading.Where(g => g.def.defName.Contains("Bald") || (g.def.hairTagFilter != null && g.def.hairTagFilter.tags != null && g.def.hairTagFilter.tags.Contains("Bald"))).ToList(); foreach (var g in baldGenes) pawn.genes.RemoveGene(g); }
            if (pawn.story.hairDef == HairDefOf.Bald || pawn.story.hairDef.defName.Contains("Bald")) { IEnumerable<HairDef> validHairs = DefDatabase<HairDef>.AllDefsListForReading.Where(h => h != HairDefOf.Bald && !h.defName.Contains("Bald") && (h.styleGender == StyleGender.Female || h.styleGender == StyleGender.Any)); if (validHairs.Any()) { pawn.story.hairDef = validHairs.RandomElement(); pawn.Drawer.renderer.SetAllGraphicsDirty(); } }
        }
        public static void DressUpYanRuYu(Pawn pawn)
        {
            if (pawn.def.defName != "Human") { if (pawn.apparel != null) foreach (var app in pawn.apparel.WornApparel) { app.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Legendary, ArtGenerationContext.Outsider); app.HitPoints = app.MaxHitPoints; } return; }
            pawn.apparel.DestroyAll();
            ThingDef fabric = DefDatabase<ThingDef>.GetNamedSilentFail("Hyperweave") ?? DefDatabase<ThingDef>.GetNamedSilentFail("DevilstrandCloth") ?? ThingDefOf.Gold;
            string[] dressDefs = { "Apparel_Dress", "Apparel_RobeRoyal", "Apparel_Corset", "Apparel_Duster", "Apparel_TribalA" };
            ThingDef chosenApparelDef = null;
            foreach (var defName in dressDefs) { chosenApparelDef = DefDatabase<ThingDef>.GetNamedSilentFail(defName); if (chosenApparelDef != null) break; }
            if (chosenApparelDef != null && fabric != null) { Apparel apparel = (Apparel)ThingMaker.MakeThing(chosenApparelDef, fabric); apparel.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Masterwork, ArtGenerationContext.Outsider); apparel.DrawColor = Color.white; pawn.apparel.Wear(apparel); }
        }
        private static bool IsCreepJoiner(PawnKindDef k) { return k.defName.Contains("Creep") || k.defName.Contains("Stranger") || k.defName.Contains("Shambler") || k.defName.Contains("Entity"); }
        private static string ToRoman(int n) { if (n >= 10) return n.ToString(); string[] r = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" }; return r[n]; }
        public static void SpawnWealth(IntVec3 loc, Map map, float bookValue)
        {
            float multiplier = GoldenBooksMod.settings.resourceYieldPct;
            int baseCount = (int)(bookValue / 10f * multiplier);
            Thing gold = ThingMaker.MakeThing(ThingDefOf.Gold); gold.stackCount = Mathf.Max(1, baseCount); GenSpawn.Spawn(gold, loc, map);
            Thing jade = ThingMaker.MakeThing(ThingDefOf.Jade); jade.stackCount = Mathf.Max(1, baseCount); GenSpawn.Spawn(jade, loc, map);
        }
        public static void TrySpawnBookworm(Pawn worker)
        {
            StoryChain_GameComponent.Get?.RegisterWorm();
            if (!Rand.Chance(0.2f)) return;
            if (worker == null || worker.Map == null) return;
            PawnKindDef bugKind = PawnKindDefOf.Megascarab;
            Pawn bug = PawnGenerator.GeneratePawn(bugKind, Faction.OfInsects);
            bug.Name = new NameTriple("", "书蠹", "");
            GenSpawn.Spawn(bug, worker.Position, worker.Map);
            bug.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Manhunter);
            Messages.Message("GoldenBooks_MessageBookworm".Translate(), bug, MessageTypeDefOf.ThreatSmall);
        }
    }

    // --- 4. 自动补丁工具 ---
    [StaticConstructorOnStartup]
    public static class GoldenBooksPatcher
    {
        static GoldenBooksPatcher() { ApplySettings(); }

        public static void ApplySettings()
        {
            if (GoldenBooksMod.settings.bookWhitelist == null) GoldenBooksMod.settings.bookWhitelist = new Dictionary<string, bool>();
            ThingCategoryDef booksCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Books");
            if (booksCat == null) return;

            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs.Where(d => d.category == ThingCategory.Item))
            {
                // 排除本Mod物品和其他干扰项
                if (def.defName.StartsWith("GoldenBooks_")) continue;
                if (def.defName.Contains("Tornado")) continue;

                bool isCandidate = def.HasComp(typeof(CompBook)) ||
                                   (def.defName.Contains("Book") && !def.defName.Contains("Blueprint") && !def.defName.Contains("Frame")) ||
                                   (def.label != null && (def.label.ToLower().Contains("book") || def.label.Contains("书") || def.label.ToLower().Contains("scroll") || def.label.Contains("卷")));

                if (!isCandidate) continue;

                if (!GoldenBooksMod.settings.bookWhitelist.ContainsKey(def.defName))
                {
                    bool defaultOn = (def.thingCategories != null && def.thingCategories.Contains(booksCat));
                    GoldenBooksMod.settings.bookWhitelist[def.defName] = defaultOn;
                }

                bool shouldBeBook = GoldenBooksMod.settings.bookWhitelist[def.defName];
                if (shouldBeBook)
                {
                    if (def.thingCategories == null) def.thingCategories = new List<ThingCategoryDef>();
                    if (!def.thingCategories.Contains(booksCat))
                    {
                        def.thingCategories.Add(booksCat);
                        if (!booksCat.childThingDefs.Contains(def)) booksCat.childThingDefs.Add(def);
                    }
                }
            }
            booksCat.ClearCachedData();
            booksCat.ResolveReferences();
        }
    }

    // --- 5. Mod 主类 (UI 终极修复版) ---
    public class GoldenBooksMod : Mod
    {
        public static GoldenBooksSettings settings;
        private Vector2 scrollPosition = Vector2.zero;
        private Vector2 bookScrollPosition = Vector2.zero;
        private string searchText = "";
        private string bookSearchText = "";

        private List<RaceCandidate> cachedRaceCandidates = null;
        private List<BookCandidate> cachedBookCandidates = null;

        private class RaceCandidate { public string DefName; public string Label; public bool IsXenotype; public bool IsPrettyDefault; }
        private class BookCandidate { public ThingDef Def; public string Label; public string ModName; }

        public GoldenBooksMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<GoldenBooksSettings>();
            new Harmony("com.maiya.goldenbooks").PatchAll();
        }

        public override string SettingsCategory() => "Golden Books 书中自有黄金屋";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.Label($"{"GoldenBooks_YieldRate".Translate()}: {settings.resourceYieldPct:P0}");
            settings.resourceYieldPct = listing.Slider(settings.resourceYieldPct, 0.01f, 2.0f);
            listing.Label($"{"GoldenBooks_SpawnChance".Translate()}: {settings.yanRuYuChance:P1}");
            settings.yanRuYuChance = listing.Slider(settings.yanRuYuChance, 0f, 1.0f);
            listing.GapLine();

            // 书灵纪剧情链
            listing.CheckboxLabeled("GoldenBooks_StoryChainToggle".Translate(), ref settings.storyChainEnabled, "GoldenBooks_StoryChainTip".Translate());
            if (settings.storyChainEnabled)
            {
                listing.Label($"{"GoldenBooks_ZhixiaThreshold".Translate()}: {settings.zhixiaThreshold}");
                settings.zhixiaThreshold = (int)listing.Slider(settings.zhixiaThreshold, 1f, 20f);
                listing.Label($"{"GoldenBooks_ZhixiaFallbackDays".Translate()}: {settings.zhixiaFallbackDays}");
                settings.zhixiaFallbackDays = (int)listing.Slider(settings.zhixiaFallbackDays, 1f, 30f);
            }
            listing.GapLine();
            listing.End();

            float halfWidth = inRect.width / 2f - 10f;
            float topY = listing.CurHeight + inRect.y;
            float bottomY = inRect.height;
            float listHeight = bottomY - topY;

            Rect leftRect = new Rect(inRect.x, topY, halfWidth, listHeight);
            DrawRaceSelector(leftRect);

            Rect rightRect = new Rect(inRect.x + halfWidth + 20f, topY, halfWidth, listHeight);
            DrawBookSelector(rightRect);
        }

        public override void WriteSettings()
        {
            base.WriteSettings();
            GoldenBooksPatcher.ApplySettings();
        }

        private void DrawRaceSelector(Rect rect)
        {
            if (cachedRaceCandidates == null) InitializeRaceCandidates();

            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(10f);

            // 按钮组 (全选/全不选/重置) - 固定在右上角
            float btnW = 60f;
            float btnH = 24f;
            float buttonsTotalWidth = btnW * 3 + 10f; // 按钮区总宽度
            float startBtnX = inner.x + inner.width - buttonsTotalWidth;

            if (Widgets.ButtonText(new Rect(startBtnX, inner.y, btnW, btnH), "GoldenBooks_BtnAll".Translate()))
            {
                foreach (var c in cachedRaceCandidates) if (MatchesFilter(c) && !settings.allowedDefNames.Contains(c.DefName)) settings.allowedDefNames.Add(c.DefName);
            }
            if (Widgets.ButtonText(new Rect(startBtnX + btnW + 5f, inner.y, btnW, btnH), "GoldenBooks_BtnNone".Translate()))
            {
                foreach (var c in cachedRaceCandidates) if (MatchesFilter(c) && settings.allowedDefNames.Contains(c.DefName)) settings.allowedDefNames.Remove(c.DefName);
            }
            // 重置按钮：恢复到 IsPrettyDefault 状态 (排除猪人等)
            if (Widgets.ButtonText(new Rect(startBtnX + btnW * 2 + 10f, inner.y, btnW, btnH), "GoldenBooks_BtnReset".Translate()))
            {
                settings.allowedDefNames.Clear();
                settings.allowedDefNames.AddRange(cachedRaceCandidates.Where(c => c.IsPrettyDefault).Select(c => c.DefName));
            }

            // 标题 & 搜索 - 动态宽度，防止遮挡
            // 搜索框宽度 = 总宽 - 按钮区宽 - 间隙
            float searchBoxWidth = inner.width - buttonsTotalWidth - 10f;
            Rect header = new Rect(inner.x, inner.y, searchBoxWidth, 24f);
            Widgets.Label(header, "颜如玉种族白名单:"); // 标题放上面一行

            Rect searchRect = new Rect(inner.x, inner.y + 26f, searchBoxWidth, 24f);
            searchText = Widgets.TextEntryLabeled(searchRect, "Search: ", searchText);

            // 列表
            Rect listRect = new Rect(inner.x, inner.y + 60f, inner.width, inner.height - 60f);
            List<RaceCandidate> filtered = cachedRaceCandidates.Where(c => MatchesFilter(c)).ToList();
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, filtered.Count * 24f);

            Widgets.BeginScrollView(listRect, ref scrollPosition, viewRect);
            Listing_Standard l = new Listing_Standard();
            l.Begin(viewRect);
            foreach (var c in filtered)
            {
                bool active = settings.allowedDefNames.Contains(c.DefName);
                bool oldActive = active;
                l.CheckboxLabeled(c.Label, ref active);
                if (active != oldActive)
                {
                    if (active) settings.allowedDefNames.Add(c.DefName);
                    else if (settings.allowedDefNames.Count > 1) settings.allowedDefNames.Remove(c.DefName);
                }
            }
            l.End();
            Widgets.EndScrollView();
        }

        private void DrawBookSelector(Rect rect)
        {
            if (cachedBookCandidates == null) InitializeBookCandidates();

            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(10f);

            // 按钮组
            float btnW = 60f;
            float btnH = 24f;
            float buttonsTotalWidth = btnW * 3 + 10f;
            float startBtnX = inner.x + inner.width - buttonsTotalWidth;

            List<BookCandidate> filtered = cachedBookCandidates.Where(b => string.IsNullOrEmpty(bookSearchText) || b.Label.ToLower().Contains(bookSearchText.ToLower()) || b.ModName.ToLower().Contains(bookSearchText.ToLower())).ToList();

            if (Widgets.ButtonText(new Rect(startBtnX, inner.y, btnW, btnH), "GoldenBooks_BtnAll".Translate()))
            {
                foreach (var b in filtered) settings.bookWhitelist[b.Def.defName] = true;
            }
            if (Widgets.ButtonText(new Rect(startBtnX + btnW + 5f, inner.y, btnW, btnH), "GoldenBooks_BtnNone".Translate()))
            {
                foreach (var b in filtered) settings.bookWhitelist[b.Def.defName] = false;
            }
            // 重置按钮：恢复到 Core/DLC 或原生 Books 分类
            if (Widgets.ButtonText(new Rect(startBtnX + btnW * 2 + 10f, inner.y, btnW, btnH), "GoldenBooks_BtnReset".Translate()))
            {
                ThingCategoryDef booksCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Books");
                foreach (var b in filtered)
                {
                    // 默认开启：本身就是书的分类，或者属于 Core/DLC
                    bool defaultOn = (booksCat != null && b.Def.thingCategories != null && b.Def.thingCategories.Contains(booksCat));
                    settings.bookWhitelist[b.Def.defName] = defaultOn;
                }
            }

            // 标题 & 搜索
            float searchBoxWidth = inner.width - buttonsTotalWidth - 10f;
            Rect header = new Rect(inner.x, inner.y, searchBoxWidth, 24f);
            Widgets.Label(header, "可作为原料的书籍 (需重启/重载):");

            Rect searchRect = new Rect(inner.x, inner.y + 26f, searchBoxWidth, 24f);
            bookSearchText = Widgets.TextEntryLabeled(searchRect, "Search: ", bookSearchText);

            // 列表
            Rect listRect = new Rect(inner.x, inner.y + 60f, inner.width, inner.height - 60f);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, filtered.Count * 32f);

            Widgets.BeginScrollView(listRect, ref bookScrollPosition, viewRect);
            float curY = 0f;
            foreach (var book in filtered)
            {
                Rect rowRect = new Rect(0f, curY, viewRect.width, 30f);
                Rect iconRect = new Rect(0f, curY, 30f, 30f);
                Widgets.ThingIcon(iconRect, book.Def);

                Rect textRect = new Rect(35f, curY, viewRect.width - 35f, 30f);
                bool active = false;
                if (settings.bookWhitelist.ContainsKey(book.Def.defName)) active = settings.bookWhitelist[book.Def.defName];

                string label = $"[{book.ModName}] {book.Label}";
                bool oldActive = active;
                Widgets.CheckboxLabeled(textRect, label, ref active);
                if (active != oldActive) settings.bookWhitelist[book.Def.defName] = active;

                curY += 32f;
            }
            Widgets.EndScrollView();
        }

        private void InitializeRaceCandidates()
        {
            cachedRaceCandidates = new List<RaceCandidate>();
            // [种族列表初始化]
            var alienRaces = DefDatabase<ThingDef>.AllDefsListForReading.Where(t => t.race != null && t.race.Humanlike && t.defName != "Human" && !t.defName.Contains("Corpse") && !t.defName.Contains("Meat") && !t.defName.Contains("Creep") && !t.defName.Contains("Shambler") && !t.label.Contains("corpse"));
            foreach (var t in alienRaces) { cachedRaceCandidates.Add(new RaceCandidate { DefName = t.defName, Label = $"[Race] {t.LabelCap}", IsPrettyDefault = true }); }

            var xenos = DefDatabase<XenotypeDef>.AllDefsListForReading.Where(x => !x.defName.Contains("Creep") && !x.defName.Contains("Shambler") && !x.defName.Contains("Metalhorror") && x.description != null);
            foreach (var x in xenos)
            {
                // [修复：丑陋种族过滤] 默认不勾选这些
                bool isPretty = !x.defName.Contains("Pig") &&
                                !x.defName.Contains("Waster") &&
                                !x.defName.Contains("Neanderthal") &&
                                !x.defName.Contains("Yttakin") &&
                                !x.defName.Contains("Hussar") &&
                                !x.defName.Contains("Impid") &&
                                !x.defName.Contains("Dirtmole");
                cachedRaceCandidates.Add(new RaceCandidate { DefName = x.defName, Label = $"[Gene] {x.LabelCap}", IsPrettyDefault = isPretty });
            }
            cachedRaceCandidates = cachedRaceCandidates.OrderBy(c => c.Label).ToList();

            if (settings.allowedDefNames == null) settings.allowedDefNames = new List<string>();
            if (settings.allowedDefNames.Count == 0 && cachedRaceCandidates.Count > 0) settings.allowedDefNames.AddRange(cachedRaceCandidates.Where(c => c.IsPrettyDefault).Select(c => c.DefName));
        }

        private void InitializeBookCandidates()
        {
            cachedBookCandidates = new List<BookCandidate>();
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs.Where(d => d.category == ThingCategory.Item))
            {
                // [修复：过滤本Mod物品和龙卷风]
                if (def.defName.StartsWith("GoldenBooks_")) continue;
                if (def.defName.Contains("Tornado")) continue;

                bool isCandidate = def.HasComp(typeof(CompBook)) ||
                                   (def.defName.Contains("Book") && !def.defName.Contains("Blueprint") && !def.defName.Contains("Frame")) ||
                                   (def.label != null && (def.label.ToLower().Contains("book") || def.label.Contains("书") || def.label.ToLower().Contains("scroll") || def.label.Contains("卷")));

                if (isCandidate)
                {
                    string modName = def.modContentPack?.Name ?? "Core";
                    cachedBookCandidates.Add(new BookCandidate { Def = def, Label = def.LabelCap, ModName = modName });
                    if (!settings.bookWhitelist.ContainsKey(def.defName))
                    {
                        ThingCategoryDef booksCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Books");
                        bool defaultOn = (booksCat != null && def.thingCategories != null && def.thingCategories.Contains(booksCat));
                        settings.bookWhitelist[def.defName] = defaultOn;
                    }
                }
            }
            cachedBookCandidates = cachedBookCandidates.OrderBy(b => b.ModName).ThenBy(b => b.Label).ToList();
        }
        private bool MatchesFilter(RaceCandidate c) => string.IsNullOrEmpty(searchText) || c.Label.ToLower().Contains(searchText.ToLower()) || c.DefName.ToLower().Contains(searchText.ToLower());
    }

    // --- 6 & 7 保持不变 ---
    public class GoldenBooks_GameComponent : GameComponent { public bool hasGivenStartingBook = false; public GoldenBooks_GameComponent(Game game) { } override public void ExposeData() { Scribe_Values.Look(ref hasGivenStartingBook, "hasGivenStartingBook", false); } override public void FinalizeInit() { base.FinalizeInit(); if (!hasGivenStartingBook && Find.AnyPlayerHomeMap != null) { GiveTheBook(); hasGivenStartingBook = true; } } private void GiveTheBook() { Map map = Find.AnyPlayerHomeMap; if (map == null) return; IntVec3 dropSpot = DropCellFinder.TradeDropSpot(map); List<Thing> giftList = new List<Thing>(); Thing book = ThingMaker.MakeThing(ThingDef.Named("GoldenBooks_QuanXueShi")); if (book.TryGetComp<CompQuality>() != null) book.TryGetComp<CompQuality>().SetQuality(QualityCategory.Legendary, ArtGenerationContext.Outsider); giftList.Add(book); Thing gold = ThingMaker.MakeThing(ThingDefOf.Gold); gold.stackCount = 2; giftList.Add(gold); Thing wood = ThingMaker.MakeThing(ThingDefOf.WoodLog); wood.stackCount = 40; giftList.Add(wood); Thing jade = ThingMaker.MakeThing(ThingDefOf.Jade); jade.stackCount = 20; giftList.Add(jade); DropPodUtility.DropThingsNear(dropSpot, map, giftList); Find.LetterStack.ReceiveLetter("GoldenBooks_GiftLabel".Translate(), "GoldenBooks_GiftText".Translate(), LetterDefOf.PositiveEvent, new TargetInfo(dropSpot, map)); } }
    [HarmonyPatch(typeof(Pawn), "Kill")] public static class Patch_PawnKill { static void Postfix(Pawn __instance) { if (__instance.health != null && __instance.health.hediffSet.HasHediff(HediffDef.Named("GoldenBooks_BookSpiritEssence"))) { Map map = __instance.MapHeld; IntVec3 pos = __instance.PositionHeld; StoryChain_GameComponent.Get?.OnYanRuYuDeath(__instance); if (map != null) { Thing book = ThingMaker.MakeThing(ThingDef.Named("GoldenBooks_QuanXueShi")); GenSpawn.Spawn(book, pos, map); MoteMaker.ThrowText(pos.ToVector3(), map, "书灵归位", Color.yellow); if (__instance.Corpse != null) __instance.Corpse.Destroy(); else if (!__instance.Destroyed) __instance.Destroy(); Messages.Message("颜如玉已化作书本回归。", new TargetInfo(pos, map), MessageTypeDefOf.PositiveEvent); } } } }
    [HarmonyPatch(typeof(GenRecipe), "MakeRecipeProducts")] public static class Patch_MakeRecipeProducts { private const string Recipe_Wealth = "Extract_Knowledge_To_Wealth"; private const string Recipe_Food = "GoldenBooks_Extract_Food"; private const string Recipe_Speed = "GoldenBooks_Extract_Speed"; private const string Recipe_Bind = "GoldenBooks_Bind_FiveClassics"; private const string Recipe_Summon = "GoldenBooks_Summon_YanRuYu"; static IEnumerable<Thing> Postfix(IEnumerable<Thing> values, RecipeDef recipeDef, Pawn worker, List<Thing> ingredients) { float totalBookValue = 0f; if (ingredients != null) foreach (var item in ingredients) totalBookValue += item.MarketValue * item.stackCount; float multiplier = GoldenBooksMod.settings.resourceYieldPct; float successChance = Mathf.Clamp01(totalBookValue / 400.0f); bool isSuccess = Rand.Chance(successChance); if (recipeDef.defName == Recipe_Food || recipeDef.defName == Recipe_Speed || recipeDef.defName == Recipe_Wealth) StoryChain_GameComponent.Get?.RegisterDismantle(); if (recipeDef.defName == Recipe_Food) { if (isSuccess) { int count = Mathf.Max(1, (int)(totalBookValue / 5f * multiplier)); Thing millet = ThingMaker.MakeThing(ThingDef.Named("GoldenBooks_GoldenMillet")); millet.stackCount = count; yield return millet; } else { Thing frag = ThingMaker.MakeThing(ThingDef.Named("GoldenBooks_BookFragment")); frag.stackCount = 1; yield return frag; GoldenBooksUtils.TrySpawnBookworm(worker); } yield break; } if (recipeDef.defName == Recipe_Speed) { if (isSuccess) { int count = (int)(totalBookValue / 100f * multiplier); if (count < 1) { Thing millet = ThingMaker.MakeThing(ThingDef.Named("GoldenBooks_GoldenMillet")); millet.stackCount = 5; yield return millet; } else { Thing talisman = ThingMaker.MakeThing(ThingDef.Named("GoldenBooks_GodSpeedTalisman")); talisman.stackCount = count; yield return talisman; } } else { Thing frag = ThingMaker.MakeThing(ThingDef.Named("GoldenBooks_BookFragment")); frag.stackCount = 1; yield return frag; GoldenBooksUtils.TrySpawnBookworm(worker); } yield break; } if (recipeDef.defName == Recipe_Wealth) { if (isSuccess) { int baseCount = (int)(totalBookValue / 10f * multiplier); Thing gold = ThingMaker.MakeThing(ThingDefOf.Gold); gold.stackCount = Mathf.Max(1, baseCount); yield return gold; Thing jade = ThingMaker.MakeThing(ThingDefOf.Jade); jade.stackCount = Mathf.Max(1, baseCount); yield return jade; if (Rand.Chance(GoldenBooksMod.settings.yanRuYuChance)) { Pawn mortalYanRuYu = GoldenBooksUtils.GenerateYanRuYu(false); if (mortalYanRuYu != null) { GoldenBooksUtils.DressUpYanRuYu(mortalYanRuYu); GoldenBooksUtils.EnsureBeautifulHair(mortalYanRuYu); string raceLabel = mortalYanRuYu.def.LabelCap; if (mortalYanRuYu.genes != null && mortalYanRuYu.genes.Xenotype != XenotypeDefOf.Baseliner) raceLabel = mortalYanRuYu.genes.Xenotype.LabelCap; Find.LetterStack.ReceiveLetter("GoldenBooks_LetterLabel".Translate(), "GoldenBooks_LetterText".Translate(mortalYanRuYu.Name.ToStringShort, raceLabel), LetterDefOf.PositiveEvent, mortalYanRuYu); yield return mortalYanRuYu; } } } else { Thing frag = ThingMaker.MakeThing(ThingDef.Named("GoldenBooks_BookFragment")); frag.stackCount = 1; yield return frag; GoldenBooksUtils.TrySpawnBookworm(worker); } yield break; } if (recipeDef.defName == Recipe_Bind) { Thing classic = ThingMaker.MakeThing(ThingDef.Named("GoldenBooks_FiveClassics")); classic.stackCount = 1; yield return classic; yield break; } if (recipeDef.defName == Recipe_Summon) { Pawn trueSpirit = GoldenBooksUtils.GenerateYanRuYu(true); if (trueSpirit != null) { GoldenBooksUtils.DressUpYanRuYu(trueSpirit); GoldenBooksUtils.EnsureBeautifulHair(trueSpirit); string raceLabel = trueSpirit.def.LabelCap; if (trueSpirit.genes != null && trueSpirit.genes.Xenotype != XenotypeDefOf.Baseliner) raceLabel = trueSpirit.genes.Xenotype.LabelCap; Find.LetterStack.ReceiveLetter("GoldenBooks_TrueSpiritLabel".Translate(), "GoldenBooks_TrueSpiritText".Translate(trueSpirit.Name.ToStringShort, raceLabel), LetterDefOf.PositiveEvent, trueSpirit); StoryChain_GameComponent.Get?.OnFirstSummon(trueSpirit); yield return trueSpirit; } yield break; } foreach (var t in values) yield return t; } }

    // --- 8. 剧情事件链补丁（书灵纪）---
    [HarmonyPatch(typeof(Game), "InitNewGame")]
    public static class Patch_GameInitNewGame { static void Postfix() { StoryChain_GameComponent.Ensure(); } }

    [HarmonyPatch(typeof(Game), "LoadedGame")]
    public static class Patch_GameLoadedGame { static void Postfix() { StoryChain_GameComponent.Ensure(); } }

    [HarmonyPatch(typeof(Thing), "SpawnSetup")]
    public static class Patch_LecternBuilt
    {
        static void Postfix(Thing __instance, bool respawningAfterLoad)
        {
            if (respawningAfterLoad) return;
            if (__instance.def != null && __instance.def.defName == "GoldenBooks_ArcaneLectern" && __instance.Faction == Faction.OfPlayer)
                StoryChain_GameComponent.Get?.OnLecternBuilt(__instance);
        }
    }
}