using System;
using Raphael.Services.Guides;
using Raphael.UI.Forms;
using Raphael.Utils;
using Raphael.UI.Framework.CustomLib.Util;
using Raphael.UI.Framework.UniverseLib.UI;
using TMPro;
using UnityEngine;

namespace Raphael.UI.ModContent;

// 0.73.0: renders a Services/Guides Guide. One visual vocabulary for every guide page:
//   header      title, one-line purpose, "Checked against <mod> <version>" (muted)
//   section     open → gold-ruled heading + content; closed → a collapsible row
//   detail      a collapsible row inside a section
//   commands    stacked rows: syntax (accent, escaped) with an Admin / Destructive tag, description under it
//   settings    stacked rows: [Section] Key = default, meaning under it
//   tab map     stacked rows: tab name, what it does, the command it sends
//   callout     a tinted card with a one-word label (no side stripe)
// Prose sizes follow the 0.13.0 readability pass: body 14, rows 13, meta 12.
public partial class MainPanel
{
    private static readonly Color GUIDE_ADMIN_TINT = new(0.86f, 0.55f, 0.55f, 1f);

    private static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

    private void RenderGuide(GameObject page, Guide guide)
    {
        if (guide == null)
        {
            AddGuideSection(page, "Guide unavailable", "This guide failed to load. Check the BepInEx log for the error.");
            return;
        }

        var title = GuideLabel(page, "GuideTitle", guide.Title, Theme.ScaledUI(18), ARTICLE_HEADING_ACCENT, FontStyles.Bold);
        ApplyStrongAccentOutline(title);
        if (!string.IsNullOrEmpty(guide.Purpose))
            GuideLabel(page, "GuidePurpose", guide.Purpose, Theme.ScaledUI(14), null, FontStyles.Normal);
        if (!string.IsNullOrEmpty(guide.CheckedAgainst))
            GuideLabel(page, "GuideChecked", $"Checked against {guide.CheckedAgainst}.", Theme.ScaledUI(12), Theme.MutedBody, FontStyles.Italic);
        GuideSpacer(page, 4);

        foreach (var section in guide.Sections)
        {
            try
            {
                if (section.Open)
                {
                    AddSectionHeading(page, section.Title);
                    RenderGuideBlocks(page, section.Blocks);
                    GuideSpacer(page, 6);
                }
                else
                {
                    CollapsibleSection.Build(page, section.Title, startExpanded: false,
                        content => RenderGuideBlocks(content, section.Blocks));
                }
            }
            catch (Exception ex)
            {
                LogUtils.LogError($"[Guide] {guide.Id}/{section.Key} failed to render: {ex}");
            }
        }
    }

