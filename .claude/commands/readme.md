\---

description: Generate a new README or surgically update an existing one based on current repo state

\---



Generate or update `README.md` for this repository. Detect which mode to use based on whether a README already exists.



\## Mode detection



Check if `README.md` exists at the repo root.



\- \*\*No README\*\* → run \*\*Generation mode\*\*

\- \*\*README exists\*\* → run \*\*Update mode\*\*

\- \*\*README exists but user explicitly asked for a rewrite\*\* → run \*\*Generation mode\*\*, but preserve any sections marked with `<!-- preserve -->` HTML comments and any custom sections the user calls out



State which mode you're in at the start of your response so I can stop you if you picked wrong.



\---



\## Generation mode



\### Discovery phase (do this first, silently)



Before writing anything, gather facts from the repo:

\- Project type and stack: read `\\\*.csproj`, `\\\*.sln`, `package.json`, `Dockerfile`, `docker-compose.yml`

\- Entry points: `Program.cs`, `Startup.cs`, top-level executables

\- Existing docs: `CLAUDE.md`, `CONTRIBUTING.md`, `docs/`, XML doc comments on public APIs

\- Tests: how they're organized, how to run them

\- CI: `.github/workflows/`, `azure-pipelines.yml` — these reveal the real build/test/deploy commands

\- License file

\- Recent commits and tags for version context



If something critical is ambiguous (what the project actually \*does\*, who it's for), ask one focused question before proceeding. Don't ask about formatting preferences — use your judgment.



\### Structure



Use this skeleton, but \*\*omit any section that would be empty or filler\*\*. A short, honest README beats a long one padded with "Contributing: PRs welcome."



1\. \*\*Title + one-line tagline\*\* — what this is, in 15 words or less

2\. \*\*Badges\*\* — build status, NuGet version, license, target framework. Only ones that actually resolve.

3\. \*\*Hero section\*\* — 2-3 sentences on what the project does and why it exists. Include a screenshot, GIF, or code snippet showing the thing in action. For a library, show the smallest possible usage example. For an app, show a screenshot.

4\. \*\*Features\*\* — bulleted, concrete, no marketing fluff. "Streams responses token-by-token" not "Blazing fast performance."

5\. \*\*Quick start\*\* — copy-pasteable commands that get someone from zero to running in under 60 seconds. Test these mentally — if a step assumes prior knowledge, spell it out or link it.

6\. \*\*Installation\*\* — separate from quick start if there are multiple install methods (NuGet, source, Docker)

7\. \*\*Usage\*\* — realistic examples, not toy ones. Show the 2-3 most common things people will actually do. Include expected output where useful.

8\. \*\*Configuration\*\* — env vars, appsettings keys, CLI flags. Use a table: Name | Default | Description.

9\. \*\*Architecture\*\* (only for non-trivial projects) — short prose + optional Mermaid diagram. How the pieces fit, where to start reading.

10\. \*\*Development\*\* — how to build, test, lint, run locally. Pull commands from CI configs to ensure they're real.

11\. \*\*Contributing\*\* — link to CONTRIBUTING.md if it exists, otherwise a short paragraph. Skip entirely if this is a personal/closed project.

12\. \*\*License\*\* — one line, link to LICENSE file.



After the title/tagline, insert this comment so future runs can date-bound their diff:



```html

<!-- README last reviewed: YYYY-MM-DD -->

```



Use today's date.



\---



\## Update mode



The goal is \*\*surgical edits that keep the README accurate\*\*, not a rewrite. Preserve voice, structure, custom sections, and anything project-specific.



\### Diff phase



1\. Read the current `README.md` fully. Note its structure, voice, and any custom sections.

2\. Look for `<!-- README last reviewed: YYYY-MM-DD -->`. If found, run `git log --since="<that date>" --stat` to see what's changed. If not found, run `git log --since="3 months ago" --stat` as a fallback and add the comment as part of your edits.

3\. Re-run the relevant parts of the discovery phase to learn current reality:

&#x20;  - Stack, target frameworks, dependencies

&#x20;  - Build/test/run commands (cross-check against CI configs)

&#x20;  - Public API surface, CLI flags, endpoints

&#x20;  - Configuration keys (`appsettings.json`, env vars)

&#x20;  - Referenced files and paths



\### Verification checklist



Walk through the existing README and check each claim against current reality:



\- \[ ] Install commands still work as written

\- \[ ] Quick start commands still work as written

\- \[ ] Version numbers, target frameworks, dependency versions are current

\- \[ ] All referenced files/paths exist

\- \[ ] Documented features are still present in code

\- \[ ] Documented config keys match current usage

\- \[ ] CLI flags / API endpoints match current code

\- \[ ] Screenshots/GIFs aren't obviously stale (flag for human review — you can't judge images)



\### Gap analysis



Identify things in the code that \*should\* be in the README but aren't:



\- New public APIs / endpoints / CLI commands added since last review

\- New config options

\- New install methods (e.g., a Dockerfile was added)

\- Major dependencies added or removed

\- Breaking changes implied by recent commits (look for `feat!:`, `BREAKING CHANGE`, major version bumps)



\### Edit rules



\- \*\*Preserve voice and structure.\*\* Match the existing tone. Don't reorganize sections unless something is genuinely broken or misleading.

\- \*\*Preserve custom sections.\*\* Sponsors, acknowledgments, project-specific intros, anything in `<!-- preserve -->` comments — leave alone.

\- \*\*Make minimal targeted edits.\*\* Change the line, not the section. Add a row, not a table.

\- \*\*Never silently delete.\*\* If a feature looks gone, flag it for review instead of removing the docs. Code might have moved.

\- \*\*Update the review date\*\* to today's date at the end.



\### Output



After making edits, print a changelog of exactly what changed:

