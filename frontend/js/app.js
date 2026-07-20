"use strict";

// ---------- Global state ----------
const state = {
  connection: null,
  avatars: [],
  createAvatar: null,
  joinAvatar: null,
  roomCode: null,
  myPlayerId: null,
  players: [],
  hostPlayerId: null,
  settings: null,
  phase: "Lobby",
  roundNumber: 0,
  roundTotal: 0,
  roundDeadlineUtc: null,
  turnDeadlineUtc: null,
  activePlayerId: null,
  guesses: [], // {word, playerId, rank, isCorrect, attemptNumber, isHint}
  bestByPlayer: {}, // playerId -> word (their current best-rank guess)
  myHintUsedThisRound: false,
};

// ---------- DOM helpers ----------
const $ = (id) => document.getElementById(id);

function showScreen(id) {
  document.querySelectorAll(".screen").forEach((el) => el.classList.add("hidden"));
  $(id).classList.remove("hidden");
}

function playerById(id) {
  return state.players.find((p) => p.id === id);
}

function setConnectionStatus(text, cls) {
  const el = $("connectionStatus");
  el.textContent = text;
  el.className = "connection-status " + cls;
}

// ---------- Avatar pickers ----------
function renderAvatarPicker(containerId, selectedKey) {
  const container = $(containerId);
  container.innerHTML = "";
  state.avatars.forEach((avatar) => {
    const btn = document.createElement("button");
    btn.type = "button";
    btn.className = "avatar-btn";
    btn.textContent = avatar;
    if (state[selectedKey] === avatar) btn.classList.add("selected");
    btn.addEventListener("click", () => {
      state[selectedKey] = avatar;
      renderAvatarPicker(containerId, selectedKey);
    });
    container.appendChild(btn);
  });
}

async function loadAvatars() {
  const res = await fetch(GUESSLY_CONFIG.apiBaseUrl + "/api/avatars");
  state.avatars = await res.json();
  state.createAvatar = state.avatars[0];
  state.joinAvatar = state.avatars[0];
  renderAvatarPicker("createAvatarPicker", "createAvatar");
  renderAvatarPicker("joinAvatarPicker", "joinAvatar");
}

// ---------- SignalR connection ----------
async function setupConnection() {
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(GUESSLY_CONFIG.hubUrl)
    .withAutomaticReconnect()
    .build();

  connection.onreconnecting(() => setConnectionStatus("reconnecting…", "disconnected"));
  connection.onreconnected(() => setConnectionStatus("connected", "connected"));
  connection.onclose(() => {
    setConnectionStatus("disconnected", "disconnected");
    // v1 has no reconnection support (per spec) — a dropped connection always
    // starts a fresh session, so send the player back to the home screen
    // rather than leaving stale room/game UI on screen.
    $("homeError").textContent = "Connection lost. Please rejoin.";
    showScreen("screen-home");
  });

  connection.on("RoomState", onRoomState);
  connection.on("RoundStarted", onRoundStarted);
  connection.on("TurnChanged", onTurnChanged);
  connection.on("GuessResult", onGuessResult);
  connection.on("GuessRejected", onGuessRejected);
  connection.on("Error", (msg) => showGuessError(msg));
  connection.on("RoundEnded", onRoundEnded);
  connection.on("GameEnded", onGameEnded);

  await connection.start();
  setConnectionStatus("connected", "connected");
  state.connection = connection;
}

// ---------- Server -> client handlers ----------
function onRoomState(dto) {
  state.roomCode = dto.code;
  state.hostPlayerId = dto.hostPlayerId;
  state.settings = dto.settings;
  state.phase = dto.phase;
  state.players = dto.players;
  state.roundNumber = dto.currentRoundNumber;

  if (state.phase === "Lobby") {
    renderLobby();
    showScreen("screen-lobby");
  } else if (state.phase === "InRound") {
    // A disconnect/reconnect mid-round sends a fresh RoomState — keep the
    // sidebar's connected/disconnected dimming in sync without waiting for
    // the next turn or guess event.
    renderScoreList();
  }
}

function onRoundStarted(dto) {
  state.phase = "InRound";
  state.roundNumber = dto.roundNumber;
  state.roundTotal = dto.totalRounds;
  state.roundDeadlineUtc = dto.roundDeadlineUtc;
  state.turnDeadlineUtc = dto.turnDeadlineUtc;
  state.activePlayerId = dto.activePlayerId;
  state.guesses = [];
  state.bestByPlayer = {};
  state.myHintUsedThisRound = false;

  $("gameRoundNum").textContent = dto.roundNumber;
  $("gameRoundTotal").textContent = dto.totalRounds;
  $("gameWordLen").textContent = dto.secretWordLength;

  renderGuessList();
  renderScoreList();
  updateTurnUi();
  showScreen("screen-game");
}

