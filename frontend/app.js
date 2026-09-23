// Minimal vanilla-JS SPA: no build step required, so the reviewer can open this
// directly (with the API running) without installing frontend tooling. Talks to the
// ASP.NET Core API over REST + a SignalR hub for real-time slot updates.

// Empty string = same origin as whatever served this page. That's correct once the
// frontend is published into the API's wwwroot (see docs/DEPLOYMENT.md / CI workflow).
// Override from the browser console (window.API_BASE_URL = "http://localhost:5000")
// if you're running the frontend separately from the API during local development.
const API_BASE = window.API_BASE_URL || "";

const state = {
  token: localStorage.getItem("token") || null,
  isAdmin: localStorage.getItem("isAdmin") === "true",
  displayName: localStorage.getItem("displayName") || "",
  currentResourceId: null,
  connection: null,
};

const el = (id) => document.getElementById(id);

function showToast(message, kind = "") {
  const toast = el("toast");
  toast.textContent = message;
  toast.className = kind;
  setTimeout(() => (toast.className = "hidden"), 3500);
}

async function api(path, options = {}) {
  const headers = { "Content-Type": "application/json", ...(options.headers || {}) };
  if (state.token) headers.Authorization = `Bearer ${state.token}`;
  const res = await fetch(`${API_BASE}${path}`, { ...options, headers });
  return res;
}

// --- Auth --------------------------------------------------------------------

function setSession(auth) {
  state.token = auth.token;
  state.isAdmin = auth.roles.includes("Admin");
  state.displayName = auth.displayName;
  localStorage.setItem("token", auth.token);
  localStorage.setItem("isAdmin", String(state.isAdmin));
  localStorage.setItem("displayName", auth.displayName);
  render();
  connectSignalR();
}

function logout() {
  state.token = null;
  localStorage.clear();
  if (state.connection) state.connection.stop();
  render();
}

el("loginForm").addEventListener("submit", async (e) => {
  e.preventDefault();
  const form = new FormData(e.target);
  const res = await api("/api/auth/login", {
    method: "POST",
    body: JSON.stringify({ email: form.get("email"), password: form.get("password") }),
  });
  if (res.ok) setSession(await res.json());
  else showToast("Login failed. Check your credentials.", "error");
});

el("registerForm").addEventListener("submit", async (e) => {
  e.preventDefault();
  const form = new FormData(e.target);
  const res = await api("/api/auth/register", {
    method: "POST",
    body: JSON.stringify({
      email: form.get("email"),
      password: form.get("password"),
      displayName: form.get("displayName"),
    }),
  });
  if (res.ok) setSession(await res.json());
  else showToast("Registration failed.", "error");
});

// --- Resources & slots ---------------------------------------------------------

async function loadResources() {
  const res = await api("/api/resources");
  const resources = await res.json();
  const list = el("resources");
  list.innerHTML = "";
  resources.forEach((r) => {
    const li = document.createElement("li");
    li.className = "resource-item";
    li.textContent = `${r.name} — ${r.location || ""}`;
    li.onclick = () => openSchedule(r.id, r.name);
    list.appendChild(li);
  });
  el("adminResourceForm").classList.toggle("hidden", !state.isAdmin);
}

el("createResourceBtn").addEventListener("click", async () => {
  const name = el("newResourceName").value.trim();
  if (!name) return;
  const res = await api("/api/resources", {
    method: "POST",
    body: JSON.stringify({ name, description: "", location: "", capacity: 4 }),
  });
  if (res.ok) {
    el("newResourceName").value = "";
    loadResources();
  } else {
    showToast("Could not create resource.", "error");
  }
});

