# Unified HMI Theme/Layout Tools

Document id: `hmi-unified-theme-layout`

This document is the stable contract for high-level WinCC Unified screen styling tools.

## Tool Selection

| Tool | Use | Writes Project |
|---|---|---|
| `BuildUnifiedHmiThemeDesignJson` | Build theme/palette execution JSON offline | No |
| `BuildUnifiedHmiLayoutDesignJson` | Build grid layout execution JSON offline | No |
| `ApplyUnifiedHmiTheme` | Apply a theme to a real Unified screen through `ApplyUnifiedHmiScreenDesignJson` | Yes |
| `ApplyUnifiedHmiLayout` | Apply a grid layout to a real Unified screen through `ApplyUnifiedHmiScreenDesignJson` | Yes |
| `ApplyUnifiedHmiScreenDesignJson` | Low-level design executor used by the high-level tools | Yes |

## Theme Input

```json
{
  "name": "PlantClean",
  "palette": {
    "Page": "0xFFF4F6F8",
    "Surface": "0xFFFFFFFF",
    "Text": "0xFF172033",
    "Border": "0xFFD7DEE8"
  }
}
```

Colors must use TIA ARGB strings such as `0xFFF4F6F8`.

## Layout Input

```json
{
  "grid": 8,
  "left": 24,
  "top": 72,
  "gap": 16,
  "columns": 2,
  "cellWidth": 160,
  "cellHeight": 80,
  "items": [
    { "name": "Card_Run", "type": "Rectangle", "text": "运行" },
    { "name": "Card_Fault", "type": "Rectangle", "colSpan": 2, "text": "故障" }
  ]
}
```

The builder calculates `left`, `top`, `width`, and `height` from row/column settings and snaps them to the configured grid.

## Real Apply Checklist

1. Run `BuildUnifiedHmiThemeDesignJson` or `BuildUnifiedHmiLayoutDesignJson` first.
2. Inspect the generated execution JSON.
3. Run `Connect`, `GetProjectTree`, and `ValidateAutomationContext`.
4. Resolve the actual HMI software path and screen name.
5. Apply with `ApplyUnifiedHmiTheme` or `ApplyUnifiedHmiLayout`.
6. Read back changed objects with `DescribeHmiScreenItem`.
7. For buttons/events, run the action recipe flow and keep `SyntaxCheck` 0 error evidence.
8. Save only after readback succeeds.

## Safety Rules

- Theme/Layout tools must not create PLC tags or guessed bindings.
- Layout tools only position/style screen items; PLC-HMI synchronization is handled by the PLC symbol precheck suite.
- Real project writes require readback evidence before saving.
- HMI event scripts still require the separate action recipe safety contract and `SyntaxCheck` evidence.
