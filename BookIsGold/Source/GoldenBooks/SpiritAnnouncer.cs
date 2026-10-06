using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace GoldenBooksMod
{
    // --- 书灵播报：事件驱动的立绘弹窗（参照 RimTuber Window_AnnouncerDialog） ---

    public class SpiritAnnounceItem
    {
        public string speakerId;   // Yan / Mo / Colonist / RuYu
        public string text;
        public Pawn speakerPawn;   // 可空：在场书灵 pawn（用 PortraitsCache 立绘）
        public ThingDef speakerDef;    // 可空：书灵 def（用 uiIcon 静态立绘）
    }

    public static class SpiritAnnouncer
    {
        private static readonly Queue<SpiritAnnounceItem> queue = new Queue<SpiritAnnounceItem>();
        private static bool windowOpen;

        public static bool Enabled => GoldenBooksMod.settings == null || GoldenBooksMod.settings.whisperAnnouncerEnabled;

        public static void Announce(string speakerId, string text, Pawn speakerPawn = null, ThingDef speakerDef = null)
        {
            Announce(new SpiritAnnounceItem { speakerId = speakerId, text = text, speakerPawn = speakerPawn, speakerDef = speakerDef });
        }

        public static void Announce(SpiritAnnounceItem item)
        {
            if (!Enabled || item == null || string.IsNullOrEmpty(item.text)) return;
            lock (queue)
            {
                queue.Enqueue(item);
                if (queue.Count > 4) { while (queue.Count > 1) queue.Dequeue(); } // 只保留最新两条，防刷屏
            }
            TryShowNext();
        }

        public static int QueueCount { get { lock (queue) return queue.Count; } }

        public static SpiritAnnounceItem DequeueNext()
        {
            lock (queue) { return queue.Count > 0 ? queue.Dequeue() : null; }
        }
        public static void TryShowNext()
        {
            lock (queue)
            {
                if (windowOpen || queue.Count == 0) return;
                SpiritAnnounceItem item = queue.Dequeue();
                windowOpen = true;
                LongEventHandler.ExecuteWhenFinished(() =>
                    Find.WindowStack.Add(new Window_SpiritAnnouncer(item, () => { windowOpen = false; TryShowNext(); })));
            }
        }

        // --- 事件短句模板（双语） ---
        private static bool IsChinese => GameComponent_BookWhispers.IsChinese;

        private static ThingDef DefYan => DefDatabase<ThingDef>.GetNamedSilentFail("GoldenBooks_BookSpirit_Yanzhongzhong");
        private static ThingDef DefMo => DefDatabase<ThingDef>.GetNamedSilentFail("GoldenBooks_BookSpirit_Zhixia");

        public static void AnnounceRaid()
        {
            if (Rand.Bool) Announce("Yan", IsChinese ? "敌至。诸位，依阵而守，书卷随行。" : "Hostiles at the gates. Hold the line — the books march with you.", null, DefYan);
            else Announce("Mo", IsChinese ? "家人们！打架了！！都在点一手保护我方输出！(๑•̀ㅂ•́)و✧" : "Guys, we've got a fight on our hands!! Everybody protect our squishies! (๑•̀ㅂ•́)و✧", null, DefMo);
        }

        public static void AnnounceDeath(Pawn p)
        {
            if (Rand.Bool) Announce("Yan", IsChinese ? p.LabelShort + "，殁。金粟难赎，惟愿安息。" : p.LabelShort + " has fallen. All the millet in the world cannot read them back.", null, DefYan);
            else Announce("Mo", IsChinese ? "……" + p.LabelShort + "去很远的地方了。灯，我替你点着。" : "..." + p.LabelShort + " has gone somewhere far away. The lamp stays lit for you.", null, DefMo);
        }

        public static void AnnounceResearch(ResearchProjectDef def)
        {
            if (def == null) return;
            if (Rand.Bool) Announce("Yan", IsChinese ? "「" + def.label + "」之功乃成。学不可以已。" : "The study of '" + def.label + "' is complete. Learning never ends.", null, DefYan);
            else Announce("Mo", IsChinese ? "研究「" + def.label + "」完成！老板们脑子变好了！绝了！(๑•̀ㅂ•́)و✧" : "Research complete: '" + def.label + "'! Everybody's brains just got an upgrade! (๑•̀ㅂ•́)و✧", null, DefMo);
        }

        public static void AnnounceSpiritTurnedBook(Pawn spirit)
        {
            Announce(spirit.def.defName == "GoldenBooks_BookSpirit_Zhixia" ? "Mo" : "Yan",
                IsChinese
                    ? (spirit.def.defName == "GoldenBooks_BookSpirit_Zhixia"
                        ? "本姑娘先回书里避一避！等我回来接着闹！(๑•̀ㅂ•́)و✧"
                        : "化作书页，非是消散，是歇息。期日再召。")
                    : (spirit.def.defName == "GoldenBooks_BookSpirit_Zhixia"
                        ? "Ducking back into the book for a bit! Wait for my comeback!"
                        : "To turn into a page is not to perish — it is to rest. Recall me when you will."),
                spirit);
        }

        public static void AnnounceRecalled(Pawn spirit)
        {
            Announce(spirit.def.defName == "GoldenBooks_BookSpirit_Zhixia" ? "Mo" : "Yan",
                IsChinese ? "回来了。接下来的故事，继续一起写。" : "I am back. The rest of our story — we write it together.",
                spirit);
        }

        public static void AnnounceAscended(string name, Pawn spirit)
        {
            Announce("Yan", IsChinese ? "自今日始，" + name + " 亦是书中人。老朽收徒了。" : "From this day, " + name + " is one of the book. My first disciple.", spirit);
        }
    }

    public class Window_SpiritAnnouncer : Window
    {
        private static Rect lastRect = Rect.zero; // 记住玩家拖动后的位置

        private SpiritAnnounceItem item;
        private readonly Action onClosed;
        private string speakerName;
        private Color nameColor;

        private float typingTimer;
        private int visibleChars;
        private const float CharsPerSecond = 30f;
        private bool typedDone;
        private float lingerTimer;
        private Vector2 scrollPos = Vector2.zero;

        private const float PortraitSize = 130f;

        // 立绘缓存（懒加载）
        private static Texture2D _yanPortrait;
        private static Texture2D _moPortrait;

        private static Texture2D GetPortrait(string speakerId)
        {
            switch (speakerId)
            {
                case "Yan":
                    if (_yanPortrait == null) _yanPortrait = ContentFinder<Texture2D>.Get("UI/Lihui/zhizhong_lihui", false);
                    return _yanPortrait;
                case "Mo":
                    if (_moPortrait == null) _moPortrait = ContentFinder<Texture2D>.Get("UI/Lihui/zhixia_lihui", false);
                    return _moPortrait;
                default: return null;
            }
        }

        public override Vector2 InitialSize => new Vector2(470f, 250f);

        public Window_SpiritAnnouncer(SpiritAnnounceItem item, Action onClosed)
        {
            this.item = item;
            this.onClosed = onClosed;
            RefreshSpeaker();
            InitCommon();
        }

        private void InitCommon()
        {
            layer = WindowLayer.Dialog;
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = false;
            draggable = true;
            resizeable = false;
            closeOnAccept = false;
            closeOnCancel = false;
            focusWhenOpened = false;
            preventCameraMotion = false;
            // 首次右上角弹出；之后回到玩家上次拖动的位置
            windowRect = lastRect != Rect.zero ? lastRect : new Rect(UI.screenWidth - InitialSize.x - 24f, 90f, InitialSize.x, InitialSize.y);
        }

        protected override void SetInitialSizeAndPosition() { }

        private void RefreshSpeaker()
        {
            speakerName = item.speakerId == "Yan" ? "颜执中·砚翁" : item.speakerId == "Mo" ? "颜知夏·墨叽" : item.speakerId == "RuYu" ? "真·颜如玉" : "殖民地书灵";
            nameColor = item.speakerId == "Mo" ? new Color(0.55f, 0.72f, 0.9f) : new Color(0.85f, 0.72f, 0.42f);
            typingTimer = 0f;
            visibleChars = 0;
            typedDone = false;
            scrollPos = Vector2.zero;
        }

        private void ShowNext()
        {
            SpiritAnnounceItem next = SpiritAnnouncer.DequeueNext();
            if (next == null) { Close(); return; }
            item = next;
            RefreshSpeaker();
        }

        public override void DoWindowContents(Rect inRect)
        {
            float y = inRect.y;

            // 标题 + 下一条按钮（队列有货时）
            GUI.color = new Color(1f, 0.85f, 0.3f, 0.85f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(inRect.x, y, inRect.width - 90f, 16f), "GoldenBooks_AnnTitle".Translate());
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            if (SpiritAnnouncer.QueueCount > 0)
            {
                if (Widgets.ButtonText(new Rect(inRect.xMax - 88f, y - 4f, 88f, 24f), "下一条 ▶")) { ShowNext(); return; }
            }
            y += 18f;

            // 名字条
            GUI.color = nameColor;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 22f), speakerName);
            GUI.color = Color.white;
            y += 24f;

            float contentH = inRect.yMax - y - 8f;

            // 立绘：在场 pawn 用 PortraitsCache；否则 def.uiIcon（引擎加载，必命中）
            Rect portraitRect = new Rect(inRect.x, y, PortraitSize, Mathf.Min(PortraitSize, contentH));
            bool drewPortrait = false;
            // 专属立绘（爷孙/殖民地书灵）
            Texture2D customPortrait = GetPortrait(item.speakerId);
            if (customPortrait != null)
            {
                GUI.DrawTexture(portraitRect, customPortrait, ScaleMode.ScaleToFit);
                drewPortrait = true;
            }
            // 在场 pawn 用 PortraitsCache
            if (!drewPortrait && item.speakerPawn != null && !item.speakerPawn.Destroyed)
            {
                RenderTexture portrait = PortraitsCache.Get(item.speakerPawn, new Vector2(PortraitSize, PortraitSize), Rot4.South, new Vector3(0f, 0f, 0.3f), 2.2f);
                if (portrait != null) { GUI.DrawTexture(portraitRect, portrait, ScaleMode.ScaleToFit); drewPortrait = true; }
            }
            if (!drewPortrait && item.speakerDef != null && item.speakerDef.uiIcon != null)
            {
                GUI.color = item.speakerDef.uiIconColor;
                GUI.DrawTexture(portraitRect, item.speakerDef.uiIcon, ScaleMode.ScaleToFit);
                GUI.color = Color.white;
                drewPortrait = true;
            }
            if (drewPortrait) Widgets.DrawBox(portraitRect, 1);

            // 打字机文字（右侧滚动区）；播完停留后自动切下一条（队列有货时）
            Rect textRect = new Rect(portraitRect.xMax + 10f, y, inRect.width - PortraitSize - 20f, contentH);
            if (!typedDone)
            {
                typingTimer += Time.deltaTime;
                int target = Mathf.Min(item.text.Length, Mathf.FloorToInt(typingTimer * CharsPerSecond));
                if (target != visibleChars) { visibleChars = target; scrollPos.y = float.MaxValue; }
                if (visibleChars >= item.text.Length) typedDone = true;
            }
            else if (SpiritAnnouncer.QueueCount > 0)
            {
                // 自适应停留：短消息 4 秒，长消息按字数放宽（上限 12 秒）
                float dwell = Mathf.Clamp(item.text.Length / 20f, 4f, 12f);
                lingerTimer += Time.deltaTime;
                if (lingerTimer >= dwell) { lingerTimer = 0f; ShowNext(); return; }
            }
            string shown = item.text.Substring(0, visibleChars);
            float h = Text.CalcHeight(shown, textRect.width - 8f) + 4f;
            Rect view = new Rect(0f, 0f, textRect.width - 8f, h);
            Widgets.BeginScrollView(textRect, ref scrollPos, view);
            Widgets.Label(view, shown);
            Widgets.EndScrollView();
        }

        public override void PostClose()
        {
            base.PostClose();
            lastRect = windowRect; // 记住位置
            onClosed?.Invoke();
        }
    }
}
