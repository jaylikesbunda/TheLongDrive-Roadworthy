use crate::error::{err, Result};

#[derive(Debug, Clone, PartialEq)]
pub enum Node {
    Value(String),
    Section(Section),
}

/// Insertion-ordered map; duplicate keys overwrite in place, like a Python dict.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct Section(pub Vec<(String, Node)>);

impl Section {
    pub fn get(&self, key: &str) -> Option<&Node> {
        self.0.iter().find(|(k, _)| k == key).map(|(_, v)| v)
    }
    pub fn get_str(&self, key: &str) -> Option<&str> {
        match self.get(key) {
            Some(Node::Value(s)) => Some(s),
            _ => None,
        }
    }
    pub fn get_section(&self, key: &str) -> Option<&Section> {
        match self.get(key) {
            Some(Node::Section(s)) => Some(s),
            _ => None,
        }
    }
    fn set(&mut self, key: String, value: Node) {
        if let Some(slot) = self.0.iter_mut().find(|(k, _)| *k == key) {
            slot.1 = value;
        } else {
            self.0.push((key, value));
        }
    }
}

#[derive(Debug, PartialEq)]
enum Token {
    Value(String),
    Open,
    Close,
}

fn tokenize(text: &str) -> Result<Vec<Token>> {
    let chars: Vec<char> = text.chars().collect();
    let mut tokens = Vec::new();
    let mut i = 0;
    while i < chars.len() {
        let c = chars[i];
        if c.is_whitespace() {
            while i < chars.len() && chars[i].is_whitespace() {
                i += 1;
            }
        } else if c == '/' && chars.get(i + 1) == Some(&'/') {
            while i < chars.len() && chars[i] != '\r' && chars[i] != '\n' {
                i += 1;
            }
        } else if c == '"' {
            let mut j = i + 1;
            let mut raw = String::new();
            let mut closed = false;
            while j < chars.len() {
                let d = chars[j];
                if d == '"' {
                    closed = true;
                    j += 1;
                    break;
                }
                if d == '\\' {
                    match chars.get(j + 1) {
                        Some(&n) if n != '\n' => {
                            raw.push('\\');
                            raw.push(n);
                            j += 2;
                        }
                        _ => return err("Malformed Steam library manifest."),
                    }
                } else {
                    raw.push(d);
                    j += 1;
                }
            }
            if !closed {
                return err("Malformed Steam library manifest.");
            }
            tokens.push(Token::Value(unescape(&raw)));
            i = j;
        } else if c == '{' {
            tokens.push(Token::Open);
            i += 1;
        } else if c == '}' {
            tokens.push(Token::Close);
            i += 1;
        } else {
            return err("Malformed Steam library manifest.");
        }
    }
    Ok(tokens)
}

fn unescape(raw: &str) -> String {
    let chars: Vec<char> = raw.chars().collect();
    let mut out = String::new();
    let mut i = 0;
    while i < chars.len() {
        if chars[i] == '\\' && i + 1 < chars.len() && (chars[i + 1] == '"' || chars[i + 1] == '\\') {
            out.push(chars[i + 1]);
            i += 2;
        } else {
            out.push(chars[i]);
            i += 1;
        }
    }
    out
}

fn section(tokens: &[Token], index: &mut usize, nested: bool) -> Result<Section> {
    let mut result = Section::default();
    while *index < tokens.len() {
        let key = &tokens[*index];
        *index += 1;
        let key = match key {
            Token::Close => {
                if !nested {
                    return err("Unbalanced Steam manifest.");
                }
                return Ok(result);
            }
            Token::Value(k) if *index < tokens.len() => k.clone(),
            _ => return err("Malformed Steam manifest entry."),
        };
        let value = &tokens[*index];
        *index += 1;
        match value {
            Token::Open => result.set(key, Node::Section(section(tokens, index, true)?)),
            Token::Value(v) => result.set(key, Node::Value(v.clone())),
            Token::Close => return err("Malformed Steam manifest value."),
        }
    }
    if nested {
        return err("Unclosed Steam manifest section.");
    }
    Ok(result)
}

pub fn parse_vdf(text: &str) -> Result<Section> {
    let tokens = tokenize(text)?;
    let mut index = 0;
    section(&tokens, &mut index, false)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_nested_sections_and_escapes() {
        let text = "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\"D:\\\\Steam Library\"\n\t}\n}\n";
        let root = parse_vdf(text).unwrap();
        let path = root.get_section("libraryfolders").unwrap().get_section("0").unwrap().get_str("path").unwrap();
        assert_eq!(path, r"D:\Steam Library");
    }

    #[test]
    fn keeps_unknown_escapes_and_handles_quotes() {
        let root = parse_vdf(r#""a" "x\"y\n""#).unwrap();
        assert_eq!(root.get_str("a"), Some("x\"y\\n"));
    }

    #[test]
    fn skips_comments_and_whitespace() {
        let root = parse_vdf("// hi\n\"k\" // trailing\n\"v\"\n").unwrap();
        assert_eq!(root.get_str("k"), Some("v"));
    }

    #[test]
    fn duplicate_keys_keep_first_position_with_last_value() {
        let root = parse_vdf(r#""a" "1" "b" "2" "a" "3""#).unwrap();
        assert_eq!(root.0[0], ("a".into(), Node::Value("3".into())));
        assert_eq!(root.0.len(), 2);
    }

    #[test]
    fn rejects_malformed_input_with_original_messages() {
        for (text, message) in [
            ("\"a\" \"unterminated", "Malformed Steam library manifest."),
            ("bare", "Malformed Steam library manifest."),
            ("}", "Unbalanced Steam manifest."),
            ("\"a\" {", "Unclosed Steam manifest section."),
            ("\"a\"", "Malformed Steam manifest entry."),
            ("{ }", "Malformed Steam manifest entry."),
            ("\"a\" }", "Malformed Steam manifest value."),
            ("\"a\" \"b\\", "Malformed Steam library manifest."),
        ] {
            assert_eq!(parse_vdf(text).unwrap_err().0, message, "{text}");
        }
    }
}
