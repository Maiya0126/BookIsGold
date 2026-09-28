using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace GoldenBooksMod
{
    // --- 颜氏手札：可翻阅的物品（Gizmo 打开阅读窗） ---
    public class HanbookItem : ThingWithComps
    {
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos())
                yield return g;

            yield return new Command_Action
            {
                defaultLabel = "GoldenBooks_HandbookRead".Translate(),
                defaultDesc = "GoldenBooks_HandbookReadDesc".Translate(),
                icon = ReaderIcon(),
                action = delegate { Find.WindowStack.Add(new Dialog_Handbook()); }
            };
        }

        private static Texture2D _icon;
        private static Texture2D ReaderIcon()
        {
            if (_icon == null)
                _icon = ContentFinder<Texture2D>.Get("UI/Buttons/OpenCodex", false)
                     ?? ContentFinder<Texture2D>.Get("UI/Commands/DesirePower", false);
            return _icon;
        }
    }

    // --- 手札阅读窗：读取模组 Story/*.md ---
    public class Dialog_Handbook : Window
    {
        private List<string> titles = new List<string>();
        private List<string> bodies = new List<string>();
        private int selected;
        private Vector2 scrollText = Vector2.zero;
        private Vector2 scrollList = Vector2.zero;

        public override Vector2 InitialSize => new Vector2(960f, 640f);

        public Dialog_Handbook()
        {
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = false;
            LoadChapters();
        }

        private void LoadChapters()
        {
            titles.Clear();
            bodies.Clear();
            try
            {
                ModContentPack pack = LoadedModManager.RunningMods.FirstOrDefault(m =>
                    m.Name != null && (m.Name.Contains("Golden Books") || m.Name.Contains("书中自有黄金屋")));
                if (pack == null) return;

                string storyDir = Path.Combine(pack.RootDir, "Story");
                if (!Directory.Exists(storyDir)) return;

                List<FileInfo> files = new DirectoryInfo(storyDir)
                    .GetFiles("*.md")
                    .Where(f => f.Name != "人物设定.md")
                    .OrderBy(f => f.Name, System.StringComparer.Ordinal)
                    .ToList();

                foreach (FileInfo f in files)
                {
                    string raw = File.ReadAllText(f.FullName, Encoding.UTF8);
                    string title = Path.GetFileNameWithoutExtension(f.Name);
                    // 去掉 markdown 标记，仅保留可读文本
                    StringBuilder sb = new StringBuilder();
                    foreach (string line in raw.Split('\n'))
                    {
                        string t = line.TrimEnd('\r');
                        if (t.StartsWith("# ")) { title = t.Substring(2).Trim(); continue; }
                        if (t.StartsWith("## ")) { sb.AppendLine().AppendLine("◆ " + t.Substring(3)); continue; }
                        if (t.StartsWith("> ")) { sb.AppendLine("　" + t.Substring(2)); continue; }
                        sb.AppendLine(t);
                    }
                    titles.Add(title);
                    bodies.Add(sb.ToString());
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning("[GoldenBooks] 手札章节读取失败: " + ex.Message);
            }
            if (titles.Count == 0)
            {
                titles.Add("GoldenBooks_HandbookEmpty".Translate());
                bodies.Add("");
            }
            selected = Mathf.Clamp(selected, 0, titles.Count - 1);
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 36f), "GoldenBooks_HandbookTitle".Translate());
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.75f, 0.65f, 0.45f);
            Widgets.Label(new Rect(0f, 38f, inRect.width, 24f), "GoldenBooks_HandbookSubtitle".Translate());
            GUI.color = Color.white;

            float listW = 240f;
            // 章节目录
            Rect listRect = new Rect(0f, 70f, listW, inRect.height - 70f);
            Widgets.DrawMenuSection(listRect);
            Rect lv = new Rect(0f, 0f, listW - 16f, titles.Count * 30f);
            Widgets.BeginScrollView(listRect.ContractedBy(4f), ref scrollList, lv);
            for (int i = 0; i < titles.Count; i++)
            {
                Rect row = new Rect(0f, i * 30f, lv.width, 28f);
                if (i == selected) Widgets.DrawHighlight(row);
                if (Widgets.ButtonText(row, titles[i]))
                    selected = i;
            }
            Widgets.EndScrollView();

            // 正文
            Rect textRect = new Rect(listW + 12f, 70f, inRect.width - listW - 12f, inRect.height - 70f);
            Widgets.DrawMenuSection(textRect);
            float h = Text.CalcHeight(bodies[selected], textRect.width - 24f);
            Rect tv = new Rect(0f, 0f, textRect.width - 24f, h);
            Widgets.BeginScrollView(textRect.ContractedBy(6f), ref scrollText, tv);
            Widgets.Label(tv, bodies[selected]);
            Widgets.EndScrollView();
        }
    }
}
