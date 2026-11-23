//! Interface direction helpers (craft-libs removed `craft_ui::direction` from the UI crate pin).

use egui::{Align, Align2, AsIdSalt, Context, Id, Panel, Rect, Ui};

pub use craft::i18n::Direction;

fn key() -> Id {
    Id::unique("craft_ui::direction")
}

/// Draw the interface on `ctx` in `dir` from the next layout on.
pub fn apply(ctx: &Context, dir: Direction) {
    ctx.data_mut(|d| d.insert_temp(key(), dir));
    ctx.all_styles_mut(|style| style.set_right_to_left(dir == Direction::Rtl));
}

#[allow(dead_code, reason = "unused when egui has the inherent method")]
trait SetRightToLeft {
    fn set_right_to_left(&mut self, _right_to_left: bool) {}
}

impl SetRightToLeft for egui::Style {}

/// Anything the interface direction can be read from.
pub trait HasDirection {
    fn direction(&self) -> Direction;
}

impl HasDirection for Direction {
    fn direction(&self) -> Direction {
        *self
    }
}

impl HasDirection for Context {
    fn direction(&self) -> Direction {
        self.data(|d| d.get_temp(key())).unwrap_or(Direction::Ltr)
    }
}

impl HasDirection for Ui {
    fn direction(&self) -> Direction {
        self.ctx().direction()
    }
}

fn is_rtl(d: &impl HasDirection) -> bool {
    d.direction() == Direction::Rtl
}

pub fn start_panel(d: &impl HasDirection, id_salt: impl AsIdSalt) -> Panel {
    if is_rtl(d) { Panel::right(id_salt) } else { Panel::left(id_salt) }
}

pub fn end_panel(d: &impl HasDirection, id_salt: impl AsIdSalt) -> Panel {
    if is_rtl(d) { Panel::left(id_salt) } else { Panel::right(id_salt) }
}

pub fn start_x(d: &impl HasDirection, rect: Rect) -> f32 {
    if is_rtl(d) { rect.right() } else { rect.left() }
}

pub fn forward(d: &impl HasDirection) -> f32 {
    if is_rtl(d) { -1.0 } else { 1.0 }
}

pub fn align2(d: &impl HasDirection, ltr: Align2) -> Align2 {
    if is_rtl(d) { ltr.flipped_x() } else { ltr }
}

pub fn start_align(d: &impl HasDirection) -> Align {
    if is_rtl(d) { Align::Max } else { Align::Min }
}