function onTurnChanged(dto) {
  state.activePlayerId = dto.activePlayerId;
  state.turnDeadlineUtc = dto.turnDeadlineUtc;
  $("guessError").textContent = "";
  updateTurnUi();
}

function onGuessRejected(dto) {
  showGuessError(dto.reason);
  // Non-turn-ending rejections (invalid/unknown word) leave the turn with the
  // same player — re-enable input immediately rather than waiting on a
  // TurnChanged that isn't coming. Turn-ending rejections (duplicate word)
  // are left disabled here; the TurnChanged broadcast (already in flight)
  // will confirm it moved on.
  if (!dto.turnEnded) {
    updateTurnUi();
  } else {
    $("guessInput").disabled = true;
    $("guessSubmitBtn").disabled = true;
    $("hintBtn").disabled = true;
  }
}

function onGuessResult(dto) {
  state.guesses.push(dto);
  if (!dto.isHint && dto.playerId) {
    const current = state.bestByPlayer[dto.playerId];
    const currentRank = current ? findGuessRank(dto.playerId, current) : Infinity;
    if (dto.rank < currentRank) state.bestByPlayer[dto.playerId] = dto.word;
  }

  renderGuessList();
  $("guessError").textContent = "";
}

function findGuessRank(playerId, word) {
  const g = state.guesses.find((x) => x.playerId === playerId && x.word === word);
  return g ? g.rank : Infinity;
}

function onRoundEnded(dto) {
  state.phase = "RoundEnd";
  state.players.forEach((p) => {
    if (dto.cumulativeScores[p.id] !== undefined) p.totalScore = dto.cumulativeScores[p.id];
  });

  const headline = dto.winnerPlayerId
    ? `🎉 ${playerById(dto.winnerPlayerId)?.avatar ?? ""} ${playerById(dto.winnerPlayerId)?.name ?? "Someone"} guessed it!`
    : "⏰ Time's up — no one guessed the word.";
  $("roundEndHeadline").textContent = `Round ${dto.roundNumber} finished — ${headline}`;
  $("roundEndSecretWord").textContent = `The secret word was: "${dto.secretWord}" (${dto.totalAttempts} total guesses)`;

  const table = $("roundEndTable");
  table.innerHTML = "<tr><th>Player</th><th>Round score</th><th>Total</th></tr>";
  [...state.players]
    .sort((a, b) => b.totalScore - a.totalScore)
    .forEach((p) => {
      const row = document.createElement("tr");
      if (p.id === dto.winnerPlayerId) row.className = "winner-row";
      row.innerHTML = `<td>${p.avatar} ${escapeHtml(p.name)}</td><td>+${dto.roundScores[p.id] ?? 0}</td><td>${p.totalScore}</td>`;
      table.appendChild(row);
    });

  const isLastRound = dto.roundNumber >= (state.settings?.roundCount ?? Infinity);
  const iAmHost = state.myPlayerId === state.hostPlayerId;
  $("startNextRoundBtn").classList.toggle("hidden", isLastRound || !iAmHost);
  $("roundEndWaitHint").classList.toggle("hidden", isLastRound || iAmHost);

  showScreen("screen-round-end");
}

function onGameEnded(dto) {
  state.phase = "GameEnd";
  const table = $("gameEndTable");
  table.innerHTML = "<tr><th>#</th><th>Player</th><th>Total score</th></tr>";
  const ranked = [...state.players].sort((a, b) => (dto.finalScores[b.id] ?? 0) - (dto.finalScores[a.id] ?? 0));
  ranked.forEach((p, idx) => {
    const row = document.createElement("tr");
    if (p.id === dto.winnerPlayerId) row.className = "winner-row";
    row.innerHTML = `<td>${idx + 1}</td><td>${p.avatar} ${escapeHtml(p.name)}</td><td>${dto.finalScores[p.id] ?? 0}</td>`;
    table.appendChild(row);
  });
  showScreen("screen-game-end");
}

