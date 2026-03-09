---
description: 'Agent for implementing and reviewing PSContentPath feature - configurable user content paths for PowerShell modules, scripts, help files, and profiles.'
tools: [vscode, execute, read, agent, edit, search, web, todo]
---

# PSContentPath Feature Agent

## Purpose

This agent assists with implementing, reviewing, and maintaining the PSContentPath infrastructure feature. It provides guidance on:
- Adding or modifying PSContentPath-related code
- Implementing the cmdlets: `Get-PSContentPath`, `Set-PSContentPath`
- Ensuring consistent path resolution across the codebase
- Reviewing changes for data safety and backward compatibility

## When to Use This Agent

- Implementing new functionality related to user content paths
- Modifying module, script, help, or profile path resolution
- Reviewing PRs that touch PSContentPath infrastructure
- Debugging path resolution issues
- Adding PSResourceGet integration points

## When NOT to Use This Agent

- General PowerShell cmdlet development unrelated to content paths
- PowerShellGet compatibility work (this feature is PSResourceGet-only)
- System-wide installation paths (AllUsers scope)

---

## Architecture Overview

### Path Resolution Order

The centralized API `Utils.GetPSContentPath()` resolves paths in this order:

1. **Configuration File**: `powershell.config.json` PSContentPath setting (set via `Set-PSContentPath`)
2. **Platform Defaults**:
   - **Windows**: `Documents\PowerShell` (to avoid breaking existing profiles/help)
   - **Unix/macOS**: `~/.local/share/powershell`

> **Note:** `$env:PSUserContentPath` is NOT a resolution source. It is an **output** — the environment variable is updated when the PSContentPath changes (e.g., via `Set-PSContentPath`) so child processes inherit it.
>
> **Future plan:** The default on Windows will change to `$env:LOCALAPPDATA\PowerShell` in a future release once a proper migration story is in place. Changing the default now would break users whose profiles and help files are in `Documents\PowerShell`.

### Content Path Ramifications

Changing the default from `Documents\PowerShell` to `LocalAppData\PowerShell` (or a custom path) affects **all** content that uses `Utils.GetPSContentPath()` as its base:

| Content Type | Path | Impact |
|-------------|------|--------|
| **Modules** | `{PSContentPath}\Modules` | Primary module install location. Legacy `Documents\PowerShell\Modules` is kept in `$env:PSModulePath` as a fallback so existing PowerShellGet-installed modules continue to work. |
| **Profiles** | `{PSContentPath}\profile.ps1` | User profiles move to the new location. Existing profiles in Documents will NOT be loaded automatically — users need to migrate or create new ones. |
| **Help** | `{PSContentPath}\Help` | Updatable help files stored per-user move to the new location. |
| **Scripts** | `{PSContentPath}\Scripts` | PSResourceGet script install location. |

### Key Internal Variables

| Variable | Description |
|----------|-------------|
| `Platform.DefaultPSContentDirectory` | Current default (Documents\PowerShell on Windows) — used to avoid breaking existing setups |
| `Platform.LocalAppDataPSContentDirectory` | Future default location (LocalAppData\PowerShell on Windows) — users can opt in via `Set-PSContentPath` |
| `$env:PSUserContentPath` | Environment variable **set** (not read) when PSContentPath changes, for child process inheritance |

### Cmdlet Summary

| Cmdlet | Purpose | Key Parameters |
|--------|---------|----------------|
| `Get-PSContentPath` | Get current content path | Returns `DirectoryInfo` with `ConfigFile` NoteProperty |
| `Set-PSContentPath` | Configure custom path | `-Path` (single absolute path), `-Default` (reset), `ConfirmImpact=High` |

---

## Implementation Guidelines

### 1. Always Use the Centralized API

```csharp
// ✅ CORRECT: Use centralized API
string contentPath = Utils.GetPSContentPath();

// ❌ WRONG: Don't hardcode paths
string contentPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PowerShell");
```

### 2. Path Parameter Conventions

Follow existing PowerShell cmdlet patterns:
- Use `-Path` for source (matches `Move-Item`, `Copy-Item`)
- Use `-Destination` for target
- Use `-LiteralPath` when wildcards should not be expanded

### 3. Error Handling for File Operations

Implement robust error handling with recovery guidance:

```csharp
// ✅ CORRECT: Separate phases with clear error handling
// Phase 1: Copy (fail fast if this fails)
// Phase 2: Delete source (collect errors, don't fail fast)
// Phase 3: Update config (only if previous phases succeeded)

// ❌ WRONG: Update config before operations complete
PowerShellConfig.Instance.SetPSContentPath(destinationPath);
// ... then do file operations that might fail
```

### 4. Coexistence with Old Paths

The old Documents path must continue to work for **modules**:
- PowerShellGet does NOT support PSContentPath
- Users may have modules in both old and new locations
- `$env:PSModulePath` includes the legacy Documents\PowerShell\Modules as a fallback
- Module resolution checks the PSContentPath location first, then falls back to Documents

