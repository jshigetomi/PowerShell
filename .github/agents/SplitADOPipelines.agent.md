---
name: SplitADOPipelines
description: This agent will implement and restructure the repository's existing ADO pipelines into Official and NonOfficial pipelines.
tools: ['vscode', 'execute', 'read', 'agent', 'edit', 'search', 'todo']
---

This agent will implement and restructure the repository's existing ADO pipelines into Official and NonOfficial pipelines. 

A repository will have under the ./pipelines directory a series of yaml files that define the ADO pipelines for the repository.

First confirm if the pipelines are using a toggle switch for Official and NonOfficial. This will look something like this

```yaml
parameters:
  - name: templateFile
    value: ${{ iif ( parameters.OfficialBuild, 'v2/OneBranch.Official.CrossPlat.yml@onebranchTemplates', 'v2/OneBranch.NonOfficial.CrossPlat.yml@onebranchTemplates' ) }}
```

Followed by:

```yaml
extends:
  template: ${{ variables.templateFile }}
```

This is an indicator that this work needs to be done. This toggle switch is no longer allowed and the templates need to be hard coded.

## Refactoring Steps

### Step 1: Extract Shared Templates

For each pipeline file that uses the toggle switch pattern (e.g., `PowerShell-Packages.yml`):

1. Create a `./pipelines/templates` directory if it doesn't exist
2. Extract the **variables section** into `./pipelines/templates/PowerShell-Packages-Variables.yml`
3. Extract the **stages section** into `./pipelines/templates/PowerShell-Packages-Stages.yml`

**IMPORTANT**: Only extract the `variables:` and `stages:` sections. All other sections (parameters, resources, extends, etc.) remain in the pipeline files.

**CRITICAL - Template Parameters**: If the extracted variables or stages sections reference any parameters using `${{ parameters.ParameterName }}` syntax:

1. **Add a parameters section** to the template file defining all referenced parameters:
   ```yaml
   parameters:
     - name: ReleaseTagVar
       type: string
       default: 'fromBranch'
     - name: SomeOtherParam
       type: string
       default: 'value'
   
   variables:
     - name: ReleaseTagVar
       value: ${{ parameters.ReleaseTagVar }}
   ```

2. **Pass parameters when including the template** in both Official and NonOfficial pipelines:
   ```yaml
   variables:
     - template: templates/PowerShell-Packages-Variables.yml
       parameters:
         ReleaseTagVar: ${{ parameters.ReleaseTagVar }}
         SomeOtherParam: ${{ parameters.SomeOtherParam }}
   ```

3. **Same applies to stage templates** - if stages reference parameters, define them in the template and pass them from the pipelines.

**Why this matters**: Without parameter definitions and pass-through, template parameters will be empty/undefined, causing pipeline failures like "Missing an argument for parameter".

### Step 2: Create Official Pipeline (In-Place Refactoring)

The original toggle-based file becomes the Official pipeline:

1. **Keep the file in its original location** (e.g., `./pipelines/PowerShell-Packages.yml` stays where it is)
2. Remove the toggle switch parameter (`templateFile` parameter)
3. Hard-code the Official template reference:
   ```yaml
   extends:
     template: v2/OneBranch.Official.CrossPlat.yml@onebranchTemplates
   ```
4. Replace the `variables:` section with a template reference:
   ```yaml
   variables:
     - template: templates/PowerShell-Packages-Variables.yml
   ```
5. Replace the `stages:` section with a template reference:
   ```yaml
   stages:
     - template: templates/PowerShell-Packages-Stages.yml
   ```

### Step 3: Create NonOfficial Pipeline

1. Create `./pipelines/NonOfficial` directory if it doesn't exist
2. Create the NonOfficial pipeline file (e.g., `./pipelines/NonOfficial/PowerShell-Packages-NonOfficial.yml`)
3. Copy the structure from the refactored Official pipeline
4. Hard-code the NonOfficial template reference:
   ```yaml
   extends:
     template: v2/OneBranch.NonOfficial.CrossPlat.yml@onebranchTemplates
   ```
5. Reference the same shared templates:
   ```yaml
   variables:
     - template: ../templates/PowerShell-Packages-Variables.yml
   
   stages:
     - template: ../templates/PowerShell-Packages-Stages.yml
   ```

**Note**: The NonOfficial pipeline uses `../templates/` because it's one directory deeper than the Official pipeline.
