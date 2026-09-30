import { spawn as nodeSpawn, execFile as nodeExecFile } from "node:child_process";
import { promisify } from "node:util";

const nodeExecFileAsync = promisify(nodeExecFile);

export function spawnHidden(command, args = [], options = {}) {
  return nodeSpawn(command, args, { ...options, windowsHide: true });
}

export function execFileHidden(command, args = [], options = {}) {
  return nodeExecFileAsync(command, args, { ...options, windowsHide: true });
}