> **Important:** Profile and help paths do NOT fall back to the old Documents location.
> Only module resolution has this dual-path behavior. Users who change their PSContentPath
> need to migrate or recreate their profiles in the new location.

### 5. Avoid ScriptBlock Invocation for File Operations

Use direct C# file operations instead of invoking PowerShell cmdlets:

```csharp
// ✅ CORRECT: Direct C# operations
foreach (var file in Directory.GetFiles(sourcePath))
{
    File.Copy(file, Path.Combine(destPath, Path.GetFileName(file)), overwrite: true);
}

// ❌ AVOID: ScriptBlock invocation (fragile, harder to debug)
var copyCmd = InvokeCommand.NewScriptBlock(@"Copy-Item ...");
copyCmd.InvokeWithContext(...);
```

---

## Code Review Checklist

When reviewing PSContentPath-related changes:

### Data Safety
- [ ] No data loss scenarios if operations partially fail
- [ ] Config updates happen AFTER successful file operations
- [ ] Clear recovery instructions provided on partial failure
- [ ] Source not deleted until destination is verified

### API Consistency
- [ ] Uses `Utils.GetPSContentPath()` instead of hardcoded paths
- [ ] Parameter names match PowerShell conventions (`-Path`, `-Destination`)
- [ ] Single-value parameters don't accept pipeline input unnecessarily

### Backward Compatibility
- [ ] Old path locations still work
- [ ] No breaking changes to existing module resolution
- [ ] PowerShellGet scenarios unaffected

### Error Handling
- [ ] Appropriate use of terminating vs non-terminating errors
- [ ] Clear error messages with actionable guidance
- [ ] Proper cleanup on failure

---

## Known Issues and Considerations

### From PR Discussion

1. **OneDrive Sync Concerns**: OneDrive replication of developer environments can cause sync conflicts. Users affected by this can opt in to LocalAppData via `Set-PSContentPath -Path "$env:LOCALAPPDATA\PowerShell"`.

2. **PSResourceGet-Only**: This feature only works with PSResourceGet, not PowerShellGet. This is by design but means both module systems will coexist.

3. **Default stays at Documents for now**: Changing the default to LocalAppData would break existing users whose profiles and help files are in `Documents\PowerShell`. The default will change in a future release with a proper migration story.

4. **Profile Migration**: When a user changes their PSContentPath (via `Set-PSContentPath`), existing profiles in the old location are NOT automatically found. Users need to copy/migrate `profile.ps1` and `Microsoft.PowerShell_profile.ps1` to the new location.

5. **Help Migration**: Updatable help files in the old location won't be found after changing PSContentPath. Users can re-run `Update-Help` to populate the new location.

### Technical Debt

- Thread safety for cached path values (use `volatile` or accept eventual consistency)

---

## Key Files

| File | Purpose |
|------|---------|
| `src/System.Management.Automation/engine/Configuration/PSContentCommands.cs` | Cmdlet implementations (`Get-PSContentPath`, `Set-PSContentPath`) |
| `src/System.Management.Automation/engine/Utils.cs` | `GetPSContentPath()` centralized API |
| `src/System.Management.Automation/CoreCLR/CorePsPlatform.cs` | Platform-specific path defaults (`DefaultPSContentDirectory`, `LocalAppDataPSContentDirectory`) |
| `src/System.Management.Automation/engine/PSConfiguration.cs` | `PowerShellConfig` - config file read/write for PSContentPath |
| `src/System.Management.Automation/engine/Modules/ModuleIntrinsics.cs` | `GetPersonalModulePath()`, `GetLegacyPersonalModulePath()`, module path assembly |
| `src/System.Management.Automation/engine/hostifaces/HostUtilities.cs` | Profile path resolution using PSContentPath |
| `src/System.Management.Automation/help/HelpUtils.cs` | Help path resolution using PSContentPath |

---

## Testing Guidance

When testing PSContentPath changes:

1. **Test module paths**: Verify modules work from both LocalAppData (new) and Documents (legacy) locations
2. **Test config persistence**: Set path via `Set-PSContentPath`, restart PowerShell, verify it persists
3. **Test env var propagation**: After `Set-PSContentPath`, verify `$env:PSUserContentPath` is updated for child processes
4. **Test profile resolution**: Verify `$PROFILE` points to the PSContentPath location, not Documents
5. **Test help resolution**: Verify `Update-Help -Scope CurrentUser` uses the PSContentPath location
6. **Test failure scenarios**: Simulate permission errors, verify no data loss
7. **Test default behavior**: New install with no config should use `Documents\PowerShell` (current default)

---

## Related Links

- RFC: [PowerShell-RFC#388](https://github.com/PowerShell/PowerShell-RFC/pull/388)
- Community Issue: [#15552](https://github.com/PowerShell/PowerShell/issues/15552)
- PSResourceGet Integration: [PSResourceGet#1912](https://github.com/PowerShell/PSResourceGet/pull/1912)
