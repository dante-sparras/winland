# Fetches the Hyprland wiki into markdown. scripts/dev.ps1 calls this.
# Do not edit the pages it writes.
param(
    [string]$Root,
    [string[]]$Only
)

$ErrorActionPreference = 'Stop'

$source = @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FetchRef
{
    public sealed class HNode
    {
        public string Name;
        public string Text = "";
        public HNode Parent;
        public readonly List<HNode> Children = new List<HNode>();
        public readonly Dictionary<string, string> Attr = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public bool IsText { get { return Name == "#text"; } }

        public static HNode El(string name) { return new HNode { Name = name }; }
        public static HNode Txt(string text) { return new HNode { Name = "#text", Text = text ?? "" }; }

        public string Get(string name)
        {
            string value;
            return Attr.TryGetValue(name, out value) ? value : null;
        }

        public void Set(string name, string value) { Attr[name] = value ?? ""; }

        public bool HasClass(string token)
        {
            string value;
            if (!Attr.TryGetValue("class", out value) || string.IsNullOrEmpty(value)) return false;
            foreach (var part in value.Split(new[] { ' ', '\t', '\n', '\r', '\f' }, StringSplitOptions.RemoveEmptyEntries))
                if (part == token) return true;
            return false;
        }

        public void Append(HNode child)
        {
            if (child.Parent != null) child.Parent.Children.Remove(child);
            child.Parent = this;
            Children.Add(child);
        }

        public void Remove()
        {
            if (Parent == null) return;
            Parent.Children.Remove(this);
            Parent = null;
        }

        public void Unwrap()
        {
            if (Parent == null) return;
            var index = Parent.Children.IndexOf(this);
            Parent.Children.RemoveAt(index);
            for (var i = 0; i < Children.Count; i++)
            {
                Children[i].Parent = Parent;
                Parent.Children.Insert(index + i, Children[i]);
            }
            Children.Clear();
            Parent = null;
        }

        public void ReplaceWith(HNode node)
        {
            if (Parent == null) return;
            var index = Parent.Children.IndexOf(this);
            if (node.Parent != null) node.Remove();
            node.Parent = Parent;
            Parent.Children[index] = node;
            Parent = null;
        }

        public List<HNode> Descendants()
        {
            var list = new List<HNode>();
            var stack = new Stack<HNode>();
            for (var i = Children.Count - 1; i >= 0; i--) stack.Push(Children[i]);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                list.Add(node);
                for (var i = node.Children.Count - 1; i >= 0; i--) stack.Push(node.Children[i]);
            }
            return list;
        }

        public HNode FindTag(params string[] names)
        {
            var wanted = new HashSet<string>(names);
            foreach (var node in Descendants())
                if (wanted.Contains(node.Name)) return node;
            return null;
        }

        public string GetText(string separator, bool strip)
        {
            var parts = new List<string>();
            Walk(this, parts, strip);
            var text = string.Join(separator, parts);
            return strip ? text.Trim() : text;
        }

        static void Walk(HNode node, List<string> parts, bool strip)
        {
            if (node.IsText)
            {
                var text = node.Text ?? "";
                if (strip) text = Regex.Replace(text, @"\s+", " ").Trim();
                if (text.Length > 0) parts.Add(text);
                return;
            }
            foreach (var child in node.Children) Walk(child, parts, strip);
        }
    }

    public static class HtmlDoc
    {
        static readonly HashSet<string> Void = new HashSet<string>
        {
            "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr"
        };
        static readonly HashSet<string> Raw = new HashSet<string> { "script", "style" };

        public static HNode Parse(string html)
        {
            var root = new HNode { Name = "#document" };
            var stack = new Stack<HNode>();
            stack.Push(root);
            var i = 0;
            var n = html == null ? 0 : html.Length;
            html = html ?? "";
            while (i < n)
            {
                if (html[i] != '<')
                {
                    var next = html.IndexOf('<', i);
                    if (next < 0) next = n;
                    var raw = html.Substring(i, next - i);
                    if (raw.Length > 0) stack.Peek().Append(HNode.Txt(WebUtility.HtmlDecode(raw)));
                    i = next;
                    continue;
                }
                if (i + 3 < n && html[i + 1] == '!' && html[i + 2] == '-' && html[i + 3] == '-')
                {
                    var end = html.IndexOf("-->", i + 4, StringComparison.Ordinal);
                    i = end < 0 ? n : end + 3;
                    continue;
                }
                if (i + 1 < n && (html[i + 1] == '!' || html[i + 1] == '?'))
                {
                    var end = html.IndexOf('>', i + 2);
                    i = end < 0 ? n : end + 1;
                    continue;
                }
                if (i + 1 < n && html[i + 1] == '/')
                {
                    var j = i + 2;
                    while (j < n && char.IsWhiteSpace(html[j])) j++;
                    var start = j;
                    while (j < n && IsName(html[j])) j++;
                    var name = html.Substring(start, j - start).ToLowerInvariant();
                    var end = html.IndexOf('>', j);
                    i = end < 0 ? n : end + 1;
                    if (name.Length > 0)
                    {
                        while (stack.Count > 1)
                        {
                            var top = stack.Pop();
                            if (top.Name == name) break;
                        }
                    }
                    continue;
                }
                if (i + 1 >= n || !IsNameStart(html[i + 1]))
                {
                    stack.Peek().Append(HNode.Txt("<"));
                    i++;
                    continue;
                }
                var k = i + 1;
                var nameStart = k;
                while (k < n && IsName(html[k])) k++;
                var tag = html.Substring(nameStart, k - nameStart).ToLowerInvariant();
                var node = HNode.El(tag);
                var self = false;
                while (k < n)
                {
                    while (k < n && char.IsWhiteSpace(html[k])) k++;
                    if (k >= n) break;
                    if (html[k] == '>') { k++; break; }
                    if (html[k] == '/' && k + 1 < n && html[k + 1] == '>') { self = true; k += 2; break; }
                    var attrStart = k;
                    while (k < n && html[k] != '=' && html[k] != '>' && html[k] != '/' && !char.IsWhiteSpace(html[k])) k++;
                    var attr = html.Substring(attrStart, k - attrStart).ToLowerInvariant();
                    if (attr.Length == 0) { k++; continue; }
                    while (k < n && char.IsWhiteSpace(html[k])) k++;
                    var val = "";
                    if (k < n && html[k] == '=')
                    {
                        k++;
                        while (k < n && char.IsWhiteSpace(html[k])) k++;
                        if (k < n && (html[k] == '"' || html[k] == '\''))
                        {
                            var quote = html[k++];
                            var valStart = k;
                            while (k < n && html[k] != quote) k++;
                            val = html.Substring(valStart, k - valStart);
                            if (k < n) k++;
                        }
                        else
                        {
                            var valStart = k;
                            while (k < n && !char.IsWhiteSpace(html[k]) && html[k] != '>') k++;
                            val = html.Substring(valStart, k - valStart);
                        }
                        val = WebUtility.HtmlDecode(val);
                    }
                    node.Attr[attr] = val;
                }
                i = k;
                if (Raw.Contains(tag) && !self)
                {
                    var close = "</" + tag;
                    var end = IndexOfIgnore(html, close, i);
                    var inner = end < 0 ? html.Substring(i) : html.Substring(i, end - i);
                    if (inner.Length > 0) node.Append(HNode.Txt(inner));
                    if (end < 0) i = n;
                    else
                    {
                        var gt = html.IndexOf('>', end);
                        i = gt < 0 ? n : gt + 1;
                    }
                    stack.Peek().Append(node);
                    continue;
                }
                stack.Peek().Append(node);
                if (!self && !Void.Contains(tag)) stack.Push(node);
            }
            return root;
        }

        public static List<HNode> Select(HNode scope, string selector)
        {
            var chains = new List<List<SimpleSel>>();
            foreach (var part in selector.Split(','))
            {
                var chain = ParseChain(part);
                if (chain.Count > 0) chains.Add(chain);
            }
            var result = new List<HNode>();
            foreach (var node in scope.Descendants())
            {
                foreach (var chain in chains)
                {
                    if (MatchChain(scope, node, chain))
                    {
                        result.Add(node);
                        break;
                    }
                }
            }
            return result;
        }

        public static HNode One(HNode scope, string selector)
        {
            var found = Select(scope, selector);
            return found.Count == 0 ? null : found[0];
        }

        static bool MatchChain(HNode scope, HNode node, List<SimpleSel> chain)
        {
            if (!Matches(node, chain[chain.Count - 1])) return false;
            var index = chain.Count - 2;
            var parent = node.Parent;
            while (index >= 0 && parent != null && !ReferenceEquals(parent, scope))
            {
                if (Matches(parent, chain[index])) index--;
                parent = parent.Parent;
            }
            return index < 0;
        }

        static bool Matches(HNode node, SimpleSel sel)
        {
            if (node.IsText) return false;
            if (sel.Tag.Length > 0 && node.Name != sel.Tag) return false;
            foreach (var cls in sel.Classes)
                if (!node.HasClass(cls)) return false;
            if (sel.Attr != null && !node.Attr.ContainsKey(sel.Attr)) return false;
            return true;
        }

        static List<SimpleSel> ParseChain(string text)
        {
            var chain = new List<SimpleSel>();
            foreach (var bit in Regex.Split(text.Trim(), @"\s+"))
                if (bit.Length > 0) chain.Add(ParseSimple(bit));
            return chain;
        }

        static SimpleSel ParseSimple(string bit)
        {
            var sel = new SimpleSel();
            var i = 0;
            if (bit[0] != '.' && bit[0] != '[')
            {
                while (i < bit.Length && bit[i] != '.' && bit[i] != '[') i++;
                sel.Tag = bit.Substring(0, i).ToLowerInvariant();
            }
            while (i < bit.Length)
            {
                if (bit[i] == '.')
                {
                    i++;
                    var start = i;
                    while (i < bit.Length && bit[i] != '.' && bit[i] != '[') i++;
                    sel.Classes.Add(bit.Substring(start, i - start));
                }
                else if (bit[i] == '[')
                {
                    i++;
                    var start = i;
                    while (i < bit.Length && bit[i] != ']') i++;
                    sel.Attr = bit.Substring(start, i - start).ToLowerInvariant();
                    if (i < bit.Length) i++;
                }
                else break;
            }
            return sel;
        }

        static int IndexOfIgnore(string text, string needle, int start)
        {
            return text.IndexOf(needle, start, StringComparison.OrdinalIgnoreCase);
        }

        static bool IsNameStart(char c) { return char.IsLetter(c); }
        static bool IsName(char c) { return char.IsLetterOrDigit(c) || c == '-' || c == ':'; }

        sealed class SimpleSel
        {
            public string Tag = "";
            public readonly List<string> Classes = new List<string>();
            public string Attr;
        }
    }

    public static class Markdown
    {
        static readonly HashSet<string> Blocks = new HashSet<string>
        {
            "address", "article", "aside", "blockquote", "div", "dl", "dt", "dd", "fieldset",
            "figcaption", "figure", "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6",
            "header", "hr", "li", "main", "nav", "ol", "p", "pre", "section", "table", "ul"
        };

        public static string From(HNode node)
        {
            var text = Render(node, false, false) ?? "";
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            text = Regex.Replace(text, @"\n{3,}", "\n\n").Trim();
            text = Regex.Replace(text, @"^(?:---\n+)+", "");
            return text.Trim() + "\n";
        }

        static string Render(HNode node, bool pre, bool noformat)
        {
            if (node.IsText)
            {
                if (pre) return node.Text ?? "";
                var text = node.Text ?? "";
                text = Regex.Replace(text, @"[ \t]*\r?\n[ \t]*", "\n");
                text = Regex.Replace(text, @"[ \t]+", " ");
                if (!noformat)
                {
                    text = text.Replace("*", "\\*");
                    text = text.Replace("_", "\\_");
                }
                return text;
            }
            switch (node.Name)
            {
                case "script":
                case "style":
                    return "";
                case "br":
                    return "  \n";
                case "hr":
                    return "\n\n---\n\n";
                case "h1":
                case "h2":
                case "h3":
                case "h4":
                case "h5":
                case "h6":
                    return Heading(node);
                case "p":
                    return BlockWrap(Inline(node));
                case "strong":
                case "b":
                    return Wrap(Inline(node), "**");
                case "em":
                case "i":
                    return Wrap(Inline(node), "*");
                case "del":
                case "s":
                    return Wrap(Inline(node), "~~");
                case "code":
                case "kbd":
                case "samp":
                    if (pre || InPre(node)) return Raw(node);
                    return CodeSpan(Raw(node));
                case "pre":
                    return RenderPre(node);
                case "a":
                    return RenderLink(node);
                case "img":
                    return RenderImage(node);
                case "blockquote":
                    return RenderQuote(node);
                case "ul":
                case "ol":
                    return "\n\n" + RenderList(node) + "\n\n";
                case "li":
                    return RenderChildren(node, false, false);
                case "table":
                    return "\n\n" + RenderTable(node) + "\n\n";
                case "q":
                    return "\"" + Inline(node) + "\"";
                default:
                    if (Blocks.Contains(node.Name)) return BlockWrap(RenderChildren(node, false, false).Trim());
                    return RenderChildren(node, pre, noformat);
            }
        }

        static string Heading(HNode node)
        {
            var level = node.Name[1] - '0';
            if (level < 1) level = 1;
            if (level > 6) level = 6;
            var text = Regex.Replace(Inline(node).Trim(), @"\s+", " ");
            return "\n\n" + new string('#', level) + " " + text + "\n\n";
        }

        static string Inline(HNode node)
        {
            return RenderChildren(node, false, false).Trim();
        }

        static string BlockWrap(string text)
        {
            text = (text ?? "").Trim();
            return text.Length == 0 ? "" : "\n\n" + text + "\n\n";
        }

        static string Wrap(string text, string mark)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var prefix = text[0] == ' ' ? " " : "";
            var suffix = text[text.Length - 1] == ' ' ? " " : "";
            text = text.Trim();
            if (text.Length == 0) return prefix + suffix;
            return prefix + mark + text + mark + suffix;
        }

        static string CodeSpan(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return "";
            var runs = 0;
            var current = 0;
            foreach (var ch in text)
            {
                if (ch == '`') current++;
                else { if (current > runs) runs = current; current = 0; }
            }
            if (current > runs) runs = current;
            var fence = new string('`', runs + 1);
            if (runs > 0) text = " " + text + " ";
            return fence + text + fence;
        }

        static string RenderPre(HNode node)
        {
            var code = First(node, "code");
            var body = code != null ? Raw(code) : Raw(node);
            body = body.Trim('\r', '\n');
            return "\n\n```" + FenceLanguage(node) + "\n" + body + "\n```\n\n";
        }

        public static string FenceLanguage(HNode pre)
        {
            var nodes = new List<HNode> { pre };
            var code = First(pre, "code");
            if (code != null) nodes.Add(code);
            foreach (var node in nodes)
            {
                string classes;
                if (node.Attr.TryGetValue("class", out classes))
                {
                    foreach (var name in classes.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (name.StartsWith("language-", StringComparison.Ordinal) && name != "language-fallback")
                            return name.Substring("language-".Length);
                    }
                }
                var data = node.Get("data-lang");
                if (!string.IsNullOrEmpty(data)) return data;
            }
            return "";
        }

        static string RenderLink(HNode node)
        {
            var href = node.Get("href");
            var text = Inline(node);
            if (string.IsNullOrEmpty(text)) return "";
            if (string.IsNullOrEmpty(href)) return text;
            var plain = text.Replace("\\_", "_");
            if (plain == href && string.IsNullOrEmpty(node.Get("title"))) return "<" + href + ">";
            var title = node.Get("title");
            var titlePart = string.IsNullOrEmpty(title) ? "" : " \"" + title.Replace("\"", "\\\"") + "\"";
            return "[" + text + "](" + href + titlePart + ")";
        }

        static string RenderImage(HNode node)
        {
            var alt = node.Get("alt") ?? "";
            var src = node.Get("src") ?? "";
            var title = node.Get("title");
            var titlePart = string.IsNullOrEmpty(title) ? "" : " \"" + title.Replace("\"", "\\\"") + "\"";
            return "![" + alt + "](" + src + titlePart + ")";
        }

        static string RenderQuote(HNode node)
        {
            var text = RenderChildren(node, false, false);
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            text = Regex.Replace(text, @"\n{3,}", "\n\n").Trim(' ', '\t', '\r', '\n');
            if (text.Length == 0) return "\n";
            var lines = text.Replace("\r\n", "\n").Split('\n');
            var sb = new StringBuilder();
            sb.Append('\n');
            foreach (var line in lines)
            {
                sb.Append(line.Length == 0 ? ">" : "> " + line);
                sb.Append('\n');
            }
            sb.Append('\n');
            return sb.ToString();
        }

        static string RenderList(HNode list)
        {
            var sb = new StringBuilder();
            var index = 1;
            int start;
            if (list.Name == "ol" && int.TryParse(list.Get("start"), out start)) index = start;
            foreach (var child in list.Children)
            {
                if (child.Name != "li") continue;
                var bullet = list.Name == "ol" ? index + ". " : "- ";
                index++;
                var inner = RenderChildren(child, false, false).Trim();
                if (inner.Length == 0)
                {
                    sb.Append(bullet.TrimEnd());
                    sb.Append('\n');
                    continue;
                }
                var lines = inner.Replace("\r\n", "\n").Split('\n');
                var pad = new string(' ', bullet.Length);
                sb.Append(bullet);
                sb.Append(lines[0].TrimStart());
                sb.Append('\n');
                for (var i = 1; i < lines.Length; i++)
                {
                    if (lines[i].Length == 0) sb.Append('\n');
                    else sb.Append(pad).Append(lines[i]).Append('\n');
                }
            }
            return sb.ToString().TrimEnd('\n');
        }

        static string RenderTable(HNode table)
        {
            var rows = new List<HNode>();
            foreach (var node in table.Descendants())
            {
                if (node.Name != "tr") continue;
                if (ReferenceEquals(Nearest(node, "table"), table)) rows.Add(node);
            }
            var sb = new StringBuilder();
            for (var r = 0; r < rows.Count; r++)
            {
                var cells = new List<HNode>();
                foreach (var child in rows[r].Children)
                    if (child.Name == "td" || child.Name == "th") cells.Add(child);
                var head = cells.Count > 0 && cells.TrueForAll(cell => cell.Name == "th");
                if (rows[r].Parent != null && rows[r].Parent.Name == "thead") head = true;
                var span = 0;
                foreach (var cell in cells) span += ColSpan(cell);
                if (span == 0) span = 1;
                if (r == 0 && !head)
                {
                    sb.Append("|");
                    for (var c = 0; c < span; c++) sb.Append("  |");
                    sb.Append('\n');
                    sb.Append("| ");
                    sb.Append(string.Join(" | ", Enumerable.Repeat("---", span)));
                    sb.Append(" |\n");
                }
                sb.Append('|');
                foreach (var cell in cells)
                {
                    var text = Regex.Replace(Inline(cell), @"\s+", " ").Trim();
                    var count = ColSpan(cell);
                    for (var c = 0; c < count; c++) sb.Append(' ').Append(text).Append(" |");
                }
                sb.Append('\n');
                if (r == 0 && head)
                {
                    sb.Append("| ");
                    sb.Append(string.Join(" | ", Enumerable.Repeat("---", span)));
                    sb.Append(" |\n");
                }
            }
            return sb.ToString().TrimEnd('\n');
        }

        static int ColSpan(HNode cell)
        {
            var value = cell.Get("colspan");
            int parsed;
            if (!string.IsNullOrEmpty(value) && int.TryParse(value, out parsed) && parsed > 0)
                return Math.Min(parsed, 1000);
            return 1;
        }

        static HNode Nearest(HNode node, string name)
        {
            var parent = node.Parent;
            while (parent != null)
            {
                if (parent.Name == name) return parent;
                parent = parent.Parent;
            }
            return null;
        }

        static string RenderChildren(HNode node, bool pre, bool noformat)
        {
            var sb = new StringBuilder();
            var format = noformat || node.Name == "pre" || node.Name == "code" || node.Name == "kbd" || node.Name == "samp";
            var inPre = pre || node.Name == "pre";
            foreach (var child in node.Children)
            {
                if (!inPre && child.IsText && string.IsNullOrWhiteSpace(child.Text))
                {
                    var prev = PreviousElement(child);
                    var next = NextElement(child);
                    if (prev == null || next == null || IsBlock(prev) || IsBlock(next)) continue;
                }
                sb.Append(Render(child, inPre, format));
            }
            return sb.ToString();
        }

        static bool IsBlock(HNode node) { return node != null && !node.IsText && Blocks.Contains(node.Name); }

        static HNode PreviousElement(HNode node)
        {
            if (node.Parent == null) return null;
            var index = node.Parent.Children.IndexOf(node);
            for (var i = index - 1; i >= 0; i--)
                if (!node.Parent.Children[i].IsText) return node.Parent.Children[i];
            return null;
        }

        static HNode NextElement(HNode node)
        {
            if (node.Parent == null) return null;
            var index = node.Parent.Children.IndexOf(node);
            for (var i = index + 1; i < node.Parent.Children.Count; i++)
                if (!node.Parent.Children[i].IsText) return node.Parent.Children[i];
            return null;
        }

        static HNode First(HNode node, string name)
        {
            foreach (var child in node.Children)
                if (child.Name == name) return child;
            return node.FindTag(name);
        }

        static bool InPre(HNode node)
        {
            var parent = node.Parent;
            while (parent != null)
            {
                if (parent.Name == "pre") return true;
                parent = parent.Parent;
            }
            return false;
        }

        static string Raw(HNode node)
        {
            var sb = new StringBuilder();
            RawWalk(node, sb);
            return sb.ToString();
        }

        static void RawWalk(HNode node, StringBuilder sb)
        {
            if (node.IsText) { sb.Append(node.Text); return; }
            foreach (var child in node.Children) RawWalk(child, sb);
        }
    }

    public abstract class FetchSite
    {
        public abstract string UserAgent { get; }
        public abstract string Entry { get; }
        public abstract string Prefix { get; }
        public abstract string[] Hosts { get; }
        public abstract string Blurb { get; }
        public abstract string Owners { get; }
        public abstract string Refresh { get; }
        public abstract string ReadmeTitle { get; }
        public abstract string OnlyMiss { get; }
        public virtual string[] ReadmeExtra { get { return new string[0]; } }
        public virtual bool PromoteTypedocs { get { return false; } }
        public abstract string Header(string url, string today);
        public abstract List<string> SourceUrls(Func<string, byte[]> fetch);
        public abstract string PageKey(Uri uri);
        public abstract string CleanTitle(string text);
        public abstract HNode SelectContent(HNode doc);
        public virtual void ExtraPrepare(HNode node) { }
        public abstract List<SideItem> Sidebar(HNode doc);

        public static string Squash(string text)
        {
            text = Regex.Replace(text ?? "", @"\s+", " ").Trim();
            return text.Length == 0 ? "Untitled" : text;
        }

        public static string StripSuffix(string text, params string[] suffixes)
        {
            foreach (var suffix in suffixes)
            {
                if (text.EndsWith(suffix, StringComparison.Ordinal))
                    text = text.Substring(0, text.Length - suffix.Length).Trim();
            }
            return text.Length == 0 ? "Untitled" : text;
        }

        public static string PathKey(Uri uri, string prefix)
        {
            var path = Uri.UnescapeDataString(uri.AbsolutePath);
            var prefixPath = new Uri(prefix).AbsolutePath;
            if (path.StartsWith(prefixPath, StringComparison.Ordinal))
                path = path.Substring(prefixPath.Length);
            return path.Trim('/');
        }

        public List<string> SitemapUrls(Func<string, byte[]> fetch, string sitemap, string[] skip)
        {
            var raw = Encoding.UTF8.GetString(fetch(sitemap));
            var urls = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var skipped = new HashSet<string>(skip ?? new string[0], StringComparer.Ordinal);
            foreach (Match match in Regex.Matches(raw, @"<loc>\s*([^<\s]+)\s*</loc>"))
            {
                var url = match.Groups[1].Value.Split('#')[0];
                if (!url.StartsWith(Prefix, StringComparison.Ordinal)) continue;
                if (skipped.Contains(url.TrimEnd('/'))) continue;
                AddUrl(urls, seen, url);
            }
            AddUrl(urls, seen, Entry);
            return urls;
        }

        public static void AddUrl(List<string> urls, HashSet<string> seen, string url)
        {
            var bare = url.Split('#')[0].TrimEnd('/');
            if (!seen.Add(bare)) return;
            var path = new Uri(bare).AbsolutePath;
            if (Path.HasExtension(path)) urls.Add(bare);
            else urls.Add(bare + "/");
        }

        public static string FileFor(string key, HashSet<string> keys)
        {
            if (key.Length == 0) return "index.md";
            var nested = key + "/";
            foreach (var other in keys)
                if (other.StartsWith(nested, StringComparison.Ordinal)) return key + "/index.md";
            return key + ".md";
        }

        public static string JoinUrl(string baseUrl, string href)
        {
            return new Uri(new Uri(baseUrl), href).AbsoluteUri;
        }
    }

    public sealed class SideItem
    {
        public string Key;
        public string Title;
    }

    public static class Engine
    {
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        static readonly HashSet<string> ImageExt = new HashSet<string>
        {
            ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".avif"
        };
        static readonly object ImageLock = new object();
        static HttpClient Http;

        public static int Run(FetchSite site, string root, string[] only)
        {
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("-Root is required.");
            root = Path.GetFullPath(root);
            EnsureClient(site.UserAgent);
            var all = site.SourceUrls(Fetch);
            var urls = all;
            if (only != null && only.Length > 0)
            {
                var wanted = new HashSet<string>(only.Select(item => (item ?? "").TrimEnd('/')), StringComparer.Ordinal);
                urls = all.Where(url => wanted.Contains(url.TrimEnd('/'))).ToList();
                if (urls.Count == 0)
                {
                    Console.Error.WriteLine(site.OnlyMiss);
                    return 1;
                }
            }
            else Clear(root);

            var keys = new HashSet<string>(all.Select(url => site.PageKey(new Uri(url))));
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var key in keys) files[key] = FetchSite.FileFor(key, keys);
            var imageDir = Path.Combine(root, "images");
            Directory.CreateDirectory(imageDir);
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var pages = new Dictionary<string, string>(StringComparer.Ordinal);
            var titles = new Dictionary<string, string>(StringComparer.Ordinal);
            var failures = new List<string>();
            var done = 0;
            var gate = new object();
            Parallel.ForEach(urls, new ParallelOptions { MaxDegreeOfParallelism = 6 }, url =>
            {
                string key = null, title = null, target = null, text = null, error = null;
                try
                {
                    key = site.PageKey(new Uri(url));
                    target = files[key];
                    var html = Encoding.UTF8.GetString(Fetch(url));
                    var prepared = Prepare(site, html, url, imageDir, target);
                    title = prepared.Item1;
                    var body = Markdown.From(prepared.Item2);
                    if (!body.TrimStart().StartsWith("#", StringComparison.Ordinal))
                        body = "# " + title + "\n\n" + body;
                    body = Rewrite(body, site, url, target, files);
                    text = site.Header(url, today) + body;
                }
                catch (Exception ex)
                {
                    error = url + " (" + ex.Message + ")";
                }
                var count = Interlocked.Increment(ref done);
                lock (gate)
                {
                    if (error != null)
                    {
                        failures.Add(error);
                        Console.WriteLine("FAIL " + error);
                    }
                    else
                    {
                        var path = Path.Combine(root, target.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        File.WriteAllText(path, text, Utf8);
                        pages[key] = target;
                        titles[key] = title;
                    }
                    if (count % 20 == 0 || count == urls.Count) Console.WriteLine(count + "/" + urls.Count);
                }
            });

            if (only == null || only.Length == 0)
            {
                var homeHtml = Encoding.UTF8.GetString(Fetch(site.Entry));
                var order = site.Sidebar(HtmlDoc.Parse(homeHtml));
                for (var i = 0; i < order.Count; i++)
                {
                    string title;
                    if (titles.TryGetValue(order[i].Key, out title)) order[i].Title = title;
                }
                var known = new HashSet<string>(order.Select(item => item.Key), StringComparer.Ordinal);
                foreach (var key in pages.Keys.OrderBy(item => item, StringComparer.Ordinal))
                {
                    if (known.Contains(key)) continue;
                    string title;
                    order.Add(new SideItem { Key = key, Title = titles.TryGetValue(key, out title) ? title : (key.Length == 0 ? "index" : key) });
                }
                WriteReadme(site, root, pages, order, today);
            }

            Console.WriteLine(pages.Count + " pages, " + failures.Count + " failures, in " + root);
            if (failures.Count > 0)
            {
                Console.WriteLine(string.Join("\n", failures));
                return 1;
            }
            return 0;
        }

        public static Tuple<string, HNode> Prepare(FetchSite site, string html, string pageUrl, string imageDir, string pageFile)
        {
            var doc = HtmlDoc.Parse(html);
            var node = site.SelectContent(doc);
            if (node == null) throw new InvalidOperationException("no content element");
            var titleNode = HtmlDoc.One(doc, "title");
            var title = site.CleanTitle(titleNode == null ? "" : titleNode.GetText(" ", true));
            foreach (var tag in HtmlDoc.Select(node, "script, style, svg, button, nav, form, astro-island").ToList())
                tag.Remove();
            site.ExtraPrepare(node);
            if (site.PromoteTypedocs)
            {
                var heading = HtmlDoc.One(node, "h2.typedocs-title-text");
                if (heading != null && node.FindTag("h1") == null) heading.Name = "h1";
            }
            ConvertCallouts(node);
            ConvertAlerts(node);
            ConvertCodeButtons(node);
            ConvertVideos(node, pageUrl);
            ConvertImages(site, node, pageUrl, imageDir, pageFile);
            return Tuple.Create(title, node);
        }

        static void ConvertCallouts(HNode node)
        {
            var callouts = new List<Tuple<HNode, HNode, string>>();
            foreach (var box in HtmlDoc.Select(node, "div"))
            {
                if (!box.HasClass("hx:rounded-lg") || !box.HasClass("hx:border")) continue;
                var title = box.FindTag("p");
                var label = title == null ? "" : title.GetText(" ", true);
                string kind = null;
                if (label == "Note" || label == "Info") kind = "NOTE";
                else if (label == "Tip") kind = "TIP";
                else if (label == "Important") kind = "IMPORTANT";
                else if (label == "Warning") kind = "WARNING";
                else if (label == "Caution" || label == "Danger") kind = "CAUTION";
                if (kind == null) continue;
                callouts.Add(Tuple.Create(box, title, kind));
            }
            for (var i = callouts.Count - 1; i >= 0; i--)
            {
                var box = callouts[i].Item1;
                var title = callouts[i].Item2;
                var quote = HNode.El("blockquote");
                var marker = HNode.El("p");
                marker.Append(HNode.Txt("[!" + callouts[i].Item3 + "]"));
                quote.Append(marker);
                foreach (var child in box.Children.ToList())
                {
                    if (ReferenceEquals(child, title)) continue;
                    quote.Append(child);
                }
                box.ReplaceWith(quote);
            }
        }

        static void ConvertAlerts(HNode node)
        {
            foreach (var alert in HtmlDoc.Select(node, ".markdown-alert").ToList())
            {
                var kind = "NOTE";
                string classes;
                if (alert.Attr.TryGetValue("class", out classes))
                {
                    foreach (var name in classes.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (name.StartsWith("markdown-alert-", StringComparison.Ordinal))
                            kind = name.Substring("markdown-alert-".Length).ToUpperInvariant();
                    }
                }
                var label = HtmlDoc.One(alert, ".markdown-alert-title");
                if (label != null) label.Remove();
                var quote = HNode.El("blockquote");
                var marker = HNode.El("p");
                marker.Append(HNode.Txt("[!" + kind + "]"));
                quote.Append(marker);
                foreach (var child in alert.Children.ToList()) quote.Append(child);
                alert.ReplaceWith(quote);
            }
        }

        static void ConvertCodeButtons(HNode node)
        {
            foreach (var pre in HtmlDoc.Select(node, "pre").ToList())
            {
                HNode button = null;
                foreach (var candidate in HtmlDoc.Select(pre, "[data-code]"))
                {
                    var code = candidate.Get("data-code");
                    if (!string.IsNullOrEmpty(code)) { button = candidate; break; }
                }
                if (button == null) continue;
                var language = "";
                var codeNode = pre.FindTag("code");
                if (codeNode != null)
                {
                    string classes;
                    if (codeNode.Attr.TryGetValue("class", out classes))
                    {
                        foreach (var name in classes.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                            if (name.StartsWith("language-", StringComparison.Ordinal)) language = name;
                    }
                }
                var replacement = HNode.El("code");
                if (language.Length > 0) replacement.Set("class", language);
                replacement.Append(HNode.Txt(button.Get("data-code")));
                var fence = HNode.El("pre");
                fence.Append(replacement);
                pre.ReplaceWith(fence);
            }
        }

        static void ConvertVideos(HNode node, string pageUrl)
        {
            foreach (var video in HtmlDoc.Select(node, "video").ToList())
            {
                var source = video.FindTag("source");
                var src = source == null ? null : source.Get("src");
                if (string.IsNullOrEmpty(src)) src = video.Get("src") ?? "";
                var href = string.IsNullOrEmpty(src) ? pageUrl : FetchSite.JoinUrl(pageUrl, src);
                var link = HNode.El("a");
                link.Set("href", href);
                link.Append(HNode.Txt("Video"));
                var paragraph = HNode.El("p");
                paragraph.Append(link);
                video.ReplaceWith(paragraph);
            }
        }

        static void ConvertImages(FetchSite site, HNode node, string pageUrl, string imageDir, string pageFile)
        {
            var hosts = new HashSet<string>(site.Hosts, StringComparer.OrdinalIgnoreCase);
            foreach (var image in HtmlDoc.Select(node, "img"))
            {
                var src = image.Get("src");
                if (string.IsNullOrEmpty(src) || src.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
                var absolute = FetchSite.JoinUrl(pageUrl, src);
                var uri = new Uri(absolute);
                if (!hosts.Contains(uri.Host))
                {
                    image.Set("src", absolute);
                    continue;
                }
                var suffix = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
                if (!ImageExt.Contains(suffix)) suffix = ".img";
                var name = Regex.Replace(Uri.UnescapeDataString(uri.AbsolutePath).Trim('/').ToLowerInvariant(), @"[^a-z0-9.-]+", "-");
                if (!name.EndsWith(suffix, StringComparison.Ordinal)) name += suffix;
                var target = Path.Combine(imageDir, name);
                lock (ImageLock)
                {
                    if (!File.Exists(target))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        File.WriteAllBytes(target, Fetch(absolute));
                    }
                }
                image.Set("src", Relative(Path.Combine("images", name).Replace('\\', '/'), pageFile));
            }
        }

        static string Rewrite(string text, FetchSite site, string pageUrl, string pageFile, Dictionary<string, string> files)
        {
            var hosts = new HashSet<string>(site.Hosts, StringComparer.OrdinalIgnoreCase);
            var prefix = site.Prefix.TrimEnd('/');
            return Regex.Replace(text, @"\]\(([^)\s]+)\)", match =>
            {
                var href = match.Groups[1].Value;
                if (href.StartsWith("#", StringComparison.Ordinal)) return match.Value;
                Uri absolute;
                if (!Uri.TryCreate(new Uri(pageUrl), href, out absolute)) return match.Value;
                var anchor = string.IsNullOrEmpty(absolute.Fragment) ? "" : absolute.Fragment;
                var keyUrl = absolute.GetLeftPart(UriPartial.Path).TrimEnd('/');
                string target;
                if (hosts.Contains(absolute.Host))
                {
                    var key = site.PageKey(new Uri(keyUrl + "/"));
                    if (files.TryGetValue(key, out target))
                        return "](" + Relative(target, pageFile) + anchor + ")";
                }
                if (!keyUrl.StartsWith(prefix, StringComparison.Ordinal))
                {
                    if (href.StartsWith("/", StringComparison.Ordinal) && hosts.Contains(absolute.Host))
                        return "](" + keyUrl + anchor + ")";
                    return match.Value;
                }
                var local = site.PageKey(new Uri(keyUrl + "/"));
                if (files.TryGetValue(local, out target))
                    return "](" + Relative(target, pageFile) + anchor + ")";
                if (href.StartsWith("/", StringComparison.Ordinal))
                    return "](" + keyUrl + anchor + ")";
                return match.Value;
            });
        }

        public static string Relative(string target, string sourceFile)
        {
            var start = Path.GetDirectoryName(sourceFile.Replace('/', Path.DirectorySeparatorChar));
            if (string.IsNullOrEmpty(start)) start = ".";
            var relative = Path.GetRelativePath(start, target.Replace('/', Path.DirectorySeparatorChar));
            return relative.Replace('\\', '/');
        }

        static void WriteReadme(FetchSite site, string root, Dictionary<string, string> pages, List<SideItem> order, string today)
        {
            var lines = new List<string>
            {
                "# " + site.ReadmeTitle,
                "",
                site.Blurb + " Fetched " + today + ".",
                "",
                site.Owners + " Do not edit these files. Run `" + site.Refresh + "` to refresh them.",
                "",
                "From <" + site.Entry + ">.",
                ""
            };
            foreach (var extra in site.ReadmeExtra)
            {
                lines.Add(extra);
                lines.Add("");
            }
            lines.Add("## Pages");
            lines.Add("");
            foreach (var item in order)
            {
                string target;
                if (!pages.TryGetValue(item.Key, out target)) continue;
                var depth = item.Key.Length == 0 ? 0 : item.Key.Count(ch => ch == '/');
                lines.Add(new string(' ', depth * 2) + "- [" + item.Title + "](" + target.Replace('\\', '/') + ")");
            }
            lines.Add("");
            File.WriteAllText(Path.Combine(root, "README.md"), string.Join("\n", lines), Utf8);
        }

        static void Clear(string root)
        {
            if (!Directory.Exists(root))
            {
                Directory.CreateDirectory(root);
                return;
            }
            var extensions = new HashSet<string>(ImageExt, StringComparer.OrdinalIgnoreCase) { ".md", ".img" };
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (extensions.Contains(Path.GetExtension(file))) File.Delete(file);
            }
            foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(item => item.Length))
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            }
        }

        static void EnsureClient(string userAgent)
        {
            if (Http != null) return;
            var handler = new SocketsHttpHandler();
            Http = new HttpClient(handler);
            Http.Timeout = TimeSpan.FromSeconds(60);
            Http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        }

        static byte[] Fetch(string url)
        {
            Exception last = null;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using (var response = Http.GetAsync(url).GetAwaiter().GetResult())
                    {
                        response.EnsureSuccessStatusCode();
                        return response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                    }
                }
                catch (Exception ex)
                {
                    if (!(ex is HttpRequestException) && !(ex is TaskCanceledException)) throw;
                    last = ex;
                }
            }
            throw last;
        }
    }
}
namespace FetchRef
{
    public sealed class HyprlandSite : FetchSite
    {
        public override string UserAgent { get { return "winland-docs-fetch"; } }
        public override string Entry { get { return "https://wiki.hypr.land/"; } }
        public override string Prefix { get { return "https://wiki.hypr.land/"; } }
        public override string[] Hosts { get { return new[] { "wiki.hypr.land" }; } }
        public override string Blurb { get { return "A copy of the Hyprland wiki, kept here so Winland can be checked against it. The wiki tracks the latest Hyprland git commit, not a tagged release."; } }
        public override string Owners { get { return "The text belongs to the Hyprland authors."; } }
        public override string Refresh { get { return "pwsh scripts/dev.ps1 -Refresh"; } }
        public override string ReadmeTitle { get { return "Hyprland documentation"; } }
        public override string OnlyMiss { get { return "none of --only matched the sitemap"; } }
        public override string[] ReadmeExtra { get { return new[] { "Videos stay linked to `dl.hypr.land`. They are not copied into this folder." }; } }
        public override string Header(string url, string today)
        {
            return "<!-- Source: " + url + " · fetched " + today + ". Do not edit, run pwsh scripts/dev.ps1. -->\n\n";
        }
        public override List<string> SourceUrls(Func<string, byte[]> fetch)
        {
            return SitemapUrls(fetch, "https://wiki.hypr.land/sitemap.xml", new[] { "https://wiki.hypr.land/tags", "https://wiki.hypr.land/categories" });
        }
        public override string PageKey(Uri uri) { return PathKey(uri, Prefix); }
        public override string CleanTitle(string text)
        {
            return StripSuffix(Squash(text), " – Hyprland Wiki", " - Hyprland Wiki");
        }
        public override HNode SelectContent(HNode doc)
        {
            var nodes = HtmlDoc.Select(doc, "div.content");
            foreach (var item in nodes)
                if (item.FindTag("h1", "h2") != null) return item;
            if (nodes.Count > 0) return nodes[nodes.Count - 1];
            return HtmlDoc.One(doc, "main") ?? HtmlDoc.One(doc, "article") ?? HtmlDoc.One(doc, "body");
        }
        public override List<SideItem> Sidebar(HNode doc)
        {
            var items = new List<SideItem>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var anchor in HtmlDoc.Select(doc, "aside a[href]"))
            {
                var href = anchor.Get("href") ?? "";
                if (href.StartsWith("#", StringComparison.Ordinal)) continue;
                var absolute = JoinUrl(Entry, href).Split('#')[0].TrimEnd('/');
                if (!absolute.StartsWith(Prefix.TrimEnd('/'), StringComparison.Ordinal)) continue;
                var key = PageKey(new Uri(absolute + "/"));
                if (seen.Contains(key)) continue;
                var title = Regex.Replace(anchor.GetText(" ", true), @"\s+", " ").Trim();
                if (title.Length == 0) continue;
                seen.Add(key);
                items.Add(new SideItem { Key = key, Title = title });
            }
            return items;
        }
    }
}
'@

Add-Type -TypeDefinition $source -Language CSharp
if ([string]::IsNullOrWhiteSpace($Root)) { throw '-Root is required.' }
$onlyArgs = [string[]]@()
if ($null -ne $Only -and $Only.Count -gt 0) { $onlyArgs = [string[]]$Only }
exit [FetchRef.Engine]::Run([FetchRef.HyprlandSite]::new(), $Root, $onlyArgs)