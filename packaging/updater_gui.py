EXPECTED_SOURCES = "__SOURCE_SIGNATURE__"

def verify_update_payload():
    import re
    with zipfile.ZipFile(io.BytesIO(payload_bytes())) as archive:
        script = archive.read("tests/scripts/Test-Stability.ps1").decode("utf-8-sig")
        line = next(x for x in script.splitlines() if x.startswith("$signatureInputs="))
        names = re.findall("'([^']+)'", line)
        signature = "\n".join(name + ":" + hashlib.sha256(archive.read(name.replace("\\", "/"))).hexdigest().upper() for name in names)
        if hashlib.sha256(signature.encode()).hexdigest().upper() != EXPECTED_SOURCES:
            raise InstallerError("Embedded sources do not match Steam fix current build.")
    print("VERIFIED: embedded installer payload and Steam fix source signature current build.", flush=True)

def update(game):
    verify_update_payload()
    game = choose_game(str(game))
    print("Steam connection fix current build: " + str(game), flush=True)
    for operation in (["--restore-all"], []):
        result = main_cli(["--game-dir", str(game), "--no-pause"] + operation)
        if result != 0:
            raise RuntimeError("Update stopped. Read the installer error above.")
    managed = game / "TheLongDrive_Data" / "Managed"
    receipt = json.loads((managed / "tldmp-v3-receipt.json").read_text(encoding="utf-8-sig"))
    if receipt.get("sourceSignature") != EXPECTED_SOURCES:
        raise RuntimeError("The installed source identity does not match the Steam fix.")
    if installed_state(game) != "v4":
        raise RuntimeError("The installed files did not pass verification.")
    print("VERIFIED: Steam fix current build installed. Restart the game and retry joining.", flush=True)



def update_gui(test_report=None):
    import tkinter as tk
    from tkinter import ttk, filedialog
    import threading
    import queue
    import contextlib
    root = tk.Tk()
    root.title("Roadworthy — Steam connection update")
    root.geometry("640x460")
    root.minsize(580, 400)
    set_window_icon(root)
    frame = ttk.Frame(root, padding=16)
    frame.pack(fill="both", expand=True)
    ttk.Label(frame, text="Install Steam connection fix", font=("Segoe UI", 15, "bold")).pack(anchor="w")
    ttk.Label(frame, text="Close The Long Drive first. Both players need this build.").pack(anchor="w", pady=(5, 14))
    path = tk.StringVar()
    row = ttk.Frame(frame)
    row.pack(fill="x")
    entry = ttk.Entry(row, textvariable=path)
    entry.pack(side="left", fill="x", expand=True)
    def browse():
        selected = filedialog.askdirectory(parent=root, title="Select The Long Drive game folder")
        if selected:
            path.set(selected)
    browse_button = ttk.Button(row, text="Browse…", command=browse)
    browse_button.pack(side="right", padx=(8, 0))
    status = tk.StringVar(value="Find your game folder, then install the update.")
    ttk.Label(frame, textvariable=status, wraplength=600).pack(anchor="w", pady=12)
    progress = ttk.Progressbar(frame, mode="indeterminate")
    progress.pack(fill="x", pady=(0, 8))
    output = tk.Text(frame, height=12, wrap="word", state="disabled", font=("Consolas", 9))
    output.pack(fill="both", expand=True)
    events = queue.Queue()
    busy = [False]
    class Writer:
        def write(self, text):
            if text: events.put(("text", text))
            return len(text)
        def flush(self): pass
        def isatty(self): return False
    def install():
        if busy[0]: return
        try:
            game = choose_game(path.get().strip())
        except Exception as error:
            status.set(str(error)); return
        busy[0] = True
        for control in (entry, browse_button, button): control.configure(state="disabled")
        status.set("Restoring old patch layers and installing the verified Steam fix…")
        progress.start(12)
        def work():
            try:
                with contextlib.redirect_stdout(Writer()), contextlib.redirect_stderr(Writer()):
                    update(game)
                events.put(("done", "Verified: Steam fix current build installed. You can restart the game."))
            except Exception as error:
                events.put(("failed", str(error)))
        threading.Thread(target=work, daemon=True).start()
    button = ttk.Button(frame, text="Install Steam fix", command=install)
    button.pack(fill="x", pady=(12, 0))
    def poll():
        while True:
            try: kind, text = events.get_nowait()
            except queue.Empty: break
            if kind == "text":
                output.configure(state="normal"); output.insert("end", text); output.see("end"); output.configure(state="disabled")
            else:
                busy[0] = False; progress.stop()
                for control in (entry, browse_button, button): control.configure(state="normal")
                status.set(text if kind == "done" else "Stopped: " + text)
        root.after(60, poll)
    def close():
        if busy[0]: status.set("Please wait for installation to finish before closing.")
        else: root.destroy()
    root.protocol("WM_DELETE_WINDOW", close)
    try: path.set(str(choose_game(None)))
    except InstallerError: pass
    poll()
    if test_report:
        verify_update_payload()
        root.update_idletasks()
        assert button["text"] == "Install Steam fix"
        assert root.winfo_width() >= 580
        Path(test_report).write_text("PASS: embedded source identity and native Tk GUI controls verified; no installation performed.")
        root.destroy()
    else:
        root.mainloop()

if __name__ == "__main__":
    if len(sys.argv) == 3 and sys.argv[1] == "--gui-self-test":
        update_gui(sys.argv[2])
    else:
        update_gui()
