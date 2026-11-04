fn main() {
    let args: Vec<String> = std::env::args().skip(1).collect();
    std::process::exit(roadworthy_core::run::run(&args, &mut roadworthy_core::ctx::StdSink));
}
