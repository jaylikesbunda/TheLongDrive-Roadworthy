pub type Version = (u128, u128, u128);

/// Strict numeric X.Y.Z only; anything else is an unknown version.
pub fn version_tuple(value: &str) -> Option<Version> {
    let parts: Vec<&str> = value.split('.').collect();
    if parts.len() != 3 || parts.iter().any(|p| p.is_empty() || !p.bytes().all(|b| b.is_ascii_digit())) {
        return None;
    }
    let n: Vec<u128> = parts.iter().map(|p| p.parse().ok()).collect::<Option<_>>()?;
    Some((n[0], n[1], n[2]))
}

pub fn version_string(v: Version) -> String {
    format!("{}.{}.{}", v.0, v.1, v.2)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn strict_triples_only() {
        assert_eq!(version_tuple("0.2.10"), Some((0, 2, 10)));
        assert_eq!(version_tuple("007.1.2"), Some((7, 1, 2)));
        for bad in ["", "1.2", "1.2.3.4", "1.2.x", "v1.2.3", "1..3", "1.2.3-beta", " 1.2.3", "1.2.-3", "١.٢.٣"] {
            assert_eq!(version_tuple(bad), None, "{bad}");
        }
    }

    #[test]
    fn numeric_not_lexical_ordering() {
        assert!(version_tuple("0.2.10") > version_tuple("0.2.9"));
    }
}
