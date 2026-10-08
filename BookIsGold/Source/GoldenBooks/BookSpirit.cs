using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using UnityEngine;

namespace GoldenBooksMod
{
    // --- 书灵本源 HediffComp：全部书灵行为（需求锁满 + 光环 + 粒子 + 再生 + 怕水） ---
    public class HediffCompProperties_BookSpiritVitality : HediffCompProperties
    {
        public float auraRadius = 9.9f;
        public float regenAmount = 1.2f;
        public int tickInterval = 150;

        public HediffCompProperties_BookSpiritVitality() { this.compClass = typeof(HediffComp_BookSpiritVitality); }
    }

    public class HediffComp_BookSpiritVitality : HediffComp
    {
        public HediffCompProperties_BookSpiritVitality Props => (HediffCompProperties_BookSpiritVitality)props;
        private Pawn Pawn => parent.pawn;

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);
            Pawn p = Pawn;
            if (p == null || p.Dead || p.needs == null) return;
            if (!p.IsHashIntervalTick(Props.tickInterval)) return;

            LockNeeds(p);
            ApplyAura(p);
            TryRegenerate(p);
            TryWaterFear(p);
        }

        // 需求恒满
        private void LockNeeds(Pawn p)
        {
            if (p.needs.food != null) p.needs.food.CurLevelPercentage = 1f;
            if (p.needs.rest != null) p.needs.rest.CurLevelPercentage = 1f;
            if (p.needs.joy != null) p.needs.joy.CurLevelPercentage = 1f;
            if (p.needs.mood != null) p.needs.mood.CurLevelPercentage = 1f;
        }

        // 光环
        private void ApplyAura(Pawn p)
        {
            string auraDef = GetAuraDef();
            if (string.IsNullOrEmpty(auraDef)) return;
            HediffDef hd = DefDatabase<HediffDef>.GetNamedSilentFail(auraDef);
            if (hd == null) return;

            IReadOnlyList<Pawn> pawns = p.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn t = pawns[i];
                if (t == p || t.Dead || t.Downed) continue;
                if (!t.RaceProps.Humanlike) continue;
                if (t.Faction != Faction.OfPlayer && !t.IsPrisoner) continue;
                if (!t.Position.InHorDistOf(p.Position, Props.auraRadius)) continue;

                Hediff ex = t.health.hediffSet.GetFirstHediffOfDef(hd);
                if (ex == null) { ex = t.health.AddHediff(hd); ex.Severity = GameComponent_Kewei.AuraSeverity(); }
                else if (GameComponent_Kewei.AuraSeverity() > 1f && ex.Severity <= 1f) ex.Severity = 1.01f;
                var dis = ex.TryGetComp<HediffComp_Disappears>();
                if (dis != null) dis.ticksToDisappear = Props.tickInterval * 2;
            }
        }

        private string GetAuraDef()
        {
            // 以名字判定（生成入口统一命名）：颜知夏 → 闹书房；其余书灵（颜执中/殖民地书灵）→ 讲学
            string nick = Pawn.Name?.ToStringShort ?? "";
            if (nick.Contains("颜知夏") || nick.Contains("知夏") || nick.Contains("墨叽")) return "GoldenBooks_NoiseStudyAura";
            return "GoldenBooks_LectureAura";
        }

        // 再生
        private void TryRegenerate(Pawn p)
        {
            if (p.InAggroMentalState) return;
            if (p.health.hediffSet.BleedRateTotal > 0f) return;
            if (Find.TickManager.TicksGame - p.mindState.lastAttackTargetTick < 600) return;
            var hd = p.health.hediffSet.hediffs;
            for (int i = 0; i < hd.Count; i++)
            {
                var inj = hd[i] as Hediff_Injury;
                if (inj == null || inj.IsPermanent() || inj.Severity <= 0f) continue;
                inj.Heal(Props.regenAmount);
            }
        }

        // 怕水
        private void TryWaterFear(Pawn p)
        {
            HediffDef wd = DefDatabase<HediffDef>.GetNamedSilentFail("GoldenBooks_SoakedBook");
            if (wd == null) return;
            if (p.Map.weatherManager.RainRate > 0.05f && !p.Position.Roofed(p.Map))
            {
                Hediff ex = p.health.hediffSet.GetFirstHediffOfDef(wd);
                if (ex == null) ex = p.health.AddHediff(wd);
                var dis = ex.TryGetComp<HediffComp_Disappears>();
                if (dis != null) dis.ticksToDisappear = 1500;
            }
        }
    }

    // --- 书灵之书数据 comp（保存名字+人格+年龄+背景，供召还还原） ---
    public class GoldenBooksCompProperties_SpiritBookData : CompProperties
    {
        public GoldenBooksCompProperties_SpiritBookData() { this.compClass = typeof(GoldenBooksComp_SpiritBookData); }
    }

    public class GoldenBooksComp_SpiritBookData : ThingComp
    {
        public string spiritName = "";
        public string persona = "";
        public float biologicalAge = -1f;
        public string gender = "";
        public string childhoodDef = "";
        public string adulthoodDef = "";

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref spiritName, "spiritName", "");
            Scribe_Values.Look(ref persona, "persona", "");
            Scribe_Values.Look(ref biologicalAge, "biologicalAge", -1f);
            Scribe_Values.Look(ref gender, "gender", "");
            Scribe_Values.Look(ref childhoodDef, "childhoodDef", "");
            Scribe_Values.Look(ref adulthoodDef, "adulthoodDef", "");
        }
    }
}
