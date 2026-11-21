// Hallow Browser 1.2 - reader mode: a clean, text-first view of an article in Hallow's typography, following light / dark.
// The extractor is our own small readability-style script (no outside library): it scores the page's paragraphs, picks the
// block that holds most of the text (fewer links = better; nav, comments, sidebars, ads and the like don't count), and returns
// the article as plain blocks (paragraphs, headings, list items, quotes, code, picture captions). The view is drawn by WPF from
// those blocks - no page HTML, scripts or styles come along. Pictures aren't fetched (only their captions show).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace HallowBrowser
{
    public static class Reader
    {
        // is this page an article? (runs after a page loads in the tab you're looking at; cheap)
        public const string Probe = "(function(){try{if(!document.body)return false;var n=0,k=0,ps=document.body.querySelectorAll('p');for(var i=0;i<ps.length&&i<400;i++){var l=(ps[i].textContent||'').trim().length;if(l>=80){n+=l;k++;}}var a=document.querySelector('article');return (k>=3&&n>=500)||(!!a&&(a.textContent||'').trim().length>=700);}catch(e){return false;}})()";

        public const string Extract = @"(function(){
function T(e){return (e.textContent||'').replace(/\s+/g,' ').trim();}
var bad=/(^|[\s_-])(comment|comments|meta|footer|footnote|sidebar|side|nav|navbar|navigation|menu|share|sharing|social|promo|related|recommend|recommended|advert|advertisement|ad|ads|sponsor|sponsored|banner|cookie|consent|subscribe|newsletter|popup|modal|breadcrumb|breadcrumbs|toolbar|skip|outbrain|taboola)([\s_-]|$)/i;
var good=/article|body|content|entry|main|page|post|text|story|prose/i;
function cls(e){var c=e.className;if(c&&typeof c!=='string')c=c.baseVal||'';return (c||'')+' '+(e.id||'');}
function weight(e){var c=cls(e),w=0;if(bad.test(c))w-=25;if(good.test(c))w+=25;return w;}
function skip(e){var tn=e.tagName;if(/^(SCRIPT|STYLE|NOSCRIPT|NAV|ASIDE|FOOTER|FORM|BUTTON|IFRAME|SVG|CANVAS|SELECT|INPUT|TEXTAREA|DIALOG|MENU|TEMPLATE|OBJECT|EMBED)$/.test(tn))return true;
 if(e.getAttribute('aria-hidden')==='true'||e.hidden)return true;var r=e.getAttribute('role');if(r&&/navigation|banner|complementary|contentinfo|dialog|menu|alert/.test(r))return true;
 var c=cls(e);return bad.test(c)&&!/article|entry|post|story|prose|main/i.test(c);}
function ld(e){var t=T(e).length||1,l=0,a=e.getElementsByTagName('a');for(var i=0;i<a.length;i++)l+=T(a[i]).length;return Math.min(1,l/t);}
var cand=[],score=new Map();
function add(e,s){if(!e||e.nodeType!==1||e===document.documentElement)return;if(!score.has(e)){var b={DIV:5,ARTICLE:25,MAIN:15,SECTION:3,TD:3,BLOCKQUOTE:3,BODY:0}[e.tagName]||0;score.set(e,b+weight(e));cand.push(e);}score.set(e,score.get(e)+s);}
var ps=document.body?document.body.querySelectorAll('p,pre,td,blockquote'):[];
for(var i=0;i<ps.length;i++){var p=ps[i],anc=p.parentElement,hid=false;for(var k=0;k<8&&anc&&anc!==document.body;k++){if(skip(anc)){hid=true;break;}anc=anc.parentElement;}if(hid)continue;
 var t=T(p);if(t.length<25)continue;var s=1+t.split(/[,、，]/).length+Math.min(3,Math.floor(t.length/100));add(p.parentElement,s);if(p.parentElement)add(p.parentElement.parentElement,s/2);}
var best=null,bs=0;for(var j=0;j<cand.length;j++){var c=cand[j],s2=score.get(c)*(1-ld(c));if(s2>bs){bs=s2;best=c;}}
function meta(n){var m=document.querySelector('meta[property=""'+n+'""],meta[name=""'+n+'""]');return m?(m.getAttribute('content')||'').trim():'';}
var h1=document.querySelector('h1');var title=meta('og:title')||(h1?T(h1):'')||document.title||'';
var by=meta('author');if(!by){var ba=document.querySelector('[rel=author],.byline,.author,[itemprop=author]');if(ba)by=T(ba).slice(0,120);}
var site=meta('og:site_name')||location.hostname;
var blocks=[],words=0;
function push(t,x,src){x=(x||'').trim();if(!x&&t!=='img')return;if(x.length>20000)x=x.slice(0,20000);var l=blocks[blocks.length-1];if(l&&t!=='img'&&l.x===x)return;var b={t:t,x:x};if(src)b.src=src;blocks.push(b);if(t!=='img')words+=x.split(/\s+/).length;}
function walk(e){for(var n=e.firstChild;n;n=n.nextSibling){
 if(n.nodeType===3){var tx=n.textContent.replace(/\s+/g,' ').trim();if(tx.length>40)push('p',tx);continue;}
 if(n.nodeType!==1||skip(n))continue;var tn=n.tagName;
 if(/^H[1-6]$/.test(tn)){var lv=+tn.charAt(1),ht=T(n);if(lv===1&&ht===title)continue;push(lv<=2?'h2':'h3',ht);continue;}
 if(tn==='P'){var pt=T(n);if(ld(n)>0.5&&pt.length<200)continue;push('p',pt);continue;}
 if(tn==='LI'){if(ld(n)>0.6)continue;push('li',T(n));continue;}
 if(tn==='PRE'){push('pre',n.textContent);continue;}
 if(tn==='BLOCKQUOTE'){push('quote',T(n));continue;}
 if(tn==='IMG'){var w=n.naturalWidth||n.width||0;if(w>=120)push('img',n.getAttribute('alt')||'',n.currentSrc||n.src);continue;}
 if(tn==='FIGCAPTION'){push('cap',T(n));continue;}
 if((tn==='UL'||tn==='OL')&&ld(n)>0.6)continue;
 if(tn==='BR'||tn==='HR')continue;
 walk(n);}}
var ok=false;
if(best){walk(best);ok=words>=80&&blocks.filter(function(b){return b.t==='p';}).length>=2;}
return JSON.stringify({ok:ok,title:title.slice(0,300),byline:(by||'').slice(0,160),site:site.slice(0,100),words:words,blocks:blocks.slice(0,2000)});
})()";

        public static double PageWidth(string w) { return w == "narrow" ? 600 : w == "wide" ? 960 : 760; }

        // the view for one tab: a small bar (size, width, close) over the article
        public static FrameworkElement View(BrowserWindow w, Tab t, ReaderDoc d)
        {
            var g = new Grid(); g.SetResourceReference(Panel.BackgroundProperty, "Bg");
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition());
            var bar = new Grid { Height = 36 }; bar.SetResourceReference(Panel.BackgroundProperty, "Bar");
            bar.ColumnDefinitions.Add(new ColumnDefinition()); bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var lab = Ui.Label("Reader  //  " + (d.Site ?? "")); lab.Margin = new Thickness(16, 0, 0, 0); lab.VerticalAlignment = VerticalAlignment.Center; lab.TextTrimming = TextTrimming.CharacterEllipsis; lab.TextWrapping = TextWrapping.NoWrap;
            bar.Children.Add(lab);
            var viewer = new FlowDocumentScrollViewer { IsToolBarVisible = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = true };
            viewer.SetResourceReference(Control.BackgroundProperty, "Bg");
            var size = Ui.Muted("", 11); size.VerticalAlignment = VerticalAlignment.Center; size.Margin = new Thickness(4, 0, 4, 0); size.MinWidth = 34; size.TextAlignment = TextAlignment.Center;
            // the text column is centred: the side padding grows with the window (the scroll bar stays at the edge)
            Action pad = () => { var fd = viewer.Document; if (fd == null) return; var side = Math.Max(28, Math.Floor((viewer.ActualWidth - 20 - PageWidth(w.S.ReaderWidth)) / 2)); fd.PagePadding = new Thickness(side, 30, side, 80); };
            viewer.SizeChanged += (a, b) => pad();
            Action render = () => { viewer.Document = Doc(w.S, d); size.Text = w.S.ReaderFontSize + " px"; pad(); };
            Action<int> font = dir =>
            {
                var sizes = BrowserSettings.ReaderSizes; int i = Array.IndexOf(sizes, w.S.ReaderFontSize); if (i < 0) i = 2;
                i = Math.Max(0, Math.Min(sizes.Length - 1, i + dir)); w.S.ReaderFontSize = sizes[i]; w.S.Save(); render();
            };
            var widthBtn = Ui.Tool("text-width", "Text width: " + w.S.ReaderWidth, null, 12);
            widthBtn.Click += (a, b) => { w.S.ReaderWidth = w.S.ReaderWidth == "narrow" ? "medium" : w.S.ReaderWidth == "medium" ? "wide" : "narrow"; w.S.Save(); widthBtn.ToolTip = "Text width: " + w.S.ReaderWidth; render(); };
            var acts = Ui.Row(Ui.Tool("minus", "Smaller text", () => font(-1), 11), size, Ui.Tool("plus", "Bigger text", () => font(1), 11), widthBtn,
                Ui.Tool("xmark", "Leave reader mode (F9)", () => w.ToggleReader(t), 12));
            acts.VerticalAlignment = VerticalAlignment.Center; acts.Margin = new Thickness(0, 0, 8, 0); Grid.SetColumn(acts, 1); bar.Children.Add(acts);
            var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom }; line.SetResourceReference(Border.BackgroundProperty, "LineDim"); Grid.SetColumnSpan(line, 2); bar.Children.Add(line);
            g.Children.Add(bar);
            Grid.SetRow(viewer, 1); g.Children.Add(viewer);
            render();
            g.Tag = viewer;
            return g;
        }

        public static FlowDocument Doc(BrowserSettings s, ReaderDoc d)
        {
            double fs = s.ReaderFontSize;
            var fd = new FlowDocument { FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"), FontSize = fs, LineHeight = fs * 1.6, PagePadding = new Thickness(36, 30, 36, 80), ColumnWidth = 100000, Tag = PageWidth(s.ReaderWidth), TextAlignment = TextAlignment.Left, IsHyphenationEnabled = false };
            fd.SetResourceReference(FlowDocument.ForegroundProperty, "Text"); fd.SetResourceReference(FlowDocument.BackgroundProperty, "Bg");
            Func<string, double, Paragraph> P = (text, size) => new Paragraph(new Run(text)) { FontSize = size, Margin = new Thickness(0, 0, 0, size * 0.85) };
            var site = P((d.Site ?? "").ToUpperInvariant(), 12); site.FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"); site.SetResourceReference(TextElement.ForegroundProperty, "Lilac"); site.Margin = new Thickness(0, 0, 0, 10); site.LineHeight = 16;
            fd.Blocks.Add(site);
            var title = P(d.Title ?? "", Math.Round(fs * 1.75)); title.FontWeight = FontWeights.SemiBold; title.LineHeight = Math.Round(fs * 1.75 * 1.22); title.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"); title.Margin = new Thickness(0, 0, 0, 12);
            fd.Blocks.Add(title);
            var meta = P((string.IsNullOrWhiteSpace(d.Byline) ? "" : d.Byline + "  -  ") + d.Minutes + " min read", Math.Round(fs * 0.78)); meta.SetResourceReference(TextElement.ForegroundProperty, "Muted"); meta.Margin = new Thickness(0, 0, 0, fs * 1.6); meta.LineHeight = fs;
            fd.Blocks.Add(meta);
            List list = null;
            foreach (var b in d.Blocks)
            {
                if (b.T != "li") list = null;
                switch (b.T)
                {
                    case "h2": { var p = P(b.X, Math.Round(fs * 1.3)); p.FontWeight = FontWeights.SemiBold; p.Margin = new Thickness(0, fs * 0.8, 0, fs * 0.5); p.LineHeight = fs * 1.3 * 1.3; fd.Blocks.Add(p); break; }
                    case "h3": { var p = P(b.X, Math.Round(fs * 1.1)); p.FontWeight = FontWeights.SemiBold; p.Margin = new Thickness(0, fs * 0.6, 0, fs * 0.4); fd.Blocks.Add(p); break; }
                    case "li":
                        if (list == null) { list = new List { MarkerStyle = TextMarkerStyle.Disc, Margin = new Thickness(0, 0, 0, fs * 0.8), Padding = new Thickness(fs * 1.4, 0, 0, 0) }; fd.Blocks.Add(list); }
                        list.ListItems.Add(new ListItem(new Paragraph(new Run(b.X)) { Margin = new Thickness(0, 0, 0, fs * 0.3) })); break;
                    case "quote":
                    {
                        var p = P(b.X, fs); p.FontStyle = FontStyles.Italic; p.BorderThickness = new Thickness(3, 0, 0, 0); p.Padding = new Thickness(fs * 0.9, 2, 0, 2);
                        p.SetResourceReference(Block.BorderBrushProperty, "AccentBrush"); p.SetResourceReference(TextElement.ForegroundProperty, "Muted"); fd.Blocks.Add(p); break;
                    }
                    case "pre":
                    {
                        var p = new Paragraph(new Run(b.X.TrimEnd())) { FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"), FontSize = Math.Round(fs * 0.8), LineHeight = Math.Round(fs * 0.8 * 1.45), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, fs * 0.9) };
                        p.SetResourceReference(Block.BackgroundProperty, "PanelHi"); fd.Blocks.Add(p); break;
                    }
                    case "img":
                        if (!string.IsNullOrWhiteSpace(b.X)) { var p = P("[Picture: " + b.X + "]", Math.Round(fs * 0.8)); p.FontStyle = FontStyles.Italic; p.SetResourceReference(TextElement.ForegroundProperty, "Muted"); fd.Blocks.Add(p); }
                        break;
                    case "cap": { var p = P(b.X, Math.Round(fs * 0.8)); p.FontStyle = FontStyles.Italic; p.SetResourceReference(TextElement.ForegroundProperty, "Muted"); fd.Blocks.Add(p); break; }
                    default: fd.Blocks.Add(P(b.X, fs)); break;
                }
            }
            return fd;
        }

        // all the text the view shows (tests)
        public static string Text(ReaderDoc d) { return string.Join("\n", new[] { d.Title }.Concat(d.Blocks.Select(b => b.X))); }

        // --snap: a made-up article
        public static ReaderDoc Mock()
        {
            var d = new ReaderDoc { Ok = true, Title = "How the season 7 matchmaker finds a game in half the time", Byline = "Hallow News desk", Site = "news.example.com", Words = 1150 };
            d.Blocks.Add(new ReaderBlock { T = "p", X = "Made-up article for a screenshot. Matchmaking used to wait for a perfect lobby. The new system starts with a wide search and narrows it while you wait, so most games start in under a minute." });
            d.Blocks.Add(new ReaderBlock { T = "h2", X = "What changed" });
            d.Blocks.Add(new ReaderBlock { T = "li", X = "Regions are searched together when queues are short." });
            d.Blocks.Add(new ReaderBlock { T = "li", X = "Parties of two get their own queue in every mode." });
            d.Blocks.Add(new ReaderBlock { T = "quote", X = "We'd rather you play than watch a spinner." });
            d.Blocks.Add(new ReaderBlock { T = "p", X = "Ranked games still match by skill first, and the season pass adds twenty more tiers. The full notes are on the patch page." });
            return d;
        }
    }
}
