//! Account storage adapter. Networking lives in the Vue host's same-origin bridge.
use inkstone_ui_egui::async_saves::{AsyncSaves, SaveOutcome};
use wasm_bindgen::prelude::*;
use wasm_bindgen_futures::JsFuture;

#[wasm_bindgen]
extern "C" {
    #[wasm_bindgen(js_namespace = inkstoneHost, js_name = preferenceScope)]
    pub fn preference_scope() -> String;
    #[wasm_bindgen(js_namespace = inkstoneHost, catch, js_name = load)]
    fn host_load() -> Result<js_sys::Promise, JsValue>;
    #[wasm_bindgen(js_namespace = inkstoneHost, catch, js_name = save)]
    fn host_save(json: &str, generation: &str, title: &str) -> Result<js_sys::Promise, JsValue>;
    #[wasm_bindgen(js_namespace = inkstoneHost, js_name = bind)]
    pub fn bind(generation: &str);
    #[wasm_bindgen(js_namespace = inkstoneHost, js_name = takeSaveRequest)]
    pub fn take_save_request() -> bool;
    #[wasm_bindgen(js_namespace = inkstoneHost, js_name = canAutosave)]
    pub fn can_autosave() -> bool;
    #[wasm_bindgen(js_namespace = inkstoneHost, js_name = state)]
    pub fn state(dirty: bool, pending: bool, generation: &str);
}

pub async fn load() -> Result<(Option<inkstone_doc::Document>, String, String), String> {
    let value = JsFuture::from(host_load().map_err(js_error)?).await.map_err(js_error)?;
    let json = value.as_string().ok_or("Invalid storage response")?;
    let value: serde_json::Value = serde_json::from_str(&json).map_err(|e| e.to_string())?;
    let title = value.get("title").and_then(|v| v.as_str()).unwrap_or("Untitled document").to_string();
    let author = value.get("author").and_then(|v| v.as_str()).unwrap_or("Writer").to_string();
    let document = match value.get("document").filter(|v| !v.is_null()) {
        Some(doc) => Some(serde_json::from_value(doc.clone()).map_err(|e| format!("Cannot open saved document: {e}"))?),
        None => None,
    };
    Ok((document, title, author))
}

pub fn save(bytes: &[u8], generation: u64, title: &str, saves: &AsyncSaves, ctx: &egui::Context) -> Result<(), String> {
    let json = std::str::from_utf8(bytes).map_err(|e| e.to_string())?;
    let promise = host_save(json, &generation.to_string(), title).map_err(js_error)?;
    let ticket = saves.start();
    let saves = saves.clone();
    let ctx = ctx.clone();
    wasm_bindgen_futures::spawn_local(async move {
        let outcome = match JsFuture::from(promise).await {
            Ok(_) => SaveOutcome::Saved,
            Err(error) => SaveOutcome::Failed(js_error(error)),
        };
        saves.finish(ticket, outcome);
        ctx.request_repaint();
    });
    Ok(())
}

fn js_error(error: JsValue) -> String {
    error
        .as_string()
        .or_else(|| js_sys::Reflect::get(&error, &JsValue::from_str("message")).ok().and_then(|v| v.as_string()))
        .unwrap_or_else(|| format!("{error:?}"))
}
