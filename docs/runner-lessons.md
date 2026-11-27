# Runner lessons (read before fixing any failed task)

Known failure patterns in the task queue and how to fix them. Claude adds to this file whenever a new pattern is solved.

## Task file rules
- `@@replace` old text must be WHOLE lines that occur exactly once, checked IN ORDER (an earlier replace can change the text a later one needs). Files may have CRLF endings: compare with `tr -d '\r'`.
- Title line (second line): no double quotes.
- Files run in alphabetical order; the queue stops at the first failure; unrun files of the batch go to `queue/failed` and must be moved back after a fix.
- A re-queued task needs a new branch letter (WT1b, WT1c ...).

## Build and setup failures
- `setup failed: dotnet ef migrations add X` with "Build failed": setup runs after the section's @@ blocks and before Aider. If the blocks reference code Aider has not written yet (Program.cs calling a new Map...Endpoints), the build fails. Fix: put model changes + migration in their own FIRST section with no `files:` (no Aider), and the wiring in a later section.
- Model change (new column) without a migration makes every Postgres test fail (pending model changes). The migration must be in the same task, in a section that runs before the backend check of the new code.
- `error CS0246 ... could not be found`: a missing using. Aider does not fix this by itself. The spec must list the exact usings (for example `using Microsoft.AspNetCore.Authentication;` for AuthenticationProperties).

## Test failures that are Claude's fault, not Aider's
- An old test expects an exact body or text (toEqual) and the new feature adds a field: update the old test in the same task (example: MenuPage.test.jsx needed `sellUnit: 'piece'`).
- An old test fixture lacks a new settings field, so a preset no longer matches: add the field to the fixture.
- TestAuthHandler gives one role per request (X-Test-Role). For a user with two roles add X-Test-Extra-Role (TestAuthHandler supports it once SUP1 is merged). Authorization runs before custom middleware, so a test user without the endpoint's role gets 403 with an empty body.
- Spec snippets must be correct code: never refer to a variable outside its scope (WT2: `prev` used outside the setCart callback).
- The database is seeded with restaurant 1 (My Restaurant) and outlet 1 (HasData), also in InMemory tests. Shop-PC code that takes the first restaurant gets restaurant 1, so a test that adds its own restaurant (id 2) and expects the shop PC to use it fails (SUP2: support user not found, switched-off setting ignored, login 200). In shop-PC tests change outlet 1's settings and use tenant 1.

## Keep Aider's passed work
- When some steps of a task passed, copy those files from the failed branch's working tree into `@@file` blocks of the re-queued task, so only the failed step is redone.

## Environment
- "working tree not clean": the runner now saves leftovers with git stash by itself.
- Aider "connection refused": Ollama is not running. Nothing to fix in the task; the task fails and is re-queued when Ollama is back.
- Release: `release.req` with the version inside (for example `2.3.2`); it runs only when no task files are waiting.

## Two-strikes rule
- If a task has already failed twice after fixes (for example SUP1b and SUP1c exist in queue/failed), do not re-queue again: move the newest failed file to `tasks/hold`, move any other waiting files back so the queue continues, and message Mahesh once: "<task> parked after 2 fixes, needs Claude".

## Test requirements for every task (Mahesh, 27 Nov)
- Unit tests (xUnit for the server, Vitest for screens) for the happy path AND negative cases: wrong input, missing fields, wrong role (403), other restaurant's data (404 or 403), limits (too long, zero, negative), switched-off settings.
- A Playwright test in e2e/tests/<feature>.spec.ts for every user-facing feature: the main flow plus at least one negative case (an error shown, a blocked action). The E2E tests run on GitHub on every push; a release only goes out when the E2E run for the current master passed.
- Never weaken or delete an existing test to make a task pass; if an old test is wrong because the feature changed it on purpose, update it and say so in the task title.
