use crate::consts::{RELEASE_PAGE, UPDATES_API, VERSION};
use crate::error::{err, InstallerError, Result};
use crate::version::{version_string, version_tuple, Version};
use serde_json::Value;
use std::io::Read;
use std::time::Duration;

const LIMIT: usize = 2 * 1024 * 1024;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct UpdateInfo {
    pub version: Option<String>,
    pub tag: Option<String>,
    pub newer: bool,
    pub url: Option<String>,
}

pub fn http_fetch(url: &str) -> std::result::Result<Vec<u8>, String> {
    let tls = native_tls::TlsConnector::new().map_err(|e| e.to_string())?;
    let agent = ureq::AgentBuilder::new().timeout(Duration::from_secs(5)).tls_connector(std::sync::Arc::new(tls)).build();
    let response = agent
        .get(url)
        .set("Accept", "application/vnd.github+json")
        .set("User-Agent", &format!("Roadworthy-Installer/{}", VERSION))
        .call()
        .map_err(|e| e.to_string())?;
    let mut data = Vec::new();
    response.into_reader().take(LIMIT as u64 + 1).read_to_end(&mut data).map_err(|e| e.to_string())?;
    Ok(data)
}

fn truthy(value: Option<&Value>) -> bool {
    match value {
        None | Some(Value::Null) | Some(Value::Bool(false)) => false,
        Some(Value::String(s)) => !s.is_empty(),
        Some(Value::Number(n)) => n.as_f64() != Some(0.0),
        Some(Value::Array(a)) => !a.is_empty(),
        Some(Value::Object(o)) => !o.is_empty(),
        Some(Value::Bool(true)) => true,
    }
}

/// Python urllib.parse.quote(tag, safe="").
pub fn quote(text: &str) -> String {
    let mut out = String::new();
    for b in text.bytes() {
        if b.is_ascii_alphanumeric() || b"_.-~".contains(&b) {
            out.push(b as char);
        } else {
            out.push_str(&format!("%{:02X}", b));
        }
    }
    out
}

pub fn check_updates() -> Result<UpdateInfo> {
    check_updates_with(VERSION, &http_fetch)
}

