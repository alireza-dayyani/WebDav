use super::{CmdError, CmdResult, Selection, Session, Value, bad, checked_text, json, stories};
use inkstone_doc::control::{ContentControl, ControlKind, ControlRange};
use inkstone_doc::{CharProps, Document, InlineObject, StoryRef};

pub(super) fn controls(s: &Session) -> CmdResult {
    let mut items = Vec::new();
    for story in stories(s) {
        for range in s.doc.control_ranges(story) {
            if items.len() >= 4000 {
                return Ok(json!({"items":items,"truncated":true}));
            }
            let mut description = super::super::controls::describe(s, &range);
            description["supportedFill"] = json!(matches!(
                range.control.kind,
                ControlKind::RichText
                    | ControlKind::Text { .. }
                    | ControlKind::CheckBox { .. }
                    | ControlKind::ComboBox { .. }
                    | ControlKind::DropDown { .. }
                    | ControlKind::Date { .. }
                    | ControlKind::Picture
                    | ControlKind::RepeatingSection { .. }
            ));
            items.push(description);
        }
    }
    Ok(json!({"items":items,"truncated":false}))
}

fn find(s: &Session, key: &str) -> Result<ControlRange, CmdError> {
    let ranges: Vec<_> =
        stories(s).into_iter().flat_map(|story| s.doc.control_ranges(story)).filter(|r| r.control.tag == key || r.control.title == key).collect();
    if ranges.len() != 1 {
        return Err(bad("each control key must identify exactly one tag or title"));
    }
    ranges.into_iter().next().ok_or_else(|| bad("control not found"))
}

pub(super) fn fill(s: &mut Session, v: &Value) -> CmdResult {
    fill_depth(s, v, 0)
}

fn fill_depth(s: &mut Session, v: &Value, depth: usize) -> CmdResult {
    if depth > 8 {
        return Err(bad("repeating controls nested too deeply"));
    }
    let values = v.get("values").and_then(Value::as_object).ok_or_else(|| bad("values must be an object"))?;
    if values.is_empty() || values.len() > 256 {
        return Err(bad("supply 1–256 control values"));
    }
    for (key, value) in values {
        let range = find(s, key)?;
        if !range.control.lock.can_edit() {
            return Err(bad("control is locked against editing"));
        }
        let at = range.content_start();
        match &range.control.kind {
            ControlKind::CheckBox { .. } => {
                let checked = value.as_bool().ok_or_else(|| bad("checkbox value must be boolean"))?;
                s.run("developer.toggleCheckBox", &json!({"at":at,"value":checked}))?;
            }
            ControlKind::Date { .. } => {
                s.run("developer.setDate", &json!({"at":at,"date":value}))?;
            }
            ControlKind::ComboBox { .. } | ControlKind::DropDown { .. } => {
                s.run("developer.chooseItem", &json!({"at":at,"value":value}))?;
            }
            ControlKind::RichText | ControlKind::Text { .. } => {
                let text = checked_text(&json!({"text":value}), "text")?.to_string();
                if matches!(range.control.kind, ControlKind::Text { multi_line: false }) && text.contains(['\n', '\r']) {
                    return Err(bad("single-line control cannot contain line breaks"));
                }
                s.sel = Selection { anchor: at, focus: range.end.clone() };
                s.run("text.insert", &json!({"text":text,"raw":true}))?;
            }
            ControlKind::Picture => {
                s.sel = Selection { anchor: at, focus: range.end.clone() };
                s.run("document.imageInfo", &json!({"data":value["data"]}))?;
                s.run("insert.picture",&json!({"data":value["data"],"width":value.get("width").cloned().unwrap_or(json!(144)),"alt":value.get("alt").cloned().unwrap_or(json!(""))}))?;
            }
            ControlKind::RepeatingSection { no_insert_delete, .. } => {
                if *no_insert_delete {
                    return Err(bad("repeating section disallows adding/removing items"));
                }
                repeat(s, &range, value, depth + 1)?;
            }
            _ => return Err(bad("this control type is not supported for filling")),
        }
    }
    Ok(json!({"filled":values.keys().collect::<Vec<_>>()}))
}

fn repeat(s: &mut Session, range: &ControlRange, values: &Value, depth: usize) -> CmdResult {
    let health = super::super::letter::health(&s.doc);
    if health["trackChanges"] == true || health["pendingRevisions"] == true || health["comments"] != 0 {
        return Err(bad("repeating sections require no comments, pending revisions or enabled Track Changes"));
    }
    let records = values.as_array().ok_or_else(|| bad("repeating section value must be an array of objects"))?;
    if records.is_empty() || records.len() > 100 {
        return Err(bad("supply 1–100 repeating records"));
    }
    if range.start.story != StoryRef::Body || range.start.path.depth() != 0 || range.end.path.depth() != 0 {
        return Err(bad("repeating sections currently require body paragraphs"));
    }
    let item = s
        .doc
        .control_ranges(StoryRef::Body)
        .into_iter()
        .filter(|r| matches!(r.control.kind, ControlKind::RepeatingSectionItem) && r.start > range.start && r.end < range.end)
        .collect::<Vec<_>>();
    if item.is_empty() {
        return Err(bad("repeating section must contain a template item"));
    }
    let item = item.first().ok_or_else(|| bad("template item missing"))?;
    let fragment = s.doc.copy_range(&item.content_start(), &item.end);
    let mut outputs = Vec::new();
    for record in records {
        let mut doc: Document = s.doc.clone();
        doc.body = fragment.blocks.iter().cloned().map(std::sync::Arc::new).collect();
        doc.parts.clear();
        doc.comments.clear();
        doc.ensure_nonempty();
        if !doc.bookmarks().is_empty() {
            return Err(bad("repeating template items cannot contain named bookmarks"));
        }
        let mut temp = Session::new(doc);
        fill_depth(&mut temp, &json!({"values":record}), depth)?;
        let start = temp.doc.start_of(StoryRef::Body);
        let end = temp.doc.end_of(StoryRef::Body);
        temp.doc.insert_object(&end, InlineObject::ControlEnd, &CharProps::default())?;
        let mut control = ContentControl::new(ControlKind::RepeatingSectionItem);
        control.block = true;
        temp.doc.insert_object(&start, InlineObject::ControlStart { control: Box::new(control) }, &CharProps::default())?;
        // Word identities must be distinct in each materialized item.
        for path in temp.doc.para_paths(StoryRef::Body) {
            if let Some(para) = temp.doc.para(StoryRef::Body, &path) {
                let offsets = para.object_offsets();
                for off in offsets {
                    let at = inkstone_doc::Pos::new(StoryRef::Body, path.clone(), off);
                    if let Some(control) = temp.doc.control_mut(&at) {
                        control.id = None;
                    }
                }
            }
        }
        outputs.push(temp.doc.body_fragment());
    }
    s.sel = Selection { anchor: range.content_start(), focus: range.end.clone() };
    let mut at = super::super::delete_selection(s)?;
    for fragment in outputs {
        at = s.doc.insert_fragment(&at, &fragment)?;
    }
    s.sel = Selection::caret(at);
    // New items carry normal content; the enclosing section remains reusable.
    if let Some(control) = s.doc.control_mut(&range.start) {
        control.showing_placeholder = false;
    }
    Ok(json!({"repeated":records.len()}))
}
