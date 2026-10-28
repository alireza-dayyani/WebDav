use super::{CmdResult, Path, Pos, Session, Value, bad, json, p, reference, stories};
use inkstone_doc::{Block, Blocks, InlineObject, StoryRef};

const MAX_ITEMS: usize = 4000;

pub(super) fn structure(s: &mut Session, v: &Value) -> CmdResult {
    let offset = super::index(v, "offset", Some(0))?;
    let limit = super::index(v, "limit", Some(200))?.clamp(1, 500);
    let mut all = Vec::new();
    for story in stories(s) {
        for path in s.doc.para_paths(story) {
            if all.len() >= MAX_ITEMS {
                break;
            }
            if let Some(para) = s.doc.para(story, &path) {
                all.push(json!({"ref":reference(story,&path),"story":story,"path":path,"text":para.plain_text(),"byteLength":para.len(),"style":para.props.style,"props":para.props,"runs":para.run_ranges().map(|(r,c)|json!({"start":r.start,"end":r.end,"props":c})).collect::<Vec<_>>()}));
            }
        }
    }
    let total = all.len();
    let items: Vec<_> = all.into_iter().skip(offset).take(limit).collect();
    Ok(
        json!({"paragraphs":items,"offset":offset,"hasMore":offset.saturating_add(limit)<total,"truncated":total>=MAX_ITEMS,"tables":tables(s)?,"parts":s.doc.parts.iter().map(|(id,part)|json!({"id":id,"kind":part.kind})).collect::<Vec<_>>(),"sections":s.doc.sections().iter().map(|(end,sp)|json!({"endBlock":end,"props":sp})).collect::<Vec<_>>(),"offsetUnit":"utf8Bytes"}),
    )
}

pub(super) fn search(s: &mut Session, v: &Value) -> CmdResult {
    let text = super::checked_text(v, "text")?;
    if text.is_empty() {
        return Err(bad("search text must not be empty"));
    }
    let scope = p::str(v, "scope").unwrap_or("body");
    if v.get("scope").is_some_and(|value| !value.is_string()) {
        return Err(bad("scope must be a string"));
    }
    if !matches!(scope, "body" | "all") {
        return Err(bad("scope must be body or all"));
    }
    let mut matches = Vec::new();
    let mut count = 0usize;
    for story in stories(s).into_iter().filter(|story| scope == "all" || *story == StoryRef::Body) {
        s.sel = crate::Selection::caret(s.doc.start_of(story));
        s.run("edit.find",&json!({"text":text,"matchCase":super::flag(v,"matchCase",true)?,"wholeWord":super::flag(v,"wholeWord",false)?,"special":false,"regex":false,"wildcards":false}))?;
        count += s.find.results.len();
        for (a, b) in s.find.results.iter().take(MAX_ITEMS.saturating_sub(matches.len())) {
            matches.push(json!({"start":a,"end":b,"target":{"range":{"start":a,"end":b}},"text":s.doc.copy_range(a,b).plain_text(),"context":s.doc.para_at(a).map(|p|p.plain_text().chars().take(240).collect::<String>())}));
        }
    }
    Ok(json!({"count":count,"matches":matches,"truncated":count>matches.len(),"offsetUnit":"utf8Bytes"}))
}

fn walk_tables(blocks: &Blocks, story: StoryRef, prefix: &[u32], out: &mut Vec<Value>) {
    if prefix.len() > 48 || out.len() >= MAX_ITEMS {
        return;
    }
    for (i, block) in blocks.iter().enumerate() {
        let mut path = prefix.to_vec();
        path.push(i as u32);
        if let Block::Table(table) = &**block {
            let cells:Vec<_>=table.rows.iter().map(|r|r.cells.iter().map(|c|json!({"text":c.blocks.iter().filter_map(|b|b.as_para()).map(|p|p.plain_text()).collect::<Vec<_>>().join("\n"),"span":c.span()})).collect::<Vec<_>>()).collect();
            out.push(json!({"ref":reference(story,&Path(path.clone())),"story":story,"path":path,"rows":table.rows.len(),"columns":table.cols(),"style":table.props.style,"cells":cells}));
            for (ri, row) in table.rows.iter().enumerate() {
                for (ci, cell) in row.cells.iter().enumerate() {
                    let mut parent = path.clone();
                    parent.extend([ri as u32, ci as u32]);
                    walk_tables(&cell.blocks, story, &parent, out);
                }
            }
        }
        if out.len() >= MAX_ITEMS {
            break;
        }
    }
}

pub(super) fn tables(s: &Session) -> CmdResult {
    let mut result = Vec::new();
    for story in stories(s) {
        if let Some(blocks) = s.doc.story(story) {
            walk_tables(blocks, story, &[], &mut result);
        }
    }
    Ok(json!({"items":result,"truncated":result.len()>=MAX_ITEMS}))
}

pub(super) fn images(s: &Session) -> CmdResult {
    let mut result = Vec::new();
    for story in stories(s) {
        for path in s.doc.para_paths(story) {
            if let Some(para) = s.doc.para(story, &path) {
                for off in para.object_offsets() {
                    if let Some(InlineObject::Image { w: width, h: height, alt, .. }) = para.object_at(off) {
                        let at = Pos::new(story, path.clone(), off);
                        result.push(json!({"ref":format!("{}@{off}",reference(story,&path)),"position":at,"width":width,"height":height,"alt":alt}));
                        if result.len() >= MAX_ITEMS {
                            return Ok(json!({"items":result,"truncated":true}));
                        }
                    }
                }
            }
        }
    }
    Ok(json!({"items":result,"truncated":false}))
}
