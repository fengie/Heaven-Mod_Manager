import test from "node:test";
import assert from "node:assert/strict";
import { buildViewModel } from "../lib/view-model.mjs";

const snapshot = {
  generatedAt:"2026-09-30T22:00:00Z",
  currentMission:"Ship the repo",
  agents:[{id:"a1",role:"support",roleLabel:"Support",status:"running",taskId:"t1",task:"Fix UI",machine:"heaven",branchName:"fix/ui",lastMessage:"Editing",updatedAt:"2026-09-30T21:59:00Z"}],
  federatedAgents:[{agent_id:"a2",provider_id:"chatgpt",role:"reviewer",effective_state:"blocked",task_id:"t2",task:"Review",heartbeat_at:"2026-09-30T21:58:00Z"}],
  tasks:[{id:"t1",objective:"Fix UI",agentId:"a1",status:"running",priority:80,branchName:"fix/ui",updatedAt:"2026-09-30T21:59:00Z"},{id:"t2",objective:"Review",agentId:"a2",status:"blocked",priority:70,updatedAt:"2026-09-30T21:58:00Z"}],
  recentEvents:[{at:"2026-09-30T21:59:30Z",type:"agent.started",agentId:"a1",message:"started"}]
};

const reports = [{schema:"agent-work-report/v1",at:"2026-09-30T21:59:40Z",agentId:"a1",taskId:"t1",phase:"progress",summary:"Half done",progress:60,source:"test"}];

test("merges Agent Control state and explicit reports", () => {
  const view = buildViewModel({snapshot,reports,now:Date.parse("2026-09-30T22:00:00Z")});
  assert.equal(view.schema,"agent-work-reports/view/v1");
  assert.equal(view.stats.activeAgents,1);
  assert.equal(view.stats.blockedAgents,1);
  assert.equal(view.agents.find(x=>x.id==="a1").progress,60);
  assert.equal(view.work.find(x=>x.id==="t1").summary,"Half done");
  assert.equal(view.work[0].id,"t2");
});

test("keeps report-only external agents visible", () => {
  const view = buildViewModel({snapshot:null,reports:[{schema:"agent-work-report/v1",at:"2026-09-30T22:00:00Z",agentId:"outside",taskId:"x",phase:"plan",summary:"Plan",source:"manual"}],now:Date.parse("2026-09-30T22:00:01Z"),sourceError:"offline"});
  assert.equal(view.source.agentControl,"unavailable");
  assert.equal(view.agents[0].id,"outside");
  assert.equal(view.work[0].id,"x");
});
