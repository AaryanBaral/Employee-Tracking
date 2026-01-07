const AGENT_URL = "http://127.0.0.1:43121/events/web";
const TOKEN = "change-this-to-a-random-string"; // match your appsettings.json
const MIN_INTERVAL_MS = 1500;
let lastSentAt = 0;
let lastSignature = "";

function domainFromUrl(url) {
  try {
    return new URL(url).hostname;
  } catch {
    return null;
  }
}

async function sendWebEvent(tab) {
  if (!tab || !tab.url) return;

  const domain = domainFromUrl(tab.url);
  if (!domain) return;

  const title = tab.title || null;
  const signature = `${domain}::${tab.url}::${title || ""}`;
  const now = Date.now();
  if (signature === lastSignature) return;
  if (now - lastSentAt < MIN_INTERVAL_MS) return;

  const payload = {
    eventId: crypto.randomUUID(),
    domain,
    title,
    url: tab.url,
    timestamp: new Date().toISOString(),
    browser: "chromium"
  };

  try {
    await fetch(AGENT_URL, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-Agent-Token": TOKEN
      },
      body: JSON.stringify(payload)
    });
    lastSentAt = now;
    lastSignature = signature;
  } catch (e) {
    // Agent may not be running; ignore for now
  }
}

// When active tab changes
chrome.tabs.onActivated.addListener(async ({ tabId }) => {
  const tab = await chrome.tabs.get(tabId);
  sendWebEvent(tab);
});

// When tab URL/title updates
chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
  if (changeInfo.status === "complete" || changeInfo.title || changeInfo.url) {
    sendWebEvent(tab);
  }
});

// On browser startup, capture current active tab
chrome.runtime.onStartup.addListener(async () => {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  sendWebEvent(tab);
});
