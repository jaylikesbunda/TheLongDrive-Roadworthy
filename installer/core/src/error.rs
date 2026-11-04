use std::fmt;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct InstallerError(pub String);

impl InstallerError {
    pub fn new(message: impl Into<String>) -> Self {
        InstallerError(message.into())
    }
}

impl fmt::Display for InstallerError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(&self.0)
    }
}

impl std::error::Error for InstallerError {}

pub type Result<T> = std::result::Result<T, InstallerError>;

pub fn err<T>(message: impl Into<String>) -> Result<T> {
    Err(InstallerError(message.into()))
}
