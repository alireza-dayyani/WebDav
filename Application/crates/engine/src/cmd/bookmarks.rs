//! Named template fields, shared by headless API jobs and the editor command registry.

use std::collections::BTreeMap;

use inkstone_doc::{Document, InlineObject, Pos, StoryRef};
use serde_json::{Value, json};

use crate::{CmdError, CmdResult, CommandSpec, Selection, Session};

const MAX_BOOKMARKS: usize = 4096;
const MAX_VALUES: usize = 256;
const MAX_TEXT_BYTES: usize = 512 * 1024;

pub fn specs() -> Vec<CommandSpec> {
    vec![
        CommandSpec::new("document.bookmarks", "List Template Bookmarks", "Automation", list).pure(),
        CommandSpec::new("document.bookmarkState", "Inspect Bookmark Content", "Automation", state).params(r#"{"name": string}"#).pure(),
        CommandSpec::new("document.fillBookmarks", "Fill Template Bookmarks", "Automation", fill)
            .params(r#"{"values": {"BookmarkName": "literal text", ...}}; exact names, all keys must match; preserves markers"#),
    ]
}

#[derive(Default)]
struct Marks {
    starts: Vec<Pos>,
    ends: Vec<Pos>,
}

/// Include secondary stories: DOCX templates commonly put fields in headers and footers.
fn scan(doc: &Document) -> Result<BTreeMap<String, Marks>, CmdError> {
    let mut marks = BTreeMap::<String, Marks>::new();
    let mut count = 0;
    for story in std::iter::once(StoryRef::Body).chain(doc.parts.keys().map(|id| StoryRef::Part(*id))) {
        for path in doc.para_paths(story) {
            let Some(para) = doc.para(story, &path) else { continue };
            for off in para.object_offsets() {
                let (name, start) = match para.object_at(off) {
                    Some(InlineObject::BookmarkStart { name }) => (name, true),
                    Some(InlineObject::BookmarkEnd { name }) => (name, false),
                    _ => continue,
                };
                count += 1;
                if count > MAX_BOOKMARKS * 2 || name.len() > 256 {
                    return Err(CmdError::Params("template bookmark limits exceeded (4096 bookmarks, 256-byte names)".into()));
                }
                let entry = marks.entry(name.clone()).or_default();
                let at = Pos { story, path: path.clone(), off };
                if start { entry.starts.push(at) } else { entry.ends.push(at) }
            }
        }
    }
    if marks.len() > MAX_BOOKMARKS {
        return Err(CmdError::Params("template bookmark limit exceeded (4096 names)".into()));
    }
    Ok(marks)
}

fn range(doc: &Document, all: &BTreeMap<String, Marks>, marks: &Marks) -> Result<(Pos, Pos), &'static str> {
    let ([start], [end]) = (marks.starts.as_slice(), marks.ends.as_slice()) else {
        return Err("Bookmark must have exactly one start and one end.");
    };
    if start.story != end.story || start >= end {
        return Err("Bookmark markers are reversed or belong to different stories.");
    }
    // Replacing across cells/tables would destroy their structure. Paragraphs within one cell
    // or a story are safe, including a range spanning several consecutive paragraphs.
    if start.path.parent() != end.path.parent() || doc.para_paths_in(start, end).iter().any(|p| p.parent() != start.path.parent()) {
        return Err("Bookmark crosses a table or cell boundary.");
    }
    if all.values().flat_map(|m| m.starts.iter().chain(&m.ends)).any(|p| p.story == start.story && start < p && p < end) {
        return Err("Bookmark contains another bookmark marker.");
    }
    let from = Pos { off: start.off + inkstone_doc::para::OBJ.len_utf8(), ..start.clone() };
    Ok((from, end.clone()))
}

fn list(s: &mut Session, _: &Value) -> CmdResult {
    let all = scan(&s.doc)?;
    let bookmarks: Vec<Value> = all
        .iter()
        .map(|(name, marks)| {
            let issue = range(&s.doc, &all, marks).err();
            json!({"name": name, "replaceable": issue.is_none(), "issue": issue})
        })
        .collect();
    Ok(json!({"bookmarks": bookmarks}))
}

// Inspection may span tables; replacement deliberately keeps its stricter structural limits.
fn state(s: &mut Session, v: &Value) -> CmdResult {
    let name = crate::p::req_str(v, "name")?;
    let all = scan(&s.doc)?;
    let Some(marks) = all.get(name) else { return Ok(json!({"valid":false,"hasContent":false})) };
    let ([start], [end]) = (marks.starts.as_slice(), marks.ends.as_slice()) else { return Ok(json!({"valid":false,"hasContent":false})) };
    if start.story != end.story
        || start >= end
        || all
            .iter()
            .filter(|(n, _)| n.as_str() != name)
            .flat_map(|(_, m)| m.starts.iter().chain(&m.ends))
            .any(|p| p.story == start.story && start < p && p < end)
    {
        return Ok(json!({"valid":false,"hasContent":false}));
    }
    let has_content = s.doc.para_paths_in(start, end).iter().any(|path| {
        let Some(para) = s.doc.para(start.story, path) else { return false };
        let a = if *path == start.path { start.off + inkstone_doc::para::OBJ.len_utf8() } else { 0 };
        let b = if *path == end.path { end.off } else { para.len() };
        para.text.get(a..b).is_some_and(|text| text.chars().any(|c| !c.is_whitespace() && c != inkstone_doc::para::OBJ))
            || para.object_offsets().iter().any(|off| {
                *off >= a && *off < b && !matches!(para.object_at(*off), Some(InlineObject::BookmarkStart { .. } | InlineObject::BookmarkEnd { .. }))
            })
    });
    Ok(json!({"valid":true,"hasContent":has_content}))
}

pub(super) fn fill(s: &mut Session, v: &Value) -> CmdResult {
    let values = v
        .get("values")
        .and_then(Value::as_object)
        .ok_or_else(|| CmdError::Params("`values` must be an object of bookmark names and strings".into()))?;
    if values.is_empty() || values.len() > MAX_VALUES {
        return Err(CmdError::Params("supply 1–256 bookmark values".into()));
    }
    if super::protect::enforced(&s.doc) || s.doc.settings.track_changes {
        return Err(CmdError::Disabled("stop document protection and disable Track Changes before filling bookmarks".into()));
    }
    let all = scan(&s.doc)?;
    let mut jobs = Vec::new();
    let mut total = 0usize;
    for (name, value) in values {
        let text = value.as_str().ok_or_else(|| CmdError::Params("bookmark values must be strings".into()))?;
        total = total.saturating_add(text.len());
        if total > MAX_TEXT_BYTES || text.chars().any(|c| c == inkstone_doc::para::OBJ || (c.is_control() && !matches!(c, '\r' | '\n' | '\t'))) {
            return Err(CmdError::Params("values exceed 512 KiB or contain unsupported control characters".into()));
        }
        let marks = all.get(name).ok_or_else(|| CmdError::Params(format!("no bookmark `{name}`")))?;
        let (a, b) = range(&s.doc, &all, marks).map_err(|why| CmdError::Params(format!("bookmark `{name}`: {why}")))?;
        jobs.push((a, b, text));
    }
    // Work backwards so earlier positions remain valid. Validation precedes every edit; the
    // registry also rolls the entire command back if a locked content control refuses a job.
    jobs.sort_by(|a, b| b.0.cmp(&a.0));
    for (a, b, text) in jobs {
        s.sel = Selection { anchor: a, focus: b };
        s.pending = None;
        super::type_text(s, &text.replace("\r\n", "\n"))?;
    }
    Ok(json!({"replaced": values.keys().collect::<Vec<_>>()}))
}

pub(super) fn select(s: &mut Session, name: &str) -> Result<(), CmdError> {
    let all = scan(&s.doc)?;
    let marks = all.get(name).ok_or_else(|| CmdError::Params(format!("no bookmark `{name}`")))?;
    let (anchor, focus) = range(&s.doc, &all, marks).map_err(|why| CmdError::Params(why.into()))?;
    s.sel = Selection { anchor, focus };
    s.pending = None;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::{Document, InlineObject, Session, StoryRef, json};
    #[test]
    fn content_inspection_accepts_a_table_without_allowing_destructive_replacement() {
        let doc: Document = serde_json::from_value(json!({"body":[
            {"kind":"para","text":"\u{fffc}","runs":[{"len":3}],"objects":[{"type":"bookmarkStart","name":"Body"}]},
            {"kind":"table","grid":[120],"rows":[{"cells":[{"blocks":[{"kind":"para","text":"Table body","runs":[{"len":10}]}]}]}]},
            {"kind":"para","text":"\u{fffc}","runs":[{"len":3}],"objects":[{"type":"bookmarkEnd","name":"Body"}]}
        ]}))
        .unwrap();
        let mut s = Session::new(doc);
        let before = s.doc.clone();
        assert_eq!(s.run("document.bookmarkState", &json!({"name":"Body"})).unwrap(), json!({"valid":true,"hasContent":true}));
        assert_eq!(s.doc, before);
        assert!(s.run("document.fillBookmarks", &json!({"values":{"Body":"would remove table"}})).is_err());
        assert_eq!(s.run("document.bookmarkState", &json!({"name":"Missing"})).unwrap()["valid"], false);
        let mut empty = Session::new(Document::new());
        empty.run("document.letterTemplate", &json!({})).unwrap();
        assert_eq!(empty.run("document.bookmarkState", &json!({"name":"Body"})).unwrap(), json!({"valid":true,"hasContent":false}));
    }
    use inkstone_doc::{CharProps, Paragraph, Part, PartKind, para_block};

    fn field(name: &str, text: &str) -> Paragraph {
        let mut p = Paragraph::with_text(text, CharProps::default());
        p.insert_object(p.len(), InlineObject::BookmarkEnd { name: name.into() }, &CharProps::default()).unwrap();
        p.insert_object(0, InlineObject::BookmarkStart { name: name.into() }, &CharProps::default()).unwrap();
        p
    }

    #[test]
    fn fills_body_and_secondary_stories_preserves_markers_and_undo() {
        let mut doc = Document::new();
        doc.body = vec![para_block(field("Name", "old")), para_block(field("Empty", ""))];
        doc.parts.insert(1, Part { kind: PartKind::Header, blocks: vec![para_block(field("Header", "header"))], ..Part::default() });
        let mut s = Session::new(doc.clone());
        s.run("document.fillBookmarks", &json!({"values": {"Name": "علی <&>", "Empty": "\"", "Header": ""}})).unwrap();
        let all = s.run("document.bookmarks", &json!({})).unwrap();
        assert_eq!(all["bookmarks"].as_array().unwrap().len(), 3);
        assert!(all["bookmarks"].as_array().unwrap().iter().all(|b| b["replaceable"] == true));
        assert!(s.doc.body[0].as_para().unwrap().text.contains("علی <&>"));
        assert!(s.doc.body[1].as_para().unwrap().text.contains('"'));
        assert!(!s.doc.parts[&1].blocks[0].as_para().unwrap().text.contains("header"));
        s.run("document.fillBookmarks", &json!({"values": {"Name": "again"}})).unwrap();
        s.run("edit.undo", &json!({})).unwrap();
        s.run("edit.undo", &json!({})).unwrap();
        assert_eq!(s.doc, doc);
    }

    #[test]
    fn invalid_batches_are_atomic_and_nested_or_duplicate_marks_are_refused() {
        let mut s = Session::new(Document::new());
        s.doc.body = vec![para_block(field("Name", "old"))];
        let before = s.doc.clone();
        for values in [json!({"Name": "new", "Missing": "x"}), json!({"Name": 5}), json!({"Name": "\u{fffc}"})] {
            assert!(s.run("document.fillBookmarks", &json!({"values": values})).is_err());
            assert_eq!(s.doc, before);
        }
        let p = s.doc.para_mut(StoryRef::Body, &inkstone_doc::Path::top(0)).unwrap();
        p.insert_object(3, InlineObject::BookmarkStart { name: "Inside".into() }, &CharProps::default()).unwrap();
        assert!(s.run("document.fillBookmarks", &json!({"values": {"Name": "new"}})).is_err());
        s.doc.body.push(para_block(field("Name", "duplicate")));
        assert!(s.run("document.fillBookmarks", &json!({"values": {"Name": "new"}})).is_err());
    }
}