async function openSchedule(resourceId, name) {
  if (state.currentResourceId && state.connection) {
    await state.connection.invoke("LeaveResourceGroup", state.currentResourceId).catch(() => {});
  }
  state.currentResourceId = resourceId;
  el("scheduleTitle").textContent = `Schedule — ${name}`;
  el("scheduleView").classList.remove("hidden");
  await loadSlots(resourceId);
  if (state.connection && state.connection.state === "Connected") {
    await state.connection.invoke("JoinResourceGroup", resourceId).catch(() => {});
  }
}

async function loadSlots(resourceId) {
  const res = await api(`/api/resources/${resourceId}/slots`);
  const slots = await res.json();
  renderSlots(slots);
}

function renderSlots(slots) {
  const grid = el("slotGrid");
  grid.innerHTML = "";
  slots.forEach((s) => {
    const div = document.createElement("div");
    div.className = `slot ${s.isBooked ? "booked" : "free"}`;
    div.dataset.slotId = s.id;
    const start = new Date(s.startUtc);
    div.textContent = `${start.toLocaleDateString()} ${start.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}`;
    if (!s.isBooked) div.onclick = () => bookSlot(s.id);
    grid.appendChild(div);
  });
}

async function bookSlot(timeSlotId) {
  const res = await api("/api/bookings", {
    method: "POST",
    body: JSON.stringify({ timeSlotId }),
  });
  if (res.status === 201) {
    showToast("Booked!", "success");
    loadMyBookings();
    // No need to manually refresh the grid here: the SignalR broadcast this same
    // action triggers will update it (and every other viewer's grid) a moment later.
  } else if (res.status === 409) {
    showToast("Too slow — someone else just booked that slot.", "error");
    loadSlots(state.currentResourceId);
  } else {
    showToast("Booking failed.", "error");
  }
}

async function loadMyBookings() {
  const res = await api("/api/bookings/mine");
  const bookings = await res.json();
  const list = el("bookingsList");
  list.innerHTML = "";
  bookings.forEach((b) => {
    const li = document.createElement("li");
    const start = new Date(b.startUtc);
    li.textContent = `${b.resourceName} — ${start.toLocaleString()} `;
    const cancelBtn = document.createElement("button");
    cancelBtn.textContent = "Cancel";
    cancelBtn.onclick = async () => {
      await api(`/api/bookings/${b.id}`, { method: "DELETE" });
      loadMyBookings();
      if (state.currentResourceId) loadSlots(state.currentResourceId);
    };
    li.appendChild(cancelBtn);
    list.appendChild(li);
  });
}

// --- Real-time updates via SignalR -----------------------------------------------

function connectSignalR() {
  if (!window.signalR) return;
  state.connection = new signalR.HubConnectionBuilder()
    .withUrl(`${API_BASE}/hubs/booking?access_token=${state.token}`)
    .withAutomaticReconnect()
    .build();

  state.connection.on("SlotStatusChanged", (payload) => {
    if (payload.resourceId !== state.currentResourceId) return;
    const slotEl = document.querySelector(`.slot[data-slot-id="${payload.timeSlotId}"]`);
    if (!slotEl) return;
    slotEl.className = `slot ${payload.isBooked ? "booked" : "free"}`;
    slotEl.onclick = payload.isBooked ? null : () => bookSlot(payload.timeSlotId);
  });

  state.connection.start()
    .then(() => {
      if (state.currentResourceId) state.connection.invoke("JoinResourceGroup", state.currentResourceId);
    })
    .catch((err) => console.error("SignalR connection failed", err));
}

// --- Render / bootstrap ---------------------------------------------------------

function render() {
  const loggedIn = !!state.token;
  el("authView").classList.toggle("hidden", loggedIn);
  el("mainView").classList.toggle("hidden", !loggedIn);
  el("authBar").innerHTML = loggedIn
    ? `Signed in as ${state.displayName}${state.isAdmin ? " (admin)" : ""} <button id="logoutBtn">Log out</button>`
    : "";
  if (loggedIn) {
    el("logoutBtn").onclick = logout;
    loadResources();
    loadMyBookings();
  }
}

render();
if (state.token) connectSignalR();