    private void RenderGuideBlocks(GameObject parent, System.Collections.Generic.List<GuideBlock> blocks)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case GPara p:
                    GuideLabel(parent, "GuideP", p.Text, Theme.ScaledUI(14), null, FontStyles.Normal);
                    break;
                case GBullets b:
                    GuideLabel(parent, "GuideBullets", JoinList(b.Items, i => "•"), Theme.ScaledUI(14), null, FontStyles.Normal);
                    break;
                case GSteps s:
                    GuideLabel(parent, "GuideSteps", JoinList(s.Items, i => $"{i + 1}."), Theme.ScaledUI(14), null, FontStyles.Normal);
                    break;
                case GCallout c:
                    RenderGuideCallout(parent, c);
                    break;
                case GDetail d:
                    CollapsibleSection.Build(parent, d.Title, startExpanded: false, content => RenderGuideBlocks(content, d.Blocks));
                    break;
                case GCommands cmds:
                    RenderGuideCommands(parent, cmds);
                    break;
                case GSettings set:
                    RenderGuideSettings(parent, set);
                    break;
                case GTabMap map:
                    RenderGuideTabMap(parent, map);
                    break;
                case GLinks links:
                    foreach (var (label, url) in links.Rows) AddLinkRow(parent, label, url);
                    break;
            }
        }
    }

    // Hanging indent so a wrapped bullet lines up under its text, not under the marker.
    private static string JoinList(string[] items, Func<int, string> marker)
    {
        if (items == null || items.Length == 0) return "";
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < items.Length; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(marker(i)).Append("<indent=1.3em>").Append(items[i]).Append("</indent>");
        }
        return sb.ToString();
    }

    private void RenderGuideCallout(GameObject parent, GCallout c)
    {
        var (label, tint) = c.Kind switch
        {
            CalloutKind.AdminOnly => ("Admin only", GUIDE_ADMIN_TINT),
            CalloutKind.OffByDefault => ("Off by default", ARTICLE_HEADING_ACCENT),
            CalloutKind.Experimental => ("Experimental", WARNING_TINT),
            CalloutKind.Planned => ("Planned", Theme.MutedBody),
            CalloutKind.Warning => ("Careful", WARNING_TINT),
            _ => ("Note", ARTICLE_HEADING_ACCENT),
        };
        var card = UIFactory.CreateVerticalGroup(parent, "GuideCallout",
            forceWidth: true, forceHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(8, 8, 6, 6), bgColor: Theme.CardBackground);
        UIFactory.SetLayoutElement(card, minWidth: 340, preferredWidth: 380, flexibleWidth: 1, flexibleHeight: 0);
        GuideLabel(card, "CalloutText", $"<b><color=#{Hex(tint)}>{label}</color></b>   {c.Text}",
            Theme.ScaledUI(13), null, FontStyles.Normal);
    }

    private void RenderGuideCommands(GameObject parent, GCommands cmds)
    {
        if (!string.IsNullOrEmpty(cmds.Title))
            GuideLabel(parent, "GuideSubhead", cmds.Title, Theme.ScaledUI(14), ARTICLE_HEADING_ACCENT, FontStyles.Bold);
        string accent = Hex(Theme.AccentMono);
        foreach (var row in cmds.Rows)
        {
            var group = GuideRowGroup(parent, "GuideCmd");
            var head = new System.Text.StringBuilder();
            head.Append("<color=#").Append(accent).Append('>').Append(GuideText.Escape(row.Syntax)).Append("</color>");
            if (row.Scope == CmdScope.Admin)
                head.Append("   <size=85%><color=#").Append(Hex(GUIDE_ADMIN_TINT)).Append(">Admin</color></size>");
            if (row.Destructive)
                head.Append("   <size=85%><color=#").Append(Hex(WARNING_TINT)).Append(">Destructive</color></size>");
            GuideLabel(group, "Syntax", head.ToString(), Theme.ScaledUI(13), null, FontStyles.Bold);
            if (!string.IsNullOrEmpty(row.Aliases))
                GuideLabel(group, "Aliases", GuideText.Escape("Also: " + row.Aliases), Theme.ScaledUI(12), Theme.MutedBody, FontStyles.Normal);
            if (!string.IsNullOrEmpty(row.Desc))
                GuideLabel(group, "Desc", row.Desc, Theme.ScaledUI(13), null, FontStyles.Normal);
        }
    }

    private void RenderGuideSettings(GameObject parent, GSettings set)
    {
        if (!string.IsNullOrEmpty(set.Title))
            GuideLabel(parent, "GuideSubhead", set.Title, Theme.ScaledUI(14), ARTICLE_HEADING_ACCENT, FontStyles.Bold);
        if (!string.IsNullOrEmpty(set.File))
            GuideLabel(parent, "GuideFile", GuideText.Escape("File: " + set.File), Theme.ScaledUI(12), Theme.MutedBody, FontStyles.Normal);
        string accent = Hex(Theme.AccentMono);
        string muted = Hex(Theme.MutedBody);
        string lastSection = null;
        foreach (var row in set.Rows)
        {
            if (row.Section != lastSection)
            {
                GuideLabel(parent, "GuideSetSection", GuideText.Escape($"[{row.Section}]"), Theme.ScaledUI(13), Theme.MutedBody, FontStyles.Bold);
                lastSection = row.Section;
            }
            var group = GuideRowGroup(parent, "GuideSet");
            var head = $"<color=#{accent}>{GuideText.Escape(row.Key)}</color>" +
                       $"<color=#{muted}>  =  {GuideText.Escape(row.Default ?? "")}</color>" +
                       (row.Applies == "restart" ? $"   <size=85%><color=#{muted}>restart to apply</color></size>" :
                        row.Applies == "live" ? $"   <size=85%><color=#{muted}>applies live</color></size>" : "");
            GuideLabel(group, "Key", head, Theme.ScaledUI(13), null, FontStyles.Bold);
            if (!string.IsNullOrEmpty(row.Meaning))
                GuideLabel(group, "Meaning", row.Meaning, Theme.ScaledUI(13), null, FontStyles.Normal);
        }
    }

    private void RenderGuideTabMap(GameObject parent, GTabMap map)
    {
        string accent = Hex(Theme.AccentMono);
        foreach (var row in map.Rows)
        {
            var group = GuideRowGroup(parent, "GuideTab");
            GuideLabel(group, "Tab", row.Tab, Theme.ScaledUI(13), null, FontStyles.Bold);
            if (!string.IsNullOrEmpty(row.Does))
                GuideLabel(group, "Does", row.Does, Theme.ScaledUI(13), null, FontStyles.Normal);
            if (!string.IsNullOrEmpty(row.Sends))
                GuideLabel(group, "Sends", $"<color=#{accent}>{GuideText.Escape(row.Sends)}</color>", Theme.ScaledUI(12), null, FontStyles.Normal);
        }
    }

    private static GameObject GuideRowGroup(GameObject parent, string name)
    {
        var group = UIFactory.CreateVerticalGroup(parent, name,
            forceWidth: true, forceHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: 1, padding: new Vector4(2, 2, 3, 3));
        UIFactory.SetLayoutElement(group, minWidth: 340, preferredWidth: 380, flexibleWidth: 1, flexibleHeight: 0);
        return group;
    }

    private static void GuideSpacer(GameObject parent, int height)
    {
        var s = UIFactory.CreateUIObject("GuideSpacer", parent);
        UIFactory.SetLayoutElement(s, minWidth: 0, preferredWidth: 0, flexibleWidth: 1,
            minHeight: height, preferredHeight: height, flexibleHeight: 0);
    }

    /// <summary>A wrapped label whose height follows its text (same recipe as AddGuideSection's body).</summary>
    private static TextMeshProUGUI GuideLabel(GameObject parent, string name, string text, int fontSize, Color? color, FontStyles style)
    {
        var lbl = UIFactory.CreateLabel(parent, name, text ?? "", TextAlignmentOptions.TopLeft, color: color, fontSize: fontSize);
        UIFactory.SetLayoutElement(lbl.GameObject, minWidth: 340, preferredWidth: 380, flexibleWidth: 1,
            minHeight: 16, flexibleHeight: 0);
        lbl.TextMesh.enableWordWrapping = true;
        lbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
        lbl.TextMesh.fontStyle = style;
        lbl.TextMesh.richText = true;
        var fitter = lbl.GameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        return lbl.TextMesh;
    }
}
