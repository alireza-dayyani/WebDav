use super::{CmdError, CmdResult, Path, Pos, Selection, Session, Value, bad, checked_text, json, p, reference_parts, select, stories};
use inkstone_doc::{InlineObject, StoryRef};

pub(super) fn replace(s: &mut Session, v: &Value) -> CmdResult {
    let find = checked_text(v, "find")?;
    let replacement = checked_text(v, "replacement")?;
    let report = super::read::search(
        s,
        &json!({"text":find,"scope":v.get("scope").unwrap_or(&json!("body")),"matchCase":v.get("matchCase").unwrap_or(&Value::Bool(true)),"wholeWord":v.get("wholeWord").unwrap_or(&Value::Bool(false))}),
    )?;
    let count = report["count"].as_u64().unwrap_or(0);
    if report["truncated"] == true {
        return Err(bad("too many matches; narrow the operation"));
    }
    if v.get("expectedMatches").is_some() && super::index(v, "expectedMatches", None)? as u64 != count {
        return Err(bad("expectedMatches did not match; nothing changed"));
    }
    let matches = report["matches"].as_array().ok_or_else(|| bad("invalid search result"))?;
    for found in matches.iter().rev() {
        select(s, &found["target"])?;
        s.run("text.insert", &json!({"text":replacement,"raw":true}))?;
    }
    Ok(json!({"replaced":count}))
}

pub(super) fn text(s: &mut Session, v: &Value) -> CmdResult {
    let text = checked_text(v, "text")?;
    let paragraph = p::str(v, "type") == Some("insertParagraph");
    select(s, if paragraph { v.get("after").unwrap_or(&v["target"]) } else { &v["target"] })?;
    if paragraph {
        let (_, end) = s.sel.ordered();
        s.sel = Selection::caret(end);
        s.run("text.newParagraph", &json!({}))?;
    } else if p::str(v, "type") == Some("insertText") {
        let (start, _) = s.sel.ordered();
        s.sel = Selection::caret(start);
    }
    s.run("text.insert", &json!({"text":text,"raw":true,"paragraphs":super::flag(v,"paragraphs",false)?}))?;
    if let Some(style) = p::str(v, "style") {
        s.run("para.style", &json!({"style":style}))?;
    }
    Ok(json!({"selection":{"start":s.sel.anchor,"end":s.sel.focus}}))
}

pub(super) fn format(s: &mut Session, v: &Value) -> CmdResult {
    select(s, &v["target"])?;
    let values = v.get("values").and_then(Value::as_object).ok_or_else(|| bad("format needs values"))?;
    for (key, value) in values {
        validate_format(key, value)?;
        let (command, params) = match key.as_str() {
            "style" => ("para.style", json!({"style":value})),
            "bold" => ("format.bold", json!({"value":value})),
            "italic" => ("format.italic", json!({"value":value})),
            "font" => ("format.font", json!({"name":value})),
            "size" => ("format.size", json!({"size":value})),
            "color" => ("format.color", json!({"color":value})),
            "rtl" => ("para.rtl", json!({"value":value})),
            "align" => ("para.align", json!({"value":value})),
            "spacing" => ("para.spacing", value.clone()),
            "lineSpacing" => ("para.lineSpacing", value.clone()),
            "language" => ("review.language", json!({"lang":value})),
            _ => return Err(bad("unsupported formatting property")),
        };
        if matches!(key.as_str(), "bold" | "italic" | "rtl") && !value.is_boolean() {
            return Err(bad("format flags must be booleans"));
        }
        s.run(command, &params)?;
    }
    Ok(json!({"formatted":true}))
}

fn table_cell(s: &Session, story: StoryRef, path: &Path, row: usize, column: usize) -> Result<(Pos, Pos), CmdError> {
    let cell = s.doc.table(story, path).and_then(|t| t.rows.get(row)).and_then(|r| r.cells.get(column)).ok_or_else(|| bad("cell not found"))?;
    if cell.blocks.iter().any(|b| b.as_para().is_none()) {
        return Err(bad("cell contains a nested table; edit its paragraphs explicitly"));
    }
    let first = cell.blocks.first().and_then(|b| b.as_para()).ok_or_else(|| bad("empty cell"))?;
    let last = cell.blocks.last().and_then(|b| b.as_para()).ok_or_else(|| bad("empty cell"))?;
    let mut a = path.0.clone();
    a.extend([row as u32, column as u32, 0]);
    let mut b = path.0.clone();
    b.extend([row as u32, column as u32, cell.blocks.len().saturating_sub(1) as u32]);
    let _ = first;
    Ok((Pos::new(story, Path(a), 0), Pos::new(story, Path(b), last.len())))
}

