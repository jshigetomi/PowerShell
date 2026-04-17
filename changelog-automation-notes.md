# Changelog Generation: Manual Cleanup Steps to Automate

Notes for the infrastructure agent on edits made by hand to `CHANGELOG/preview.md` for v7.7.0-preview.1, so the `Get-ChangeLog` / release tooling can do them automatically next time.

## Context

- Base tag: `v7.6.0-rc.1`
- Target: master HEAD (`72be78e6` at time of generation)
- Initial generated changelog had 257 PR refs (254 unique).
- After cleanup: 194 unique PR refs.
- Net removed: 60 entries (3 within-file dupes + 29 already-shipped + 31 intermediate dependency bumps − 3 reused PR numbers in the bump set).

---

## Cleanup Step 1 — Filter cherry-picked backports

**Problem:** When a fix lands on master and is later cherry-picked to a `release/*` branch (with a different PR number prefixed `[release/v7.6] ...`), the master-side PR currently shows up in the preview changelog even though its content already shipped under the backport PR number.

**Detection:** Use `git cherry <last-shipped-tag> master`. Lines starting with `-` are master commits whose patch-id matches a commit already on the release branch. They should be excluded.

**Implementation:**

```pwsh
# Get only the truly-new master commits
git cherry v7.6.0-rc.1 master |
    Where-Object { $_ -like '+ *' } |
    ForEach-Object { $_.Substring(2) }
```

**29 PRs removed in this pass for v7.7.0-preview.1:**

```
25571, 25763, 26134, 26193, 26223, 26224, 26233, 26282, 26290, 26291,
26304, 26404, 26412, 26414, 26489, 26491, 26589, 26595, 26602, 26610,
26621, 26690, 26691, 26746, 26753, 26780, 26796, 26845, 26857
```

