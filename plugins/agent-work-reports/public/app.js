const state = { data: null, openWork: new Set(), openAgents: new Set(), requestSeq: 0, timer: null };
const $ = id => document.getElementById(id);

function esc(value) {
  return String(value ?? "").replace(/[&<>"']/g, ch => ({ "&":"&amp;", "<":"&lt;", ">":"&gt;", '"':"&quot;", "'":"&#39;" }[ch]));
}
function fmtTime(value) {
  if (!value) return "—";
  const d = new Date(value); if (!Number.isFinite(d.getTime())) return "—";
  return d.toLocaleString([], { month:"short", day:"numeric", hour:"numeric", minute:"2-digit" });
}
function age(value) {
  if (!value) return "unknown";
  const ms = Date.now() - new Date(value).getTime(); if (!Number.isFinite(ms)) return "unknown";
  if (ms < 60000) return `${Math.max(0,Math.round(ms/1000))}s ago`;
  if (ms < 3600000) return `${Math.round(ms/60000)}m ago`;
  if (ms < 86400000) return `${Math.round(ms/3600000)}h ago`;
  return `${Math.round(ms/86400000)}d ago`;
}
function matches(item) {
  const query = $("search").value.trim().toLowerCase();
  const filter = $("filter").value;
  if (filter !== "all" && item.bucket !== filter) return false;
  if (!query) return true;
  return Object.values(item).filter(v => typeof v === "string" || typeof v === "number").join(" ").toLowerCase().includes(query);
}
function progressStyle(value) { return `width:${Math.max(0, Math.min(100, Number(value)||0))}%`; }

function renderStats() {
  const s = state.data.stats;
  const entries = [[s.activeAgents,"Active agents"],[s.blockedAgents,"Blocked / attention"],[s.doneAgentsToday,"Done today"],[s.knownAgents,"Known agents"],[s.openWork,"Open work"],[s.doneWork,"Completed work"]];
  $("stats").innerHTML = entries.map(([v,l])=>`<div class="stat"><strong>${esc(v)}</strong><span>${esc(l)}</span></div>`).join("");
}

function renderWork() {
  const items = state.data.work.filter(matches);
  $("workCount").textContent = `${items.length} shown`;
  if (!items.length) { $("work").innerHTML = `<div class="empty">No work matches this view.</div>`; return; }
  $("work").innerHTML = items.map(item => {
    const open = state.openWork.has(item.id) ? " open" : "";
    return `<article class="row${open}" data-work="${esc(item.id)}">
      <button class="row-main" type="button" data-toggle-work="${esc(item.id)}">
        <span class="dot ${esc(item.bucket)}"></span>
        <span><div class="title">${esc(item.title)}</div><div class="meta">${esc(item.status)} · ${esc(item.ownerRole || item.ownerId || "unassigned")} · ${esc(item.branch || "no branch")} · ${esc(age(item.updatedAt))}</div></span>
        <span class="pct">${esc(item.progress)}%</span>
      </button>
      <div class="bar"><i style="${progressStyle(item.progress)}"></i></div>
      <div class="details">
        <div class="summary">${esc(item.summary)}</div>
        <div class="detail-grid"><span><b>Task</b> ${esc(item.id)}</span><span><b>Owner</b> ${esc(item.ownerId || "—")}</span><span><b>Status</b> ${esc(item.status)}</span><span><b>Updated</b> ${esc(fmtTime(item.updatedAt))}</span></div>
        ${item.blockers?.length ? `<div><b>Blockers:</b> ${esc(item.blockers.join(" · "))}</div>` : ""}
        ${item.dependencies?.length ? `<div><b>Depends on:</b> ${esc(item.dependencies.join(" · "))}</div>` : ""}
      </div>
    </article>`;
  }).join("");
}

function renderAgents() {
  const items = state.data.agents.filter(matches);
  $("agentCount").textContent = `${items.length} shown`;
  if (!items.length) { $("agents").innerHTML = `<div class="empty">No agents match this view.</div>`; return; }
  $("agents").innerHTML = items.map(agent => {
    const open = state.openAgents.has(agent.id) ? " open" : "";
    const reports = agent.recentReports?.slice(0,4).map(r=>`<div class="summary"><span class="pill">${esc(r.phase)}</span> ${esc(r.summary)} <span class="meta">${esc(age(r.at))}</span></div>`).join("") || "";
    return `<article class="agent${open}" data-agent="${esc(agent.id)}">
      <button class="agent-head" type="button" data-toggle-agent="${esc(agent.id)}">
        <span class="dot ${esc(agent.bucket)}"></span>
        <span style="min-width:0;flex:1"><div class="agent-name">${esc(agent.role)} · ${esc(agent.id)}</div><div class="agent-task">${esc(agent.task)}</div><span class="pill">${esc(agent.status)}</span><span class="pill">${esc(agent.machine || agent.provider)}</span><span class="pill">${esc(agent.progress)}%</span></span>
      </button>
      <div class="agent-body">
        <div class="summary">${esc(agent.summary)}</div>
        <div class="detail-grid"><span><b>Task</b> ${esc(agent.taskId || "—")}</span><span><b>Branch</b> ${esc(agent.branch || "—")}</span><span><b>Provider</b> ${esc(agent.provider)}</span><span><b>Updated</b> ${esc(fmtTime(agent.updatedAt))}</span></div>
        ${reports}
        ${agent.source === "managed" ? `<button class="control log" data-log-agent="${esc(agent.id)}" type="button">View log</button><pre class="log-output" id="log-${encodeURIComponent(agent.id)}"></pre>` : ""}
      </div>
    </article>`;
  }).join("");
}

function renderActivity() {
  const items = state.data.activity || [];
  $("activityCount").textContent = `(${items.length})`;
  $("activity").innerHTML = items.length ? items.map(item=>`<div class="activity-item"><time>${esc(fmtTime(item.at))}</time><span class="kind">${esc(item.kind)}</span><span>${esc(item.message)}</span></div>`).join("") : `<div class="empty">No recent activity.</div>`;
}

function render() {
  if (!state.data) return;
  const src = state.data.source;
  $("source").className = `source${src.agentControl === "online" ? "" : " bad"}`;
  $("source").textContent = src.agentControl === "online" ? `Agent Control online · snapshot ${age(src.agentControlGeneratedAt)} · dashboard refreshed ${age(state.data.generatedAt)}` : `Agent Control unavailable · showing persisted reports${src.error ? ` · ${src.error}` : ""}`;
  renderStats(); renderWork(); renderAgents(); renderActivity();
}

async function refresh() {
  const seq = ++state.requestSeq;
  const controller = new AbortController();
  const timer = setTimeout(()=>controller.abort(), 5000);
  try {
    const response = await fetch("/api/view", { cache:"no-store", signal:controller.signal });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const data = await response.json();
    if (seq !== state.requestSeq) return;
    state.data = data;
    render();
  } catch (error) {
    if (seq !== state.requestSeq) return;
    $("source").className = "source bad";
    $("source").textContent = `Refresh failed: ${error.name === "AbortError" ? "timed out" : error.message}`;
  } finally {
    clearTimeout(timer);
    clearTimeout(state.timer);
    state.timer = setTimeout(refresh, document.hidden ? 15000 : 4000);
  }
}

async function loadLog(agentId, button) {
  const output = document.getElementById(`log-${encodeURIComponent(agentId)}`);
  if (!output) return;
  if (output.classList.contains("show")) { output.classList.remove("show"); button.textContent="View log"; return; }
  button.disabled = true; button.textContent = "Loading…";
  try {
    const response = await fetch(`/api/agents/${encodeURIComponent(agentId)}/log`, { cache:"no-store" });
    const data = await response.json();
    output.textContent = typeof data.log === "string" ? data.log : JSON.stringify(data.log ?? data, null, 2);
    output.classList.add("show"); button.textContent = "Hide log";
  } catch (error) {
    output.textContent = `Could not load log: ${error.message}`; output.classList.add("show"); button.textContent="Hide log";
  } finally { button.disabled = false; }
}

document.addEventListener("click", event => {
  const work = event.target.closest("[data-toggle-work]");
  if (work) { const id=work.dataset.toggleWork; state.openWork.has(id)?state.openWork.delete(id):state.openWork.add(id); renderWork(); return; }
  const agent = event.target.closest("[data-toggle-agent]");
  if (agent) { const id=agent.dataset.toggleAgent; state.openAgents.has(id)?state.openAgents.delete(id):state.openAgents.add(id); renderAgents(); return; }
  const log = event.target.closest("[data-log-agent]");
  if (log) loadLog(log.dataset.logAgent, log);
});
$("search").addEventListener("input", render);
$("filter").addEventListener("change", render);
$("refresh").addEventListener("click", ()=>{ clearTimeout(state.timer); refresh(); });
document.addEventListener("visibilitychange", ()=>{ clearTimeout(state.timer); state.timer=setTimeout(refresh, document.hidden?15000:250); });
refresh();
