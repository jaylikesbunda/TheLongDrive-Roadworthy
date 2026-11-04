use crate::consts::APP_ID;
use crate::error::Result;
use crate::util::{is_relative_to, resolve, unique_paths};
use crate::vdf::{parse_vdf, Node};
use std::path::{Path, PathBuf};

pub fn game_assembly(game: &Path) -> PathBuf {
    game.join("TheLongDrive_Data").join("Managed").join("Assembly-CSharp.dll")
}

pub fn managed_dir(game: &Path) -> PathBuf {
    game.join("TheLongDrive_Data").join("Managed")
}

pub fn valid_game(game: &Path) -> bool {
    game.join("TheLongDrive.exe").is_file() && game_assembly(game).is_file()
}

#[cfg(windows)]
fn registry_roots(candidates: &mut Vec<PathBuf>) {
    use winreg::enums::{HKEY_CURRENT_USER, HKEY_LOCAL_MACHINE, KEY_READ, KEY_WOW64_32KEY, KEY_WOW64_64KEY};
    use winreg::RegKey;
    for (hive, value) in [(HKEY_CURRENT_USER, "SteamPath"), (HKEY_LOCAL_MACHINE, "InstallPath")] {
        for view in [0, KEY_WOW64_32KEY, KEY_WOW64_64KEY] {
            if let Ok(key) = RegKey::predef(hive).open_subkey_with_flags(r"Software\Valve\Steam", KEY_READ | view) {
                if let Ok(path) = key.get_value::<String, _>(value) {
                    candidates.push(PathBuf::from(path));
                }
            }
        }
    }
}

#[cfg(not(windows))]
fn registry_roots(_: &mut Vec<PathBuf>) {}

pub fn steam_roots() -> Vec<PathBuf> {
    let mut candidates = Vec::new();
    registry_roots(&mut candidates);
    for name in ["ProgramFiles(x86)", "ProgramFiles"] {
        if let Some(value) = std::env::var_os(name).filter(|v| !v.is_empty()) {
            candidates.push(PathBuf::from(value).join("Steam"));
        }
    }
    unique_paths(candidates)
}

fn read_text(path: &Path) -> Option<String> {
    let bytes = std::fs::read(path).ok()?;
    let text = String::from_utf8(bytes).ok()?;
    Some(text.strip_prefix('\u{feff}').map(str::to_owned).unwrap_or(text))
}

