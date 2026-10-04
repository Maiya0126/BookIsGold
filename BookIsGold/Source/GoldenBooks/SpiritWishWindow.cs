using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using System.Threading.Tasks;
using Verse;

namespace GoldenBooksMod
{
    // --- 书灵许愿窗口（参照 RimTuber CallIn 模式） ---
    public class Window_SpiritWish : Window
    {
        private string wishInput = "";
        private string lastReply = "";
        private string lastSpeaker;
        private bool lastApproved;
        private bool hasResult;
        private bool waiting;
        private Vector2 historyScroll = Vector2.zero;
        private readonly List<string> history = new List<string>();

        public override Vector2 InitialSize => new Vector2(520f, 560f);

        public Window_SpiritWish()
        {
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = false;
            draggable = true;
            resizeable = false;
            preventCameraMotion = false;
        }

        private string SpeakerName(string id) => id == "Yan" ? "颜执中·砚翁" : id == "Mo" ? "颜知夏·墨叽" : "书灵";

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width - 24f, 34f), "GoldenBooks_WishTitle".Translate());
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.75f, 0.65f, 0.45f);
            Widgets.Label(new Rect(0f, 36f, inRect.width, 24f), "GoldenBooks_WishSubtitle".Translate());
            GUI.color = Color.white;

            float y = 66f;

            // 冷却/次数状态
            int used = GameComponent_WhisperWish.WishesUsedToday();
            int limit = GoldenBooksMod.settings.wishDailyLimit;
            GUI.color = used >= limit ? new Color(0.8f, 0.5f, 0.4f) : new Color(0.75f, 0.65f, 0.45f);
            Widgets.Label(new Rect(0f, y, inRect.width, 22f),
                "GoldenBooks_WishStatus".Translate(used, limit, SpiritWish.FragmentCostText(used)));
            GUI.color = Color.white;
            y += 28f;

            // 预设按钮网格（4 列 × 3 行）
            string[] ids = SpiritWish.PresetIds();
            float bw = (inRect.width - 30f) / 4f;
            for (int i = 0; i < ids.Length; i++)
            {
                int row = i / 4, col = i % 4;
                Rect b = new Rect(col * (bw + 10f), y + row * 34f, bw, 30f);
                string name = SpiritWish.PresetName(ids[i]);
                if (Widgets.ButtonText(b, name))
                {
                    SubmitWish(SpiritWish.PresetIdOfIndex(i), name);
                    return;
                }
            }
            y += 34f * Mathf.CeilToInt(ids.Length / 4f) + 10f;

            // 自由输入（API 可用时）
            if (GameComponent_BookWhispers.HasApiConfig())
            {
                Widgets.Label(new Rect(0f, y, 120f, 26f), "GoldenBooks_WishFree".Translate());
                wishInput = Widgets.TextEntryLabeled(new Rect(120f, y, inRect.width - 240f, 26f), "", wishInput);
                if (Widgets.ButtonText(new Rect(inRect.xMax - 110f, y, 110f, 26f), "GoldenBooks_WishSend".Translate()))
                {
                    if (!string.IsNullOrEmpty(wishInput.Trim()))
                    {
                        SubmitWish("free:" + wishInput.Trim(), wishInput.Trim());
                        wishInput = "";
                    }
                }
                y += 34f;
            }
            else
            {
                GUI.color = new Color(0.6f, 0.6f, 0.6f);
                Widgets.Label(new Rect(0f, y, inRect.width, 22f), "GoldenBooks_WishFreeHint".Translate());
                GUI.color = Color.white;
                y += 28f;
            }

            // 回复区
            Rect replyRect = new Rect(0f, y + 6f, inRect.width, inRect.yMax - y - 14f);
            Widgets.DrawMenuSection(replyRect);
            if (waiting)
            {
                Widgets.Label(replyRect.ContractedBy(10f), "GoldenBooks_WishWaiting".Translate());
                return;
            }
            if (hasResult)
            {
                GUI.color = lastApproved ? new Color(0.6f, 0.9f, 0.6f) : new Color(0.9f, 0.6f, 0.5f);
                Widgets.Label(new Rect(replyRect.x + 10f, replyRect.y + 8f, replyRect.width - 20f, 24f),
                    (lastApproved ? "✓ " : "✗ ") + SpeakerName(lastSpeaker));
                GUI.color = Color.white;
                float h = Text.CalcHeight(lastReply, replyRect.width - 20f);
                Rect view = new Rect(0f, 0f, replyRect.width - 20f, h);
                Rect scrollRect = new Rect(replyRect.x + 10f, replyRect.y + 34f, replyRect.width - 20f, replyRect.height - 44f);
                Widgets.BeginScrollView(scrollRect, ref historyScroll, view);
                Widgets.Label(view, lastReply);
                Widgets.EndScrollView();
            }
            else
            {
                GUI.color = new Color(0.7f, 0.65f, 0.5f);
                Widgets.Label(replyRect.ContractedBy(10f), "GoldenBooks_WishHint".Translate());
                GUI.color = Color.white;
            }
        }

        private void SubmitWish(string wishId, string displayName)
        {
            // 冷却检查
            if (!GameComponent_WhisperWish.CanWishToday())
            {
                Messages.Message("GoldenBooks_WishCooldown".Translate(GoldenBooksMod.settings.wishDailyLimit), MessageTypeDefOf.RejectInput);
                return;
            }

            bool isYan = Rand.Bool;
            string speakerId = isYan ? "Yan" : "Mo";
            int wishIndex = GameComponent_WhisperWish.WishesUsedToday();

            // 材料消耗（第 2 次起）
            int fragCost = SpiritWish.FragmentCost(wishIndex);
            Map map = Find.AnyPlayerHomeMap;
            if (fragCost > 0 && map != null && !ConsumeFragments(map, fragCost))
            {
                Messages.Message("GoldenBooks_WishNoFragments".Translate(fragCost), MessageTypeDefOf.RejectInput);
                return;
            }

            // 执笔人随机播报窗（即时反应）
            string wishName = displayName;
            SpiritAnnouncer.Announce(speakerId,
                isYan
                    ? (GameComponent_BookWhispers.IsChinese ? "东家所求，老朽思量。" : "A request, master? Let me think.")
                    : (GameComponent_BookWhispers.IsChinese ? "许愿？包在本姑娘身上！" : "A wish? Leave it to me!"),
                null);

            waiting = true;
            hasResult = false;

            // 异步判定（API 或回落离线）
            Task.Run(async () =>
            {
                SpiritWish.WishResult result = null;
                if (GameComponent_BookWhispers.HasApiConfig())
                {
                    string wishText = wishId.StartsWith("free:") ? wishId.Substring(5) : SpiritWish.PresetName(wishId);
                    result = await SpiritWish.JudgeViaApi(wishText, speakerId);
                }
                SpiritWish.WishResult final = result ?? SpiritWish.JudgeOffline(wishId, speakerId, wishIndex);

                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    waiting = false;
                    hasResult = true;
                    lastSpeaker = speakerId;
                    lastApproved = final.approved;
                    lastReply = final.reason;

                    // 记录进书语 + 次数
                    string record = (final.approved ? "✓" : "✗") + " " + SpeakerName(speakerId) + "：「" + wishName + "」";
                    GameComponent_WhisperWish.RegisterWish(record);

                    // 执行
                    if (final.approved)
                    {
                        if (final.calmDay)
                        {
                            Messages.Message("GoldenBooks_WishCalm".Translate(SpeakerName(lastSpeaker)), MessageTypeDefOf.PositiveEvent);
                        }
                        else if (final.customInspiration)
                        {
                            SpiritWish.ExecuteInspiration();
                        }
                        else if (!string.IsNullOrEmpty(final.incidentDef))
                        {
                            if (!SpiritWish.ExecuteIncident(final.incidentDef, final.pointsFactor))
                                Messages.Message("GoldenBooks_WishExecuteFail".Translate(), MessageTypeDefOf.NeutralEvent);
                        }
                        // 成功播报（与判定播报合流）
                        SpiritAnnouncer.Announce(speakerId, final.reason, null);
                    }
                    else
                    {
                        SpiritAnnouncer.Announce(speakerId, final.reason, null);
                    }
                });
            });
        }

        private static bool ConsumeFragments(Map map, int need)
        {
            int found = 0;
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableAlways))
                if (t.def.defName == "GoldenBooks_BookFragment") found += t.stackCount;
            if (found < need) return false;
            int left = need;
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableAlways).ToList())
            {
                if (left <= 0) break;
                if (t.def.defName != "GoldenBooks_BookFragment") continue;
                int take = Math.Min(left, t.stackCount);
                t.SplitOff(take).Destroy();
                left -= take;
            }
            return true;
        }
    }
}
