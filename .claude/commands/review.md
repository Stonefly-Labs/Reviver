\---

description: Review staged changes for bugs, design issues, and missing tests

\---



Review the currently staged git changes (`git diff --staged`). If nothing is staged, review unstaged changes instead and mention that you did.



Go through the diff and flag issues in these categories. Skip categories that have nothing worth saying — don't pad.



\## 🐛 Bugs \& correctness

Logic errors, off-by-ones, null/empty edge cases, race conditions, incorrect async handling, swallowed exceptions, resource leaks (undisposed IDisposables, unclosed streams).



\## 🏗️ Design \& architecture

Violations of project conventions in CLAUDE.md. Layering issues (e.g., EF Core leaking into Core). Methods doing too much. Misplaced responsibilities. Premature abstraction or obvious duplication.



\## 🧪 Tests

Missing test coverage for new logic. Tests that assert on implementation rather than behavior. Missing edge cases. Confirm xUnit + NSubstitute are used (flag Moq or MSTest if they snuck in).



\## 🔒 Security \& data

SQL injection risk, unvalidated input, secrets in code, PII in logs, missing authorization checks, unsafe deserialization.



\## ⚡ Performance

N+1 queries, sync-over-async, unnecessary allocations in hot paths, missing `ConfigureAwait` in library code, large objects on the LOH.



\## ✨ Style \& polish

Naming, async suffix conventions, `var` usage, nullable annotations. Keep this section short — only call out things that actually hurt readability.



\---



For each issue:

\- Quote the specific line or block (file:line)

\- Explain \*why\* it's a problem, not just what it is

\- Suggest the fix concretely (show code if it's non-obvious)



End with a one-line verdict: \*\*Ship it\*\*, \*\*Ship with minor fixes\*\*, or \*\*Needs work\*\* — and a sentence on why.



Be direct. If the diff is clean, say so in two sentences and stop. Don't invent issues to seem thorough.

