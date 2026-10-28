//! Generic template composition; account/recipient rules stay in the hosting API.
use crate::{CmdError, CmdResult, CommandSpec, Selection, Session, p};
use inkstone_doc::{Document, InlineObject, Pos, StoryRef};
use serde_json::{Value, json};

pub fn specs() -> Vec<CommandSpec> {
    vec![
        CommandSpec::new("document.compose", "Compose Template", "Automation", compose)
            .params(r#"{"values"?: object, "images"?: {bookmark: {data: base64, width: pt}}, "sourcePath"?: string, "bodyBookmark"?: string}"#),
        CommandSpec::new("document.preflight", "Validate Document for Generation", "Automation", |s, _| Ok(health(&s.doc))).pure(),
        CommandSpec::new("document.letterTemplate", "Create Letter Template", "Automation", starter)
            .params(r#"{"language": "en-US|fa-IR|ar-SA|ur-PK", "paper"?: "A5|A4|Letter"}"#),
        CommandSpec::new("document.imageInfo", "Validate Image", "Automation", image_info).pure(),
    ]
}

pub fn health(doc: &Document) -> Value {
    // Inspect actual revision marks, not just the Track Changes setting or historical IDs.
    fn changed(v: &Value) -> bool {
        match v {
            Value::Array(a) => a.iter().any(changed),
            Value::Object(o) => o.iter().any(|(k, v)| {
                if matches!(k.as_str(), "media" | "passthrough" | "revisions" | "glossary") {
                    return false;
                }
                (matches!(k.as_str(), "ins" | "del" | "fmtChange" | "numChange") && !v.is_null()) || changed(v)
            }),
            _ => false,
        }
    }
    let pending = serde_json::to_value(doc).map(|v| changed(&v)).unwrap_or(true);
    json!({"trackChanges": doc.settings.track_changes, "protected": super::protect::enforced(doc), "comments": doc.comments.len(), "pendingRevisions": pending})
}

fn require_ready(doc: &Document) -> Result<(), CmdError> {
    let h = health(doc);
    if h["trackChanges"] == true || h["protected"] == true || h["pendingRevisions"] == true || h["comments"] != 0 {
        return Err(CmdError::Params("document must have no comments, pending revisions, protection or enabled Track Changes".into()));
    }
    Ok(())
}

fn image_info(_: &mut Session, v: &Value) -> CmdResult {
    let bytes = super::insert::base64_decode(p::req_str(v, "data")?).ok_or_else(|| CmdError::Params("invalid image base64".into()))?;
    if bytes.len() > 512 * 1024 {
        return Err(CmdError::Params("image exceeds 512 KiB".into()));
    }
    let (w, h) = inkstone_render::image_size(&bytes).ok_or_else(|| CmdError::Params("invalid image".into()))?;
    if w == 0 || h == 0 || w > 4096 || h > 4096 {
        return Err(CmdError::Params("image dimensions exceed 4096 pixels".into()));
    }
    image::load_from_memory(&bytes).map_err(|_| CmdError::Params("image cannot be decoded".into()))?;
    Ok(json!({"width": w, "height": h}))
}

fn compose(s: &mut Session, v: &Value) -> CmdResult {
    require_ready(&s.doc)?;
    if let Some(values) = v.get("values").and_then(Value::as_object).filter(|v| !v.is_empty()) {
        let mut values = values.clone();
        let body_name = p::str(v, "bodyBookmark").unwrap_or("Body");
        if let Some(text) = values.get(body_name).and_then(Value::as_str).filter(|t| t.contains('\n')) {
            super::bookmarks::select(s, body_name)?;
            if s.doc.para_at(&s.sel.anchor).and_then(|p| p.props.style.as_deref()) == Some("InkstoneLetterBody") {
                // Plain body input uses paragraphs; addresses and arbitrary bookmark
                // replacements retain their existing soft-line-break behavior.
                let paragraphs = text.replace("\r\n", "\n").replace('\n', "\u{2029}");
                values.insert(body_name.into(), json!(paragraphs));
            }
        }
        if let Some(text) = values.get("Attachments").and_then(Value::as_str).filter(|t| !t.is_empty()) {
            super::bookmarks::select(s, "Attachments")?;
            if let Some(para) = s.doc.para_at(&s.sel.anchor)
                && para.props.style.as_deref() == Some("InkstoneLetterCopies")
                && para.text.split('\u{fffc}').next().is_some_and(str::is_empty)
            {
                let persian = s.doc.styles.get("InkstoneLetterBody").and_then(|st| st.chr.lang.as_deref()) == Some("fa-IR");
                let label = if persian { "پیوست‌ها: " } else { "Enclosures: " };
                values.insert("Attachments".into(), json!(format!("{label}{text}")));
            }
        }
        super::bookmarks::fill(s, &json!({"values": values}))?;
    }
    if let Some(path) = p::str(v, "sourcePath") {
        let mut source = crate::io::open_path(std::path::Path::new(path)).map_err(CmdError::Failed)?;
        require_ready(&source)?;
        // Imported body bookmarks must not collide with the destination template fields.
        for story in std::iter::once(StoryRef::Body).chain(source.parts.keys().map(|id| StoryRef::Part(*id))).collect::<Vec<_>>() {
            for path in source.para_paths(story) {
                let Some(para) = source.para(story, &path) else { continue };
                let offsets: Vec<_> = para
                    .object_offsets()
                    .into_iter()
                    .filter(|off| matches!(para.object_at(*off), Some(InlineObject::BookmarkStart { .. } | InlineObject::BookmarkEnd { .. })))
                    .collect();
                for off in offsets.into_iter().rev() {
                    source.para_mut(story, &path)?.delete(off, off + inkstone_doc::para::OBJ.len_utf8())?;
                }
            }
        }
        super::bookmarks::select(s, p::str(v, "bodyBookmark").unwrap_or("Body"))?;
        let fragment = super::paste::adopt(s, source);
        let at = super::delete_selection(s)?;
        // A standalone body field adopts the source's first paragraph formatting too.
        // Inline fields retain the surrounding template paragraph's formatting.
        if let Some(inkstone_doc::Block::Para(first)) = fragment.blocks.first() {
            let target = s.doc.para_mut(at.story, &at.path)?;
            if target.text.chars().all(|c| c.is_whitespace() || c == '\u{fffc}')
                && target.objects.iter().all(|o| matches!(o, InlineObject::BookmarkStart { .. } | InlineObject::BookmarkEnd { .. }))
            {
                target.props = first.props.clone();
                target.mark = first.mark.clone();
                target.touch();
            }
        }
        let end = s.doc.insert_fragment(&at, &fragment)?;
        s.sel = Selection::caret(end);
    }
    if let Some(images) = v.get("images").and_then(Value::as_object) {
        if images.len() > 4 {
            return Err(CmdError::Params("at most four template images".into()));
        }
        for (name, image) in images {
            let info = image_info(s, image)?;
            super::bookmarks::select(s, name)?;
            let box_size = match s.doc.para(s.sel.focus.story, &s.sel.focus.path).and_then(|p| p.props.style.as_deref()) {
                Some("InkstoneLetterLogo") => Some((36.0, 30.0)),
                Some("InkstoneLetterSignature") => Some((120.0, 40.0)),
                Some("InkstoneLetterQRCode") => Some((26.0, 26.0)),
                _ => None,
            };
            let mut fitted = image.clone();
            if let Some((max_width, max_height)) = box_size {
                let ratio = info["width"].as_f64().unwrap_or(1.0) / info["height"].as_f64().unwrap_or(1.0);
                let width = (p::f32(image, "width").unwrap_or(max_width) as f64).min(max_width as f64).min(max_height * ratio);
                fitted["width"] = json!(width);
            }
            super::insert::picture(s, &fitted)?;
        }
    }
    Ok(json!({"composed": true}))
}

fn starter(s: &mut Session, v: &Value) -> CmdResult {
    let language = p::str(v, "language").unwrap_or("en-US");
    s.doc = super::letter_templates::build(language, p::str(v, "paper"))?;
    s.sel = Selection::caret(Pos::body(0, 0));
    Ok(json!({"created": true}))
}

#[cfg(test)]
mod tests {
    use super::{Document, Session, json};
    use inkstone_doc::{Block, CharProps, Paragraph, para_block};
    #[test]
    fn official_profiles_use_script_fonts_and_plain_body_paragraphs() {
        for (language, width, face, size) in [("fa-IR", 419.5276, "B Zar", 13.0), ("en-US", 612.0, "Times New Roman", 12.0)] {
            let mut s = Session::new(Document::new());
            s.run("document.letterTemplate", &json!({"language":language})).unwrap();
            assert!((s.doc.last_section.page_w - width).abs() < 0.01);
            let style = s.doc.styles.get("InkstoneLetterBody").unwrap();
            assert_eq!(style.chr.font_cs.as_deref(), Some(face));
            assert_eq!(style.chr.size_cs, Some(size));
            assert_eq!(style.chr.bold_cs, Some(language == "fa-IR"));
            s.run("document.compose", &json!({"values":{"Body":"First paragraph.\nSecond paragraph.","Attachments":"outline.docx"}})).unwrap();
            for text in ["First paragraph.", "Second paragraph."] {
                assert!(s.doc.body.iter().any(|b| matches!(&**b, Block::Para(p) if p.text.contains(text) && !p.text.contains('\n') && p.props.style.as_deref() == Some("InkstoneLetterBody"))));
            }
            assert!(s.doc.body.iter().any(|b| matches!(&**b, Block::Para(p) if p.text.contains("outline.docx") && (p.text.contains("Enclosures:") || p.text.contains("پیوست‌ها:")))));
            s.run("document.compose", &json!({"values":{"Attachments":"updated.docx"}})).unwrap();
            assert!(s.doc.body.iter().any(|b| matches!(&**b, Block::Para(p) if p.text.contains("updated.docx") && (p.text.contains("Enclosures:") || p.text.contains("پیوست‌ها:")))));
            let before = s.doc.clone();
            assert!(s.run("document.letterTemplate", &json!({"language":language,"paper":"A3"})).is_err());
            assert_eq!(s.doc, before);
        }
    }
    #[test]
    fn importing_styles_preserves_dependent_children_and_source_id_collisions() {
        for reserved in [false, true] {
            let mut target = Document::new();
            let mut normal = target.styles.get("Normal").unwrap().clone();
            let mut child = normal.clone();
            child.id = "SharedChild".into();
            child.based_on = Some("Normal".into());
            target.styles.upsert(child);
            let mut source = target.clone();
            if reserved {
                let mut named = normal.clone();
                named.id = "Imported_Normal".into();
                source.styles.upsert(named);
            }
            normal.chr.bold = Some(true);
            source.styles.upsert(normal);
            let mut para = Paragraph::with_text("Source child style", CharProps::default());
            para.props.style = Some("SharedChild".into());
            source.body = vec![para_block(para)];
            let mut s = Session::new(target);
            let fragment = super::super::paste::adopt(&mut s, source);
            let prefix = if reserved { "Imported__" } else { "Imported_" };
            let imported = s.doc.styles.get(&format!("{prefix}SharedChild")).unwrap();
            assert_eq!(imported.based_on.as_deref(), Some(format!("{prefix}Normal").as_str()));
            assert_eq!(s.doc.styles.get("SharedChild").unwrap().based_on.as_deref(), Some("Normal"));
            assert!(fragment.blocks.iter().any(|b| matches!(b, Block::Para(p) if p.props.style.as_deref() == Some(imported.id.as_str()))));
        }
    }
    #[test]
    fn starter_composes_literal_fields_and_rejects_revisions_atomically() {
        let mut s = Session::new(Document::new());
        s.run("document.letterTemplate", &json!({"language":"fa-IR"})).unwrap();
        s.run("document.compose", &json!({"values":{"Subject":"موضوع آزمایشی", "Body":"متن نامه"}})).unwrap();
        assert!(s.doc.body.iter().any(|b| matches!(&**b, Block::Para(p) if p.text.contains("متن نامه"))));
        let before = s.doc.clone();
        assert!(s.run("document.compose", &json!({"values":{"Subject":"changed"}, "images":{"Missing":{"data":"invalid"}}})).is_err());
        assert_eq!(s.doc, before);
        s.doc.settings.track_changes = true;
        assert!(s.run("document.compose", &json!({"values":{"Body":"refused"}})).is_err());
    }
}
