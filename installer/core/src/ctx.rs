use std::fs::File;
use std::io::Write;

/// Where the installer's user-visible output goes (console for the CLI, events for the GUI).
pub trait Sink {
    fn out(&mut self, text: &str);
    fn err(&mut self, text: &str);
    fn progress(&mut self, _percent: u8, _phase: &str) {}
}

pub struct StdSink;

impl Sink for StdSink {
    fn out(&mut self, text: &str) {
        let mut stdout = std::io::stdout();
        let _ = stdout.write_all(text.as_bytes());
        let _ = stdout.flush();
    }
    fn err(&mut self, text: &str) {
        let mut stderr = std::io::stderr();
        let _ = stderr.write_all(text.as_bytes());
        let _ = stderr.flush();
    }
}

pub struct Ctx<'a> {
    pub sink: &'a mut dyn Sink,
    pub log: Option<File>,
}

impl<'a> Ctx<'a> {
    pub fn new(sink: &'a mut dyn Sink) -> Self {
        Ctx { sink, log: None }
    }

    pub fn println(&mut self, text: &str) {
        self.sink.out(&format!("{text}\n"));
    }

    /// Helper output is echoed to the console and the log, like the Python tee.
    pub fn tee(&mut self, line: &str) {
        self.sink.out(line);
        if let Some(log) = self.log.as_mut() {
            let _ = log.write_all(line.as_bytes());
            let _ = log.flush();
        }
    }

    pub fn emit(&mut self, percent: u8, phase: &str) {
        self.sink.progress(percent, phase);
        self.sink.out(&format!("[{percent:>3}%] {phase}\n"));
    }
}
