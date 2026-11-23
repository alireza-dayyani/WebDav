//! The system region new documents follow (#838): the paper File › New starts on, the default
//! measurement unit and the decimal separator.
//!
//! `INKSTONE_REGION` names a region outright; otherwise it is the system's regional format,
//! read once for desktop and web by [`inkstone_ui_egui::i18n::system_region`].

/// The system region.
pub fn system() -> inkstone_engine::Region {
    let named = std::env::var("INKSTONE_REGION").ok().filter(|v| !v.trim().is_empty());
    // Runs without preferences (agents' and screenshot runs) behave the same on every machine:
    // en-US unless `INKSTONE_REGION` says otherwise.
    if std::env::var_os("INKSTONE_NO_PREFS").is_some() && named.is_none() {
        return inkstone_engine::Region::default();
    }
    if let Some(tag) = named {
        return inkstone_engine::Region::from_tag(&tag);
    }
    inkstone_ui_egui::i18n::system_region()
}
