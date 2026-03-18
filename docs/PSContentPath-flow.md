# PSContentPath Resolution & Configuration Flow

## Config File Location — Startup Resolution (Windows)

```mermaid
flowchart TD
    A["PowerShellConfig<br/>initialization"] --> B{"Config file exists in<br/>LocalAppData?<br/>%LOCALAPPDATA%\\PowerShell\\<br/>powershell.config.json"}
    B -->|Yes| C["Use LocalAppData<br/>as config directory"]
    B -->|No| D{"Config file exists in<br/>Documents / OneDrive?<br/>%PERSONAL%\\PowerShell\\<br/>powershell.config.json"}
    D -->|Yes| E["Use Documents path<br/>(backwards compatibility)"]
    E --> F["Send telemetry<br/>(UserConfigLocation:Documents)"]
    D -->|No| G["Default to LocalAppData<br/>for new config files"]

    C --> H["perUserConfigFile set"]
    F --> H
    G --> H

    style C fill:#d4edda
    style E fill:#fff3cd
    style G fill:#d4edda
```

> **Note:** On Unix/macOS, both locations resolve to the same XDG data directory (`~/.local/share/powershell`), so there is no fallback chain.

## GetPSContentPath — Resolution Flow

```mermaid
flowchart TD
    A["Caller invokes<br/>Utils.GetPSContentPath()"] --> B["PowerShellConfig.Instance<br/>.GetPSContentPath()"]
    B --> C{"Read PSUserContentPath<br/>from powershell.config.json<br/>(CurrentUser scope)"}
    C -->|"Key found &<br/>non-empty"| D["Expand environment<br/>variables in path"]
    D --> E["Send telemetry flag<br/>(CustomPSContentPath)"]
    E --> F["Return custom path"]
    C -->|"Key missing<br/>or empty"| G{"Platform?"}
    G -->|Windows| H["Platform.DefaultPSContentDirectory<br/>= Documents\\PowerShell"]
    G -->|Unix / macOS| I["Platform.DefaultPSContentDirectory<br/>= ~/.local/share/powershell"]
    H --> J["Return platform default"]
    I --> J
```

## Set-PSContentPath — Configuration Flow

```mermaid
flowchart TD
    A["User runs<br/>Set-PSContentPath"] --> B{"Parameter set?"}
    B -->|"-Path &lt;string&gt;"| C["Expand environment<br/>variables in path"]
    B -->|"-Default"| K["path = null<br/>(reset to default)"]

    C --> D{"Validate path"}
    D -->|"Invalid chars or<br/>not rooted"| E["Throw terminating<br/>error"]
    D -->|"Valid"| F{"Directory<br/>exists?"}
    F -->|No| G["Write warning<br/>(non-terminating)"]
    F -->|Yes| H["Continue"]
    G --> H

    K --> L["PowerShellConfig.Instance<br/>.SetPSContentPath(null)"]
    L --> M["RemoveValueFromFile<br/>(CurrentUser scope)"]
    M --> N["Update cached JObject"]

    H --> O["ShouldProcess<br/>confirmation<br/>(ConfirmImpact=High)"]
    O -->|Denied| Z["No-op"]
    O -->|Confirmed| P["PowerShellConfig.Instance<br/>.SetPSContentPath(path)"]
    P --> Q["WriteValueToFile<br/>(CurrentUser scope)"]
    Q --> R["Acquire write lock"]
    R --> S["Open FileStream<br/>(FileShare.None)"]
    S --> T["Read existing JSON,<br/>modify key, write back"]
    T --> U["Update cached JObject"]

    N --> V["UpdatePSUserContentPath<br/>Variable(newPath)"]
    U --> V
    V --> W["Set $PSUserContentPath<br/>in global scope"]
    W --> X["Set $env:PSUserContentPath<br/>for child process<br/>inheritance"]
```

## Consumers — How Paths Derive from PSContentPath

```mermaid
flowchart LR
    CP["Utils.GetPSContentPath()"] --> MOD["Modules<br/>PSContentPath\\Modules"]
    CP --> PROF["Profiles<br/>PSContentPath\\profile.ps1"]
    CP --> HELP["Help<br/>PSContentPath\\Help"]
    CP --> SCRIPTS["Scripts<br/>PSContentPath\\Scripts"]

    MOD --> LEGACY{"PSContentPath ≠<br/>Documents\\PowerShell?"}
    LEGACY -->|Yes| FALLBACK["Legacy path added<br/>to $env:PSModulePath<br/>(Documents\\PowerShell\\Modules)"]
    LEGACY -->|No| NOFALLBACK["No extra path<br/>needed"]
```

## Config File I/O — Thread Safety

```mermaid
sequenceDiagram
    participant Caller
    participant Config as PowerShellConfig
    participant Lock as ReaderWriterLockSlim
    participant Cache as JObject Cache
    participant File as powershell.config.json

    Note over Caller,File: READ PATH (concurrent reads allowed)
    Caller->>Config: GetPSContentPath()
    Config->>Lock: Acquire read lock
    Lock-->>Config: Granted
    Config->>Cache: Check configRoots[CurrentUser]
    alt Cache miss (first read)
        Config->>File: Read & parse JSON
        Config->>Cache: Store via InterlockedCompareExchange
    end
    Cache-->>Config: Return JObject
    Config->>Lock: Release read lock
    Config-->>Caller: Return path value

    Note over Caller,File: WRITE PATH (exclusive lock)
    Caller->>Config: SetPSContentPath(path)
    Config->>Lock: Acquire write lock (exclusive)
    Lock-->>Config: Granted
    Config->>File: Open FileStream (FileShare.None)
    Config->>File: Read → modify → write back
    Config->>Cache: Update cached JObject
    Config->>Lock: Release write lock
    Config-->>Caller: Done
```
