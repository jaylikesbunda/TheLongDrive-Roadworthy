"use strict";
const { invoke } = window.__TAURI__.core;
const { listen } = window.__TAURI__.event;

const $ = (id) => document.getElementById(id);
const text = (id, value) => { $(id).textContent = value; };
const pages = [...document.querySelectorAll(".page")];

const app = { version: "", build: "", page: "loading", info: null, game: "", token: 0, op: null,
  percent: 0, lastError: "", updateUrl: null, debounce: null };

function go(name) {
  app.page = name;
  for (const page of pages) page.classList.toggle("active", page.dataset.page === name);
  const focus = document.querySelector(".page.active [data-focus]");
  if (focus) focus.focus();
}

function installedLabel(info) {
  return info.version ? `v${info.version}` : "an older build";
}

const CAN_INSTALL = ["original", "legacy", "update"];

function showHome() {
  const info = app.info;
  const primary = $("primary");
  primary.hidden = false;
  $("restore").hidden = info.state === "original";
  text("homeNote", "");
  switch (info.relation) {
    case "original":
      text("homeTitle", "Ready to install");
      text("homeText", `Roadworthy v${app.version} will be added to your game. Your saves are not changed.`);
      text("homeNote", "Everyone in a multiplayer session needs the same build.");
      text("primary", "Install");
      primary.dataset.mode = "install";
      break;
    case "legacy":
    case "update":
      text("homeTitle", "Update available");
      text("homeText", `Roadworthy ${installedLabel(info)} is installed. This will update it to v${app.version}.`);
      text("homeNote", "Everyone in a multiplayer session needs the same build.");
      text("primary", `Update to v${app.version}`);
      primary.dataset.mode = "install";
      break;
    case "current":
      text("homeTitle", "You're up to date");
      text("homeText", `Roadworthy v${app.version} is installed.`);
      text("primary", "Close");
      primary.dataset.mode = "close";
      break;
    default:
      text("homeTitle", "Newer version installed");
      text("homeText", `Roadworthy ${installedLabel(info)} is installed and won't be replaced by this older installer.`);
      text("primary", "Close");
      primary.dataset.mode = "close";
  }
  go("home");
}

async function inspect(path, fromFolder) {
  const token = ++app.token;
  try {
    const info = await invoke("inspect", { path });
    if (token !== app.token) return;
    app.info = info; app.game = path;
    showHome();
  } catch (error) {
    if (token !== app.token) return;
    text("folderError", String(error));
    text("folderLead", fromFolder ? "That folder didn't work. Choose the game folder." : "We couldn't use the detected folder. Choose the game folder.");
    go("folder");
  }
}

function setProgress(percent, phase) {
  app.percent = percent;
  $("fill").style.width = percent + "%";
  $("fill").parentElement.setAttribute("aria-valuenow", String(percent));
  if (phase) text("workPhase", phase);
}

function resetLogs() {
  for (const id of ["workLog", "doneLog"]) { $(id).textContent = ""; $(id).hidden = true; }
  for (const id of ["workDetails", "doneDetails"]) { $(id).textContent = "Show details"; $(id).setAttribute("aria-expanded", "false"); }
  app.lastError = "";
}

function appendLog(chunk) {
  for (const id of ["workLog", "doneLog"]) { const log = $(id); log.textContent += chunk; log.scrollTop = log.scrollHeight; }
  const match = /Stopped: ([^\n]+)/.exec(chunk);
  if (match) app.lastError = match[1];
}

function toggleLog(logId, buttonId) {
  const log = $(logId), button = $(buttonId);
  log.hidden = !log.hidden;
  button.textContent = log.hidden ? "Show details" : "Hide details";
  button.setAttribute("aria-expanded", String(!log.hidden));
}

async function startOperation(action) {
  app.op = action;
  resetLogs();
  const updating = app.info.relation === "legacy" || app.info.relation === "update";
  text("workTitle", action === "restore" ? "Restoring" : updating ? "Updating" : "Installing");
  setProgress(0, "Starting...");
  go("working");
  try {
    await invoke("run_action", { action, path: app.game });
  } catch (error) {
    finish(1, String(error));
  }
}

function finish(code, message) {
  const failed = code !== 0;
  $("doneIcon").className = "icon " + (failed ? "fail" : "ok");
  $("back").hidden = !failed;
  $("doneDetails").hidden = !failed;
  if (failed) {
    text("doneTitle", "Something went wrong");
    text("doneText", message || app.lastError || "The operation stopped. Nothing was left half-installed that the installer could not verify.");
    $("doneLog").hidden = false;
    text("doneDetails", "Hide details");
    $("doneDetails").setAttribute("aria-expanded", "true");
  } else if (app.op === "restore") {
    text("doneTitle", "Original game restored");
    text("doneText", "The game is back to its original files. Your saves were not changed.");
  } else {
    const updated = app.info && (app.info.relation === "legacy" || app.info.relation === "update");
    text("doneTitle", updated ? "Updated" : "Installed");
    text("doneText", `Roadworthy v${app.version} is ready. You can launch the game now.`);
  }
  go("done");
}

async function checkReleases() {
  try {
    const result = await invoke("check_updates");
    if (result.newer) {
      text("updateText", `${result.tag} is available.`);
      app.updateUrl = result.url;
      $("updateNote").hidden = false;
    }
  } catch (_) { /* offline installation still works */ }
}

function chooseFolder() {
  text("folderError", "");
  $("path").value = app.game || $("path").value;
  go("folder");
  $("path").focus();
}

async function start() {
  const bundled = await invoke("bundled");
  app.version = bundled.version; app.build = bundled.build;
  text("subtitle", `The Long Drive \u00b7 beta ${bundled.beta} \u00b7 build ${bundled.build}`);

  $("primary").addEventListener("click", () => {
    if ($("primary").dataset.mode === "close") invoke("quit"); else startOperation("install");
  });
  $("restore").addEventListener("click", () => startOperation("restore"));
  $("change").addEventListener("click", chooseFolder);
  $("continue").addEventListener("click", () => { const path = $("path").value.trim(); if (path) inspect(path, true); else text("folderError", "Enter or browse to the game folder."); });
  $("path").addEventListener("keydown", (event) => { if (event.key === "Enter") $("continue").click(); });
  $("browse").addEventListener("click", async () => { const folder = await invoke("browse"); if (folder) { $("path").value = folder; $("continue").click(); } });
  $("workDetails").addEventListener("click", () => toggleLog("workLog", "workDetails"));
  $("doneDetails").addEventListener("click", () => toggleLog("doneLog", "doneDetails"));
  $("close").addEventListener("click", () => invoke("quit"));
  $("back").addEventListener("click", () => inspect(app.game, false));
  $("getNewer").addEventListener("click", () => app.updateUrl && invoke("open_release", { url: app.updateUrl }).catch(() => {}));

  await listen("rw-log", (event) => appendLog(event.payload));
  await listen("rw-progress", (event) => setProgress(event.payload.percent, event.payload.phase));
  await listen("rw-close-blocked", () => text("workPhase", "Please let this finish before closing."));
  await listen("rw-done", (event) => finish(event.payload.code, ""));

  checkReleases();
  const prefilled = await invoke("prefill");
  const games = prefilled ? [prefilled] : await invoke("detect");
  for (const game of games) { const option = document.createElement("option"); option.value = game; $("games").appendChild(option); }
  if (games.length) { $("path").value = games[0]; inspect(games[0], false); return; }
  text("folderLead", "We couldn't find it automatically. Choose the game folder.");
  go("folder");
}

start().catch((error) => { text("folderError", `Could not start: ${error}`); go("folder"); });
