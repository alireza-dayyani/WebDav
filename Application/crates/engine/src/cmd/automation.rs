//! Bounded document automation. The HTTP host supplies owned models and trusted source paths;
//! callers select typed operations rather than arbitrary filesystem or desktop commands.
use crate::{CmdError, CmdResult, CommandSpec, Selection, Session, p};
use inkstone_doc::{Path, Pos, StoryRef};
use serde_json::{Value, json};

mod fields;
mod read;
#[cfg(test)]
mod tests;
mod validation;
mod write;

pub fn specs() -> Vec<CommandSpec> {
    vec![
        CommandSpec::new("document.query", "Query Document", "Automation", query).params(r#"{"action":string}"#).pure(),
        CommandSpec::new("document.batch", "Edit Document", "Automation", batch).params(r#"{"operations":[{"type":string}]}"#),
        CommandSpec::new("document.assemble", "Assemble Documents", "Automation", write::assemble)
            .params(r#"{"sourcePaths":[string],"separator"?:"paragraph|pageBreak","stylePolicy"?:"preserveSource"}"#),
    ]
}

fn query(s: &mut Session, v: &Value) -> CmdResult {
    match p::req_str(v, "action")? {
        "structure" => read::structure(s, v),
        "search" => read::search(s, v),
        "tables" => read::tables(s),
        "images" => read::images(s),
        "controls" => fields::controls(s),
        "styles" => Ok(json!(s.doc.styles)),
        "properties" => Ok(json!({"core":s.doc.core,"custom":s.doc.custom_props,"language":s.doc.styles.default_chr.lang})),
        "comments" => s.run("review.comments", &json!({})),
        "changes" => s.run("review.changes", &json!({})),
        "validate" => validation::validate(s),
        "normalization" => write::normalization(s, v, false),
        _ => Err(bad("unknown query action")),
    }
}

fn batch(s: &mut Session, v: &Value) -> CmdResult {
    if let Some(actor) = p::str(v, "actor") {
        s.author = actor.chars().take(256).collect();
    }
    let operations = v.get("operations").and_then(Value::as_array).ok_or_else(|| bad("operations must be an array"))?;
    if operations.is_empty() || operations.len() > 256 {
        return Err(bad("supply 1–256 operations"));
    }
    let mut replies = Vec::new();
    for (i, op) in operations.iter().enumerate() {
        replies.push(operation(s, op).map_err(|e| bad(&format!("operation {i}: {e}")))?);
    }
    Ok(json!({"results":replies,"warnings":["DOCX export uses the native model; unsupported Word features may not round-trip losslessly."]}))
}

fn operation(s: &mut Session, v: &Value) -> CmdResult {
    match p::req_str(v, "type")? {
        "replaceText" => write::replace(s, v),
        "insertText" | "replaceRange" | "insertParagraph" => write::text(s, v),
        "format" => write::format(s, v),
        "fillBookmarks" => s.run("document.fillBookmarks", &json!({"values":v["values"]})),
        "updateTable" => write::table(s, v),
        "fillControls" => fields::fill(s, v),
        "insertImage" | "replaceImage" => write::image(s, v),
        "properties" => write::properties(s, &v["values"]),
        "normalize" => write::normalization(s, v, true),
        "addComment" => {
            select(s, &v["target"])?;
            s.run("review.newComment", &json!({"text":checked_text(v,"text")?}))
        }
        "replyComment" => s.run("review.reply", &json!({"id":v["id"],"text":checked_text(v,"text")?})),
        "resolveComment" => s.run("review.resolveComment", &json!({"id":v["id"],"value":flag(v,"resolved",true)?})),
        "deleteComment" => {
            if v.get("id").and_then(Value::as_u64).is_none() {
                return Err(bad("comment id required"));
            }
            s.run("review.deleteComment", &json!({"id":v["id"]}))
        }
        "trackChanges" => {
            let value = v.get("enabled").and_then(Value::as_bool).ok_or_else(|| bad("enabled must be boolean"))?;
            s.run("review.trackChanges", &json!({"value":value}))
        }
        "resolveChanges" => write::resolve_changes(s, v),
        _ => Err(bad("unknown operation type")),
    }
}

fn bad(message: &str) -> CmdError {
    CmdError::Params(message.into())
}

fn checked_text<'a>(v: &'a Value, key: &str) -> Result<&'a str, CmdError> {
    let text = p::req_str(v, key)?;
    if text.len() > 512 * 1024 || text.chars().any(|c| c == '\u{fffc}' || (c.is_control() && !matches!(c, '\n' | '\r' | '\t'))) {
        return Err(bad("text exceeds 512 KiB or contains unsupported control characters"));
    }
    Ok(text)
}

fn stories(s: &Session) -> Vec<StoryRef> {
    std::iter::once(StoryRef::Body).chain(s.doc.parts.keys().map(|id| StoryRef::Part(*id))).collect()
}

fn index(v: &Value, key: &str, default: Option<usize>) -> Result<usize, CmdError> {
    match v.get(key) {
        Some(value) => value
            .as_u64()
            .and_then(|n| usize::try_from(n).ok())
            .ok_or_else(|| bad("index/count must be a nonnegative integer within platform bounds")),
        None => default.ok_or_else(|| bad("required index/count missing")),
    }
}

fn flag(v: &Value, key: &str, default: bool) -> Result<bool, CmdError> {
    match v.get(key) {
        Some(value) => value.as_bool().ok_or_else(|| bad("flag must be boolean")),
        None => Ok(default),
    }
}

fn position(s: &Session, v: &Value) -> Result<Pos, CmdError> {
    let pos: Pos = serde_json::from_value(v.clone()).map_err(|_| bad("invalid position"))?;
    let para = s.doc.para_at(&pos).ok_or_else(|| bad("position does not identify a paragraph"))?;
    if pos.path.0.len() > 49 || !para.text.is_char_boundary(pos.off) {
        return Err(bad("position must be a UTF-8 byte boundary"));
    }
    Ok(pos)
}

fn select(s: &mut Session, v: &Value) -> Result<(), CmdError> {
    if let Some(name) = p::str(v, "bookmark") {
        return super::bookmarks::select(s, name);
    }
    if let Some(reference) = p::str(v, "ref") {
        let (story, path) = reference_parts(reference)?;
        let para = s.doc.para(story, &path).ok_or_else(|| bad("paragraph not found"))?;
        let start = index(v, "start", Some(0))?;
        let end = index(v, "end", Some(para.len()))?;
        let a = position(s, &json!({"story":story,"path":path,"off":start}))?;
        let b = position(s, &json!({"story":story,"path":path,"off":end}))?;
        if start > end {
            return Err(bad("range starts after its end"));
        }
        s.sel = Selection { anchor: a, focus: b };
        return Ok(());
    }
    if let Some(range) = v.get("range") {
        let a = position(s, &range["start"])?;
        let b = position(s, &range["end"])?;
        if a.story != b.story || a > b {
            return Err(bad("invalid range"));
        }
        s.run("select.range", &json!({"anchor":a,"focus":b}))?;
    } else if let Some(pos) = v.get("position") {
        let pos = position(s, pos)?;
        s.sel = Selection::caret(pos);
    } else if let Some(path) = v.get("path") {
        let path: Path = serde_json::from_value(path.clone()).map_err(|_| bad("invalid path"))?;
        let story: StoryRef = serde_json::from_value(v.get("story").cloned().unwrap_or(json!("body"))).map_err(|_| bad("invalid story"))?;
        let para = s.doc.para(story, &path).ok_or_else(|| bad("paragraph not found"))?;
        s.sel = Selection { anchor: Pos::new(story, path.clone(), 0), focus: Pos::new(story, path, para.len()) };
    } else {
        return Err(bad("target needs bookmark, position, path, or range"));
    }
    Ok(())
}

fn reference(story: StoryRef, path: &Path) -> String {
    let prefix = match story {
        StoryRef::Body => "b".into(),
        StoryRef::Part(id) => format!("p{id}"),
    };
    format!("{prefix}.{}", path.0.iter().map(ToString::to_string).collect::<Vec<_>>().join("."))
}

fn reference_parts(reference: &str) -> Result<(StoryRef, Path), CmdError> {
    let mut pieces = reference.split('.');
    let first = pieces.next().unwrap_or("");
    let story = if first == "b" {
        StoryRef::Body
    } else {
        StoryRef::Part(first.strip_prefix('p').and_then(|n| n.parse().ok()).ok_or_else(|| bad("invalid reference"))?)
    };
    let path: Vec<u32> = pieces.map(|n| n.parse().map_err(|_| bad("invalid reference"))).collect::<Result<_, _>>()?;
    if path.is_empty() || path.len() > 49 || !(path.len() - 1).is_multiple_of(3) {
        return Err(bad("invalid reference path"));
    }
    Ok((story, Path(path)))
}
