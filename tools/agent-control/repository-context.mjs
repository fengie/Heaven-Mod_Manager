import path from "node:path";
import { fileURLToPath } from "node:url";
import { buildRepositoryBootstrap, findRepositoryContext, readRepositoryContext } from "./lib/repository-bootstrap.mjs";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const flags = {};
const help = "Use --document PATH --sha256 HASH [--line N --lines N | --search TEXT [--results N] | --heading TEXT [--results N]], or --role ROLE for a local bootstrap.";
try {
  for (let index = 2; index < process.argv.length; index += 2) {
    const key = process.argv[index];
    if (!["--document", "--sha256", "--line", "--lines", "--role", "--search", "--heading", "--results"].includes(key) || !process.argv[index + 1]) throw new Error(help);
    flags[key] = process.argv[index + 1];
  }
  const navigationMode = flags["--search"] ? "search" : flags["--heading"] ? "heading" : null;
  const contextFlags = ["--sha256", "--line", "--lines", "--search", "--heading", "--results"];
  if (!flags["--document"] && contextFlags.some(key => flags[key])) throw new Error("Context retrieval flags require --document.");
  if (flags["--search"] && flags["--heading"]) throw new Error("Choose either --search or --heading, not both.");
  if (navigationMode && [flags["--line"], flags["--lines"]].some(Boolean)) throw new Error("Context navigation cannot be combined with pagination flags.");
  if (flags["--results"] && !navigationMode) throw new Error("--results requires --search or --heading.");

  const result = flags["--document"]
    ? navigationMode
      ? findRepositoryContext({
          root,
          document: flags["--document"],
          expectedSha256: flags["--sha256"],
          query: flags[navigationMode === "search" ? "--search" : "--heading"],
          mode: navigationMode,
          maxResults: Number(flags["--results"] || 20)
        })
      : readRepositoryContext({
          root,
          document: flags["--document"],
          expectedSha256: flags["--sha256"],
          startLine: Number(flags["--line"] || 1),
          maxLines: Number(flags["--lines"] || 120)
        })
    : await buildRepositoryBootstrap({ root, role: flags["--role"] || "support" });
  console.log(JSON.stringify(result));
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
}
