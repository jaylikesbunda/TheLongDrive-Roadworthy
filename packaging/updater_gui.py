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
    result = main_cli(["--game-dir", str(game), "--no-pause"])
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
    verify_update_payload()
    if test_report is not None:
        result = gui_self_test()
        Path(test_report).write_text('gui_self_test=' + str(result) + chr(10), encoding='utf-8')
        raise SystemExit(result)
    return run_gui()


if __name__ == '__main__':
    # build_steam_updater strips the installer template's own __main__ and
    # appends this footer, so this dispatch is the generated EXE's entrypoint.
    _args = sys.argv[1:]
    _report = _args[_args.index('--gui-self-test') + 1] if '--gui-self-test' in _args else None
    raise SystemExit(update_gui(_report) or 0)
