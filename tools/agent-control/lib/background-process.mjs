import { spawn as nodeSpawn, execFile as nodeExecFile } from "node:child_process";
import { promisify } from "node:util";

const nodeExecFileAsync = promisify(nodeExecFile);

export function hiddenWindowsOptions(options = {}) {
  return { ...options, windowsHide: true };
}

export function spawnHidden(command, args = [], options = {}) {
  return nodeSpawn(command, args, hiddenWindowsOptions(options));
}

export function execFileHidden(command, args = [], options = {}) {
  return nodeExecFileAsync(command, args, hiddenWindowsOptions(options));
}