fn validate_format(key: &str, value: &Value) -> Result<(), CmdError> {
    let valid = match key {
        "bold" | "italic" | "rtl" => value.is_boolean(),
        "font" | "style" | "language" => value.as_str().is_some_and(|s| !s.trim().is_empty() && s.len() <= 256),
        "size" => value.as_f64().is_some_and(|n| (1.0..=400.0).contains(&n)),
        "color" => value.as_str().is_some_and(|s| s == "auto" || (s.len() == 6 && s.chars().all(|c| c.is_ascii_hexdigit()))),
        "align" => value.as_str().is_some_and(|s| matches!(s, "left" | "center" | "right" | "justify" | "distribute" | "start" | "end")),
        "spacing" => value.as_object().is_some_and(|o| {
            !o.is_empty()
                && o.iter().all(|(k, v)| matches!(k.as_str(), "before" | "after") && v.as_f64().is_some_and(|n| (0.0..=1584.0).contains(&n)))
        }),
        "lineSpacing" => value.as_object().is_some_and(|o| {
            o.len() == 1
                && o.iter().all(|(k, v)| matches!(k.as_str(), "value" | "atLeast" | "exactly") && v.as_f64().is_some_and(|n| n > 0.0 && n <= 1584.0))
        }),
        _ => false,
    };
    if valid { Ok(()) } else { Err(bad("invalid formatting property or value")) }
}

pub(super) fn table(s: &mut Session, v: &Value) -> CmdResult {
    let (story, path) = reference_parts(p::req_str(v, "ref")?)?;
    for key in ["appendRows", "cells"] {
        if v.get(key).is_some_and(|value| !value.is_null() && !value.is_array()) {
            return Err(bad("table rows/cells must be arrays"));
        }
    }
    if ["appendRows", "cells"].iter().all(|key| v.get(*key).and_then(Value::as_array).is_none_or(Vec::is_empty)) {
        return Err(bad("supply cells or appended rows"));
    }
    if let Some(rows) = v.get("appendRows").and_then(Value::as_array) {
        if rows.len() > 256 {
            return Err(bad("at most 256 appended rows"));
        }
        for row in rows {
            let count = s.doc.table(story, &path).map(|t| t.rows.len()).ok_or_else(|| bad("table not found"))?;
            let (a, _) = table_cell(s, story, &path, count.saturating_sub(1), 0)?;
            s.sel = Selection::caret(a);
            s.run("table.insertRowBelow", &json!({}))?;
            let values = row.as_array().ok_or_else(|| bad("row must be an array of strings"))?;
            let actual = s.doc.table(story, &path).and_then(|t| t.rows.get(count)).map_or(0, |r| r.cells.len());
            if values.len() != actual {
                return Err(bad("row values must match the appended row's physical cells"));
            }
            for (ci, value) in values.iter().enumerate() {
                set_cell(s, story, &path, count, ci, value)?;
            }
        }
    }
    if let Some(cells) = v.get("cells").and_then(Value::as_array) {
        if cells.len() > 1024 {
            return Err(bad("at most 1024 cells"));
        }
        for cell in cells {
            let row = super::index(cell, "row", None)?;
            let col = super::index(cell, "column", None)?;
            set_cell(s, story, &path, row, col, &cell["text"])?;
        }
    }
    Ok(json!({"updated":true}))
}

fn set_cell(s: &mut Session, story: StoryRef, path: &Path, row: usize, col: usize, value: &Value) -> CmdResult {
    let (a, b) = table_cell(s, story, path, row, col)?;
    s.sel = Selection { anchor: a, focus: b };
    let text = checked_text(&json!({"text":value}), "text")?.to_string();
    s.run("text.insert", &json!({"text":text,"raw":true,"paragraphs":true}))
}

