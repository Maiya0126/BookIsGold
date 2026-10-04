using System;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace GoldenBooksMod
{
    // --- 书灵许愿：玩家提出需求，执笔书灵以人设判定并安排事件 ---

    public static class SpiritWish
    {
        // 核心预设 12 项（离线规则引擎也稳定）
        public static readonly (string id, string incidentDef, float pointsFactor, bool danger)[] Presets =
        {
            ("raidBig",     "RaidEnemy",                  1.0f, true),
            ("raidSmall",   "RaidEnemy",                  0.5f, true),
            ("manhunter",   "ManhunterPack",              1.0f, true),
            ("caravan",     "TraderCaravanArrival",       0f,   false),
            ("visitor",     "VisitorGroup",               0f,   false),
            ("wanderer",    "WandererJoin",               0f,   false),
            ("podCrash",    "ResourcePodCrash",           0f,   false),
            ("farmAnimals", "FarmAnimalsWanderIn",        0f,   false),
            ("bookBox",     "GoldenBooks_LostBookBox",    0f,   false),
            ("wormSwarm",   "GoldenBooks_WormSwarm",      0f,   true),
            ("inspiration", null,                         0f,   false),
            ("calmDay",     null,                         0f,   false)
        };

        // 扩展白名单（自由输入映射用，原版事件）
        public static readonly string[] ExtendedWhitelist =
        {
            "RaidEnemy", "ManhunterPack", "HerdMigration", "ColdSnap", "HeatWave", "Flashstorm",
            "SolarEclipse", "Aurora", "ToxicFallout", "VolcanicWinter", "ResourcePodCrash",
            "WandererJoin", "FarmAnimalsWanderIn", "TraderCaravanArrival", "VisitorGroup", "Party"
        };

        public static string[] PresetIds() => Presets.Select(p => p.id).ToArray();

        public static string PresetName(string id)
        {
            bool zh = GameComponent_BookWhispers.IsChinese;
            switch (id)
            {
                case "raidBig": return zh ? "来一场大袭击" : "A big raid";
                case "raidSmall": return zh ? "来一场小袭击" : "A small raid";
                case "manhunter": return zh ? "随机大型兽群来袭" : "Aggressive beast pack";
                case "caravan": return zh ? "商队来访" : "Trade caravan";
                case "visitor": return zh ? "访客到访" : "Visitors";
                case "wanderer": return zh ? "新人加入" : "A wanderer joins";
                case "podCrash": return zh ? "资源舱坠落" : "Resource pod crash";
                case "farmAnimals": return zh ? "农场动物造访" : "Farm animals wander in";
                case "bookBox": return zh ? "落难书箱" : "Lost crate of books";
                case "wormSwarm": return zh ? "书蠹出没" : "Bookworms on the move";
                case "inspiration": return zh ? "灵感顿悟" : "A flash of inspiration";
                case "calmDay": return zh ? "平静一日" : "A calm day";
                default: return id;
            }
        }

        public static string PresetIdOfIndex(int i) => Presets[i].id;

        // 材料消耗：第1次免费，第2次残卷×3，第3次残卷×6
        public static int FragmentCost(int wishIndex) => wishIndex <= 0 ? 0 : wishIndex * 3;

        public static string FragmentCostText(int wishIndex)
        {
            int cost = FragmentCost(wishIndex);
            return cost <= 0
                ? (GameComponent_BookWhispers.IsChinese ? "免费" : "Free")
                : (GameComponent_BookWhispers.IsChinese ? "古籍残卷×" + cost : "Book fragments ×" + cost);
        }

        // --- 判定 ---
        public class WishResult
        {
            public bool approved;
            public string reason;
            public string incidentDef;   // 可空（灵感/平静为自定义执行）
            public float pointsFactor = 1f;
            public bool customInspiration;
            public bool calmDay;
        }

        // 离线规则引擎
        public static WishResult JudgeOffline(string id, string speakerId, int wishIndex)
        {
            bool isYan = speakerId == "Yan";
            bool zh = GameComponent_BookWhispers.IsChinese;
            var preset = Presets.FirstOrDefault(x => x.id == id);
            var result = new WishResult();

            if (preset.incidentDef == null && id == "inspiration") { result.customInspiration = true; }
            else if (preset.incidentDef == null && id == "calmDay") { result.calmDay = true; }
            else if (preset.incidentDef != null) result.incidentDef = preset.incidentDef;
            result.pointsFactor = preset.pointsFactor;

            // 人设判定
            if (preset.danger)
            {
                float rejectChance = isYan ? 0.6f : 0.25f;
                // 第二次起爷爷更严
                if (isYan && wishIndex >= 1) rejectChance = Mathf.Min(0.85f, rejectChance + 0.25f);
                if (Rand.Chance(rejectChance))
                {
                    result.approved = false;
                    result.reason = isYan
                        ? (zh ? (wishIndex >= 1 ? "今日已允一求，岂可再扰。学而不思则罔——先去想想今天学了什么。" : "劫乃试炼，岂可招之。尔等根基未稳，且去读书。") : (wishIndex >= 1 ? "One wish was granted today — that is enough. Go reflect on what you have learned." : "A raid is a trial, not a play. Your foundations are unsteady — go read."))
                        : (zh ? (wishIndex >= 1 ? "本姑娘有点累了……要不明天再说？" + (Rand.Bool ? "(っ˘̩╭╮˘̩)っ" : "(－_－)zzz") : "打架是很累的诶！让我想想……嗯，批了也行。") : (wishIndex >= 1 ? "I'm a bit tired... maybe tomorrow?" : "Fights are exhausting! Let me think... fine, approved."));
                    return result;
                }
                result.approved = true;
                result.reason = isYan
                    ? (zh ? "既然东家开口，老朽便依你一回。诸位，列阵。" : "As you wish, master. Everyone — formation.")
                    : (zh ? "绝了！！就等这句话！都给我打起精神来！(๑•̀ㅂ•́)و✧" : "YES!! This is what I've been waiting for! Everybody up! (๑•̀ㅂ•́)و✧");
                return result;
            }

            // 非危险类：基本同意
            result.approved = true;
            result.reason = isYan
                ? (zh ? "此事无碍，老朽允了。" : "Granted — no harm in it.")
                : (zh ? "包在本姑娘身上！小场面～" : "Leave it to me! Easy~");
            return result;
        }

        // API 判定（LLM 输出 JSON）
        public static async Task<WishResult> JudgeViaApi(string wishText, string speakerId)
        {
            try
            {
                bool isYan = speakerId == "Yan";
                string persona = isYan ? GameComponent_BookWhispers.PersonaYan() : GameComponent_BookWhispers.PersonaMo();
                string whitelist = string.Join(", ", ExtendedWhitelist);
                string sys = GameComponent_BookWhispers.IsChinese
                    ? persona + "\n\n现在玩家（你的东家/老板）向你许愿，希望殖民地发生某个事件。\n\n可选事件白名单（只能从中选择）：\n" + whitelist + "\n\n以及两种特殊处理：\"inspiration\"（给某人灵感）、\"calmDay\"（什么都不发生，平安一日）。\n\n请以你的人设判断是否答应。答应则输出 JSON：{\"approve\":true,\"reason\":\"<以你的人设口吻说的一句话>\",\"incident\":\"<白名单中的事件名，灵感/calmDay 对应 inspiration/calmDay>\"}；拒绝则输出 {\"approve\":false,\"reason\":\"<以你的人设口吻说的拒绝理由>\"}。只输出 JSON，不要任何其他内容。"
                    : persona + "\n\nThe player makes a wish for the colony.\n\nWhitelist of possible incidents (choose only from these):\n" + whitelist + "\n\nPlus two special options: \"inspiration\" and \"calmDay\".\n\nJudge in character whether to approve. If approved output JSON: {\"approve\":true,\"reason\":\"<one in-character sentence>\",\"incident\":\"<name from whitelist, or inspiration/calmDay>\"}. If rejected output {\"approve\":false,\"reason\":\"<in-character refusal>\"}. Output JSON only.";
                string user = GameComponent_BookWhispers.IsChinese ? "玩家的愿望：" + wishText : "The player's wish: " + wishText;

                using (var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(40) })
                using (var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, GoldenBooksMod.WhisperRequestUrl()))
                {
                    GoldenBooksMod.ApplyAuth(req);
                    string model = string.IsNullOrEmpty(GoldenBooksMod.settings.whisperModel) ? "default" : GoldenBooksMod.settings.whisperModel;
                    string body = "{\"model\":\"" + model + "\",\"messages\":[" +
                                  "{\"role\":\"system\",\"content\":" + GameComponent_BookWhispers.JsonEscapePublic(sys) + "}," +
                                  "{\"role\":\"user\",\"content\":" + GameComponent_BookWhispers.JsonEscapePublic(user) + "}]}";
                    req.Content = new System.Net.Http.StringContent(body, Encoding.UTF8, "application/json");
                    var resp = await client.SendAsync(req);
                    if (!resp.IsSuccessStatusCode) return null;
                    string json = await resp.Content.ReadAsStringAsync();
                    string content = GameComponent_BookWhispers.ExtractJsonFieldPublic(json, "content");
                    if (string.IsNullOrEmpty(content)) return null;

                    bool approve = content.Contains("\"approve\"") && content.Contains("true");
                    string reason = GameComponent_BookWhispers.ExtractJsonFieldPublic(content, "reason") ?? "";
                    string incident = GameComponent_BookWhispers.ExtractJsonFieldPublic(content, "incident") ?? "";

                    var result = new WishResult { approved = approve, reason = reason };
                    if (approve)
                    {
                        if (incident == "inspiration") result.customInspiration = true;
                        else if (incident == "calmDay") result.calmDay = true;
                        else if (ExtendedWhitelist.Contains(incident)) result.incidentDef = incident;
                        else { result.approved = false; result.reason = reason; } // 白名单外回落拒绝
                    }
                    return result;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[GoldenBooks] 许愿API失败: " + ex.Message);
                return null;
            }
        }

        // --- 执行 ---
        public static bool ExecuteIncident(string incidentDef, float pointsFactor)
        {
            try
            {
                IncidentDef def = DefDatabase<IncidentDef>.GetNamedSilentFail(incidentDef);
                if (def == null) return false;
                Map map = Find.AnyPlayerHomeMap;
                if (map == null) return false;
                IncidentParms parms = new IncidentParms
                {
                    target = map,
                    points = StorytellerUtility.DefaultThreatPointsNow(map) * pointsFactor,
                    faction = null
                };
                return def.Worker.TryExecute(parms);
            }
            catch (Exception ex)
            {
                Log.Warning("[GoldenBooks] 许愿事件执行失败: " + ex.Message);
                return false;
            }
        }

        public static void ExecuteInspiration()
        {
            Map map = Find.AnyPlayerHomeMap;
            if (map == null) return;
            Pawn target = map.mapPawns.FreeColonistsSpawned.Where(p => !p.Dead && !p.InMentalState).RandomElementWithFallback();
            if (target == null) return;
            InspirationDef def = DefDatabase<InspirationDef>.AllDefsListForReading.Where(d => d.Worker.InspirationCanOccur(target)).RandomElementWithFallback();
            if (def != null && target.mindState.inspirationHandler.TryStartInspiration(def, "书灵的顿悟", true))
            {
                Find.LetterStack.ReceiveLetter("GoldenBooks_WishInspirationLabel".Translate(),
                    "GoldenBooks_WishInspirationText".Translate(target.LabelShort, def.LabelCap), LetterDefOf.PositiveEvent, target);
            }
        }
    }

    // --- 许愿组件：每日次数与状态 ---
    public class GameComponent_WhisperWish : GameComponent
    {
        public static GameComponent_WhisperWish Get => Current.Game?.GetComponent<GameComponent_WhisperWish>();

        public int lastWishDay = -1;
        public int wishCountToday;
        public string lastWishRecord = ""; // 写进书语的记录

        public GameComponent_WhisperWish(Game game) { }

        public static void Ensure()
        {
            Game game = Current.Game;
            if (game == null || game.components == null) return;
            if (game.GetComponent<GameComponent_WhisperWish>() == null)
                game.components.Add(new GameComponent_WhisperWish(game));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref lastWishDay, "lastWishDay", -1);
            Scribe_Values.Look(ref wishCountToday, "wishCountToday", 0);
            Scribe_Values.Look(ref lastWishRecord, "lastWishRecord", "");
        }

        public static bool CanWishToday()
        {
            var w = Get;
            if (w == null) return false;
            if (GoldenBooksMod.settings == null || !GoldenBooksMod.settings.whisperWishEnabled) return false;
            int day = Find.TickManager.TicksGame / 60000;
            if (w.lastWishDay != day) return true;                 // 新的一天
            return w.wishCountToday < GoldenBooksMod.settings.wishDailyLimit;
        }

        public static int WishesUsedToday()
        {
            var w = Get;
            if (w == null) return 0;
            int day = Find.TickManager.TicksGame / 60000;
            return w.lastWishDay == day ? w.wishCountToday : 0;
        }

        public static void RegisterWish(string record)
        {
            var w = Get;
            if (w == null) return;
            int day = Find.TickManager.TicksGame / 60000;
            if (w.lastWishDay != day) { w.lastWishDay = day; w.wishCountToday = 0; }
            w.wishCountToday++;
            w.lastWishRecord = record;
        }

        public static void ResetCooldown()
        {
            var w = Get;
            if (w != null) w.lastWishDay = -1;
        }
    }
}
