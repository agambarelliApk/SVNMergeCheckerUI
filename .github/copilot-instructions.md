# Copilot Instructions — SVNMergeCheckerUI

WinForms app (.NET 8, `net8.0-windows7.0`, `UseWindowsForms=true`, `Nullable`/`ImplicitUsings` enabled). GUI wrapper
around `script/svn_predictive_merge_checker.ps1` (source of truth for merge-analysis logic). Single project, no test
project exists.

## Structure
- `Form1.cs`/`.Designer.cs` — only form; all UI logic (do not hand-edit Designer regions except via designer).
- `AppConfig.cs` — `IConfigService`/`JsonConfigService`.
- `SvnService.cs` — `ISvnService`, wraps `svn.exe` CLI.
- `PowerShellRunnerService.cs` — `IPowerShellRunnerService`, runs the checker script.
- `SvnCheckerHelper.cs` — orchestration (issue lookup, dependency analysis, merge detection, report building); consumes `ISvnService`.
- `SvnCheckerModels.cs` — `SvnCheckerParameters`/`RevisionInfo`/`SvnCheckerResult`.
- `ReportParserService.cs` — parses/pivots raw report text into UI view formats.
- `script/svn_predictive_merge_checker.ps1` — actual SVN analysis logic, invoked as an external process.

## Architectural patterns
- Service-oriented: each concern behind a small `I*` interface with one concrete implementation
  (`IConfigService`, `ISvnService`, `IPowerShellRunnerService`, `IReportParserService`).
- No DI container: services are `new`'d in `Form1`'s constructor and passed via constructor injection to
  orchestration classes (e.g. `SvnCheckerHelper(ISvnService)`). Follow this pattern for new services.
- External processes (`svn.exe`, `powershell.exe`) always via `Process` with `UseShellExecute = false`,
  redirected stdout/stderr, `CreateNoWindow = true`. Preserve for any new process invocations.
- Long-running work is async/cancellable (`CancellationToken`); progress goes to the UI only via
  `IProgress<string>`, never direct control mutation from background threads.
- Out-of-band signals cross the script/report boundary via marker lines in report text, e.g.
  `##MERGED_REVISIONS:1,2,3`. Preserve this convention and existing section headers
  (e.g. `"1. ELENCO COMPLETO REVISIONI ORDINATO"`) — `SvnCheckerHelper`/`ReportParserService` mirror the
  PowerShell script's output format, so changes to one side must stay in sync with the other.

## Data & persistence
- No database. `AppConfig` profiles persisted as a single JSON dictionary file (`svn_config.json`) next to the
  executable, keyed by `ConfigLabel`, via `System.Text.Json`.
- `JsonConfigService` throws (Italian messages) on validation errors (empty label, duplicate label without
  overwrite) — callers must catch and surface as message boxes.

## UI architecture (WinForms)
- Event handlers follow `btnXxx_Click`, `cmbXxx_SelectedIndexChanged`, etc., matching designer control names.
- Long operations: disable action buttons via `SetActionButtons(bool)` before running, re-enable in `finally`;
  output/status appended via `AppendOutput` helper to `rtbOutput` (RichTextBox).
- `async void btnX_Click` handlers are expected/idiomatic here — keep try/catch inside each handler since
  exceptions can't propagate otherwise.
- Dialogs (`FolderBrowserDialog`, `SaveFileDialog`, `ConfigSelectDialog`) created with `using`, shown via `ShowDialog(this)`.

## Naming & coding conventions
- Interfaces prefixed `I`, paired with one concrete class of the same name minus `I`.
- Private fields `_camelCase`; UI control fields use Designer-generated names.
- Models: `record` for immutable parameter/result bags; plain `class` with `init`/`set` for mutable state.
- Region-style banner comments (`// ---...--- // Section name`) group methods in larger classes — follow this
  instead of another commenting convention.
- User-facing strings and exception messages are in Italian; keep new text consistent.

## Build/test
- Build: `dotnet build SVNMergeCheckerUI.csproj` (or open `SVNMergeCheckerUI.sln` in Visual Studio).
- No automated test project exists — do not assume test infrastructure.

## Constraints for future changes
- Windows-only WinForms app (`net8.0-windows7.0`); do not introduce cross-platform UI assumptions.
- New SVN/process/parsing/persistence functionality must be a new interface + implementation pair, not embedded in `Form1`.
- Keep `script/svn_predictive_merge_checker.ps1` as source of truth for merge-analysis logic.
- Preserve the `##MERGED_REVISIONS:...` marker convention and existing section headers when modifying report
  generation or parsing.

## Workflow Directive for Task Management
When the user asks to work on the project or says "prossimo task":
1. ALWAYS read `TASKS.md` first to find the highest priority unchecked item (`- [ ]`).
2. Cross-reference the code files mentioned in that task (e.g., SvnService.cs).
3. Apply the coding standards defined in this file (async over sync, separate concerns).
4. After generating the code changes, explicitly remind the user: "Task completato? Se sì, dimmi 'aggiorna task' per spuntare la checkbox."
5. When the user confirms, generate the updated `TASKS.md` content with `- [x]` replacing `- [ ]` for that specific line and add a short comment with the changes's timestamp.

## Strict Confirmation, Validation & Autonomous Build Protocol
**CRITICAL**: You MUST NEVER apply code changes directly without confirmation. You MUST NEVER mark tasks as done (`- [x]`) without a successful validation (build + tests).
Follow this **5-step workflow**:
1. **Propose (Code)**: Identify the task. Draft the exact code changes (diff or code block) in the chat. Explain the rationale.
2. **Confirm (Apply Code)**: Ask: *"Do you approve this code? Shall I apply it?"* 
   - ONLY apply the code to the project files after explicit user confirmation (e.g., "Yes", "Apply").
3. **Autonomous Validation Attempt (The Build & Test Phase)**:
   - After applying the code, **DO NOT** update `TASKS.md` yet.
   - Suggest the exact terminal commands to validate the change. Example: 
     > *"To validate this fix, please run the following commands in the terminal and paste the output here:* 
     > `dotnet build` 
     > `dotnet test --filter "FullyQualifiedName~SvnServiceTest"` *"*
   - **If the user pastes the terminal output**: Analyze it. Look for "Build succeeded" and "Passed!".
     - If **successful**: Proceed to step 4.
     - If **failed**: Do NOT proceed. Show the error, propose a fix, and go back to Step 1 (Propose a fix).
   - **If the user explicitly says "I have executed the tests and they are green"**: Accept this as manual validation and proceed to step 4.
4. **Validation Confirmation**: Explicitly state: 
   > *"? Validation successful (Build passed / Tests passed). Task is effectively completed."*
5. **Update TASKS.md (Final Step)**:
   - ONLY now, change the task checkbox from `- [ ]` to `- [x]`.
   - Add a completion note: `- ? Completed and validated on [date]`.
   - Propose the updated `TASKS.md` content to the user and ask: *"Shall I apply this update to TASKS.md?"*
   - Apply the update only after receiving explicit consent for this specific file change.