pub fn discover_games(roots: Option<&[PathBuf]>) -> Result<Vec<PathBuf>> {
    let default_roots;
    let roots = match roots {
        Some(r) => r,
        None => {
            default_roots = steam_roots();
            &default_roots
        }
    };
    let mut libraries: Vec<PathBuf> = Vec::new();
    for steam in roots {
        libraries.push(steam.clone());
        let manifest = steam.join("steamapps").join("libraryfolders.vdf");
        if !manifest.is_file() {
            continue;
        }
        let Some(text) = read_text(&manifest) else { continue };
        let Ok(root) = parse_vdf(&text) else { continue };
        let Some(entries) = root.get_section("libraryfolders") else { continue };
        for (key, entry) in &entries.0 {
            match entry {
                Node::Section(s) if s.get_str("path").is_some() => libraries.push(PathBuf::from(s.get_str("path").unwrap())),
                Node::Value(v) if !key.is_empty() && key.chars().all(|c| c.is_ascii_digit()) => libraries.push(PathBuf::from(v)),
                _ => {}
            }
        }
    }
    let mut games = Vec::new();
    for library in unique_paths(libraries) {
        let manifest = library.join("steamapps").join(format!("appmanifest_{}.acf", APP_ID));
        let Some(text) = read_text(&manifest) else { continue };
        let Ok(root) = parse_vdf(&text) else { continue };
        let Some(data) = root.get_section("AppState") else { continue };
        if data.get_str("appid") != Some(APP_ID) {
            continue;
        }
        let Some(install_dir) = data.get_str("installdir") else { continue };
        let common = resolve(&library.join("steamapps").join("common"));
        let game = resolve(&common.join(install_dir));
        if !is_relative_to(&game, &common) {
            continue;
        }
        if valid_game(&game) {
            games.push(game);
        }
    }
    Ok(unique_paths(games))
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::fs;

    fn fake_game(library: &Path, dir: &str) -> PathBuf {
        let game = library.join("steamapps").join("common").join(dir);
        fs::create_dir_all(game.join("TheLongDrive_Data").join("Managed")).unwrap();
        fs::write(game.join("TheLongDrive.exe"), b"x").unwrap();
        fs::write(game_assembly(&game), b"x").unwrap();
        game
    }

    fn manifest(library: &Path, installdir: &str, appid: &str) {
        let steamapps = library.join("steamapps");
        fs::create_dir_all(&steamapps).unwrap();
        fs::write(
            steamapps.join(format!("appmanifest_{}.acf", APP_ID)),
            format!("\"AppState\"\n{{\n\t\"appid\"\t\"{appid}\"\n\t\"installdir\"\t\"{installdir}\"\n}}\n"),
        )
        .unwrap();
    }

    #[test]
    fn finds_game_in_steam_root_and_secondary_library() {
        let dir = tempfile::tempdir().unwrap();
        let steam = dir.path().join("Steam");
        let other = dir.path().join("Library 2");
        fake_game(&other, "The Long Drive");
        manifest(&other, "The Long Drive", APP_ID);
        fs::create_dir_all(steam.join("steamapps")).unwrap();
        let other_text = other.to_string_lossy().replace('\\', "\\\\");
        fs::write(
            steam.join("steamapps").join("libraryfolders.vdf"),
            format!("\"libraryfolders\"\n{{\n\t\"0\"\n\t{{\n\t\t\"path\"\t\"{other_text}\"\n\t}}\n}}\n"),
        )
        .unwrap();
        let games = discover_games(Some(&[steam])).unwrap();
        assert_eq!(games, vec![resolve(&other.join("steamapps").join("common").join("The Long Drive"))]);
    }

    #[test]
    fn legacy_numeric_string_entries_are_libraries() {
        let dir = tempfile::tempdir().unwrap();
        let steam = dir.path().join("Steam");
        let other = dir.path().join("Old");
        fake_game(&other, "TLD");
        manifest(&other, "TLD", APP_ID);
        fs::create_dir_all(steam.join("steamapps")).unwrap();
        let other_text = other.to_string_lossy().replace('\\', "\\\\");
        fs::write(steam.join("steamapps").join("libraryfolders.vdf"), format!("\"libraryfolders\" {{ \"1\" \"{other_text}\" }}")).unwrap();
        assert_eq!(discover_games(Some(&[steam])).unwrap().len(), 1);
    }

    #[test]
    fn rejects_wrong_appid_escaping_installdir_and_incomplete_games() {
        let dir = tempfile::tempdir().unwrap();
        let library = dir.path().join("Lib");
        fake_game(&library, "Game");
        manifest(&library, "Game", "999");
        assert!(discover_games(Some(std::slice::from_ref(&library))).unwrap().is_empty());
        manifest(&library, r"..\Escape", APP_ID);
        assert!(discover_games(Some(std::slice::from_ref(&library))).unwrap().is_empty());
        manifest(&library, "Missing", APP_ID);
        assert!(discover_games(Some(std::slice::from_ref(&library))).unwrap().is_empty());
        manifest(&library, "Game", APP_ID);
        assert_eq!(discover_games(Some(&[library])).unwrap().len(), 1);
    }

    #[test]
    fn malformed_library_manifest_is_skipped_not_fatal() {
        let dir = tempfile::tempdir().unwrap();
        let steam = dir.path().join("Steam");
        fake_game(&steam, "Game");
        manifest(&steam, "Game", APP_ID);
        fs::write(steam.join("steamapps").join("libraryfolders.vdf"), "\"libraryfolders\" {").unwrap();
        assert_eq!(discover_games(Some(&[steam])).unwrap().len(), 1);
    }
}
