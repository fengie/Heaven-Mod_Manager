import path from "node:path";
import { fileURLToPath } from "node:url";
import { buildRepositoryBootstrap, readRepositoryContext } from "./lib/repository-bootstrap.mjs";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const flags = {};
try {
  for (let index = 2; index < process.argv.length; index += 2) {
    const key = process.argv[index];
    if (!["--document", "--sha256", "--line", "--lines", "--role"].includes(key) || !process.argv[index + 1]) throw new Error("Use --document PATH --sha256 HASH [--line N --lines N], or --role ROLE for a local bootstrap.");
    flags[key] = process.argv[index + 1];
  }
  if (!flags["--document"] && ["--sha256", "--line", "--lines"].some(key => flags[key])) throw new Error("Context pagination flags require --document.");
  const result = flags["--document"]
    ? readRepositoryContext({ root, document: flags["--document"], expectedSha256: flags["--sha256"], startLine: Number(flags["--line"] || 1), maxLines: Number(flags["--lines"] || 120) })
    : await buildRepositoryBootstrap({ root, role: flags["--role"] || "support" });
  console.log(JSON.stringify(result, null, 2));
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
}
