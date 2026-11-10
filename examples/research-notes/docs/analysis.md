# Analysis notes

Document data assumptions, transformations and observations next to the project that uses them.

## Data quality

Record missing values and the filtering rule before interpreting a result.

```csharp
var usable = rows.Where(row => row.Amount >= 0).ToArray();
```

## Interpret the result

Explain what the calculation supports and what still requires investigation.
