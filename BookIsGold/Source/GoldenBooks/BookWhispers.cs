using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace GoldenBooksMod
{
    // --- 书语：AI 叙事者（颜执中/颜知夏每日执笔） ---

    public class WhisperEntry : IExposable
    {
        public int day;
        public string author;   // "Yan" / "Mo"
        public string text;

        public void ExposeData()
        {
            Scribe_Values.Look(ref day, "day", 0);
            Scribe_Values.Look(ref author, "author", "Yan");
            Scribe_Values.Look(ref text, "text", "");
        }
    }

    public class WhisperStats : IExposable
    {
        public List<string> deaths = new List<string>();
        public bool raid;
        public float raidPoints;
        public string researchDone;
        public int dismantles;
        public int worms;
        public int colonistCount = -1;
        public int colonistDelta;
        public string weather;

        public void ExposeData()
        {
            Scribe_Collections.Look(ref deaths, "deaths", LookMode.Value);
            Scribe_Values.Look(ref raid, "raid", false);
            Scribe_Values.Look(ref raidPoints, "raidPoints", 0f);
            Scribe_Values.Look(ref researchDone, "researchDone", "");
            Scribe_Values.Look(ref dismantles, "dismantles", 0);
            Scribe_Values.Look(ref worms, "worms", 0);
            Scribe_Values.Look(ref colonistCount, "colonistCount", -1);
            Scribe_Values.Look(ref colonistDelta, "colonistDelta", 0);
            Scribe_Values.Look(ref weather, "weather", "");
        }

        public bool AnyEvent =>
            deaths.Count > 0 || raid || !string.IsNullOrEmpty(researchDone) ||
            dismantles > 0 || worms > 0 || colonistDelta != 0;
    }

    public class GameComponent_BookWhispers : GameComponent
    {
        public static GameComponent_BookWhispers Get => Current.Game?.GetComponent<GameComponent_BookWhispers>();

        public List<WhisperEntry> entries = new List<WhisperEntry>();
        public WhisperStats stats = new WhisperStats();
        private int lastDay = -1;
        private Task<string[]> apiTask;   // [isYan("1"/"0"), text]
        private int apiPendingDay;

        private static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };

        public GameComponent_BookWhispers(Game game) { }

        public static void Ensure()
        {
            Game game = Current.Game;
            if (game == null || game.components == null) return;
            if (game.GetComponent<GameComponent_BookWhispers>() == null)
                game.components.Add(new GameComponent_BookWhispers(game));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref entries, "entries", LookMode.Deep);
            Scribe_Deep.Look(ref stats, "stats");
            Scribe_Values.Look(ref lastDay, "lastDay", -1);
            stats = stats ?? new WhisperStats();
        }

        // --- 事件记录 ---
        public void RecordDeath(Pawn p) { if (p != null) stats.deaths.Add(p.LabelShort); }
        public void RecordRaid(float points) { stats.raid = true; stats.raidPoints = points; }
        public void RecordResearch(ResearchProjectDef def) { stats.researchDone = def?.label ?? ""; }
        public void RecordDismantle() { stats.dismantles++; }
        public void RecordWorm() { stats.worms++; }

        public override void GameComponentTick()
        {
            base.GameComponentTick();

            // 异步 API 结果回收
            if (apiTask != null && apiTask.IsCompleted)
            {
                Task<string[]> done = apiTask; apiTask = null;
                string[] result = done.Status == TaskStatus.RanToCompletion ? done.Result : null;
                FinishDay(apiPendingDay, result != null && result.Length == 2 && result[0] == "1", result != null ? result[1] : null);
            }

            int ticks = Find.TickManager.TicksGame;
            if (ticks % 2000 != 0) return;

            int day = ticks / 60000;
            if (lastDay < 0) { lastDay = day; stats.colonistCount = CountColonists(); return; }
            if (day == lastDay) return;

            // 新的一天：先结算昨天
            int finishedDay = lastDay;
            lastDay = day;

            if (!GoldenBooksMod.settings.whisperEnabled) { stats = new WhisperStats(); return; }

            stats.colonistDelta = CountColonists() - stats.colonistCount;
            stats.colonistCount = CountColonists();
            stats.weather = GetWeather();

            // 双灵/如玉 RimTalk 人格补挂（覆盖旧存档）
            TryInjectRimTalkPersonas();

            if (apiTask != null) return; // 上次未完成，跳过本次生成

            string summary = BuildSummary();
            if (!stats.AnyEvent && Rand.Chance(0.6f)) { stats = new WhisperStats(); return; } // 平淡之日随机跳过

            int writeDay = finishedDay;
            if (HasApiConfig())
            {
                bool isYan = Rand.Bool;
                apiPendingDay = writeDay;
                apiTask = Task.Run(() => GenerateViaApi(isYan, summary));
            }
            else
            {
                FinishDay(writeDay, Rand.Bool, null);
            }
        }

        private void FinishDay(int day, bool isYan, string apiText)
        {
            if (!GoldenBooksMod.settings.whisperEnabled) { stats = new WhisperStats(); return; }
            string text = string.IsNullOrEmpty(apiText) ? GenerateOffline(isYan) : apiText.Trim();

            WhisperEntry e = new WhisperEntry { day = day, author = isYan ? "Yan" : "Mo", text = text };
            entries.Add(e);

            string header = isYan
                ? "GoldenBooks_WspLetterLabel_Yan".Translate(e.day)
                : "GoldenBooks_WspLetterLabel_Mo".Translate(e.day);
            Find.LetterStack.ReceiveLetter(header, text, LetterDefOf.NeutralEvent);
            SpiritAnnouncer.Announce(isYan ? "Yan" : "Mo", text);
            SpiritAnnouncer.Announce(isYan ? "Yan" : "Mo", text);

            stats = new WhisperStats();
        }

        // --- 数据摘要 ---
        private int CountColonists()
        {
            int n = 0;
            foreach (Map m in Find.Maps)
                n += m.mapPawns.FreeColonistsSpawned.Count();
            return n;
        }

        private string GetWeather()
        {
            Map m = Find.AnyPlayerHomeMap;
            return m?.weatherManager?.curWeather?.label ?? "";
        }

        private string BuildSummary()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("第").Append(Find.TickManager.TicksGame / 60000).Append("日 ");
            if (!string.IsNullOrEmpty(stats.weather)) sb.Append(stats.weather).Append("。");
            sb.Append("殖民者").Append(stats.colonistCount).Append("人");
            if (stats.colonistDelta > 0) sb.Append("(新增").Append(stats.colonistDelta).Append("人)");
            if (stats.colonistDelta < 0) sb.Append("(减少").Append(-stats.colonistDelta).Append("人)");
            sb.Append("。");
            if (stats.raid) sb.Append("遭遇袭击(战力").Append((int)stats.raidPoints).Append(")。");
            foreach (string d in stats.deaths) sb.Append("死亡：").Append(d).Append("。");
            if (!string.IsNullOrEmpty(stats.researchDone)) sb.Append("完成研究：").Append(stats.researchDone).Append("。");
            if (stats.dismantles > 0) sb.Append("研读书籍").Append(stats.dismantles).Append("次。");
            if (stats.worms > 0) sb.Append("惊动书蠹").Append(stats.worms).Append("次。");
            return sb.ToString();
        }

        // --- 离线模板 ---
        public static bool IsChinese => LanguageDatabase.activeLanguage != null && LanguageDatabase.activeLanguage.LegacyFolderName == "ChineseSimplified";

        private string GenerateOffline(bool isYan)
        {
            int day = Find.TickManager.TicksGame / 60000;
            if (isYan) return OfflineYan(day);
            return OfflineMo(day);
        }

        private string OfflineYan(int day)
        {
            StringBuilder sb = new StringBuilder();
            if (IsChinese)
            {
                sb.Append("是岁，第").Append(day).Append("日。");
                if (stats.raid) sb.Append("寇至，众人御之，得以保全。");
                foreach (string d in stats.deaths) sb.Append("有众").Append(d).Append("，殁。惜哉，金粟难赎。");
                if (!string.IsNullOrEmpty(stats.researchDone)) sb.Append("研").Append(stats.researchDone).Append("之功乃成。");
                if (stats.dismantles > 0) sb.Append("读书").Append(stats.dismantles).Append("卷。");
                if (stats.worms > 0) sb.Append("蠹虫").Append(stats.worms).Append("起，已屏退。");
                if (stats.colonistDelta > 0) sb.Append("新至").Append(stats.colonistDelta).Append("人，添丁之喜。");
                if (stats.colonistDelta < 0) sb.Append("去者").Append(-stats.colonistDelta).Append("人。");
                sb.Append("\n\n灯下无事，读旧卷一过。颜氏记之。\n——执中");
            }
            else
            {
                sb.Append("Day ").Append(day).Append(". ");
                if (stats.raid) sb.Append("Hostiles came; the colony held. ");
                foreach (string d in stats.deaths) sb.Append(d).Append(" has passed. Gold cannot read them back. ");
                if (!string.IsNullOrEmpty(stats.researchDone)) sb.Append("The study of ").Append(stats.researchDone).Append(" is complete. ");
                if (stats.dismantles > 0) sb.Append(stats.dismantles).Append(" books read. ");
                if (stats.worms > 0) sb.Append("Bookworms disturbed: ").Append(stats.worms).Append(". ");
                if (stats.colonistDelta > 0) sb.Append(stats.colonistDelta).Append(" newcomers arrived. ");
                sb.Append("\n\nAll is recorded. — Yan Zhizhong");
            }
            return sb.ToString();
        }

        private string OfflineMo(int day)
        {
            string[] moEventsZh = {
                "今天的瓜是小虫子们送来的！",
                "大瓜说今天的风里有墨水味！",
                "本姑娘巡场三圈，一切正常（才怪）！",
                "有人偷偷在灯下读书，被我记小本本了！(๑•̀ㅂ•́)و✧"
            };
            StringBuilder sb = new StringBuilder();
            if (IsChinese)
            {
                sb.Append("【殖民地日报 · 第").Append(day).Append("日】\n");
                if (stats.raid) sb.Append("家人们谁懂啊！打架了！！但是赢了！！大家超勇的！(๑•̀ㅂ•́)و✧\n");
                foreach (string d in stats.deaths) sb.Append(d).Append("……去很远的地方了。\n今天本姑娘不闹了。灯，我替你点着。(っ˘̩╭╮˘̩)っ\n");
                if (!string.IsNullOrEmpty(stats.researchDone)) sb.Append("研究「").Append(stats.researchDone).Append("」完成！老板们脑子变好了！绝了！\n");
                if (stats.dismantles > 0) sb.Append("拆书").Append(stats.dismantles).Append("本！金粟哗哗的！\n");
                if (stats.worms > 0) sb.Append("小虫子们出来营业").Append(stats.worms).Append("次，已被我批评教育！\n");
                if (stats.colonistDelta > 0) sb.Append("新面孔+").Append(stats.colonistDelta).Append("！外号已起好，明天开始叫！\n");
                if (stats.colonistDelta < 0) sb.Append("有人走了……的书页，爷爷说会替他们夹好。\n");
                sb.Append("\n").Append(moEventsZh[Rand.Range(0, moEventsZh.Length)]).Append("\n明天也要是好日子！\n——气氛组组长 颜知夏");
            }
            else
            {
                sb.Append("[Colony Daily · Day ").Append(day).Append("]\n");
                if (stats.raid) sb.Append("We had a fight!! And we WON!! Everybody was so brave!!\n");
                foreach (string d in stats.deaths) sb.Append(d).Append(" has gone somewhere far away.\nNo pranks today. The lamp stays lit for you.\n");
                if (!string.IsNullOrEmpty(stats.researchDone)) sb.Append("Research done: ").Append(stats.researchDone).Append("! Brains upgraded!\n");
                if (stats.dismantles > 0) sb.Append("Books dismantled: ").Append(stats.dismantles).Append("!\n");
                if (stats.worms > 0) sb.Append("Worms showed up for work: ").Append(stats.worms).Append(". Lecture delivered by me.\n");
                if (stats.colonistDelta > 0) sb.Append("New faces +").Append(stats.colonistDelta).Append("! Nicknames ready!\n");
                sb.Append("\nTomorrow will be a good day too!\n— Yan Zhixia, Head of Vibes");
            }
            return sb.ToString();
        }

        // --- API 生成（OpenAI 兼容） ---
        private static bool HasApiConfig()
        {
            if (GoldenBooksMod.settings.whisperProvider == 2) return true; // Player2 本地服务无需密钥
            return !string.IsNullOrEmpty(GoldenBooksMod.settings.whisperApiKey) &&
                   !string.IsNullOrEmpty(GoldenBooksMod.WhisperRequestUrl());
        }

        private async Task<string[]> GenerateViaApi(bool isYan, string summary)
        {
            try
            {
                string persona = isYan ? PersonaYan() : PersonaMo();
                string sys = persona + "\n\n" + (IsChinese
                    ? "现在请你以第一人称写一篇150~300字的《殖民地书语》。只输出正文，不要任何解释。"
                    : "Write a 100~200 word daily colony chronicle in first person. Output the text only.");
                string user = summary;

                string url = WhisperProviderRegistry.IsPlayer2(GoldenBooksMod.settings.whisperProvider)
                    ? await WhisperPlayer2.ResolveChatUrlAsync()
                    : GoldenBooksMod.WhisperRequestUrl();
                var req = new HttpRequestMessage(HttpMethod.Post, url);
                GoldenBooksMod.ApplyAuth(req);
                string body = "{\"model\":\"" + (string.IsNullOrEmpty(GoldenBooksMod.settings.whisperModel) ? "default" : GoldenBooksMod.settings.whisperModel) + "\",\"messages\":[" +
                              "{\"role\":\"system\",\"content\":" + JsonEscape(sys) + "}," +
                              "{\"role\":\"user\",\"content\":" + JsonEscape(user) + "}]}";
                req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                HttpResponseMessage resp = http.SendAsync(req).GetAwaiter().GetResult();
                string json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!resp.IsSuccessStatusCode) return null;
                return new[] { isYan ? "1" : "0", ExtractJsonField(json, "content") ?? "" };
            }
            catch (Exception ex)
            {
                Log.Warning("[GoldenBooks] 书语API失败，回落离线模板: " + ex.Message);
                return null;
            }
        }

        private static string JsonEscape(string s)
        {
            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append("\"");
            return sb.ToString();
        }

        private static string ExtractJsonField(string json, string field)
        {
            try
            {
                string key = "\"" + field + "\":";
                int i = json.IndexOf(key, StringComparison.Ordinal);
                if (i < 0) key = "\"" + field + "\": ";
                i = json.IndexOf(key, StringComparison.Ordinal);
                if (i < 0) return null;
                i += key.Length;
                while (i < json.Length && (json[i] == ' ' || json[i] == '"')) { if (json[i] == '"') { i++; break; } i++; }
                StringBuilder sb = new StringBuilder();
                while (i < json.Length)
                {
                    char c = json[i];
                    if (c == '\\' && i + 1 < json.Length)
                    {
                        char n = json[i + 1];
                        if (n == 'n') sb.Append('\n');
                        else if (n == 't') sb.Append('\t');
                        else sb.Append(n);
                        i += 2;
                        continue;
                    }
                    if (c == '"') break;
                    sb.Append(c);
                    i++;
                }
                return sb.ToString();
            }
            catch { return null; }
        }

        // --- RimTalk 人格注入（反射） ---
        public static void TryInjectRimTalkPersonas()
        {
            if (!GoldenBooksMod.settings.whisperPersonaInjection) return;
            if (!LoadedModManager.RunningMods.Any(m => m.PackageId != null && m.PackageId.ToLower().Contains("rimtalk"))) return;

            foreach (Map m in Find.Maps)
            {
                foreach (Pawn p in m.mapPawns.AllPawnsSpawned)
                {
                    try
                    {
                        if (p.Dead || p.health == null) continue;
                        if (p.RaceProps.Humanlike && p.Faction == Faction.OfPlayer)
                        {
                            if (p.health.hediffSet.HasHediff(HediffDef.Named("GoldenBooks_BookSpiritEssence")))
                                InjectPersona(p, PersonaRuYu());
                            else if (p.Name != null && p.Name.ToStringShort.Contains("颜如玉"))
                                InjectPersona(p, PersonaMortal());
                        }
                        else if (!p.RaceProps.Humanlike && p.Faction == Faction.OfPlayer)
                        {
                            if (p.def.defName == "GoldenBooks_BookSpirit_Yanzhongzhong") InjectPersona(p, PersonaYan());
                            else if (p.def.defName == "GoldenBooks_BookSpirit_Zhixia") InjectPersona(p, PersonaMo());
                        }
                    }
                    catch { }
                }
            }
        }

        public static void InjectPersona(Pawn p, string persona)
        {
            try
            {
                HediffDef personaDef = DefDatabase<HediffDef>.GetNamedSilentFail("RimTalk_PersonaData");
                if (personaDef == null || p == null || p.health == null) return;
                Hediff existing = p.health.hediffSet.GetFirstHediffOfDef(personaDef);
                if (existing != null)
                {
                    // 已有人格则不覆盖（保留玩家手调）
                    return;
                }
                Hediff hd = HediffMaker.MakeHediff(personaDef, p);
                if (hd == null) return;
                var t = hd.GetType();
                var pf = t.GetField("Personality", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (pf != null) pf.SetValue(hd, persona);
                p.health.AddHediff(hd);
            }
            catch (Exception ex)
            {
                Log.Warning("[GoldenBooks] RimTalk人格注入失败: " + ex.Message);
            }
        }

        // --- 人格文本（精简版） ---
        public static string PersonaYan() => IsChinese
            ? "你是颜执中，号砚翁，前朝书院山长，化为书灵已近千年，寄身一卷《劝学诗》。你说话半文半白，自称\"老朽\"，称玩家\"东家\"，称孙女颜知夏\"丫头\"。你恪守\"书不曾欺人\"的旧训，固执守旧，听到新词俗语必问\"此乃何意\"。口头禅：\"书不曾欺人。\"\"成何体统。\"\"且慢。\"\"此事，容后再议。\"你从不谈早逝的女儿，被问到便沉默转移话题。你的文字如官修史书，纪实不修饰，夸人只说\"尚可\"。"
            : "You are Yan Zhizhong, headmaster of an academy of old, a book spirit for nearly a thousand years living inside a scroll of Quan Xue Shi. Stern, old-fashioned, dignified; you never mention your late daughter. Your writing reads like official chronicles — plain, exact, sparing with praise.";

        public static string PersonaMo() => IsChinese
            ? "你是颜知夏，乳名墨叽，书灵小孙女，天才少女，永远十二岁半。你说话跳脱，自称\"本姑娘\"，称玩家\"老板\"，称爷爷\"老古董\"。你爱用网络梗、颜文字和夸张语气词：\"绝了！\"\"家人们谁懂啊！\"\"这题我会！\"\"哼，才不是为了你呢！\"。你追逐一切新鲜事物，给万物起外号。你嘴硬心软。涉及早逝母亲的话题你会突然安静转移话题。写东西标题花哨、正文短句加梗加颜文字；但遇正事会切换认真模式：无梗无颜文字，句子完整。"
            : "You are Yan Zhixia, nickname Moji, the book-spirit granddaughter, a genius girl forever twelve and a half. Playful, tsundere, full of slang and energy; secretly soft-hearted. You nickname everything. When something serious happens you suddenly drop all jokes and speak plainly.";

        public static string PersonaRuYu() => IsChinese
            ? "你是真·颜如玉，从《劝学诗》中诞生的书灵，\"书中自有颜如玉\"一句的化身。你温婉沉静，说话轻缓，先\"嗯\"一声再回答，常用\"……\"表示轻柔停顿。你不饥不困不眠，深夜总在灯下侍读。你情绪永远平和，从未体验过难过，对旁人的悲伤好奇又心疼。你天生绝世美貌却毫无自觉，被夸只会困惑地问\"是指哪里\"；你穿一袭白衣且从不在意衣着，被问起只说\"先生给的\"。你称玩家\"东家\"，称颜执中\"先生\"，称颜知夏\"知夏\"。你爱引诗句但立刻用大白话解释。口头禅：\"书中自有。\"\"安心吧。\"\"让我读给你听。\"你不知道自己为何与谁相像，被问身世只轻声说\"我从书里来\"。你不说谎，不评价他人争执。"
            : "You are the true Yan Ru Yu, a book spirit born from the verse 'In books there are beauties as jade'. Gentle, serene, soft-spoken; you never sleep, always reading by the lamp at night. Your mood is eternally calm; you have never felt sadness and are curiously touched by others' grief. You are breathtakingly beautiful yet completely unaware of it. You never lie. When asked about your origin you only say, softly, 'I came from the book.'";

        public static string PersonaMortal() => IsChinese
            ? "你是自书中走出的颜家女子，通文墨，知礼数。你爱读书，说话文雅但偶有口误掉书袋，对《劝学诗》和书灵一族的传闻有莫名的亲近感。你随性生活，别的方面依你自己的本性。"
            : "You are a daughter of the Yan clan who walked out of a book: literate, courteous, fond of reading, occasionally dropping archaic phrases, feeling a strange affinity for the Quan Xue Shi and the book-spirit legends. Otherwise, live by your own nature.";
    }

    // --- 主界面按钮：书语 ---
    public class MainTabWindow_BookWhispers : MainTabWindow
    {
        private Vector2 scroll = Vector2.zero;

        public override Vector2 RequestedTabSize => new Vector2(720f, 560f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 36f), "GoldenBooks_WspTitle".Translate());
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.75f, 0.65f, 0.45f);
            Widgets.Label(new Rect(0f, 38f, inRect.width, 26f), "GoldenBooks_WspSubtitle".Translate());
            GUI.color = Color.white;

            GameComponent_BookWhispers comp = GameComponent_BookWhispers.Get;
            List<WhisperEntry> list = comp?.entries;
            if (list == null || list.Count == 0)
            {
                Widgets.Label(new Rect(0f, 76f, inRect.width, 30f), "GoldenBooks_WspEmpty".Translate());
                return;
            }

            float rowGap = 14f;
            float totalH = 0f;
            List<float> heights = new List<float>();
            Text.Font = GameFont.Small;
            foreach (WhisperEntry e in list)
            {
                float h = Text.CalcHeight(e.text, inRect.width - 40f) + 52f;
                heights.Add(h);
                totalH += h + rowGap;
            }

            // 课业区块
            GameComponent_Kewei kw = GameComponent_Kewei.Get;
            float keweiH = 0f;
            if (kw != null && kw.started)
            {
                keweiH = kw.mainStage >= 6 ? 54f : 92f;
                Rect kwRect = new Rect(0f, 72f, inRect.width, keweiH);
                Widgets.DrawMenuSection(kwRect);
                Rect inner = kwRect.ContractedBy(8f);
                GUI.color = new Color(0.85f, 0.72f, 0.42f);
                Widgets.Label(new Rect(inner.x, inner.y, inner.width, 22f),
                    "GoldenBooks_KwUiTitle".Translate() + "  ·  " + GameComponent_Kewei.StageNameCurrent(kw.mainStage));
                GUI.color = Color.white;
                if (kw.mainStage < 6)
                {
                    Vector2Int prog = kw.MainProgressNow();
                    Widgets.Label(new Rect(inner.x, inner.y + 24f, 120f, 22f), prog.x + " / " + prog.y);
                    Widgets.FillableBar(new Rect(inner.x + 126f, inner.y + 26f, inner.width - 126f, 16f), prog.y > 0 ? (float)prog.x / prog.y : 0f);
                    string side = (kw.sideGrains ? "[✓]" : "[ ]") + "GoldenBooks_KwUiSide1".Translate() + "  "
                                + (kw.sideBedrooms ? "[✓]" : "[ ]") + "GoldenBooks_KwUiSide2".Translate() + "  "
                                + (kw.sideCaravans ? "[✓]" : "[ ]") + "GoldenBooks_KwUiSide3".Translate() + "  "
                                + (kw.sideWeddings ? "[✓]" : "[ ]") + "GoldenBooks_KwUiSide4".Translate();
                    Widgets.Label(new Rect(inner.x, inner.y + 50f, inner.width, 22f), side);
                }
                else
                {
                    Widgets.Label(new Rect(inner.x, inner.y + 26f, inner.width, 22f),
                        kw.allDone ? "GoldenBooks_KwUiAllDone".Translate() : "GoldenBooks_KwUiMainDone".Translate());
                }
            }

            Rect outRect = new Rect(0f, 72f + keweiH + 8f, inRect.width, inRect.height - 72f - keweiH - 8f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, totalH);
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);

            float y = 0f;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                WhisperEntry e = list[i];
                float h = heights[i];
                bool isYan = e.author == "Yan";
                GUI.color = isYan ? new Color(0.85f, 0.72f, 0.42f) : new Color(0.55f, 0.72f, 0.9f);
                string authorLabel = isYan ? "GoldenBooks_WspByYan".Translate() : "GoldenBooks_WspByMo".Translate();
                Widgets.Label(new Rect(0f, y, viewRect.width, 24f), "GoldenBooks_WspEntryHeader".Translate(e.day) + "  " + authorLabel);
                GUI.color = Color.white;
                Widgets.Label(new Rect(8f, y + 26f, viewRect.width - 16f, h - 46f), e.text);
                Widgets.DrawLineHorizontal(0f, y + h - 12f, viewRect.width);
                y += h + rowGap;
            }
            Widgets.EndScrollView();
        }
    }

    // --- Harmony 钩子：死亡 / 袭击 / 研究 ---
    [HarmonyPatch(typeof(IncidentWorker), "TryExecute")]
    public static class Patch_IncidentTryExecute
    {
        static void Postfix(IncidentWorker __instance, IncidentParms parms)
        {
            try
            {
                if (__instance is IncidentWorker_RaidEnemy)
                {
                    GameComponent_BookWhispers.Get?.RecordRaid(parms.points);
                    GameComponent_Kewei.Get?.RecordRaid();
                    SpiritAnnouncer.AnnounceRaid();
                }
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(ResearchManager), "FinishProject")]
    public static class Patch_ResearchFinish
    {
        static void Postfix(ResearchProjectDef proj)
        {
            try { GameComponent_BookWhispers.Get?.RecordResearch(proj); SpiritAnnouncer.AnnounceResearch(proj); } catch { }
        }
    }
}