pub(super) fn image(s: &mut Session, v: &Value) -> CmdResult {
    for key in ["width", "height"] {
        if v.get(key).filter(|n| !n.is_null()).is_some_and(|value| !value.as_f64().is_some_and(|n| (4.0..=4000.0).contains(&n))) {
            return Err(bad("image dimensions must be 4–4000 points"));
        }
    }
    if p::str(v, "type") == Some("replaceImage") {
        let reference = p::req_str(v, "ref")?;
        let (block, off) = reference.split_once('@').ok_or_else(|| bad("invalid image reference"))?;
        let (story, path) = reference_parts(block)?;
        let off: usize = off.parse().map_err(|_| bad("invalid object offset"))?;
        let at = super::position(s, &json!({"story":story,"path":path,"off":off}))?;
        if !matches!(s.doc.para_at(&at).and_then(|p| p.object_at(off)), Some(InlineObject::Image { .. })) {
            return Err(bad("reference is not a picture"));
        }
        s.sel = Selection { anchor: at.clone(), focus: Pos { off: off + inkstone_doc::para::OBJ.len_utf8(), ..at } };
    } else {
        select(s, &v["target"])?;
    }
    s.run("document.imageInfo", &json!({"data":v["data"]}))?;
    let result = s.run(
        "insert.picture",
        &json!({"data":v["data"],"width":v.get("width").cloned().unwrap_or(json!(144)),"alt":v.get("alt").cloned().unwrap_or(json!(""))}),
    )?;
    if v.get("height").is_some() {
        s.run("picture.size", &json!({"width":v.get("width").cloned().unwrap_or(json!(144)),"height":v["height"],"lockAspect":false}))?;
    }
    Ok(result)
}

pub(super) fn resolve_changes(s: &mut Session, v: &Value) -> CmdResult {
    let accept = match p::req_str(v, "decision")? {
        "accept" => true,
        "reject" => false,
        _ => return Err(bad("decision must be accept or reject")),
    };
    if p::bool(v, "all") == Some(true) {
        return s
            .run(if accept { "review.acceptAll" } else { "review.rejectAll" }, &json!({"stopTracking":p::bool(v,"stopTracking").unwrap_or(false)}));
    }
    select(s, &v["target"])?;
    s.run(if accept { "review.accept" } else { "review.reject" }, &json!({}))
}

pub(super) fn properties(s: &mut Session, v: &Value) -> CmdResult {
    let values = v.as_object().ok_or_else(|| bad("properties must be an object"))?;
    for (key, value) in values {
        if !matches!(key.as_str(), "title" | "subject" | "author" | "keywords" | "comments" | "category" | "custom" | "language") {
            return Err(bad("unknown document property"));
        }
        if key != "custom" {
            checked_text(&json!({"text":value}), "text")?;
        }
    }
    if let Some(language) = v.get("language") {
        s.run("review.language", &json!({"lang":language,"default":true}))?;
    }
    s.run("file.properties", v)
}

fn normalize_text(text: &str, rules: &[String]) -> String {
    let mut out = text.to_string();
    for rule in rules {
        out = match rule.as_str() {
            "persianLetters" => out.replace('ي', "ی").replace('ك', "ک"),
            "persianDigits" => out
                .chars()
                .map(|c| {
                    c.to_digit(10)
                        .or_else(|| "٠١٢٣٤٥٦٧٨٩".chars().position(|n| n == c).map(|i| i as u32))
                        .and_then(|n| char::from_u32('۰' as u32 + n))
                        .unwrap_or(c)
                })
                .collect(),
            "latinDigits" => out
                .chars()
                .map(|c| "۰۱۲۳۴۵۶۷۸۹".chars().position(|n| n == c).or_else(|| "٠١٢٣٤٥٦٧٨٩".chars().position(|n| n == c)))
                .zip(out.chars())
                .map(|(n, c)| n.and_then(|n| char::from_u32('0' as u32 + n as u32)).unwrap_or(c))
                .collect(),
            "trimTrailingSpaces" => out.lines().map(|line| line.trim_end_matches([' ', '\t'])).collect::<Vec<_>>().join("\n"),
            "halfSpace" => out.replace("می ", "می‌").replace("نمی ", "نمی‌"),
            _ => out,
        };
    }
    out
}

