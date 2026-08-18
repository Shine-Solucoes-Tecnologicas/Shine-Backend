# Backend security checks

The `Backend Security` workflow runs on pull requests and pushes to `main`, and can also be started manually.

## Required checks

- **CodeQL (C#):** performs semantic static analysis with the `security-and-quality` query suite. Findings are published to GitHub code scanning.
- **Gitleaks:** scans Git history with the upstream default rules. Output is redacted by the action and PR comments/artifact uploads are disabled so detected values are not copied to secondary locations.
- **NuGet audit:** evaluates direct and transitive dependencies. Low and Moderate advisories create warnings; High and Critical advisories fail the job. Failure to obtain or parse advisory data also fails the job.

Branch protection should require both `CodeQL (C#)` and `Dependencies and secrets` before merging.

## Exceptions

Security exceptions are a last resort and must be narrow. Every exception must be reviewed in a pull request and recorded in the table below with:

- a responsible owner;
- the rule, package/advisory, or exact Gitleaks fingerprint;
- a technical justification;
- a Jira issue tracking the permanent correction;
- an expiration date no more than 90 days in the future.

Expired exceptions must be removed or renewed through a new review. Broad path exclusions, wildcard secret allowlists, and disabling an entire check are not accepted.

| Scope | Owner | Justification | Jira | Expires |
| --- | --- | --- | --- | --- |
| No active exceptions | — | — | — | — |

For a confirmed Gitleaks false positive, add only its exact fingerprint to `.gitleaksignore` and add the corresponding row above. Do not place the detected value in documentation, commit messages, logs, or Jira.

## Version maintenance

Third-party GitHub Actions are pinned to immutable commit SHAs, with the release tag retained in a comment for readability. Dependabot checks GitHub Actions and NuGet dependencies weekly and opens reviewable pull requests. Action updates must keep immutable SHA pins and pass all required checks.
