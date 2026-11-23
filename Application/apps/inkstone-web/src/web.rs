//! The browser shell: the framework host (files, intake, clipboard, print, settings), the
//! unsaved-changes guard and the eframe web runner.

use std::cell::{Cell, RefCell};
use std::rc::Rc;
use std::sync::Arc;

use craft::automation::control::ControlChannel;
use craft::host::{Files, Host, Wake};
use craft::platform::appearance::SystemReader;
use craft::platform::browser_files::BrowserFiles;
use craft::platform::clipboard::{Clipboard, ClipboardError, EventClipboard, Flavor, LateOffers, TEXT};
use craft::platform::open::{DropTarget, DroppedFile, FileIntake, IntakeSender};
use craft::print::spool::Spool;
use craft::settings::SharedSettings;
use craft::ui::InstallSpec;
use craft::ui::shell::embed;
use craft::window::system::NoWindows;
use wasm_bindgen::JsCast as _;
use inkstone_engine::Session;
use inkstone_ui_egui::async_saves::AsyncSaves;
use inkstone_ui_egui::prefs::Prefs;
use inkstone_ui_egui::{Services, WordApp};

mod font_store;
const CANVAS_ID: &str = "inkstone_canvas";
const LOADING_ID: &str = "inkstone_loading";

pub fn start() {
    eframe::WebLogger::init(log::LevelFilter::Info).ok();
    wasm_bindgen_futures::spawn_local(async {
        let Some(document) = web_sys::window().and_then(|w| w.document()) else {
            log::error!("no document");
            return;
        };
        let Some(canvas) = document.get_element_by_id(CANVAS_ID).and_then(|e| e.dyn_into::<web_sys::HtmlCanvasElement>().ok()) else {
            log::error!("missing <canvas id=\"{CANVAS_ID}\">");
            return;
        };
        // Host modes (`craft-web-host`): `?host=parent`, a same-origin page framing Inkstone, opens documents in
        // it and stores what it saves (`host`); unframed there is no parent, so that URL on its
        // own stays the normal app with downloads. `data-host="page"` on the canvas opts a page
        // in instead: it runs Inkstone in its own document and talks over DOM `CustomEvent`s
        // on the canvas (`host`). The host guards leaving the page, so this one doesn't.
        let bridge = crate::host::Bridge::detect(&canvas);
        let app_bridge = bridge.clone();
        let dirty = Rc::new(Cell::new(false));
        let cloud_saves = AsyncSaves::default();
        let (saved_document, saved_title, saved_author, saved_bookmark) = match crate::cloud::load().await {
            Ok(loaded) => loaded,
            Err(error) => {
                if let Some(el) = document.get_element_by_id(LOADING_ID) {
                    el.set_text_content(Some(&format!("Cannot open this document: {error}. Return to My documents and try again.")));
                }
                return;
            }
        };
        let mut options = eframe::WebOptions::default();
        if query().contains("webgl")
            && let eframe::egui_wgpu::WgpuSetup::CreateNew(create) = &mut options.wgpu_options.wgpu_setup
        {
            create.instance_descriptor.backends = eframe::wgpu::Backends::GL;
        }
        let result = eframe::WebRunner::new()
            .start(
                canvas,
                options,
                Box::new(move |cc| {
                    if let Some(rs) = &cc.wgpu_render_state {
                        log::info!("inkstone-web: wgpu backend {:?}", rs.adapter.get_info().backend);
                    }
                    let ctx = cc.egui_ctx.clone();
                    // Fonts the user added on an earlier visit (#582); the app notices them arrive.
                    font_store::load_all(ctx.clone());

                    // The clipboard: the browser's paste event feeds it; a late paste (a picture read
                    // asynchronously) arrives through `late_offers`. The pass clock counts frames, so a late
                    // offer is readable in every pass of its frame.
                    let frame_ctx = ctx.clone();
                    let copy_ctx = ctx.clone();
                    let event_clipboard = EventClipboard::new(move || frame_ctx.cumulative_frame_nr(), move |flavors| copy_out(&copy_ctx, flavors))
                        .with_wake(waker(&ctx));
                    let late_offers = event_clipboard.late_offers();
                    if let Err(e) = listen_for_pasted_pictures(late_offers) {
                        log::error!("pasting pictures is unavailable: {e}");
                    }

                    // The browser's locale picks the new document's paper, the units and the
                    // decimal separator (#838).
                    let region = inkstone_ui_egui::i18n::system_region();

                    // The stored interface language picks the fonts' script coverage, so the settings open first.
                    let (settings, report) = crate::prefs::open();
                    if report.has_problems() {
                        log::warn!("preferences: {report:?}");
                    }
                    let lang = inkstone_ui_egui::i18n::Lang::from_pref(&settings.lock().get().language);
                    let spec = InstallSpec::new(craft::host::target::ShellInfo::current(), Arc::new(NoWindows))
                        .app_id("inkstone")
                        .system(SystemReader::start(waker(&ctx)))
                        .clipboard(Clipboard::new(event_clipboard));
                    if let Err(e) = embed::install(&ctx, spec, &inkstone_ui_egui::ui_fonts(lang)) {
                        log::error!("inkstone-web: the UI runtime did not install: {e:?}");
                    }

                    // Files: the browser's pickers and downloads; a host page stores what is saved instead.
                    let browser = Arc::new(BrowserFiles::new(waker(&ctx)));
                    if app_bridge.is_none()
                        && let Err(e) = guard_unload(dirty.clone(), browser.clone())
                    {
                        log::error!("no unsaved-changes guard: {e}");
                    }
                    let files: Arc<dyn Files> =
                        if app_bridge.is_some() { Arc::new(crate::host::HostedFiles::new(browser.clone())) } else { browser.clone() };

                    let host = Arc::new(
                        Host::builder(inkstone_ui_egui::APP)
                            .i18n(inkstone_ui_egui::i18n::catalogs())
                            .files(files)
                            .settings(Arc::new(settings.clone()))
                            .lang(lang.craft_lang())
                            .build(),
                    );

                    let (intake, sender) = FileIntake::new(Wake::new(waker(&ctx)));

                    // In host mode the parent sends the document to edit; no sample meanwhile.
                    let sample = app_bridge.is_none() && query().contains("sample");
                    let doc = saved_document.unwrap_or_else(|| {
                        if sample { inkstone_engine::sample::sample_document() } else { inkstone_engine::blank_document(&region) }
                    });
                    let mut session = Session::new(doc);
                    if let Some(bookmark) = &saved_bookmark {
                        let _ = session.run("edit.goto", &serde_json::json!({"bookmark": bookmark}));
                        let _ = session.run("select.collapse", &serde_json::json!({"end": true}));
                    }
                    session.path = Some(std::path::PathBuf::from(format!("{saved_title}.json")));
                    crate::cloud::bind(&session.document_id().to_string());
                    session.author = saved_author;
                    session.system_region = region;
                    let messages = Rc::new(RefCell::new(Vec::new()));
                    let cloud = if app_bridge.is_none() { Some(cloud_saves.clone()) } else { None };
                    let mut services = services(&host, ctx.clone(), dirty, messages.clone(), intake, cloud);
                    if let Some(bridge) = &app_bridge {
                        bridge.connect(&mut services, sender.clone());
                    }
                    let requests = crate::api::install(ctx.clone());
                    let mut app =
                        WordApp::new(session, services).with_host(host).with_settings(settings.clone()).with_control(ControlChannel::new(requests));
                    app.autosave = false;
                    app.tell_about_font_downloads();
                    // A region chosen in File › Options replaces the browser's for the first blank
                    // document too (#838).
                    if !sample && app.session.prefs.region.is_some() {
                        let doc = app.session.blank_document();
                        app.session.set_document(doc);
                    }
                    // No document to show: start on File › Home, as the desktop app does (#766).
                    if saved_document.is_none() && !sample {
                        app.start_screen();
                    }
                    // The host's name for the user signs this visit's work, but isn't stored as the
                    // browser's own preference, which a visit outside the host would restore.
                    let mut author = None;
                    if app_bridge.is_some()
                        && let Some(name) = query_param("author")
                    {
                        let own = app.session.author.clone();
                        crate::host::Bridge::set_author(&mut app, &name);
                        author = crate::prefs::HostAuthor::new(&app.session.author, &own);
                    }
                    Ok(Box::new(WebShell {
                        app,
                        sender,
                        messages,
                        settings,
                        author,
                        cloud_saves,
                        cloud_save_at: js_sys::Date::now(),
                    }))
                }),
            )
            .await;
        // Ready only once the runner is up: starting can still fail after the app is made.
        if let Some(bridge) = &bridge {
            match &result {
                Ok(()) => bridge.post_ready(),
                Err(e) => bridge.post_failed(&format!("{e:?}")),
            }
        }
        if let Some(el) = document.get_element_by_id(LOADING_ID) {
            match result {
                Ok(()) => el.remove(),
                Err(e) => el.set_inner_html(&format!("<p>Inkstone failed to start: {e:?}</p><p>A browser with WebGPU or WebGL2 is required.</p>")),
            }
        }
    });
}