pub fn check_updates_with(bundled: &str, fetch: &dyn Fn(&str) -> std::result::Result<Vec<u8>, String>) -> Result<UpdateInfo> {
    let bundled = version_tuple(bundled);
    let mut best: Option<(Version, String)> = None;
    for page in 1..=3 {
        let url = UPDATES_API.replace("{}", &page.to_string());
        let data = fetch(&url).map_err(|e| InstallerError(format!("Could not check for newer releases: {e}")))?;
        if data.len() > LIMIT {
            return err("The release list response was too large; refusing it.");
        }
        let releases: Value = serde_json::from_slice(&data).map_err(|_| InstallerError("The release list was not valid JSON.".into()))?;
        let Value::Array(releases) = releases else {
            return err("The release list had an unexpected shape.");
        };
        for entry in &releases {
            let Value::Object(map) = entry else { continue };
            if truthy(map.get("draft")) || !truthy(map.get("published_at")) {
                continue;
            }
            let Some(tag) = map.get("tag_name").and_then(Value::as_str) else { continue };
            let stripped = if tag.to_lowercase().starts_with('v') { &tag[1..] } else { tag };
            let Some(candidate) = version_tuple(stripped) else { continue };
            if best.as_ref().is_none_or(|(b, _)| candidate > *b) {
                best = Some((candidate, tag.to_string()));
            }
        }
        if releases.len() < 100 {
            break;
        }
    }
    let Some((best, tag)) = best else {
        return Ok(UpdateInfo { version: None, tag: None, newer: false, url: None });
    };
    let newer = bundled.is_some_and(|b| best > b);
    Ok(UpdateInfo { version: Some(version_string(best)), url: Some(format!("{}{}", RELEASE_PAGE, quote(&tag))), tag: Some(tag), newer })
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;
    use std::cell::RefCell;

    fn release(tag: &str, draft: bool, published: &str) -> Value {
        json!({"tag_name": tag, "draft": draft, "prerelease": true, "published_at": published, "html_url": "https://evil.example/x", "assets": []})
    }

    fn check(pages: Vec<Value>) -> (Result<UpdateInfo>, Vec<String>) {
        let calls = RefCell::new(Vec::new());
        let result = check_updates_with("0.2.0", &|url| {
            calls.borrow_mut().push(url.to_string());
            let page: usize = url.rsplit("page=").next().unwrap().parse().unwrap();
            Ok(serde_json::to_vec(pages.get(page - 1).unwrap_or(&json!([]))).unwrap())
        });
        (result, calls.into_inner())
    }

    #[test]
    fn numeric_max_wins_including_prerelease_and_url_is_built_locally() {
        let (r, _) = check(vec![json!([release("v0.2.9", false, "2026-01-01T00:00:00Z"), release("v0.2.10", false, "2026-01-01T00:00:00Z"), release("0.1.2", false, "2026-01-01T00:00:00Z")])]);
        let r = r.unwrap();
        assert_eq!(r.version.as_deref(), Some("0.2.10"));
        assert!(r.newer);
        assert_eq!(r.url.as_deref(), Some(&*format!("{}v0.2.10", RELEASE_PAGE)));
    }

    #[test]
    fn older_equal_drafts_and_unpublished_are_not_newer() {
        let (r, _) = check(vec![json!([release("v0.1.2", false, "2026-01-01T00:00:00Z"), release("v9.9.9", true, "2026-01-01T00:00:00Z"), release("v8.8.8", false, "")])]);
        let r = r.unwrap();
        assert!(!r.newer);
        assert_eq!(r.version.as_deref(), Some("0.1.2"));
        let (r, _) = check(vec![json!([release("v0.2.0", false, "2026-01-01T00:00:00Z")])]);
        assert!(!r.unwrap().newer);
    }

    #[test]
    fn no_valid_release_reports_none() {
        let (r, _) = check(vec![json!([release("nightly", false, "2026-01-01T00:00:00Z"), json!("junk")])]);
        assert_eq!(r.unwrap(), UpdateInfo { version: None, tag: None, newer: false, url: None });
    }

    #[test]
    fn pre_suffixes_parse_and_rank_below_the_release() {
        let fetch = |tags: Vec<&str>| {
            let page = Value::Array(tags.iter().map(|t| release(t, false, "2026-01-01T00:00:00Z")).collect());
            check_updates_with("0.2.0-pre1", &move |_| Ok(serde_json::to_vec(&page).unwrap()))
        };
        let r = fetch(vec!["v0.2.0-pre1"]).unwrap();
        assert_eq!(r.version.as_deref(), Some("0.2.0-pre1"));
        assert!(!r.newer);
        let r = fetch(vec!["v0.2.0-pre1", "v0.2.0-pre2"]).unwrap();
        assert_eq!(r.version.as_deref(), Some("0.2.0-pre2"));
        assert!(r.newer);
        let r = fetch(vec!["v0.2.0-pre2", "v0.2.0"]).unwrap();
        assert_eq!(r.version.as_deref(), Some("0.2.0"));
        assert!(r.newer);
    }

    #[test]
    fn short_page_stops_and_full_pages_fetch_up_to_three() {
        let (_, calls) = check(vec![json!([release("v0.1.0", false, "x")])]);
        assert_eq!(calls.len(), 1);
        let full = |tag: &str| Value::Array((0..100).map(|_| release(tag, false, "x")).collect());
        let (_, calls) = check(vec![full("v0.1.0"), full("v0.1.1"), full("v0.1.2"), full("v0.1.3")]);
        assert_eq!(calls.len(), 3);
        assert!(calls[2].ends_with("page=3"));
    }

    #[test]
    fn failures_use_original_messages() {
        let e = check_updates_with("0.2.0", &|_| Err("offline".into())).unwrap_err();
        assert_eq!(e.0, "Could not check for newer releases: offline");
        let e = check_updates_with("0.2.0", &|_| Ok(b"not json".to_vec())).unwrap_err();
        assert_eq!(e.0, "The release list was not valid JSON.");
        let e = check_updates_with("0.2.0", &|_| Ok(b"{}".to_vec())).unwrap_err();
        assert_eq!(e.0, "The release list had an unexpected shape.");
        let e = check_updates_with("0.2.0", &|_| Ok(vec![b' '; LIMIT + 1])).unwrap_err();
        assert_eq!(e.0, "The release list response was too large; refusing it.");
    }

    #[test]
    fn quote_matches_python_with_empty_safe_set() {
        assert_eq!(quote("v0.2.0-beta_1~x"), "v0.2.0-beta_1~x");
        assert_eq!(quote("v1/2 3"), "v1%2F2%203");
    }
}