// ---------- Rendering ----------
function renderLobby() {
  $("lobbyCode").textContent = state.roomCode;
  const list = $("lobbyPlayers");
  list.innerHTML = "";
  state.players.forEach((p) => {
    const li = document.createElement("li");
    if (p.id === state.hostPlayerId) li.classList.add("host");
    if (!p.isConnected) li.classList.add("disconnected");
    li.innerHTML = `<span class="avatar">${p.avatar}</span><span>${escapeHtml(p.name)}</span>`;
    list.appendChild(li);
  });

  const iAmHost = state.myPlayerId === state.hostPlayerId;
  document.querySelectorAll("#settingsForm input").forEach((el) => (el.disabled = !iAmHost));
  $("saveSettingsBtn").classList.toggle("hidden", !iAmHost);
  $("settingsReadonlyHint").classList.toggle("hidden", iAmHost);
  $("startGameBtn").classList.toggle("hidden", !iAmHost);

  if (state.settings) {
    $("setRounds").value = state.settings.roundCount;
    $("setTurnSeconds").value = state.settings.turnSeconds;
    $("setRoundSeconds").value = state.settings.roundSeconds;
    $("setMinLen").value = state.settings.minWordLength;
    $("setMaxLen").value = state.settings.maxWordLength;
  }

  const connectedCount = state.players.filter((p) => p.isConnected).length;
  $("startGameBtn").disabled = connectedCount < 2;
}

function rankTierClass(rank) {
  if (rank === 1) return "tier-winner";
  if (rank <= 10) return "tier-hot";
  if (rank <= 300) return "tier-green";
  if (rank <= 1500) return "tier-yellow";
  return "tier-red";
}

function renderGuessList() {
  const container = $("guessList");
  container.innerHTML = "";
  const sorted = [...state.guesses].sort((a, b) => a.rank - b.rank);

  sorted.forEach((g) => {
    const guesser = g.isHint ? null : playerById(g.playerId);
    const isBest = !g.isHint && state.bestByPlayer[g.playerId] === g.word;
    const row = document.createElement("div");
    row.className = "guess-row " + rankTierClass(g.rank);
    row.innerHTML = `
      <span class="rank">#${g.rank}</span>
      <span class="word">${escapeHtml(g.word)}</span>
      ${g.isHint ? `<span class="best-marker" title="Hint — belongs to no one">💡</span>` : ""}
      ${isBest ? `<span class="best-marker" title="${guesser?.name ?? ""}'s best guess">${guesser?.avatar ?? "⭐"}</span>` : ""}
    `;
    container.appendChild(row);
  });
}

function renderScoreList() {
  const list = $("scoreList");
  list.innerHTML = "";
  [...state.players]
    .sort((a, b) => b.totalScore - a.totalScore)
    .forEach((p) => {
      const li = document.createElement("li");
      if (p.id === state.activePlayerId) li.classList.add("active-turn");
      if (!p.isConnected) li.classList.add("disconnected");
      li.innerHTML = `<span class="avatar">${p.avatar}</span><span>${escapeHtml(p.name)}</span><span class="score">${p.totalScore}</span>`;
      list.appendChild(li);
    });
}

function updateTurnUi() {
  renderScoreList();
  const activePlayer = playerById(state.activePlayerId);
  const isMyTurn = state.activePlayerId === state.myPlayerId;
  const banner = $("turnBanner");
  banner.textContent = isMyTurn
    ? "🎯 Your turn — make a guess!"
    : `Waiting for ${activePlayer ? activePlayer.avatar + " " + activePlayer.name : "…"} to guess…`;
  banner.classList.toggle("your-turn", isMyTurn);

  $("guessInput").disabled = !isMyTurn;
  $("guessSubmitBtn").disabled = !isMyTurn;
  $("hintBtn").disabled = !isMyTurn || state.myHintUsedThisRound;
  if (isMyTurn) $("guessInput").focus();
}

function showGuessError(msg) {
  $("guessError").textContent = msg;
}

function escapeHtml(str) {
  const div = document.createElement("div");
  div.textContent = str ?? "";
  return div.innerHTML;
}

// ---------- Countdown loop (client renders against server-provided deadlines) ----------
setInterval(() => {
  if (state.phase !== "InRound") return;
  const now = Date.now();
  const roundRemaining = state.roundDeadlineUtc ? Math.max(0, Math.round((new Date(state.roundDeadlineUtc) - now) / 1000)) : "--";
  const turnRemaining = state.turnDeadlineUtc ? Math.max(0, Math.round((new Date(state.turnDeadlineUtc) - now) / 1000)) : "--";
  $("roundTimer").textContent = roundRemaining;
  $("turnTimer").textContent = turnRemaining;
}, 250);