fn query() -> String {
    web_sys::window().and_then(|w| w.location().search().ok()).unwrap_or_default()
}

fn query_param(key: &str) -> Option<String> {
    web_sys::UrlSearchParams::new_with_str(&query()).ok()?.get(key)
}

/// A wake that asks `ctx` for a frame; callable from any task.
fn waker(ctx: &egui::Context) -> impl Fn() + Send + Sync + Clone + 'static {
    let ctx = ctx.clone();
    move || ctx.request_repaint()
}

/// The clipboard's copy path: eframe's own text copy is the browser's only write.
fn copy_out(ctx: &egui::Context, flavors: &[Flavor]) -> Result<usize, ClipboardError> {
    let (index, text) = flavors.iter().enumerate().find(|(_, f)| f.mime == TEXT).ok_or(ClipboardError::NoFormat)?;
    ctx.copy_text(String::from_utf8_lossy(&text.data).into_owned());
    Ok(index)
}

/// Wraps the app to read dropped files asynchronously (browsers can't read them synchronously)
/// and feed them through the intake.
struct WebShell {
    app: WordApp,
    sender: IntakeSender,
    messages: Rc<RefCell<Vec<String>>>,
    settings: SharedSettings<Prefs>,
    /// Set when a host page named the user: its name is not kept in the stored preferences.
    author: Option<crate::prefs::HostAuthor>,
    cloud_saves: AsyncSaves,
    cloud_save_at: f64,
}

