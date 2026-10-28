//! Original letter layouts; source guidance and font requirements: docs/LETTER_TEMPLATES.md.
use crate::CmdError;
use inkstone_doc::props::LineSpacing;
use inkstone_doc::styles::Style;
use inkstone_doc::{Align, Block, CharProps, Document, InlineObject, ParaProps, Paragraph, PartKind, para_block};

pub(super) fn build(language: &str, paper: Option<&str>) -> Result<Document, CmdError> {
    let fa = language == "fa-IR";
    let rtl = matches!(language, "fa-IR" | "ar-SA" | "ur-PK");
    let paper = paper.unwrap_or(if fa {
        "A5"
    } else if rtl {
        "A4"
    } else {
        "Letter"
    });
    let (width, height) = match paper {
        "A5" => (419.5276, 595.2756),
        "A4" => (595.2756, 841.8898),
        "Letter" => (612.0, 792.0),
        _ => return Err(CmdError::Params("paper must be A5, A4 or Letter".into())),
    };
    let mut doc = Document::new();
    doc.body.clear();
    doc.last_section.page_w = width;
    doc.last_section.page_h = height;
    doc.last_section.margin_left = if paper == "A5" { 36.0 } else { 72.0 };
    doc.last_section.margin_right = doc.last_section.margin_left;
    doc.last_section.margin_top = 72.0;
    doc.last_section.margin_bottom = if fa { 84.0 } else { 76.0 };
    doc.last_section.header = 18.0;
    doc.last_section.footer = 18.0;
    doc.last_section.rtl = rtl;
    define_styles(&mut doc, language, fa, rtl);
    let field = |name: &str, prefix: &str, style: &str| -> Result<_, CmdError> {
        let st = doc.styles.get(&format!("InkstoneLetter{style}")).ok_or_else(|| CmdError::Failed("letter style missing".into()))?;
        let mut para = Paragraph::with_text(prefix, st.chr.clone());
        para.props = st.para.clone();
        para.props.style = Some(st.id.clone());
        let mut marker = st.chr.clone();
        if matches!(name, "Date" | "Number" | "Recipients" | "Tracking") {
            marker.rtl = Some(false);
        }
        para.insert_object(para.len(), InlineObject::BookmarkStart { name: name.into() }, &marker)?;
        para.insert_object(para.len(), InlineObject::BookmarkEnd { name: name.into() }, &marker)?;
        Ok(para_block(para))
    };
    let text = |value: &str, style: &str| -> Result<_, CmdError> {
        let st = doc.styles.get(&format!("InkstoneLetter{style}")).ok_or_else(|| CmdError::Failed("letter style missing".into()))?;
        let mut para = Paragraph::with_text(value, st.chr.clone());
        para.props = st.para.clone();
        para.props.style = Some(st.id.clone());
        Ok(para_block(para))
    };
    let header = vec![field("Logo", "", "Logo")?, field("Organization", "", "Brand")?];
    let mut blocks = Vec::new();
    if !fa {
        blocks.push(field("Address", "", "Body")?);
    }
    blocks.push(field("Date", if fa { "تاریخ: " } else { "" }, if fa { "Meta" } else { "Body" })?);
    blocks.push(field("Number", if fa { "شماره: " } else { "Reference: " }, "Meta")?);
    // Optional fields have no permanent labels that would remain empty after a fill.
    blocks.push(field("Classification", "", "Meta")?);
    blocks.push(field("Priority", "", "Meta")?);
    blocks.push(field("Recipients", if fa { "گیرنده: " } else { "To: " }, if fa { "Heading" } else { "Body" })?);
    blocks.push(field("Subject", if fa { "موضوع: " } else { "Subject: " }, "Heading")?);
    blocks.push(text(if fa { "با سلام و احترام" } else { "Dear colleague:" }, "Body")?);
    blocks.push(field("Body", "", "Body")?);
    let mut closing = text(if fa { "با احترام" } else { "Sincerely," }, "Body")?;
    if let Block::Para(p) = std::sync::Arc::make_mut(&mut closing) {
        p.props.keep_next = Some(true);
        p.props.space_after = Some(4.0);
    }
    blocks.push(closing);
    if !fa {
        blocks.push(field("Signature", "", "Signature")?);
    }
    for name in ["SenderName", "SenderTitle"] {
        let mut signer = field(name, "", if fa { "Heading" } else { "Body" })?;
        if let Block::Para(p) = std::sync::Arc::make_mut(&mut signer) {
            p.props.keep_next = Some(name == "SenderName" || fa);
            p.props.space_after = Some(2.0);
        }
        blocks.push(signer);
    }
    if fa {
        blocks.push(field("Signature", "", "Signature")?);
    }
    blocks.push(field("Copies", "", "Copies")?);
    blocks.push(field("Attachments", "", "Copies")?);
    let mut footer = Vec::new();
    if fa {
        footer.push(field("Address", "", "Contact")?);
    }
    footer.push(field("Tracking", "", "PageNumber")?);
    footer.push(field("QRCode", "", "QRCode")?);
    let page_chars = doc.styles.get("InkstoneLetterPageNumber").ok_or_else(|| CmdError::Failed("page number style missing".into()))?.chr.clone();
    let mut pages = Paragraph::new().styled("InkstoneLetterPageNumber");
    for (index, instr) in ["PAGE", "NUMPAGES"].into_iter().enumerate() {
        if index > 0 {
            pages.insert_text(pages.len(), " / ", &page_chars)?;
        }
        pages.insert_object(
            pages.len(),
            InlineObject::Field { instr: instr.into(), result: "1".into(), locked: false, dirty: false, code: false, form: String::new() },
            &page_chars,
        )?;
    }
    pages.props.bidi = Some(false);
    pages.props.align = Some(Align::Center);
    footer.push(para_block(pages));
    doc.body = blocks;
    let header_id = doc.add_part(PartKind::Header, header);
    let footer_id = doc.add_part(PartKind::Footer, footer);
    doc.last_section.headers.default = Some(header_id);
    doc.last_section.footers.default = Some(footer_id);
    Ok(doc)
}

