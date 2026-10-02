using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using RimWorld;
using UnityEngine;
using Verse;

namespace GoldenBooksMod
{
    // --- 供应商注册表（参照 RimTalk AIProviderRegistry） ---
    public class WhisperProviderDef
    {
        public string Label;
        public string EndpointUrl;      // 聊天补全端点（Player2 为根地址）
        public string ListModelsUrl;    // 可选：模型列表端点
    }

    public static class WhisperProviderRegistry
    {
        public const string Player2ClientId = "019e12ba-6062-79ae-8c76-de13bea9af7a";
        public const string Player2LocalUrl = "http://localhost:4315";
        public const string Player2RemoteUrl = "https://api.player2.game";

        public static readonly WhisperProviderDef[] Defs =
        {
            new WhisperProviderDef { Label = "DeepSeek",        EndpointUrl = "https://api.deepseek.com/v1/chat/completions", ListModelsUrl = "https://api.deepseek.com/models" },
            new WhisperProviderDef { Label = "OpenAI",          EndpointUrl = "https://api.openai.com/v1/chat/completions", ListModelsUrl = "https://api.openai.com/v1/models" },
            new WhisperProviderDef { Label = "Player2",         EndpointUrl = Player2RemoteUrl },
            new WhisperProviderDef { Label = "Google (Gemini)", EndpointUrl = "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions", ListModelsUrl = "https://generativelanguage.googleapis.com/v1beta/openai/models" },
            new WhisperProviderDef { Label = "Grok",            EndpointUrl = "https://api.x.ai/v1/chat/completions", ListModelsUrl = "https://api.x.ai/v1/models" },
            new WhisperProviderDef { Label = "GLM",             EndpointUrl = "https://api.z.ai/api/paas/v4/chat/completions", ListModelsUrl = "https://api.z.ai/api/paas/v4/models" },
            new WhisperProviderDef { Label = "GLM (Coding)",    EndpointUrl = "https://api.z.ai/api/coding/paas/v4/chat/completions" },
            new WhisperProviderDef { Label = "Alibaba (Intl)",  EndpointUrl = "https://dashscope-intl.aliyuncs.com/compatible-mode/v1/chat/completions" },
            new WhisperProviderDef { Label = "Alibaba (CN)",    EndpointUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions" },
            new WhisperProviderDef { Label = "OpenRouter",      EndpointUrl = "https://openrouter.ai/api/v1/chat/completions" },
            new WhisperProviderDef { Label = "Claude",          EndpointUrl = "https://api.anthropic.com/v1/chat/completions" },
            new WhisperProviderDef { Label = "Moonshot",        EndpointUrl = "https://api.moonshot.ai/v1/chat/completions", ListModelsUrl = "https://api.moonshot.ai/v1/models" },
            new WhisperProviderDef { Label = "自定义 (Custom)", EndpointUrl = "" }
        };

        public static bool IsPlayer2(int index) => Defs[index].Label == "Player2";
    }

    // --- Player2 客户端（参照 RimTalk Player2Client/Player2AuthService） ---
    public static class WhisperPlayer2
    {
        private const string DeviceNewEndpoint = "https://api.player2.game/v1/login/device/new";
        private const string DeviceTokenEndpoint = "https://api.player2.game/v1/login/device/token";

        public static bool IsAuthenticating { get; private set; }
        public static string ApprovalUrl { get; private set; }
        private static string _localKeyCache;
        private static DateTime _lastLocalProbe = DateTime.MinValue;
        private static System.Threading.CancellationTokenSource _authCts;

        public static string GetKey() => GoldenBooksMod.settings.whisperP2Key;