impl eframe::App for WebShell {
    /// Runs one browser frame: forwards queued host messages to the status bar, advances the app,
    /// then ticks the settings store, which writes changed preferences to browser storage at once
    /// (a reload or closed tab never runs an exit path). The browser has no window-close path.
    fn logic(&mut self, ctx: &egui::Context, _frame: &mut eframe::Frame) {
        let msgs = std::mem::take(&mut *self.messages.borrow_mut());
        for msg in msgs {
            self.app.status(msg);
        }
        self.app.logic(ctx);
        if self.app.services.cloud_save.is_some() {
            let now = js_sys::Date::now();
            let requested = crate::cloud::take_save_request();
            let automatic = self.app.session.dirty && crate::cloud::can_autosave() && now - self.cloud_save_at > 10_000.0;
            if (requested || automatic) && self.cloud_saves.pending() == 0 && self.app.has_document() {
                self.cloud_save_at = now;
                if let Err(error) = self.app.run("file.save", serde_json::json!({})) {
                    self.app.status(error);
                }
            }
            crate::cloud::state(
                self.app.has_unsaved_documents(),
                self.cloud_saves.pending() > 0,
                &self.app.session.document_id().to_string(),
            );
            ctx.request_repaint_after(std::time::Duration::from_millis(500));
        }
        if let Some(author) = &self.author {
            author.keep_own_author(&self.settings);
        }
        self.settings.lock().tick(web_time::Instant::now());
    }

    /// Takes the browser's dropped files once per frame, before any pass, and spawns asynchronous
    /// byte reads into the intake (browsers can't read them synchronously); then hands the runtime's
    /// output to the app and gives the coming passes the app's input.
    fn raw_input_hook(&mut self, ctx: &egui::Context, raw: &mut egui::RawInput) {
        for f in std::mem::take(&mut raw.dropped_files) {
            let sender = self.sender.clone();
            wasm_bindgen_futures::spawn_local(async move {
                let name = f.path().file_name().map(|n| n.to_string_lossy().to_string()).unwrap_or_else(|| "dropped".into());
                match f.bytes_async().await {
                    Ok(bytes) => {
                        let file = DroppedFile { path: None, name, bytes: Some(Arc::from(bytes)) };
                        sender.dropped(vec![file], DropTarget::Elsewhere);
                    }
                    Err(e) => log::error!("couldn't read dropped file {name}: {e}"),
                }
            });
        }
        embed::before_input(ctx, |out| self.app.frame_input(out));
    }

    /// Draws the app.
    fn ui(&mut self, ui: &mut egui::Ui, _frame: &mut eframe::Frame) {
        // No `embed::before_ui`: it fits and routes a native window, and the page's canvas is neither.
        self.app.ui(ui);
    }
}

/// Largest picture pasted from the browser's clipboard (as `insert.picture` allows).
const MAX_PASTED_BYTES: f64 = (200u32 << 20) as f64;
/// Most pictures one paste inserts.
const MAX_PASTED_PICTURES: u32 = 20;