pub(super) fn normalization(s: &mut Session, v: &Value, apply: bool) -> CmdResult {
    let rules: Vec<String> = serde_json::from_value(v["rules"].clone()).map_err(|_| bad("rules must be an array"))?;
    if rules.is_empty()
        || rules.iter().any(|r| !matches!(r.as_str(), "persianLetters" | "persianDigits" | "latinDigits" | "trimTrailingSpaces" | "halfSpace"))
    {
        return Err(bad("unknown or missing normalization rule"));
    }
    if rules.iter().any(|r| r == "latinDigits") && rules.iter().any(|r| r == "persianDigits") {
        return Err(bad("choose one digit system"));
    }
    let mut changes = Vec::new();
    for story in stories(s) {
        for path in s.doc.para_paths(story) {
            if let Some(para) = s.doc.para(story, &path) {
                // Preserve object markers and all existing formatting by editing changed scalar ranges.
                let before = para.text.clone();
                let after = normalize_text(&before, &rules);
                if before != after {
                    changes.push((story, path, before, after));
                }
                if changes.len() > 4000 {
                    return Err(bad("too many changed paragraphs"));
                }
            }
        }
    }
    let report: Vec<_> =
        changes.iter().map(|(story, path, before, after)| json!({"ref":super::reference(*story,path),"before":before,"after":after})).collect();
    if apply {
        for (story, path, before, after) in changes.into_iter().rev() {
            // A shared prefix/suffix confines the replacement and leaves unrelated runs intact.
            let prefix = before.chars().zip(after.chars()).take_while(|(a, b)| a == b).map(|(c, _)| c.len_utf8()).sum::<usize>();
            let old_tail = before.get(prefix..).unwrap_or("");
            let new_tail = after.get(prefix..).unwrap_or("");
            let suffix = old_tail.chars().rev().zip(new_tail.chars().rev()).take_while(|(a, b)| a == b).map(|(c, _)| c.len_utf8()).sum::<usize>();
            let end = before.len().saturating_sub(suffix);
            let text = after.get(prefix..after.len().saturating_sub(suffix)).unwrap_or("");
            if before.get(prefix..end).is_some_and(|t| t.contains('\u{fffc}')) {
                return Err(bad("normalization crosses an inline object; narrow the rules"));
            }
            s.sel = Selection { anchor: Pos::new(story, path.clone(), prefix), focus: Pos::new(story, path, end) };
            s.run("text.insert", &json!({"text":text,"raw":true}))?;
        }
    }
    Ok(json!({"changes":report,"applied":apply}))
}

pub(super) fn assemble(s: &mut Session, v: &Value) -> CmdResult {
    let paths = v.get("sourcePaths").and_then(Value::as_array).ok_or_else(|| bad("trusted sourcePaths required"))?;
    if paths.is_empty() || paths.len() > 32 {
        return Err(bad("supply 1–32 documents"));
    }
    if p::str(v, "stylePolicy").unwrap_or("preserveSource") != "preserveSource" {
        return Err(bad("stylePolicy must be preserveSource"));
    }
    let separator = p::str(v, "separator").unwrap_or("paragraph");
    if !matches!(separator, "paragraph" | "pageBreak") {
        return Err(bad("unsupported separator"));
    }
    for path in paths {
        let path = path.as_str().ok_or_else(|| bad("source path required"))?;
        let source = crate::io::open_path(std::path::Path::new(path)).map_err(CmdError::Failed)?;
        let health = super::super::letter::health(&source);
        if health["protected"] == true || health["pendingRevisions"] == true || health["comments"] != 0 {
            return Err(bad("assemble sources must have no protection, comments or pending revisions"));
        }
        s.sel = Selection::caret(s.doc.end_of(StoryRef::Body));
        s.run("text.newParagraph", &json!({}))?;
        if separator == "pageBreak" {
            s.run("text.pageBreak", &json!({}))?;
        }
        let fragment = super::super::paste::adopt(s, source);
        let at = s.sel.focus.clone();
        let end = s.doc.insert_fragment(&at, &fragment)?;
        s.sel = Selection::caret(end);
    }
    Ok(
        json!({"assembled":paths.len()+1,"stylePolicy":"preserveSource","warnings":["Source body content is assembled; source page sections and headers/footers are not copied."]}),
    )
}
