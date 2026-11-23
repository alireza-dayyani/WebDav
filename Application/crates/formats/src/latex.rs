//! LaTeX (`.tex`) import and export via `craft-latex`.

use inkstone_doc::{Document, SectionProps};

pub use craft_latex::{escape_text, latex_to_linear, linear_to_latex};

pub fn import(bytes: &[u8]) -> Document {
    inkstone_docx::from_shared_model(craft_latex::import(bytes)).unwrap_or_default()
}

pub fn import_on(bytes: &[u8], page: &SectionProps) -> Document {
    let mut d = import(bytes);
    d.last_section = page.clone();
    d
}

pub fn parse(text: &str) -> craft_flow::Flow {
    craft_latex::parse(text)
}

const CRAFT_LATEX_BANNER: &str = "% Written by WordCraft (https://getartcraft.com/apps/wordcraft).\n";
const INKSTONE_LATEX_BANNER: &str = "% Written by Inkstone (/).\n";

pub fn export(doc: &Document) -> String {
    match inkstone_docx::to_shared_model(doc) {
        Ok(shared) => {
            let mut out = craft_latex::export(&shared);
            if out.starts_with(CRAFT_LATEX_BANNER) {
                out.replace_range(..CRAFT_LATEX_BANNER.len(), INKSTONE_LATEX_BANNER);
            }
            out
        }
        Err(_) => String::new(),
    }
}
