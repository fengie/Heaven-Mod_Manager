import test from "node:test";
import assert from "node:assert/strict";
import http from "node:http";
import { loadAgentControlLog, loadAgentControlSnapshot } from "../lib/agent-control-adapter.mjs";

async function withServer(handler, run) {
  const server = http.createServer(handler);
  await new Promise((resolve, reject) => {
    server.once("error", reject);
    server.listen(0, "127.0.0.1", resolve);
  });
  try {
    const address = server.address();
    await run(`http://127.0.0.1:${address.port}`);
  } finally {
    await new Promise(resolve => server.close(resolve));
  }
}

test("reads Agent Control snapshot and log over loopback", async () => {
  await withServer((req, res) => {
    res.setHeader("content-type", "application/json");
    if (req.url === "/api/snapshot") return res.end(JSON.stringify({ ok:true, agents:[{id:"a"}] }));
    if (req.url === "/api/agents/a/log") return res.end(JSON.stringify({ id:"a", tail:"line one\\nline two" }));
    res.statusCode = 404;
    res.end(JSON.stringify({ error:"not found" }));
  }, async base => {
    const snapshot = await loadAgentControlSnapshot({ base });
    const log = await loadAgentControlLog("a", { base });
    assert.equal(snapshot.agents[0].id, "a");
    assert.match(log.tail, /line two/);
  });
});

test("refuses non-loopback Agent Control sources", async () => {
  await assert.rejects(() => loadAgentControlSnapshot({ base:"http://example.com:7331" }), /loopback/i);
});
