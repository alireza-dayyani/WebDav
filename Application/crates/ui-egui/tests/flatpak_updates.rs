//! Source and release Flatpak manifests keep the same finish-args. Inkstone has no in-app updater,
//! so network and Flatpak session access are not required by default.
fn finish_args(manifest: &str) -> Vec<&str> {
    manifest
        .split("finish-args:")
        .nth(1)
        .unwrap_or_default()
        .lines()
        .take_while(|line| line.is_empty() || line.starts_with(' ') || line.starts_with('#'))
        .filter_map(|line| line.trim().strip_prefix("- "))
        .map(|arg| arg.trim_matches(['\'', '"']))
        .collect()
}

#[test]
fn flatpak_manifest_finish_args_match_and_skip_builtin_updates() {
    let manifests = [
        include_str!("../../../packaging/linux/flatpak/ai.storyteller.inkstone.bundle.yml"),
        include_str!("../../../packaging/linux/flatpak/ai.storyteller.inkstone.yml"),
    ];
    let args = manifests.map(finish_args);
    assert_eq!(args[0], args[1], "source and release permissions must agree");
    for permissions in args {
        for forbidden in ["--share=network", "--talk-name=org.freedesktop.Flatpak"] {
            assert!(!permissions.contains(&forbidden), "Inkstone flatpak must not enable built-in updates: {forbidden}");
        }
    }
}
