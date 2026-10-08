using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace GoldenBooksMod
{
    // --- AI 书籍阅读：点击书本 Gizmo 按需生成书中内容 ---

    // 动态内容缓存 comp：挂在书的 ThingWithComps 上，生成文本随存档保存
    public class GoldenBooksCompProperties_BookText : CompProperties
    {
        public GoldenBooksCompProperties_BookText() { this.compClass = typeof(GoldenBooksComp_BookText); }
    }

    public class GoldenBooksComp_BookText : ThingComp
    {
        public string bookText = "";

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref bookText, "bookText", "");
        }
    }

    // API 生成器（复用书语供应商注册表与鉴权）
    public static class BookReadingApi
    {
        public static bool CanRead => GameComponent_BookWhispers.HasApiConfig();

        public static async Task<string> GenerateBookTextAsync(string bookTitle, string bookDesc)
        {
            try
            {
                bool zh = GameComponent_BookWhispers.IsChinese;
                string sys = zh
                    ? "你是一位为边缘世界殖民地图书馆撰写藏书的作者。根据书名（和简短描述，若有），写一页这本书的内容节选。要求：150~300字；风格需贴合书名气质（科技书用说明书体、小说用叙事体、古籍用文言味等）；可自然融入边缘世界世界的元素（机械族、部落、异星生物、远行商队等）；只输出正文，不要解释、不要标题。"
                    : "You are an author writing library books for a RimWorld colony. Given a book title (and short description if any), write one excerpt page of the book, 100~200 words, styled to match the title (manual-like for tech books, narrative for novels, archaic for ancient tomes), subtly weaving in RimWorld lore (mechanoids, tribes, alien wildlife, trade caravans). Output text only.";
                string user = zh ? "书名：《" + bookTitle + "》" + (string.IsNullOrEmpty(bookDesc) ? "" : "\n简介：" + bookDesc) : "Title: \"" + bookTitle + "\"" + (string.IsNullOrEmpty(bookDesc) ? "" : "\nDescription: " + bookDesc);

                string url = WhisperProviderRegistry.IsPlayer2(GoldenBooksMod.settings.whisperProvider)
                    ? await WhisperPlayer2.ResolveChatUrlAsync()
                    : GoldenBooksMod.WhisperRequestUrl();
                using (var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, url))
                {
                    GoldenBooksMod.ApplyAuth(req);
                    string model = string.IsNullOrEmpty(GoldenBooksMod.settings.whisperModel) ? "default" : GoldenBooksMod.settings.whisperModel;
                    string body = "{\"model\":\"" + model + "\",\"messages\":[" +
                                  "{\"role\":\"system\",\"content\":" + GameComponent_BookWhispers.JsonEscapePublic(sys) + "}," +
                                  "{\"role\":\"user\",\"content\":" + GameComponent_BookWhispers.JsonEscapePublic(user) + "}]}";
                    req.Content = new System.Net.Http.StringContent(body, Encoding.UTF8, "application/json");
                    using (var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(60) })
                    {
                        var resp = await client.SendAsync(req);
                        if (!resp.IsSuccessStatusCode) return null;
                        string json = await resp.Content.ReadAsStringAsync();
                        string content = GameComponent_BookWhispers.ExtractJsonFieldPublic(json, "content");
                        return string.IsNullOrEmpty(content) ? null : content.Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[GoldenBooks] 书籍阅读 API 失败: " + ex.Message);
                return null;
            }
        }

        // 离线回落：泛黄残句
        public static string OfflineText()
        {
            string[] zhPool =
            {
                "……纸页泛黄，墨迹在潮气里晕开。能辨认的只有残句：\"凡至此读者，皆有缘人。愿灯火长明，愿书页不蛀。\"其后是一页虫蛀的洞，恰好看不出原文写的是什么。",
                "……这一页只有一行小字：\"借阅者众，归还真迹者寡。\"落款处的名字被水渍糊掉了，只余一枚指纹大小的墨点。",
                "……夹在书页间的一张糖纸，背面用极细的笔写着：\"读到这里的人，替我把这页读完。\"后面的字迹越来越淡，终于没有了。"
            };
            string[] enPool =
            {
                "...The page is yellowed, ink blooming in the damp. Only a fragment survives: 'To whoever reads this far: keep the lamp lit, and mind the bookworms.' The rest is a lacework of insect holes.",
                "...A single line fills the page: 'Many borrow; few return.' The signature has drowned in a water stain, leaving only a thumbprint of ink.",
                "...A candy wrapper is pressed between the pages. On its back, in the finest hand: 'Whoever reads this — finish the page for me.' The letters fade to nothing."
            };
            var pool = GameComponent_BookWhispers.IsChinese ? zhPool : enPool;
            return pool[Rand.Range(0, pool.Length)];
        }
    }

    // 阅读窗口：先显示“正在读取该书本信息中…”，完成后呈文书页
    public class Dialog_ReadBook : Window
    {
        private readonly Book book;
        private readonly string title;
        private string text;
        private bool generating;
        private Vector2 scroll = Vector2.zero;

        public override Vector2 InitialSize => new Vector2(560f, 460f);

        public Dialog_ReadBook(Book book)
        {
            this.book = book;
            this.title = book.Title;
            forcePause = false;
            doCloseX = true;
            absorbInputAroundWindow = false;
            draggable = true;
            resizeable = false;

            var comp = book.TryGetComp<GoldenBooksComp_BookText>();
            if (comp != null && !string.IsNullOrEmpty(comp.bookText))
            {
                text = comp.bookText;
                generating = false;
            }
            else
            {
                text = "";
                generating = true;
                GenerateAndStore();
            }
        }

        private async void GenerateAndStore()
        {
            // Book.Title 为实例级随机书名；DescriptionFlavor 为生成的风味文本
            string result = await BookReadingApi.GenerateBookTextAsync(book.Title, book.DescriptionFlavor);
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                text = result ?? BookReadingApi.OfflineText();
                generating = false;
                var comp = book.TryGetComp<GoldenBooksComp_BookText>();
                if (comp != null && result != null) comp.bookText = result; // 失败回落不缓存，可重试
            });
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 34f), title);
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.75f, 0.65f, 0.45f);
            Widgets.Label(new Rect(0f, 36f, inRect.width, 22f), "GoldenBooks_ReadBookSubtitle".Translate());
            GUI.color = Color.white;

            Rect bodyRect = new Rect(0f, 64f, inRect.width, inRect.height - 64f);
            Widgets.DrawMenuSection(bodyRect);
            Rect inner = bodyRect.ContractedBy(12f);

            if (generating)
            {
                GUI.color = new Color(0.85f, 0.72f, 0.42f);
                Widgets.Label(inner, "GoldenBooks_ReadBookLoading".Translate());
                GUI.color = Color.white;
                return;
            }

            float h = Text.CalcHeight(text, inner.width - 8f);
            Rect view = new Rect(0f, 0f, inner.width - 8f, h);
            Widgets.BeginScrollView(inner, ref scroll, view);
            Widgets.Label(view, text);
            Widgets.EndScrollView();
        }
    }

    // Harmony 钩子：书本物品追加“翻阅此书”Gizmo
    [HarmonyPatch(typeof(ThingWithComps), "GetGizmos")]
    public static class Patch_BookGetGizmos
    {
        private static Texture2D _icon;
        private static Texture2D Icon()
        {
            if (_icon == null)
                _icon = ContentFinder<Texture2D>.Get("UI/Buttons/OpenCodex", false)
                     ?? ContentFinder<Texture2D>.Get("UI/Commands/DesirePower", false);
            return _icon;
        }

        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, ThingWithComps __instance)
        {
            foreach (Gizmo g in __result) yield return g;
            Book book = __instance as Book;
            if (book == null || !BookReadingApi.CanRead) yield break;
            if (book.def.defName.StartsWith("GoldenBooks_")) yield break;

            yield return new Command_Action
            {
                defaultLabel = "GoldenBooks_ReadBookGizmo".Translate(),
                defaultDesc = "GoldenBooks_ReadBookGizmoDesc".Translate(),
                icon = Icon(),
                action = delegate
                {
                    EnsureBookTextComp(book);
                    Find.WindowStack.Add(new Dialog_ReadBook(book));
                }
            };
        }

        // 动态挂载文本缓存 comp（def 未声明，需运行时注入）
        private static void EnsureBookTextComp(Book book)
        {
            if (book.TryGetComp<GoldenBooksComp_BookText>() != null) return;
            try
            {
                var comp = new GoldenBooksComp_BookText();
                var props = new GoldenBooksCompProperties_BookText();
                comp.parent = book;
                // ThingComp.Initialize(props) 会设置 this.props 并调用 comp.InitializeComps
                comp.Initialize(props);
                book.AllComps.Add(comp);
            }
            catch (Exception ex) { Log.Warning("[GoldenBooks] 挂载书本文本 comp 失败: " + ex.Message); }
        }
    }
}
