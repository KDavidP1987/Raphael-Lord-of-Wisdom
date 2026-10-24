using System;
using Raphael.Services;
using Raphael.UI.Framework.UniverseLib.UI;
using Raphael.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using Raphael.UI.Framework.CustomLib.Util;

namespace Raphael.UI.Forms;

// Builds a small "form panel" inside any GameObject parent:
//
//   ┌─────────────────────────────────────────┐
//   │ Title (italic bold)                     │
//   │ [Label1:] [input widget]                │
//   │ [Label2:] [input widget]                │
//   │ ...                                     │
//   │ [Submit]   (right-aligned)              │
//   └─────────────────────────────────────────┘
//
// Submit substitutes {field.Name} tokens in the command template with each
// field's value, validates, then sends through MessageService.EnqueueMessage.
//
// Call sites (e.g. MainPanel.BuildAdminTab) write a single FormBuilder.Build
// per command instead of constructing widgets by hand.
public static class FormBuilder
{
    /// <summary>Build a form into <paramref name="parent"/>. Returns the form's root GameObject.</summary>
    public static GameObject Build(
        GameObject parent,
        string title,
        string commandTemplate,
        params FormField[] fields)
        => BuildInternal(parent, title, commandTemplate, onSubmitted: null, fields: fields);

    /// <summary>
    /// Same as <see cref="Build(GameObject,string,string,FormField[])"/> but with a
    /// post-submit callback that fires after <see cref="MessageService.EnqueueMessage"/>
    /// returns. Use to chain a follow-up command (e.g. send `.fam l` after a delete
    /// to refresh the box-content list). Not invoked if validation fails.
    /// </summary>
    public static GameObject Build(
        GameObject parent,
        string title,
        string commandTemplate,
        Action onSubmitted,
        params FormField[] fields)
        => BuildInternal(parent, title, commandTemplate, onSubmitted, fields);