/// Pasting a picture (#45): eframe's paste handler only reads text, so a clipboard holding just
/// pictures (a screenshot, a browser's Copy Image) is read here and offered to the clipboard as a
/// late paste; the app inserts it only when the page is the edit target (#1349). A clipboard with
/// text is left to eframe.
fn listen_for_pasted_pictures(late: LateOffers) -> Result<(), String> {
    let document = web_sys::window().and_then(|w| w.document()).ok_or("no document")?;
    let late = Rc::new(late);
    let on_paste = wasm_bindgen::closure::Closure::<dyn FnMut(web_sys::ClipboardEvent)>::new(move |e: web_sys::ClipboardEvent| {
        // The target is always eframe's hidden text input while the page has focus; the app decides (#1349).
        let Some(data) = e.clipboard_data() else { return };
        if !data.get_data("text").unwrap_or_default().is_empty() {
            return;
        }
        let Some(files) = data.files() else { return };
        for i in 0..files.length().min(MAX_PASTED_PICTURES) {
            let Some(file) = files.get(i) else { continue };
            if !file.type_().starts_with("image/") {
                continue;
            }
            if file.size() > MAX_PASTED_BYTES {
                log::warn!("pasted picture is larger than 200 MB; not inserted");
                continue;
            }
            let late = late.clone();
            let mime = file.type_();
            wasm_bindgen_futures::spawn_local(async move {
                match wasm_bindgen_futures::JsFuture::from(file.array_buffer()).await {
                    Ok(buf) => {
                        let bytes = js_sys::Uint8Array::new(&buf).to_vec();
                        late.offer(vec![Flavor::new(mime, bytes)]);
                    }
                    Err(e) => log::error!("couldn't read the pasted picture: {e:?}"),
                }
            });
        }
    });
    document.add_event_listener_with_callback("paste", on_paste.as_ref().unchecked_ref()).map_err(|e| format!("{e:?}"))?;
    // The listener lives as long as the page.
    on_paste.forget();
    Ok(())
}

/// `dirty` is the flag the `beforeunload` guard reads ([`guard_unload`]).
fn services(
    host: &Host,
    ctx: egui::Context,
    dirty: Rc<Cell<bool>>,
    messages: Rc<RefCell<Vec<String>>>,
    intake: FileIntake,
    cloud_saves: Option<AsyncSaves>,
) -> Services {
    let font_ctx = ctx.clone();
    let font_messages = messages;
    let cloud_ctx = ctx;
    Services {
        print: Some(Spool::new(host.jobs_shared().clone())),
        intake: Some(intake),
        on_dirty: Some(Box::new(move |d| dirty.set(d))),
        store_font: Some(Box::new(font_store::store)),
        forget_font: Some(Box::new(font_store::forget)),
        load_system_fonts: Some(Box::new(move || {
            load_system_fonts(font_ctx, font_messages.clone());
        })),
        cloud_save: cloud_saves.as_ref().map(|saves| {
            let saves = saves.clone();
            let ctx = cloud_ctx.clone();
            Box::new(move |bytes, generation, title| crate::cloud::save(bytes, generation, title, &saves, &ctx)) as _
        }),
        async_saves: cloud_saves,
        ..Default::default()
    }
}

/// Most local fonts one Load System Installed Fonts installs (a designer's machine has thousands).
const MAX_SYSTEM_FONTS: usize = 500;
/// Most font bytes one Load System Installed Fonts reads into memory (wasm memory is small).
const MAX_SYSTEM_FONT_BYTES: usize = 256 << 20;
/// Most local fonts looked at, installed or not (already available, unreadable, too big).
const MAX_SYSTEM_FONTS_TRIED: u32 = 4000;

