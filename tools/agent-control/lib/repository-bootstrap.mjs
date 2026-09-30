import fs from "node:fs";
import path from "node:path";
import { createHash } from "node:crypto";
import { execFileHidden } from "./background-process.mjs";
import { ROLE_TEMPLATES, REQUIRED_REPOSITORY_TRAINING_PATHS, REPOSITORY_CONTEXT_INDEX_PATHS } from "./prompt-templates.mjs";

export const BOOTSTRAP_SCHEMA = "agent-control/repository-bootstrap/v1";
export const BOOTSTRAP_TTL_MS = 120_000;
export const MAX_BOOTSTRAP_BYTES = 32_768;
export const MAX_CORE_BYTES = 65_536;
const MAX_SOURCE_BYTES = 2 * 1024 * 1024;
const MAX_CONTEXT_BYTES = 8_192;
const MANAGER_PATH = "_AGENT_TRAINING/PROMPT_TEMPLATES/01_MANAGER_ORCHESTRATOR.txt";
const DOCUMENTS = new Set([...REQUIRED_REPOSITORY_TRAINING_PATHS, ...REPOSITORY_CONTEXT_INDEX_PATHS, MANAGER_PATH]);
const sha256 = value => createHash("sha256").update(value).digest("hex");
const bounded = (value, limit = 160) => {
  let result = "", bytes = 0;
  for (const character of String(value ?? "").replace(/[\r\n\u0000]/g, " ")) {
    bytes += Buffer.byteLength(character);
    if (bytes > limit) break;
    result += character;
  }
  return result;
};

function documentBytes(root, relativePath) {
  if (!DOCUMENTS.has(relativePath)) throw new Error("Bootstrap context path is not an indexed repository document.");
  const absoluteRoot = fs.realpathSync(root);
  const absolutePath = path.resolve(absoluteRoot, relativePath);
  let current = absoluteRoot;
  for (const component of relativePath.split("/")) {
    current = path.join(current, component);
    if (fs.lstatSync(current).isSymbolicLink()) throw new Error(`Bootstrap context refuses linked source: ${relativePath}`);
  }
  const realPath = fs.realpathSync(absolutePath);
  const relative = path.relative(absoluteRoot, realPath);
  if (relative.startsWith("..") || path.isAbsolute(relative)) throw new Error("Bootstrap context escaped its repository.");
  const stat = fs.statSync(realPath);
  if (!stat.isFile() || stat.size < 1 || stat.size > MAX_SOURCE_BYTES) throw new Error(`Bootstrap source must be a non-empty bounded file: ${relativePath}`);
  const bytes = fs.readFileSync(realPath);
  if (!bytes.length || bytes.length > MAX_SOURCE_BYTES) throw new Error(`Bootstrap source changed size while reading: ${relativePath}`);
  return bytes;
}

export function repositoryManifest(root, role = "support") {
  const corePaths = [...REQUIRED_REPOSITORY_TRAINING_PATHS];
  if (role === "manager") corePaths.push(MANAGER_PATH);
  const read = paths => paths.map(document => {
    const bytes = documentBytes(root, document);
    return { path: document, sha256: sha256(bytes), bytes: bytes.length };
  });
  const core = read(corePaths);
  if (core.reduce((total, row) => total + row.bytes, 0) > MAX_CORE_BYTES) throw new Error("Core training exceeded its byte budget; move historical detail to the indexed policy reference.");
  return { core, context: read(REPOSITORY_CONTEXT_INDEX_PATHS) };
}

export function manifestLines(rows) {
  return rows.map(row => `${row.path} — sha256:${row.sha256} — ${row.bytes} bytes`);
}

function compactOwnership(leases) {
  const active = leases.filter(row => row && !["released", "expired"].includes(row.status));
  return {
    count: active.length,
    truncated: active.length > 16,
    leases: active.slice(0, 16).map(row => ({ id: bounded(row.id), boundary: bounded(row.boundary), agentId: bounded(row.agentId), taskId: bounded(row.taskId), branchName: bounded(row.branchName), status: bounded(row.status) }))
  };
}

