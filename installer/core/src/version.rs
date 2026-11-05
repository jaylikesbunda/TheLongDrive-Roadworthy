/// (major, minor, patch, rank, pre): rank 1 marks a final X.Y.Z release, which
/// ranks above every -preN of the same X.Y.Z; rank 0 marks a -preN prerelease,
/// where pre is N.
pub type Version = (u128, u128, u128, u8, u128);

/// Strict X.Y.Z or X.Y.Z-preN only; anything else is an unknown version.
pub fn version_tuple(value: &str) -> Option<Version> {
    let (number, pre) = match value.split_once("-pre") {
        Some((number, pre)) => (number, Some(pre)),
        None => (value, None),
    };
    let parts: Vec<&str> = number.split('.').collect();
    if parts.len() != 3 || parts.iter().any(|p| p.is_empty() || !p.bytes().all(|b| b.is_ascii_digit())) {
        return None;
    }
    let n: Vec<u128> = parts.iter().map(|p| p.parse().ok()).collect::<Option<_>>()?;
    let (rank, pre) = match pre {
        None => (1, 0),
        Some(pre) if !pre.is_empty() && pre.bytes().all(|b| b.is_ascii_digit()) => (0, pre.parse().ok()?),
        Some(_) => return None,
    };
    Some((n[0], n[1], n[2], rank, pre))
}

pub fn version_string(v: Version) -> String {
    let (major, minor, patch, rank, pre) = v;
    if rank == 0 {
        format!("{major}.{minor}.{patch}-pre{pre}")
    } else {
        format!("{major}.{minor}.{patch}")
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn strict_triples_and_pre_suffixes_only() {
        assert_eq!(version_tuple("0.2.10"), Some((0, 2, 10, 1, 0)));
        assert_eq!(version_tuple("007.1.2"), Some((7, 1, 2, 1, 0)));
        assert_eq!(version_tuple("0.2.0-pre1"), Some((0, 2, 0, 0, 1)));
        for bad in ["", "1.2", "1.2.3.4", "1.2.x", "v1.2.3", "1..3", "1.2.3-beta", " 1.2.3", "1.2.-3", "١.٢.٣"] {
            assert_eq!(version_tuple(bad), None, "{bad}");
        }
    }

    #[test]
    fn numeric_not_lexical_ordering() {
        assert!(version_tuple("0.2.10") > version_tuple("0.2.9"));
    }

    #[test]
    fn pre_releases_rank_below_their_release() {
        assert!(version_tuple("0.2.0-pre1") < version_tuple("0.2.0-pre2"));
        assert!(version_tuple("0.2.0-pre2") < version_tuple("0.2.0"));
        assert!(version_tuple("0.2.1-pre9") > version_tuple("0.2.0"));
    }

    #[test]
    fn version_string_round_trips_pre_suffixes() {
        assert_eq!(version_string(version_tuple("0.2.0-pre1").unwrap()), "0.2.0-pre1");
        assert_eq!(version_string(version_tuple("007.1.2").unwrap()), "7.1.2");
    }
}
