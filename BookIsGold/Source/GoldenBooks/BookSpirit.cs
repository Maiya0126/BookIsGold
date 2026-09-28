using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace GoldenBooksMod
{
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
