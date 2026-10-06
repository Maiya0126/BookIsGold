using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using UnityEngine;

namespace GoldenBooksMod
{
    // --- 书灵本源：需求恒满（不食不眠不娱、心情恒定） ---
    public class HediffCompProperties_BookSpiritVitality : HediffCompProperties
    {
        public HediffCompProperties_BookSpiritVitality() { this.compClass = typeof(HediffComp_BookSpiritVitality); }
    }

    public class HediffComp_BookSpiritVitality : HediffComp
    {
        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);
            Pawn p = parent.pawn;
            if (p == null || p.Dead || p.needs == null) return;
            if (!p.IsHashIntervalTick(120)) return;
            if (p.needs.food != null) p.needs.food.CurLevelPercentage = 1f;
            if (p.needs.rest != null) p.needs.rest.CurLevelPercentage = 1f;
            if (p.needs.joy != null) p.needs.joy.CurLevelPercentage = 1f;
            if (p.needs.mood != null) p.needs.mood.CurLevelPercentage = 1f;
        }
    }

    // --- 书灵组件：光环 / 再生 / 怕水 ---
    public class CompProperties_BookSpirit : CompProperties
    {
        public float auraRadius = 9.9f;
        public string auraHediff;
        public string waterHediff;
        public float regenAmount = 1.2f;
        public int tickInterval = 150;

        public CompProperties_BookSpirit() { this.compClass = typeof(Comp_BookSpirit); }
    }

    public class Comp_BookSpirit : ThingComp
    {
        public CompProperties_BookSpirit Props => (CompProperties_BookSpirit)props;
        private Pawn Pawn => parent as Pawn;

        public override void CompTick()
        {
            base.CompTick();
            Pawn p = Pawn;
            if (p == null || p.Dead || p.Map == null) return;
            if (!p.IsHashIntervalTick(Props.tickInterval)) return;

            // 自挂"书灵本源"（覆盖旧存档与所有生成路径）
            HediffDef vit = DefDatabase<HediffDef>.GetNamedSilentFail("GoldenBooks_SpiritVitality");
            if (vit != null && p.health != null && p.health.hediffSet.GetFirstHediffOfDef(vit) == null)
                p.health.AddHediff(vit);

            ApplyAura(p);
            TryRegenerate(p);
            TryWaterFear(p);
        }

        // 光环：影响范围内的友方人类小人
        private void ApplyAura(Pawn p)
        {
            if (string.IsNullOrEmpty(Props.auraHediff)) return;
            HediffDef hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(Props.auraHediff);
            if (hediffDef == null) return;

            IReadOnlyList<Pawn> pawns = p.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn target = pawns[i];
                if (target == p || target.Dead || target.Downed) continue;
                if (!target.RaceProps.Humanlike) continue;
                if (target.Faction != Faction.OfPlayer && !target.IsPrisoner) continue;
                if (!target.Position.InHorDistOf(p.Position, Props.auraRadius)) continue;

                Hediff existing = target.health.hediffSet.GetFirstHediffOfDef(hediffDef);
                if (existing == null)
                {
                    existing = target.health.AddHediff(hediffDef);
                    existing.Severity = GameComponent_Kewei.AuraSeverity();
                }
                else if (GameComponent_Kewei.AuraSeverity() > 1f && existing.Severity <= 1f)
                {
                    existing.Severity = 1.01f;
                }
                HediffComp_Disappears dis = existing.TryGetComp<HediffComp_Disappears>();
                if (dis != null) dis.ticksToDisappear = Props.tickInterval * 2;
            }
        }

        // 再生：脱战缓慢恢复（流血或战斗中不减）
        private void TryRegenerate(Pawn p)
        {
            if (p.InAggroMentalState) return;
            if (p.health.hediffSet.BleedRateTotal > 0f) return;
            if (Find.TickManager.TicksGame - p.mindState.lastAttackTargetTick < 600) return;

            List<Hediff> hediffs = p.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff_Injury injury = hediffs[i] as Hediff_Injury;
                if (injury == null || injury.IsPermanent()) continue;
                if (injury.Severity <= 0f) continue;
                injury.Heal(Props.regenAmount);
            }
        }

        // 书灵灵能粒子效果（原版 PsycastAreaEffect 灵能旋涡）
        private void SpawnSpiritParticles(Pawn p)
        {
            if (Rand.Chance(0.25f))
            {
                Vector3 offset = new Vector3(Rand.Range(-0.4f, 0.4f), 0f, Rand.Range(-0.3f, 0.3f));
                FleckMaker.Static(p.DrawPos + offset, p.Map, FleckDefOf.PsycastAreaEffect, Rand.Range(0.3f, 0.6f));
            }
            if (Rand.Chance(0.1f))
            {
                FleckMaker.ThrowMetaIcon(p.Position, p.Map, FleckDefOf.Meditating);
            }
        }

        // 怕水：雨中且无屋顶 → 淋湿负面
        private void TryWaterFear(Pawn p)
        {
            if (string.IsNullOrEmpty(Props.waterHediff)) return;
            bool raining = p.Map.weatherManager.RainRate > 0.05f;
            bool exposed = !p.Position.Roofed(p.Map);
            HediffDef waterDef = DefDatabase<HediffDef>.GetNamedSilentFail(Props.waterHediff);

            if (raining && exposed && waterDef != null)
            {
                Hediff existing = p.health.hediffSet.GetFirstHediffOfDef(waterDef);
                if (existing == null) existing = p.health.AddHediff(waterDef);
                HediffComp_Disappears dis = existing.TryGetComp<HediffComp_Disappears>();
                if (dis != null) dis.ticksToDisappear = 1500;
            }
        }
    }
}
