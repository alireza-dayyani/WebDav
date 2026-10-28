use super::{CmdResult, Session, Value, json, reference, stories};
use inkstone_doc::{CharProps, InlineObject};
use std::collections::BTreeSet;

fn collect_fonts(props: &CharProps, out: &mut BTreeSet<String>) {
    for font in [&props.font, &props.font_cs, &props.font_ea].into_iter().flatten() {
        if !font.is_empty() && out.len() < 4000 {
            out.insert(font.clone());
        }
    }
}

fn field_reference(instruction: &str) -> Option<String> {
    let mut words = instruction.split_whitespace();
    if !matches!(words.next()?.to_uppercase().as_str(), "REF" | "PAGEREF" | "NOTEREF") {
        return None;
    }
    words.next().map(|word| word.trim_matches('"').to_string())
}

fn missing_reference(instruction: &str, names: &BTreeSet<String>) -> Option<String> {
    field_reference(instruction).filter(|name| !names.contains(name))
}

pub(super) fn validate(s: &mut Session) -> CmdResult {
    let health = s.run("document.preflight", &json!({}))?;
    let accessibility = s.run("file.accessibility", &json!({}))?;
    let bookmarks = s.run("document.bookmarks", &json!({}))?;
    let names: BTreeSet<String> = bookmarks
        .get("bookmarks")
        .and_then(Value::as_array)
        .into_iter()
        .flatten()
        .filter_map(|bookmark| bookmark.get("name").and_then(Value::as_str).map(str::to_string))
        .collect();
    let mut placeholders = Vec::new();
    let mut broken = Vec::new();
    let mut fonts = BTreeSet::new();
    collect_fonts(&s.doc.styles.default_chr, &mut fonts);
    for style in &s.doc.styles.styles {
        collect_fonts(&style.chr, &mut fonts);
    }
    for story in stories(s) {
        for path in s.doc.para_paths(story) {
            if let Some(para) = s.doc.para(story, &path) {
                let reference = reference(story, &path);
                if (para.text.contains("{{") || para.text.contains("}}")) && placeholders.len() < 4000 {
                    placeholders.push(json!({"ref":reference,"issue":"Possible unfilled placeholder"}));
                }
                for (_, props) in para.run_ranges() {
                    collect_fonts(props, &mut fonts);
                    if let Some(name) =
                        props.link.as_deref().and_then(|link| link.strip_prefix('#')).filter(|name| *name != "_top" && !names.contains(*name))
                        && broken.len() < 4000
                    {
                        broken.push(json!({"ref":reference,"target":name,"kind":"bookmarkLink"}));
                    }
                }
                for object in &para.objects {
                    if let InlineObject::Field { instr, .. } | InlineObject::FieldStart { instr, .. } = object
                        && let Some(name) = missing_reference(instr, &names)
                        && broken.len() < 4000
                    {
                        broken.push(json!({"ref":reference,"target":name,"kind":"fieldReference"}));
                    }
                }
            }
        }
    }
    let db = inkstone_fonts::FontDb::global();
    #[cfg(not(target_arch = "wasm32"))]
    db.load_system_fonts();
    let available: BTreeSet<_> = db.families().into_iter().map(|f| f.to_lowercase()).collect();
    let missing: Vec<_> = fonts
        .into_iter()
        .filter(|name| {
            !available.contains(&name.to_lowercase()) && db.english_family(name).is_none_or(|english| !available.contains(&english.to_lowercase()))
        })
        .collect();
    let unfilled: Vec<Value> = stories(s)
        .into_iter()
        .flat_map(|story| s.doc.control_ranges(story))
        .filter(|range| range.control.showing_placeholder)
        .take(4000)
        .map(|range| super::super::controls::describe(s, &range))
        .collect();
    Ok(
        json!({"health":health,"accessibility":accessibility,"bookmarks":bookmarks,"placeholders":placeholders,"unfilledControls":unfilled,"missingFonts":missing,"brokenReferences":broken,"checks":["documentHealth","accessibility","bookmarkRanges","placeholderMarkers","contentControlPlaceholders","fontAvailability","internalLinks","bookmarkReferenceFields"],"warnings":["Font availability describes this server. External URLs and unsupported Word field types are not validated."]}),
    )
}