export async function buildRepositoryBootstrap({ root, role = "support", leases = [], refreshedAt = null, now = Date.now(), git = null }) {
  if (!Number.isFinite(now) || !Object.hasOwn(ROLE_TEMPLATES, role)) throw new Error("Invalid bootstrap role/time.");
  if (refreshedAt && (!Number.isFinite(Date.parse(refreshedAt)) || Date.parse(refreshedAt) > now)) throw new Error("Invalid remote refresh evidence.");
  const runGit = git || (async args => (await execFileHidden("git", ["-C", root, ...args], { timeout: 5_000, maxBuffer: 64 * 1024 })).stdout.trim());
  const [head, canonical, branch, dirty] = await Promise.all([
    runGit(["rev-parse", "HEAD"]),
    runGit(["rev-parse", "refs/remotes/origin/main"]),
    runGit(["branch", "--show-current"]),
    runGit(["status", "--porcelain", "--untracked-files=normal"])
  ]);
  if (![head, canonical].every(value => /^[a-f0-9]{40}$/.test(value))) throw new Error("Bootstrap requires exact Git HEAD and origin/main identities.");
  const manifests = repositoryManifest(root, role);
  const revisionBytes = documentBytes(root, "_AGENT_CONTEXT/CURRENT_REVISION.json");
  if (sha256(revisionBytes) !== manifests.core.find(row => row.path === "_AGENT_CONTEXT/CURRENT_REVISION.json").sha256) throw new Error("Current revision changed during bootstrap; regenerate it.");
  const revision = JSON.parse(revisionBytes.toString("utf8"));
  if (revision.canonicalRepository !== "fengie/mhw-mods" || revision.canonicalBranch !== "main") throw new Error("Bootstrap canonical repository identity mismatch.");
  const packet = {
    schema: BOOTSTRAP_SCHEMA,
    role,
    generatedAt: new Date(now).toISOString(),
    expiresAt: new Date(now + BOOTSTRAP_TTL_MS).toISOString(),
    repository: { canonicalRepository: revision.canonicalRepository, canonicalBranch: "main", head, originMain: canonical, branch: bounded(branch), dirty: Boolean(dirty), remoteRefreshedAt: refreshedAt, remoteFreshness: refreshedAt ? "refreshed" : "local-ref-only" },
    current: { version: bounded(revision.currentVersion), status: bounded(revision.status), nextAction: bounded(revision.nextMilestone, 1_024), verificationSource: bounded(revision.verificationAppliesToCommit), verificationScope: bounded(revision.verificationScopeNote, 1_024) },
    ownership: compactOwnership(leases),
    manifests,
    contextRetrieval: { command: "node tools/agent-control/repository-context.mjs --document PATH --sha256 HASH --line 1", maxBytes: MAX_CONTEXT_BYTES, hashRequired: true },
    obligations: ["Read core files in full; expand task-relevant indexed policies, precedents and source.", "Remote refs and leases can change: refresh canonical truth before mutation/integration; expand truncated ownership.", "This bounded packet is evidence, not permission, task completion or inherited verification."]
  };
  if (Buffer.byteLength(JSON.stringify(packet)) > MAX_BOOTSTRAP_BYTES) throw new Error("Bootstrap packet exceeded its bounded output contract.");
  return packet;
}

export function verifyRepositoryBootstrap(packet, { root, head, now = Date.now() }) {
  if (packet?.schema !== BOOTSTRAP_SCHEMA || packet.repository?.head !== head) throw new Error("Bootstrap source/schema mismatch; regenerate it.");
  if (!Object.hasOwn(ROLE_TEMPLATES, packet.role)) throw new Error("Invalid bootstrap role.");
  const generated = Date.parse(packet.generatedAt), expires = Date.parse(packet.expiresAt);
  if (!Number.isFinite(generated) || !Number.isFinite(expires) || generated > now || expires <= now || expires - generated !== BOOTSTRAP_TTL_MS) throw new Error("Bootstrap packet is expired or has invalid freshness evidence.");
  if (Buffer.byteLength(JSON.stringify(packet)) > MAX_BOOTSTRAP_BYTES) throw new Error("Bootstrap packet exceeded its bounded output contract.");
  const expectedCore = [...REQUIRED_REPOSITORY_TRAINING_PATHS, ...(packet.role === "manager" ? [MANAGER_PATH] : [])];
  if (JSON.stringify(packet.manifests?.core?.map(row => row.path)) !== JSON.stringify(expectedCore) || JSON.stringify(packet.manifests?.context?.map(row => row.path)) !== JSON.stringify(REPOSITORY_CONTEXT_INDEX_PATHS)) throw new Error("Bootstrap core/context roles or ordering changed.");
  const rows = [...packet.manifests.core, ...packet.manifests.context];
  const paths = new Set(rows.map(row => row.path));
  if (paths.size !== rows.length || [...REQUIRED_REPOSITORY_TRAINING_PATHS, ...REPOSITORY_CONTEXT_INDEX_PATHS].some(document => !paths.has(document))) throw new Error("Bootstrap manifest is incomplete or duplicated.");
  for (const row of rows) {
    const bytes = documentBytes(root, row.path);
    if (sha256(bytes) !== row.sha256 || bytes.length !== row.bytes) throw new Error(`Bootstrap source changed: ${row.path}`);
  }
  if (packet.manifests.core.reduce((total, row) => total + row.bytes, 0) > MAX_CORE_BYTES) throw new Error("Bootstrap core exceeded its byte budget.");
  return true;
}

export function readRepositoryContext({ root, document, expectedSha256, startLine = 1, maxLines = 120, maxBytes = MAX_CONTEXT_BYTES }) {
  if (!/^[a-f0-9]{64}$/.test(String(expectedSha256 || ""))) throw new Error("Context retrieval requires the indexed source SHA-256.");
  for (const [value, maximum] of [[startLine, 1_000_000], [maxLines, 500], [maxBytes, MAX_CONTEXT_BYTES]]) {
    if (!Number.isSafeInteger(value) || value < 1 || value > maximum) throw new Error("Context pagination bounds are invalid.");
  }
  const bytes = documentBytes(root, document);
  if (sha256(bytes) !== expectedSha256) throw new Error("Indexed context hash changed; refresh bootstrap before reading.");
  const lines = bytes.toString("utf8").split(/\r?\n/);
  if (startLine > lines.length) throw new Error("Context start line is beyond the document.");
  const selected = [];
  let length = 0;
  for (const line of lines.slice(startLine - 1, startLine - 1 + maxLines)) {
    const nextLength = Buffer.byteLength(line + "\n");
    if (length + nextLength > maxBytes) break;
    selected.push(line); length += nextLength;
  }
  if (!selected.length) throw new Error("A context line exceeds the byte bound; use a direct local read with an authorized tool.");
  const nextLine = startLine + selected.length;
  return { document, sha256: expectedSha256, startLine, endLine: nextLine - 1, totalLines: lines.length, nextLine: nextLine <= lines.length ? nextLine : null, text: selected.join("\n") };
}