/// Query the browser's Local Font Access API (`window.queryLocalFonts()`) and install the faces
/// into the global font database, up to [`MAX_SYSTEM_FONTS`] faces and [`MAX_SYSTEM_FONT_BYTES`].
fn load_system_fonts(ctx: egui::Context, messages: Rc<RefCell<Vec<String>>>) {
    wasm_bindgen_futures::spawn_local(async move {
        let Some(window) = web_sys::window() else {
            return;
        };
        let query_fn = match js_sys::Reflect::get(&window, &wasm_bindgen::JsValue::from_str("queryLocalFonts")) {
            Ok(val) if val.is_function() => js_sys::Function::from(val),
            _ => {
                messages.borrow_mut().push(inkstone_ui_egui::i18n::t("Local font access is not supported by your browser.").to_string());
                ctx.request_repaint();
                return;
            }
        };
        let fonts_promise = match query_fn.call0(&window) {
            Ok(p) => js_sys::Promise::from(p),
            Err(e) => {
                log::warn!("queryLocalFonts call failed: {e:?}");
                messages.borrow_mut().push(inkstone_ui_egui::i18n::t("Local font access was denied.").to_string());
                ctx.request_repaint();
                return;
            }
        };
        let fonts_val = match wasm_bindgen_futures::JsFuture::from(fonts_promise).await {
            Ok(val) => val,
            Err(e) => {
                log::warn!("queryLocalFonts promise rejected: {e:?}");
                messages.borrow_mut().push(inkstone_ui_egui::i18n::t("Local font access was denied.").to_string());
                ctx.request_repaint();
                return;
            }
        };
        let font_list = js_sys::Array::from(&fonts_val);
        let len = font_list.length();
        let mut loaded = 0usize;
        let mut total_bytes = 0usize;
        for i in 0..len.min(MAX_SYSTEM_FONTS_TRIED) {
            if loaded >= MAX_SYSTEM_FONTS || total_bytes >= MAX_SYSTEM_FONT_BYTES {
                log::warn!("local fonts: stopped after {loaded} fonts ({total_bytes} bytes)");
                break;
            }
            let font_data = font_list.get(i);
            let Ok(blob_fn) = js_sys::Reflect::get(&font_data, &wasm_bindgen::JsValue::from_str("blob")) else {
                continue;
            };
            if !blob_fn.is_function() {
                continue;
            }
            let Ok(blob_val) = js_sys::Function::from(blob_fn).call0(&font_data) else {
                continue;
            };
            let Ok(blob_js) = wasm_bindgen_futures::JsFuture::from(js_sys::Promise::from(blob_val)).await else {
                continue;
            };
            let Ok(blob) = blob_js.dyn_into::<web_sys::Blob>() else {
                continue;
            };
            // Skip a face too big on its own or for what is left of the budget, before reading it.
            let size = blob.size().clamp(0.0, usize::MAX as f64) as usize;
            if size > inkstone_fonts::MAX_FONT_FILE_BYTES || total_bytes.saturating_add(size) > MAX_SYSTEM_FONT_BYTES {
                continue;
            }
            let Ok(array_buffer) = wasm_bindgen_futures::JsFuture::from(blob.array_buffer()).await else {
                continue;
            };
            let bytes = js_sys::Uint8Array::new(&array_buffer).to_vec();
            if bytes.len() > inkstone_fonts::MAX_FONT_FILE_BYTES {
                continue;
            }
            total_bytes = total_bytes.saturating_add(bytes.len());
            let ps_name = js_sys::Reflect::get(&font_data, &wasm_bindgen::JsValue::from_str("postscriptName"))
                .ok()
                .and_then(|v| v.as_string())
                .unwrap_or_else(|| format!("local-font-{i}"));
            let name = format!("{ps_name}.ttf");
            match inkstone_fonts::FontDb::global().install_font(&name, &bytes) {
                Ok(_) => loaded += 1,
                Err(inkstone_fonts::UserFontError::AlreadyAvailable(_)) => {}
                Err(e) => log::warn!("local font {name} skipped: {e}"),
            }
        }
        if loaded > 0 {
            let msg = inkstone_ui_egui::i18n::fmt(inkstone_ui_egui::i18n::t("Loaded {count} system fonts."), &[("count", &loaded.to_string())]);
            messages.borrow_mut().push(msg);
        } else {
            messages.borrow_mut().push(inkstone_ui_egui::i18n::t("All system fonts are already available.").to_string());
        }
        ctx.request_repaint();
    });
}

/// Closing, reloading or leaving the tab with unsaved changes asks first. Browsers show their own
/// confirmation (a page can only ask for it), so there is no Save button: Cancel, then File › Save.
fn guard_unload(dirty: Rc<Cell<bool>>, files: Arc<BrowserFiles>) -> Result<(), String> {
    let window = web_sys::window().ok_or("no window")?;
    let on_unload = wasm_bindgen::closure::Closure::<dyn FnMut(web_sys::BeforeUnloadEvent)>::new(move |e: web_sys::BeforeUnloadEvent| {
        // A save still in the browser's picker hasn't written anything yet.
        if dirty.get() || files.pick_pending() {
            e.prevent_default();
            // Older browsers ask only when `returnValue` is set.
            e.set_return_value("unsaved changes");
        }
    });
    window.add_event_listener_with_callback("beforeunload", on_unload.as_ref().unchecked_ref()).map_err(|e| format!("{e:?}"))?;
    // The listener lives as long as the page.
    on_unload.forget();
    Ok(())
}
