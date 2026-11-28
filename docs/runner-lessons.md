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
- Tests that sell and then read "today" (shifts, day report, expenses) fail when CI runs at the business-day start hour (4 AM IST = 22:30 UTC): the shift and its bills land in different business days (FIX1, DayCloseTests). New tests of that kind call `TestBusinessDay.AwayFromDayStart(factory)` or set `outlet.BusinessDayStartHour = TestBusinessDay.SafeStartHour(outlet.TimeZone, DateTime.UtcNow);` in their Build().
- A PR the PC passed but that shows `NOT merged (OPEN)` usually failed a GitHub check; this run cannot read GitHub, so tell Mahesh and fix the cause as a task.

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

## In-memory tests miss PostgreSQL column types
- AuditLogs.Details (and other json/jsonb columns) must get valid JSON: use JsonSerializer.Serialize(new { ... }). The in-memory database accepts any text, so only PostgreSQL rejects it (production 500, 28 Nov). Tests that write such columns must parse the value with JsonDocument.Parse, or run in the Postgres test classes.

## Aider rewrites algorithms its own way
- C1: the spec described WithDependencies and Validate in words; Aider threw on a missing dependency instead of adding it, and checked unknown key / duplicate / dependency per key instead of in three passes (3 tries failed). Fix: for small algorithms where order or behaviour matters, put the exact method body in the spec as a code block and say "copy exactly".

## Rush-hour guard
- Off while there are no live customers (Mahesh, 28 Nov 2026): the file is tasks/peak-guard.off. When Mahesh says real customers are live, rename it back to peak-guard.on so releases wait until after 15:00 / 23:00 IST.

## Editing the status file
- Re-read roadmap-status.md just before writing it and change only your own lines; Claude in chat also edits it. A whole-file write from an old copy loses the other side's changes (happened 28 Nov).

## Wrong-type query values gave 500 (UI1b, 28 Nov)
- `?take=abc` on an `int?` parameter: tests run in Development, where minimal APIs throw BadHttpRequestException, and `app.UseExceptionHandler()` turned it into 500. Fixed in Program.cs with `ExceptionHandlerOptions.StatusCodeSelector` (BadHttpRequestException -> its own StatusCode, else 500). A test expecting 400 for a bad query value now works.

## Readonly example pages mislead Aider (UI1b, 28 Nov)
- UI1-page: FeedbackListPage (server answers `{ items, count }`) was readonly, so Aider read `data.items` / `data.count` although the audit API answers a plain array: nothing rendered, 3 tries failed. Fix: give the exact component code ("copy exactly") and do not list an example whose response shape differs.
- Vitest: when the heading is shown during loading, `await findByRole('heading')` does not wait for the data. Wait for loaded content (`await screen.findAllByRole('row')`) before synchronous getAllBy queries.