// ---------- User actions ----------
function wireUpUi() {
  $("createRoomBtn").addEventListener("click", async () => {
    $("homeError").textContent = "";
    try {
      const result = await state.connection.invoke("CreateRoom", $("createName").value, state.createAvatar);
      applyJoinResult(result);
    } catch (err) {
      $("homeError").textContent = cleanHubError(err);
    }
  });

  $("joinRoomBtn").addEventListener("click", async () => {
    $("homeError").textContent = "";
    try {
      const code = $("joinCode").value.trim().toUpperCase();
      const result = await state.connection.invoke("JoinRoom", code, $("joinName").value, state.joinAvatar);
      applyJoinResult(result);
    } catch (err) {
      $("homeError").textContent = cleanHubError(err);
    }
  });

  $("settingsForm").addEventListener("submit", async (e) => {
    e.preventDefault();
    $("lobbyError").textContent = "";
    const settings = {
      roundCount: Number($("setRounds").value),
      turnSeconds: Number($("setTurnSeconds").value),
      roundSeconds: Number($("setRoundSeconds").value),
      minWordLength: Number($("setMinLen").value),
      maxWordLength: Number($("setMaxLen").value),
    };
    try {
      await state.connection.invoke("UpdateSettings", settings);
    } catch (err) {
      $("lobbyError").textContent = cleanHubError(err);
    }
  });

  $("startGameBtn").addEventListener("click", async () => {
    $("lobbyError").textContent = "";
    try {
      await state.connection.invoke("StartGame");
    } catch (err) {
      $("lobbyError").textContent = cleanHubError(err);
    }
  });

  $("guessForm").addEventListener("submit", async (e) => {
    e.preventDefault();
    const input = $("guessInput");
    const word = input.value.trim();
    if (!word) return;
    input.value = "";
    // Optimistically lock the controls the instant we submit — the guess may
    // end our turn, and we don't want a network-latency window where the
    // input still looks usable. onGuessRejected re-enables it if the server
    // says our turn didn't actually end (invalid/unknown word).
    input.disabled = true;
    $("guessSubmitBtn").disabled = true;
    $("hintBtn").disabled = true;
    try {
      await state.connection.invoke("SubmitGuess", word);
    } catch (err) {
      showGuessError(cleanHubError(err));
      updateTurnUi();
    }
  });

  $("startNextRoundBtn").addEventListener("click", async () => {
    try {
      await state.connection.invoke("StartNextRound");
    } catch (err) {
      window.alert(cleanHubError(err));
    }
  });

  $("hintBtn").addEventListener("click", async () => {
    // Requesting a hint always ends the turn — lock controls immediately,
    // same reasoning as the guess submit handler above.
    $("guessInput").disabled = true;
    $("guessSubmitBtn").disabled = true;
    $("hintBtn").disabled = true;
    try {
      await state.connection.invoke("RequestHint");
      state.myHintUsedThisRound = true;
    } catch (err) {
      showGuessError(cleanHubError(err));
      updateTurnUi();
    }
  });

  $("playAgainBtn").addEventListener("click", () => window.location.reload());

  $("copyInviteBtn").addEventListener("click", async () => {
    const url = `${window.location.origin}${window.location.pathname}?room=${state.roomCode}`;
    try {
      await navigator.clipboard.writeText(url);
      const btn = $("copyInviteBtn");
      const original = btn.textContent;
      btn.textContent = "✅ Copied!";
      setTimeout(() => (btn.textContent = original), 1500);
    } catch {
      window.prompt("Copy this invite link:", url);
    }
  });
}

// Prefill the join form from an invite link like index.html?room=ABCDE
function applyInviteLinkIfPresent() {
  const roomFromUrl = new URLSearchParams(window.location.search).get("room");
  if (!roomFromUrl) return;
  $("joinCode").value = roomFromUrl.toUpperCase();
  $("joinName").focus();
}

function applyJoinResult(result) {
  state.myPlayerId = result.yourPlayerId;
  onRoomState(result.room);
}

function cleanHubError(err) {
  const msg = err?.message ?? String(err);
  // strip the generic SignalR wrapper text, keep just the HubException message
  const marker = "HubException: ";
  const idx = msg.indexOf(marker);
  return idx >= 0 ? msg.slice(idx + marker.length) : msg;
}

// ---------- Boot ----------
(async function main() {
  wireUpUi();
  applyInviteLinkIfPresent();
  await loadAvatars();
  await setupConnection();
})();
