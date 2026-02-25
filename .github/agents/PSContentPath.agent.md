---
description: 'Agent for working with PSContentPath integration in PSResourceGet'
tools: [vscode, execute, read, agent, edit, search, web, todo]
---

# PSContentPath Agent

## Feature Summary: PSContentPath Infrastructure (PR #26509)

### Overview
The `PSContentPath` feature introduces a configurable user content path system for PowerShell, allowing users to customize where PowerShell stores user content (modules, scripts, help files, and profiles). This addresses long-standing community requests for flexible content storage, particularly for roaming profiles and containerized environments.

### Related Issues/RFCs
- Community feedback: [#15552](https://github.com/PowerShell/PowerShell/issues/15552)
- PSResourceGet integration: [PowerShell/PSResourceGet#1912](https://github.com/PowerShell/PSResourceGet/pull/1912)
- RFC: [PowerShell/PowerShell-RFC#388](https://github.com/PowerShell/PowerShell-RFC/pull/388)

### Key Changes

#### New Cmdlets
- **`Get-PSContentPath`** - Retrieves the current PowerShell content path as a `DirectoryInfo` object
  - Returns a `System.IO.DirectoryInfo` object with an additional `ConfigFile` NoteProperty
  - The `ConfigFile` property contains the path to the user configuration file
  - No parameters - always returns both the directory and config file information
- **`Set-PSContentPath`** - Sets a custom content path location
  - `-Path` parameter to set a custom path
  - `-Default` parameter to reset to platform default
  - ConfirmImpact is set to High, prompting for confirmation by default

#### Path Defaults
- **Windows**: Changed from `Documents\PowerShell` to `LocalAppData\PowerShell`
- **Fallback logic**: Checks LocalAppData first, then OneDrive

#### Architecture
- New internal variables: `DefaultPSContentDirectory`, `LocalAppDataPSContentDirectory`
- Readonly PowerShell variable: `$PSUserContentPath` exposes the current content path
- Configuration key: `PSUserContentPathConfigKey` for persisting custom path in powershell.config.json
- Centralized API: `Utils.GetPSContentPath()` for all content path references
- Lazy migration from old config directory to new default location

---

## Discussion Summary

### Key Participants
- **@jshigetomi** (Author)
- **@iSazonov** (Reviewer)
- **@kilasuit** (Reviewer)
- **@Copilot** (Automated review)

### Major Discussion Points

#### 1. Migration Cmdlets Necessity (@iSazonov)
**Position**: Helper cmdlets may be unnecessary - just add the new path and install modules there by default.
- Quote: "If all the modules I installed work as before, why should I explicitly move them to another location?"
- The old scheme will coexist with the new one
- Users can roll back from PSResourceGet to PowerShellGet

#### 2. Data Loss Concerns (@Copilot)
**Issue**: Potential data loss if copy succeeds but deletion fails during move operation.
- PSContentPath gets updated to destination before source cleanup completes
- **Author response**: Considering letting users delete manually instead of automatic deletion

#### 3. PSResourceGet vs PowerShellGet Support
- Feature only supported in PSResourceGet, NOT PowerShellGet
- This means old and new schemes will coexist
- @iSazonov argues this means migration tools may create confusion

#### 4. OneDrive Replication Concerns (@iSazonov)
- Skeptical that replicating developer environment via OneDrive is beneficial
- Personal experience: Rejected this scenario due to sync problems
- Believes concerns about changing defaults are "greatly exaggerated"

#### 5. Historical Precedent
@iSazonov cited Install-Module behavior change:
- PowerShellGet 1.x: Default was AllUsers (required elevation)
- PowerShellGet 2.0+: Default is CurrentUser (no elevation)
- This shows "changes in this area did not cause a catastrophe"

### Copilot Code Review Issues
1. **ScriptBlock invocation pattern** - Using `InvokeCommand.NewScriptBlock` with `InvokeWithContext` for Copy-Item is fragile; suggested using direct C# file operations
2. **Error handling** - Terminating error thrown without rollback mechanism if deletion fails after successful copy

### Current Status
- Draft PR
- Labels: `CL-Engine`
- 35 commits, +731/-47 lines across 13 files
- Awaiting review from @adityapatwardhan and @daxian-dbw

---

## Agent Instructions

When working with PSContentPath-related code:

1. **Understand the path resolution order**:
   - Check `PSContentPathConfigKey` in powershell.config.json first
   - Fall back to LocalAppData, then OneDrive on Windows
   - Use platform-specific defaults on Unix
   - The readonly `$PSUserContentPath` variable exposes the resolved path

2. **Use centralized API**:
   - Always use `Utils.GetPSContentPath()` for content path resolution
   - Don't hardcode paths like `Documents\PowerShell`

3. **Configuration reading best practices**:
   - Use `Utils.GetPolicySetting<T>(Utils.SystemWideThenCurrentUserConfig)` to read from both system-wide AND current user config
   - Group Policy takes precedence over config file settings
   - This pattern is demonstrated in `ClrFacade.IsTrustedHost()` (from custom forks)
   - Example pattern:
     ```csharp
     var setting = Utils.GetPolicySetting<SettingType>(Utils.SystemWideThenCurrentUserConfig);
     ```
   - For PSContentPath specifically, currently only reads from `ConfigScope.CurrentUser`
   - Consider if system-wide config should also be checked

4. **Consider coexistence**:
   - Old path locations must continue to work
   - PowerShellGet will NOT support this feature
   - Only PSResourceGet integrates with new paths

5. **Migration considerations**:
   - Lazy migration happens on startup
   - Config files migrate from old location to new location automatically
   - Be cautious about data loss during move operations
