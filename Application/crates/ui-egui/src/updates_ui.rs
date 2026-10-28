//! Update information for the self-hosted Inkstone distribution.
use crate::WordApp;

#[derive(Default)]
pub(crate) struct Runtime {
    pub(crate) manual_window_open: bool,
}

impl WordApp {
    /// Updates are supplied by the operator of this Inkstone installation.
    #[cfg(not(target_arch = "wasm32"))]
    pub fn set_startup_update_check(&mut self, _on: bool) {}
}

pub(crate) fn check(app: &mut WordApp) {
    app.updates_rt.manual_window_open = true;
}

pub(crate) fn tick(_app: &mut WordApp, _ctx: &egui::Context) {}

pub(crate) fn show(app: &mut WordApp, ctx: &egui::Context) {
    if app.updates_rt.manual_window_open {
        let mut open = true;
        let mut close = false;
        egui::Window::new(tl!("Check for Updates"))
            .open(&mut open)
            .resizable(false)
            .collapsible(false)
            .anchor(egui::Align2::CENTER_CENTER, egui::vec2(0.0, 0.0))
            .show(ctx, |ui| {
                ui.label(tl!("Updates are managed by the operator of this Inkstone installation."));
                if ui.button(tl!("Close")).clicked() {
                    close = true;
                }
            });
        if !open || close {
            app.updates_rt.manual_window_open = false;
        }
    }
}
