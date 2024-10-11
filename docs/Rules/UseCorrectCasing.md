---
description: Use exact casing of cmdlet/function/parameter name.
ms.date: 06/28/2023
ms.topic: reference
title: UseCorrectCasing
---
# UseCorrectCasing

**Severity Level: Information**

## Description

This is a style/formatting rule. PowerShell is case insensitive wherever possible,
so the casing of cmdlet names, parameters, keywords and operators does not matter.
This rule nonetheless ensures consistent casing for clarity and readability.
Using lowercase keywords helps distinguish them from commands.
Using lowercase operators helps distinguish them from parameters.

## How

Use exact casing for type names.

Use exact casing of the cmdlet and its parameters, e.g.
`Invoke-Command { 'foo' } -RunAsAdministrator`.

Use lowercase for language keywords and operators.

## Example

### Wrong

```powershell
ForEach ($file IN get-childitem -recurse) {
    $file.Extension -Eq '.txt'
}
```

### Correct

```powershell
foreach ($file in Get-ChildItem -Recurse) {
    $file.Extension -eq '.txt'
}
```
