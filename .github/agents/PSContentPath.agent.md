---
description: 'Agent for implementing and reviewing PSContentPath feature - configurable user content paths for PowerShell modules, scripts, help files, and profiles.'
tools: [vscode, execute, read, agent, edit, search, web, todo]
---

# PSContentPath Feature Agent

## Purpose

This agent assists with implementing, reviewing, and maintaining the PSContentPath infrastructure feature. It provides guidance on:
- Adding or modifying PSContentPath-related code
- Implementing the cmdlets: `Get-PSContentPath`, `Set-PSContentPath`, `Move-PSContent`
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

1. **Environment Variable**: `$env:PSUserContentPath` (if set)
2. **Configuration File**: `powershell.config.json` PSContentPath setting
3. **Platform Defaults**:
   - **Windows**: `$env:LOCALAPPDATA\PowerShell` (primary), then `Documents\PowerShell` (OneDrive fallback)
   - **Unix/macOS**: `~/.local/share/powershell`

### Key Internal Variables

| Variable | Description |
|----------|-------------|
| `Platform.DefaultPSContentDirectory` | Platform default (Documents\PowerShell on Windows) |
| `Platform.LocalAppDataPSContentDirectory` | LocalAppData location (Windows only) |
| `PSUserContentPathEnvVar` | Environment variable name for override |

### Cmdlet Summary

| Cmdlet | Purpose | Key Parameters |
|--------|---------|----------------|
| `Get-PSContentPath` | Get current content path | `-Size` (show directory size info) |
| `Set-PSContentPath` | Configure custom path | `-Path` (single absolute path) |
| `Move-PSContent` | Migrate content between locations | `-Path`, `-Destination`, `-Copy`, `-Force` |

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

The old path scheme must continue to work:
- PowerShellGet does NOT support PSContentPath
- Users may have modules in both old and new locations
- Module resolution should check both paths

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

1. **Migration Cmdlets Debate**: Some reviewers question whether `Move-PSContent` is necessary. The old scheme will coexist, so users could just start using the new location without migrating.

2. **OneDrive Sync Concerns**: OneDrive replication of developer environments can cause sync conflicts. The default was changed to LocalAppData to avoid this.

3. **PSResourceGet-Only**: This feature only works with PSResourceGet, not PowerShellGet. This is by design but means both module systems will coexist.

### Technical Debt

- Consider replacing ScriptBlock invocation in `Move-PSContent` with direct C# file operations
- Thread safety for cached path values (use `volatile` or accept eventual consistency)

---

## Key Files

| File | Purpose |
|------|---------|
| `src/System.Management.Automation/engine/Configuration/PSContentPathCommands.cs` | Cmdlet implementations |
| `src/System.Management.Automation/engine/Utils.cs` | `GetPSContentPath()` API |
| `src/System.Management.Automation/engine/Platform.cs` | Platform-specific path defaults |
| `src/System.Management.Automation/engine/Configuration/PowerShellConfig.cs` | Config file handling |
| `src/System.Management.Automation/help/HelpUtils.cs` | Help path resolution using PSContentPath |

---

## Testing Guidance

When testing PSContentPath changes:

1. **Test both paths exist**: Verify modules work from old and new locations
2. **Test environment variable override**: Set `$env:PSUserContentPath` and verify it takes precedence
3. **Test config persistence**: Set path via cmdlet, restart PowerShell, verify it persists
4. **Test migration scenarios**: Move content, verify destination, check source cleanup
5. **Test failure scenarios**: Simulate permission errors, verify no data loss

---

## Related Links

- RFC: [PowerShell-RFC#388](https://github.com/PowerShell/PowerShell-RFC/pull/388)
- Community Issue: [#15552](https://github.com/PowerShell/PowerShell/issues/15552)
- PSResourceGet Integration: [PSResourceGet#1912](https://github.com/PowerShell/PSResourceGet/pull/1912)