**Verification:** Spot-checked 8 of these with `git patch-id --stable` against their backport counterparts on `release/v7.6`. All 8 had identical patch IDs (e.g., master #25763 ↔ release #26571 "Properly Expand Aliases", master #26845 ↔ release #26847 "Skip flaky Update-Help test").

**Caveat:** `git cherry` only catches identical patches. If a backport was modified during cherry-pick (conflict resolution, partial pick), it will still show up as `+`. As a secondary filter, exclude any commit whose subject begins with `[release/...]` on the release branch and whose body references the master PR.

---

## Cleanup Step 2 — Deduplicate cross-section listings

**Problem:** PRs that are both Breaking Changes and General Cmdlet Updates were listed in both sections.

**Detection:** Within-file duplicate scan after section assignment.

```pwsh
$content = Get-Content .\CHANGELOG\preview.md -Raw
$prs = [regex]::Matches($content, '#(\d{4,6})\b') | ForEach-Object { $_.Groups[1].Value }
$prs | Group-Object | Where-Object Count -gt 1
```

**Rule:** Each PR appears at most once in the changelog. Section precedence (highest wins):

1. Breaking Changes
2. Engine Updates and Fixes
3. General Cmdlet Updates and Fixes
4. Code Cleanup
5. Performance
6. Tools
7. Tests
8. Build and Packaging Improvements
9. Documentation and Help Content

If a PR qualifies for multiple sections, keep it in the highest-precedence one and drop the others.

**3 PRs removed from "General Cmdlet Updates" because they were already listed under "Breaking Changes":** #26485, #26552, #26668.

---

## Cleanup Step 3 — Consolidate dependabot bumps

**Problem:** Each Dependabot PR generates a bump entry like `Bump <pkg> from <old> to <new>`. Across a release window there can be many incremental bumps for the same dependency. The changelog should show **only the cumulative version range** as a single entry.

**Detection:** Group all `Bump ` entries by package name (the part between `Bump ` and ` from`).

**Rule per dependency group:**

1. Pick the highest-numbered PR (newest bump).
2. Rewrite its version range to `from <oldest "from" version in the group> to <newest "to" version in the group>`.
3. Remove all other bumps for that dependency.

**Implementation sketch:**

```pwsh
$entries = [regex]::Matches($content, '<li>Bump (?<pkg>\S+) from (?<from>\S+) to (?<to>\S+) \(#(?<pr>\d+)\)</li>')
$grouped = $entries | Group-Object { $_.Groups['pkg'].Value }
foreach ($g in $grouped) {
    $sorted = $g.Group | Sort-Object { [int]$_.Groups['pr'].Value }
    $oldestFrom = $sorted[0].Groups['from'].Value
    $newest     = $sorted[-1]
    $newestTo   = $newest.Groups['to'].Value
    $newestPR   = $newest.Groups['pr'].Value
    # Replace newest entry's "from X to Y" with "from $oldestFrom to $newestTo", drop the rest.
}
```

**For v7.7.0-preview.1, this consolidated 37 bumps into 6:**

| Dependency                          | Final range          | PR kept |
|-------------------------------------|----------------------|---------|
| `github/codeql-action`              | 3.30.3 → 4.35.1      | #27120  |
| `actions/checkout`                  | 4 → 6.0.2            | #27206  |
| `actions/upload-artifact`           | 4 → 7                | #26914  |
| `actions/dependency-review-action`  | 4.7.3 → 4.9.0        | #26938  |
| `actions/setup-dotnet`              | 4 → 5                | #26327  |
| `ossf/scorecard-action`             | 2.4.2 → 2.4.3        | #26128  |

**31 intermediate bump PRs removed:**

```
26159, 26183, 26184, 26248, 26249, 26263, 26264, 26273, 26308, 26309,
26328, 26359, 26421, 26451, 26484, 26505, 26527, 26554, 26586, 26615,
26616, 26623, 26686, 26726, 26741, 26755, 26839, 26861, 26879, 26942, 27087
```

---

## Cleanup Step 4 — Tag base branch correctness

**Problem:** Default `Get-ChangeLog` may use the most recent tag on the current branch. For preview-line releases cut from `master`, that's wrong if the most recent tag lives on a `release/*` branch.

**Rule:** For a `vX.Y.0-preview.N` cut from `master`, the diff base should be the most recent **previous-line tag that the changelog should be relative to** (typically the latest `vX.(Y-1).0-rc.*` or `vX.(Y-1).0`). Use `git cherry <base> master` not `git log <base>..master`.

For v7.7.0-preview.1 the correct base is `v7.6.0-rc.1`.

---

## Verification script

Drop-in sanity check that should be run after generation:

```pwsh
$content = Get-Content .\CHANGELOG\preview.md -Raw
$prs = [regex]::Matches($content, '#(\d{4,6})\b') | ForEach-Object { $_.Groups[1].Value }

# 1. No within-file duplicates
$dupes = $prs | Group-Object | Where-Object Count -gt 1
if ($dupes) { throw "Duplicate PR refs: $($dupes.Name -join ', ')" }

# 2. No PRs already shipped in any prior CHANGELOG
$blob = (Get-ChildItem .\CHANGELOG\*.md -Exclude preview.md, README.md | Get-Content -Raw) -join "`n"
$shipped = $prs | Where-Object { $blob -match "#$_\b" }
if ($shipped) { throw "Already-shipped PRs present: $($shipped -join ', ')" }

# 3. No more than one bump entry per dependency
$bumps = [regex]::Matches($content, '<li>Bump (\S+) from')
$bumpGroups = $bumps | Group-Object { $_.Groups[1].Value } | Where-Object Count -gt 1
if ($bumpGroups) { throw "Multiple bumps for: $($bumpGroups.Name -join ', ')" }

# 4. Truly-new commit count matches (sanity)
$expected = (git cherry v7.6.0-rc.1 master | Where-Object { $_ -like '+ *' }).Count
"Expected truly-new commits: $expected (file currently has $(($prs | Sort-Object -Unique).Count) PR refs after bump consolidation)"
```

---

## Suggested `Get-ChangeLog` enhancements

1. **Add a `-DedupeAgainstReleaseBranch <branchPattern>` switch** that runs `git cherry` and excludes `-` lines.
2. **Add a `-ConsolidateDependencyBumps` switch** that groups `Bump <pkg>` entries and emits only the cumulative version per dependency.
3. **Enforce single-section assignment** — if a PR matches multiple section labels, choose the highest-precedence section per the ordered list above.
4. **Validate against prior changelogs** — fail the build if a PR number in `preview.md` already appears in any sibling `CHANGELOG/*.md`.