fn define_styles(doc: &mut Document, language: &str, fa: bool, rtl: bool) {
    let face = if fa { "B Zar" } else { "Times New Roman" };
    let chars = |font: &str, size: f32, bold: bool| CharProps {
        font: Some(font.into()),
        font_cs: Some(font.into()),
        size: Some(size),
        size_cs: Some(size),
        bold: Some(bold),
        bold_cs: Some(bold),
        lang: Some(language.into()),
        lang_bidi: Some(language.into()),
        ..CharProps::default()
    };
    let body = chars(face, if fa { 13.0 } else { 12.0 }, fa);
    let heading = chars(if fa { "B Titr" } else { face }, if fa { 11.0 } else { 12.0 }, true);
    let meta = chars(face, if fa { 9.0 } else { 10.0 }, fa);
    let contact = chars(face, if fa { 7.0 } else { 9.0 }, fa);
    let body_para = ParaProps {
        bidi: Some(rtl),
        align: Some(if fa { Align::Justify } else { Align::Left }),
        line_spacing: Some(LineSpacing::Multiple(if fa { 1.15 } else { 1.0 })),
        space_before: Some(0.0),
        space_after: Some(if fa { 7.0 } else { 12.0 }),
        indent_first: Some(0.0),
        widow_control: Some(true),
        ..ParaProps::default()
    };
    // Word treats bold in styles as a toggle. Keep the base unbolded and make
    // the Persian letter styles themselves bold instead of toggling twice.
    let mut base = body.clone();
    base.bold = Some(false);
    base.bold_cs = Some(false);
    doc.styles.default_chr = base.clone();
    doc.styles.default_para = body_para.clone();
    if let Some(mut normal) = doc.styles.get("Normal").cloned() {
        normal.chr = base;
        normal.para = body_para.clone();
        doc.styles.upsert(normal);
    }
    let definitions = [
        ("Body", body.clone(), body_para.clone()),
        ("Heading", heading.clone(), ParaProps { align: Some(Align::Left), keep_next: Some(true), ..body_para.clone() }),
        ("Meta", meta, ParaProps { align: Some(Align::Left), space_after: Some(2.0), ..body_para.clone() }),
        ("Copies", chars(face, 9.0, fa), ParaProps { align: Some(Align::Left), space_after: Some(3.0), ..body_para.clone() }),
        ("Contact", contact.clone(), ParaProps { align: Some(Align::Left), space_after: Some(2.0), ..body_para.clone() }),
        (
            "Brand",
            if fa { contact.clone() } else { heading },
            ParaProps { align: Some(if fa { Align::Center } else { Align::Left }), space_after: Some(2.0), ..body_para.clone() },
        ),
        (
            "Logo",
            contact.clone(),
            ParaProps { align: Some(if fa { Align::Center } else { Align::Left }), space_after: Some(0.0), ..body_para.clone() },
        ),
        (
            "Signature",
            body,
            ParaProps { align: Some(Align::Left), space_after: Some(2.0), keep_lines: Some(true), keep_next: Some(!fa), ..body_para.clone() },
        ),
        ("QRCode", contact, ParaProps { bidi: Some(false), align: Some(Align::Center), space_after: Some(0.0), ..body_para.clone() }),
        (
            "PageNumber",
            chars("Times New Roman", 7.0, false),
            ParaProps {
                bidi: Some(false),
                align: Some(Align::Center),
                space_after: Some(0.0),
                line_spacing: Some(LineSpacing::Multiple(1.0)),
                ..ParaProps::default()
            },
        ),
    ];
    for (name, chr, para) in definitions {
        doc.styles.upsert(Style {
            id: format!("InkstoneLetter{name}"),
            name: format!("Letter {name}"),
            based_on: Some("Normal".into()),
            chr,
            para,
            ..Style::default()
        });
    }
}