        // 探测本地客户端：可用则本地登录换 p2Key 并缓存
        public static async Task<bool> TryLocalLoginAsync()
        {
            try
            {
                if ((DateTime.Now - _lastLocalProbe).TotalSeconds < 3 && _localKeyCache != null)
                    return true;
                _lastLocalProbe = DateTime.Now;

                using (var health = new HttpRequestMessage(HttpMethod.Get, WhisperProviderRegistry.Player2LocalUrl + "/v1/health"))
                using (var clientH = new HttpClient { Timeout = TimeSpan.FromSeconds(3) })
                {
                    var hr = await clientH.SendAsync(health);
                    if (!hr.IsSuccessStatusCode) return false;
                }

                using (var login = new HttpRequestMessage(HttpMethod.Post,
                    WhisperProviderRegistry.Player2LocalUrl + "/v1/login/web/" + WhisperProviderRegistry.Player2ClientId))
                using (var clientL = new HttpClient { Timeout = TimeSpan.FromSeconds(4) })
                {
                    login.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                    var lr = await clientL.SendAsync(login);
                    string text = await lr.Content.ReadAsStringAsync();
                    string key = ExtractJsonField(text, "p2Key");
                    if (!string.IsNullOrEmpty(key))
                    {
                        _localKeyCache = key;
                        GoldenBooksMod.settings.whisperP2Key = key;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        // 设备码流程：浏览器授权后拿 Web p2Key（异步轮询）
        public static void StartDeviceAuth()
        {
            if (IsAuthenticating)
            {
                if (!string.IsNullOrEmpty(ApprovalUrl)) Application.OpenURL(ApprovalUrl);
                return;
            }
            _authCts = new System.Threading.CancellationTokenSource();
            IsAuthenticating = true;
            Task.Run(async () =>
            {
                try
                {
                    await RunDeviceAuthFlowAsync(_authCts.Token);
                }
                catch (Exception ex)
                {
                    LongEventHandler.ExecuteWhenFinished(() =>
                        Messages.Message("Player2 授权失败：" + ex.Message, MessageTypeDefOf.RejectInput));
                }
                finally { IsAuthenticating = false; ApprovalUrl = null; }
            });
        }

        public static void CancelAuth()
        {
            try { _authCts?.Cancel(); _authCts?.Dispose(); } catch { }
            _authCts = null;
            IsAuthenticating = false;
            ApprovalUrl = null;
        }

        private static async Task RunDeviceAuthFlowAsync(System.Threading.CancellationToken ct)
        {
            string clientId = WhisperProviderRegistry.Player2ClientId;
            string newBody = "{\"client_id\":\"" + clientId + "\"}";
            string codeText = await PostAsync(DeviceNewEndpoint, newBody, 10);
            string deviceCode = ExtractJsonField(codeText, "deviceCode");
            string approval = ExtractJsonField(codeText, "verificationUriComplete");
            if (string.IsNullOrEmpty(deviceCode)) throw new Exception("获取设备码失败");
            ApprovalUrl = approval;
            if (!string.IsNullOrEmpty(approval)) Application.OpenURL(approval);
            LongEventHandler.ExecuteWhenFinished(() =>
                Messages.Message("Player2：已打开浏览器，请在网页上批准授权。", MessageTypeDefOf.NeutralEvent));

            int interval = 5, maxSeconds = 600, elapsed = 0;
            string tokenBody = "{\"client_id\":\"" + clientId + "\",\"device_code\":\"" + deviceCode +
                               "\",\"grant_type\":\"urn:ietf:params:oauth:grant-type:device_code\"}";
            while (!ct.IsCancellationRequested && elapsed < maxSeconds)
            {
                await Task.Delay(interval * 1000, ct);
                elapsed += interval;
                string tokenText = await PostAsync(DeviceTokenEndpoint, tokenBody, 10);
                string key = ExtractJsonField(tokenText, "p2Key");
                if (!string.IsNullOrEmpty(key))
                {
                    GoldenBooksMod.settings.whisperP2Key = key;
                    LongEventHandler.ExecuteWhenFinished(() =>
                        Messages.Message("Player2：Web 密钥获取成功！", MessageTypeDefOf.PositiveEvent));
                    return;
                }
            }
            throw new Exception("授权超时，请重试");
        }

        private static async Task<string> PostAsync(string url, string body, int timeoutSeconds)
        {
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) })
            using (var req = new HttpRequestMessage(HttpMethod.Post, url))
            {
                req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                var resp = await client.SendAsync(req);
                return await resp.Content.ReadAsStringAsync();
            }
        }

        private static string ExtractJsonField(string json, string field)
        {
            try
            {
                string key = "\"" + field + "\"";
                int i = json.IndexOf(key, StringComparison.Ordinal);
                if (i < 0) return null;
                i = json.IndexOf(':', i);
                if (i < 0) return null;
                i++;
                while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
                if (i >= json.Length || json[i] != '"') return null;
                i++;
                var sb = new StringBuilder();
                while (i < json.Length)
                {
                    char c = json[i];
                    if (c == '\\' && i + 1 < json.Length)
                    {
                        char n = json[i + 1];
                        if (n == 'n') sb.Append('\n');
                        else if (n == '"') sb.Append('"');
                        else if (n == '\\') sb.Append('\\');
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

        // 决定实际聊天 URL：本地客户端可用优先，否则远程 + 已保存的 Web 密钥
        public static async Task<string> ResolveChatUrlAsync()
        {
            bool localOk = await TryLocalLoginAsync();
            if (localOk) return WhisperProviderRegistry.Player2LocalUrl + "/v1/chat/completions";
            if (!string.IsNullOrEmpty(GoldenBooksMod.settings.whisperP2Key))
                return WhisperProviderRegistry.Player2RemoteUrl + "/v1/chat/completions";
            throw new Exception("Player2 客户端未运行，且尚未获取 Web 密钥（请先点击「登录获取密钥」）");
        }
    }

    // --- 从 RimTalk / RimTuber 的存档配置导入 ---
    // RimWorld 将各模组设置存为 Config/Mod_<标识>_<类名>.xml，故按 ModSettings Class 特征扫描定位。
    public static class WhisperConfigImporter
    {
        public class ImportedConfig
        {
            public int ProviderIndex;
            public string ApiKey;
            public string Model;
            public string BaseUrl;
        }

        // 扫描 Config 目录，找到包含指定 ModSettings 类名的文件
        private static string FindSettingsFile(string settingsClassName)
        {
            string dir = GenFilePaths.ConfigFolderPath;
            if (!Directory.Exists(dir)) return null;
            foreach (FileInfo f in new DirectoryInfo(dir).GetFiles("*.xml"))
            {
                try
                {
                    using (StreamReader r = new StreamReader(f.FullName, Encoding.UTF8))
                    {
                        string head = r.ReadToEnd();
                        if (head.Contains("ModSettings Class=\"" + settingsClassName + "\"")) return f.FullName;
                    }
                }
                catch { }
            }
            return null;
        }

        // 供应商枚举名 → 本模组下拉序号
        private static int MapProviderName(string name)
        {
            if (string.IsNullOrEmpty(name)) return ProviderIndexOf("自定义 (Custom)");
            switch (name)
            {
                case "DeepSeek": return ProviderIndexOf("DeepSeek");
                case "OpenAI": return ProviderIndexOf("OpenAI");
                case "Player2": return ProviderIndexOf("Player2");
                case "Google": return ProviderIndexOf("Google (Gemini)");
                case "Grok": return ProviderIndexOf("Grok");
                case "GLM": return ProviderIndexOf("GLM");
                case "GLMCoding": return ProviderIndexOf("GLM (Coding)");
                case "AlibabaIntl": return ProviderIndexOf("Alibaba (Intl)");
                case "AlibabaCN": return ProviderIndexOf("Alibaba (CN)");
                case "OpenRouter": return ProviderIndexOf("OpenRouter");
                case "Claude": return ProviderIndexOf("Claude");
                case "Moonshot": return ProviderIndexOf("Moonshot");
                default: return ProviderIndexOf("自定义 (Custom)"); // Custom/Local/None
            }
        }

        private static bool BoolText(string s, bool def)
        {
            if (string.IsNullOrEmpty(s)) return def;
            return s.Equals("True", StringComparison.OrdinalIgnoreCase) || s == "1";
        }

        public static ImportedConfig ImportFrom(string settingsClassName)
        {
            string file = FindSettingsFile(settingsClassName);
            if (file == null) return null;
            XmlDocument doc = new XmlDocument();
            doc.Load(file);

            if (settingsClassName.Contains("RimTalk")) return ParseRimTalk(doc);
            if (settingsClassName.Contains("RimTuber")) return ParseRimTuber(doc);
            return null;
        }

        // RimTalk：useSimpleConfig 简单模式 / cloudConfigs 进阶模式
        private static ImportedConfig ParseRimTalk(XmlDocument doc)
        {
            bool useSimple = BoolText(GetText(doc, "useSimpleConfig"), true);
            if (useSimple)
            {
                string provName = GetText(doc, "simpleProvider");
                int idx;
                if (int.TryParse(provName, out int provInt)) idx = MapLegacyIndex(provInt);
                else idx = MapProviderName(provName);

                var r = new ImportedConfig { ProviderIndex = idx };
                if (idx == ProviderIndexOf("Player2"))
                    r.ApiKey = GetText(doc, "simplePlayer2ApiKey");
                else
                    r.ApiKey = GetText(doc, "simpleApiKey");
                return r;
            }

            // 进阶模式：取第一个启用且有密钥的云配置
            foreach (XmlNode li in doc.SelectNodes("//cloudConfigs/li"))
            {
                string enabled = GetChild(li, "isEnabled") ?? "True";
                string apiKey = GetChild(li, "apiKey");
                if (!BoolText(enabled, true) || string.IsNullOrEmpty(apiKey)) continue;
                var r = new ImportedConfig
                {
                    ProviderIndex = MapProviderName(GetChild(li, "provider")),
                    ApiKey = apiKey,
                    BaseUrl = GetChild(li, "baseUrl"),
                    Model = ""
                };
                string sel = GetChild(li, "selectedModel");
                string custom = GetChild(li, "customModelName");
                r.Model = sel == "Custom" ? custom : sel;
                if (r.Model == "ChooseModel") r.Model = "";
                return r;
            }
            // 回落：本地模型配置
            XmlNode local = doc.SelectSingleNode("//localConfig");
            string lb = local?["baseUrl"]?.InnerText;
            if (!string.IsNullOrEmpty(lb))
                return new ImportedConfig { ProviderIndex = ProviderIndexOf("自定义 (Custom)"), BaseUrl = lb, Model = local["customModelName"]?.InnerText ?? "" };
            return null;
        }

        // 旧版本数字枚举序号兜底
        private static int MapLegacyIndex(int i)
        {
            switch (i)
            {
                case 0: return ProviderIndexOf("Google (Gemini)");
                case 1: return ProviderIndexOf("OpenAI");
                case 2: return ProviderIndexOf("DeepSeek");
                case 3: return ProviderIndexOf("Grok");
                case 4: return ProviderIndexOf("GLM");
                case 5: return ProviderIndexOf("GLM (Coding)");
                case 6: return ProviderIndexOf("Alibaba (Intl)");
                case 7: return ProviderIndexOf("Alibaba (CN)");
                case 8: return ProviderIndexOf("OpenRouter");
                case 9: return ProviderIndexOf("Player2");
                case 13: return ProviderIndexOf("Claude");
                case 14: return ProviderIndexOf("Moonshot");
                default: return ProviderIndexOf("自定义 (Custom)");
            }
        }

        // RimTuber（用户自研）：APIConfigs 列表，取第一个带密钥的配置；Player2 项除外
        private static ImportedConfig ParseRimTuber(XmlDocument doc)
        {
            foreach (XmlNode li in doc.SelectNodes("//APIConfigs/li"))
            {
                string prov = GetChild(li, "provider");
                string apiKey = GetChild(li, "apiKey");
                string url = GetChild(li, "endpointUrl");
                string model = GetChild(li, "model");

                if (prov == "Player2")
                {
                    var p2 = new ImportedConfig { ProviderIndex = ProviderIndexOf("Player2") };
                    if (!string.IsNullOrEmpty(apiKey)) p2.ApiKey = apiKey;
                    return p2;
                }
                if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(url)) continue;
                return new ImportedConfig
                {
                    ProviderIndex = ProviderIndexOf("自定义 (Custom)"),
                    ApiKey = apiKey,
                    BaseUrl = url,
                    Model = model
                };
            }
            return null;
        }

        public static int ProviderIndexOf(string label)
        {
            for (int i = 0; i < WhisperProviderRegistry.Defs.Length; i++)
                if (WhisperProviderRegistry.Defs[i].Label == label) return i;
            return ProviderIndexOf("自定义 (Custom)");
        }

        private static string GetText(XmlDocument doc, string field)
        {
            var n = doc.SelectSingleNode("//" + field);
            return n?.InnerText?.Trim();
        }

        private static string GetChild(XmlNode parent, string name)
        {
            return parent?.SelectNodes(name)?.Cast<XmlNode>()
                .FirstOrDefault()?.InnerText?.Trim();
        }
    }
    // --- 人格复制/提取/模板 ---
    public static class WhisperPersonaHelper
    {
        // 覆盖目标 Pawn 的 RimTalk 人格
        public static void CopyPersonaToPawn(Pawn to, string persona)
        {
            try
            {
                HediffDef pd = DefDatabase<HediffDef>.GetNamedSilentFail("RimTalk_PersonaData");
                if (pd == null || to == null || to.health == null || string.IsNullOrEmpty(persona)) return;
                Hediff ex = to.health.hediffSet.GetFirstHediffOfDef(pd);
                if (ex != null) to.health.RemoveHediff(ex);
                Hediff hd = HediffMaker.MakeHediff(pd, to);
                if (hd == null) return;
                var f = hd.GetType().GetField("Personality", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (f != null) f.SetValue(hd, persona);
                to.health.AddHediff(hd);
            }
            catch (Exception ex)
            {
                Log.Warning("[GoldenBooks] 人格复制失败: " + ex.Message);
            }
        }

        // 从 Pawn 提取 RimTalk 人格文本
        public static string ExtractPersonaFrom(Pawn p)
        {
            try
            {
                HediffDef pd = DefDatabase<HediffDef>.GetNamedSilentFail("RimTalk_PersonaData");
                if (pd == null || p?.health?.hediffSet == null) return null;
                Hediff h = p.health.hediffSet.GetFirstHediffOfDef(pd);
                if (h == null) return null;
                var f = h.GetType().GetField("Personality", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                return f?.GetValue(h) as string;
            }
            catch { return null; }
        }

        public static string PersonaColonist(string name)
        {
            return GoldenBooksMod.settings != null && GameComponent_BookWhispers.IsChinese
                ? "你是 " + name + "，一位由殖民地学者化灵而成的书灵。你保留了化灵前的记忆与性情，但如今栖身于浅金册页之中——不食不眠，以读书人的心念为生。你说话仍是你自己的风格，只是偶尔会提到书页、灯光与墨的气味。"
                : "You are " + name + ", a colony scholar ascended into a book spirit. You keep your old memories and temperament, now dwelling within pale-golden pages — no food, no sleep, sustained by the devotion of readers. You still speak in your own style, occasionally mentioning pages, lamplight and the smell of ink.";
        }
    }
}