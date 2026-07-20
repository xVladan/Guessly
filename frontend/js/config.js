// Single place the frontend points at the backend. Auto-detects local dev vs.
// production by hostname, so no manual edit is needed when deploying — but if
// you ever rename the production API domain, update PROD_API_BASE_URL below.
const PROD_API_BASE_URL = "https://guessly.weddio.net";

const isLocalHost = ["localhost", "127.0.0.1"].includes(window.location.hostname);
const apiBaseUrl = isLocalHost ? "http://localhost:5000" : PROD_API_BASE_URL;

const GUESSLY_CONFIG = {
  apiBaseUrl,
  hubUrl: `${apiBaseUrl}/hub/game`,
};