    private static GameObject BuildInternal(
        GameObject parent,
        string title,
        string commandTemplate,
        Action onSubmitted,
        FormField[] fields)
    {
        if (fields == null) fields = Array.Empty<FormField>();

        var form = UIFactory.CreateVerticalGroup(parent, $"Form_{title}",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(4, 4, 4, 4));
        // No fixed preferredHeight - the form's VerticalLayoutGroup auto-sums
        // its title + field rows + submit row. Earlier versions tried to predict
        // the height (70 + fields*34) but every variant of EnumField/PlayerNameField
        // is a different actual height, so the prediction was always slightly
        // wrong and the submit button got cut off. minHeight is a floor so a
        // zero-field form still draws.
        UIFactory.SetLayoutElement(form,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 60, flexibleHeight: 0);

        // Title row
        var titleLbl = UIFactory.CreateLabel(form, "FormTitle", title,
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(13));
        UIFactory.SetLayoutElement(titleLbl.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(20), preferredHeight: Theme.ScaledHeight(22), flexibleHeight: 0);
        titleLbl.TextMesh.fontStyle = FontStyles.Bold | FontStyles.Italic;
        titleLbl.TextMesh.enableWordWrapping = false;
        titleLbl.TextMesh.overflowMode = TextOverflowModes.Overflow;

        // Field rows: [Label:] [input widget]
        // forceExpandWidth:false so each field's OWN flexibleWidth is honored — only FREE-FORM text
        // fields (flex 1) stretch to fill the row; integer, toggle, AND dropdown/enum fields (flex 0)
        // keep their narrow preferred width so a numeric/picker form reads tidily instead of every
        // control stretching edge-to-edge. spacing 10 + a label right-pad give the label text
        // breathing room from the input (was sitting flush against it).
        foreach (var field in fields)
        {
            var row = UIFactory.CreateHorizontalGroup(form, $"Row_{field.Name}",
                forceExpandWidth: false, forceExpandHeight: false,
                childControlWidth: true, childControlHeight: true,
                spacing: 10, padding: new Vector4(0, 0, 0, 0));
            UIFactory.SetLayoutElement(row,
                minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
                minHeight: Theme.ScaledHeight(28), preferredHeight: Theme.ScaledHeight(30), flexibleHeight: 0);

            var lbl = UIFactory.CreateLabel(row, "Label", field.Label + ":",
                TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
            UIFactory.SetLayoutElement(lbl.GameObject,
                minWidth: 96, preferredWidth: 110, flexibleWidth: 0,
                minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);
            lbl.TextMesh.enableWordWrapping = false;
            lbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
            lbl.TextMesh.margin = new Vector4(0, 0, 6, 0);   // right inset so the label can't touch the field

            field.Build(row);
        }

        // Submit row (Submit button right-aligned, status label left-flexes)
        var submitRow = UIFactory.CreateHorizontalGroup(form, "SubmitRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 2, 2));
        UIFactory.SetLayoutElement(submitRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(30), preferredHeight: Theme.ScaledHeight(32), flexibleHeight: 0);

        var statusLbl = UIFactory.CreateLabel(submitRow, "FormStatus", "",
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(statusLbl.GameObject,
            minWidth: 100, preferredWidth: 220, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(22), preferredHeight: Theme.ScaledHeight(24), flexibleHeight: 0);
        statusLbl.TextMesh.fontStyle = FontStyles.Italic;
        statusLbl.TextMesh.enableWordWrapping = false;
        statusLbl.TextMesh.overflowMode = TextOverflowModes.Overflow;

        var submit = UIFactory.CreateButton(submitRow, "Submit", "Submit");
        UIFactory.SetLayoutElement(submit.GameObject,
            minWidth: 90, preferredWidth: 110, flexibleWidth: 0,
            minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        var submitText = submit.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (submitText != null)
        {
            submitText.enableWordWrapping = false;
            submitText.overflowMode = TextOverflowModes.Overflow;
            submitText.alignment = TextAlignmentOptions.Center;
            submitText.fontSize = Theme.ScaledUI(13);
        }
        TooltipHover.Attach(submit.GameObject, $"Send the command: {commandTemplate}");

        submit.OnClick = () => HandleSubmit(commandTemplate, fields, statusLbl.TextMesh, onSubmitted);

        return form;
    }

    private static void HandleSubmit(string template, FormField[] fields, TextMeshProUGUI status, Action onSubmitted)
    {
        // Validate every field; surface the first failure in the status label.
        foreach (var f in fields)
        {
            if (!f.IsValid())
            {
                var msg = $"Invalid: {f.Label}";
                if (status != null) status.text = msg;
                LogUtils.LogWarning($"Form submit blocked - {f.Name} value '{f.GetValueString()}' failed validation.");
                return;
            }
        }

        if (!MessageService.IsInitialized)
        {
            if (status != null) status.text = "Not connected to server yet.";
            return;
        }

        // Token substitution: every {field.Name} in the template becomes the field's token.
        // 0.73.0: GetToken == GetValueString for every field except a quoted TextField (encoded once here, by
        // VcfArg) and a DecimalField (invariant text), so existing forms send byte-identical commands. An optional
        // field left blank drops its token; it is always a trailing placeholder, so trimming the end closes the gap.
        string command = template;
        bool omitted = false;
        foreach (var f in fields)
        {
            command = command.Replace("{" + f.Name + "}", f.GetToken());
            if (f is TextField tf && tf.IsOmitted) omitted = true;
        }
        if (omitted) command = command.TrimEnd();

        // Feed the player-name cache if any field looks like a player name.
        foreach (var f in fields)
            if (f is PlayerNameField) PlayerNameCacheService.Add(f.GetValueString());

        MessageService.EnqueueMessage(command);
        if (status != null) status.text = $"Sent: {command}";
        LogUtils.LogInfo($"Form sent: {command}");

        // Release UI focus on submit so the player isn't stuck in typing-mode
        // (game input was suspended while a field was focused; releasing
        // immediately restores gameplay input regardless of the user's
        // Suspend-Game-Input setting).
        try { EventSystem.current?.SetSelectedGameObject(null); }
        catch { /* harmless - EventSystem may not exist on first frame */ }

        // Optional caller hook fires after the primary command is enqueued.
        // Use to chain a follow-up command (e.g. refresh-the-list after a
        // delete). Wrapped so a buggy caller can't poison the form click.
        if (onSubmitted != null)
        {
            try { onSubmitted(); }
            catch (Exception ex) { LogUtils.LogError($"Form onSubmitted handler threw: {ex}"); }
        }
    }
}
