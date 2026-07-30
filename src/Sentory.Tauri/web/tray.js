const { invoke } = window.__TAURI__.core;
const { listen } = window.__TAURI__.event;

const elements = {
  status: document.querySelector("#tray-status"),
  discordStatus: document.querySelector("#tray-discord-status"),
  discordDetection: document.querySelector("#tray-discord-detection"),
  discordState: document.querySelector("#tray-discord-state"),
  openLabel: document.querySelector("#tray-open-label"),
  doubleClick: document.querySelector("#tray-double-click"),
  pauseLabel: document.querySelector("#tray-pause-label"),
  pauseIcon: document.querySelector("#tray-pause-icon"),
  pauseSwitch: document.querySelector("#tray-pause-switch"),
  startupLabel: document.querySelector("#tray-startup-label"),
  startupCheck: document.querySelector("#tray-startup-check"),
  discordLabel: document.querySelector("#tray-discord-label"),
  accessibility: document.querySelector("#tray-accessibility"),
  discordCheck: document.querySelector("#tray-discord-check"),
  repair: document.querySelector("#tray-repair"),
  repairLabel: document.querySelector("#tray-repair-label"),
  openDataLabel: document.querySelector("#tray-open-data-label"),
  exitLabel: document.querySelector("#tray-exit-label"),
};

function applyTrayState(state) {
  if (!state) return;
  document.documentElement.dataset.theme = state.dark ? "dark" : "light";
  elements.status.textContent = state.statusLabel;
  elements.discordStatus.hidden = !state.showDiscordStatus;
  elements.discordDetection.textContent = state.discordDetectionLabel;
  elements.discordState.textContent = state.discordStatusLabel;
  elements.openLabel.textContent = state.openLabel;
  elements.doubleClick.textContent = state.doubleClickLabel;
  elements.pauseLabel.textContent = state.paused ? state.resumeLabel : state.pauseLabel;
  elements.pauseIcon.innerHTML = state.paused ? "&#xE768;" : "&#xE769;";
  elements.pauseSwitch.classList.toggle("on", state.paused);
  elements.startupLabel.textContent = state.startupLabel;
  elements.startupCheck.classList.toggle("off", !state.startupEnabled);
  elements.discordLabel.textContent = state.discordLabel;
  elements.accessibility.textContent = state.accessibilityLabel;
  elements.discordCheck.classList.toggle("off", !state.discordEnabled);
  elements.repair.hidden = !state.showDiscordRepair;
  elements.repairLabel.textContent = state.repairLabel;
  elements.openDataLabel.textContent = state.openDataLabel;
  elements.exitLabel.textContent = state.exitLabel;
}

async function runTrayAction(action) {
  await invoke("tray_action", { action });
}

for (const button of document.querySelectorAll("[data-action]")) {
  button.addEventListener("click", () => {
    void runTrayAction(button.dataset.action);
  });
}

document.addEventListener("contextmenu", event => event.preventDefault());
document.addEventListener("keydown", event => {
  if (event.key === "Escape") void invoke("tray_hide");
});

listen("tray-state", event => applyTrayState(event.payload));
invoke("tray_state_get").then(applyTrayState).catch(() => {});
