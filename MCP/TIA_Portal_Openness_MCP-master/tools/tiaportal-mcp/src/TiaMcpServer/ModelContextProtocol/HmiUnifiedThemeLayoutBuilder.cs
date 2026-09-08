using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TiaMcpServer.ModelContextProtocol
{
    /// <summary>
    /// Unified HMI 主题/布局执行 JSON 构建器。
    /// 只生成 ApplyUnifiedHmiScreenDesignJson 可消费的 JSON，不连接 TIA，不修改项目。
    /// </summary>
    public static class HmiUnifiedThemeLayoutBuilder
    {
        public static JsonObject BuildThemeDesign(JsonObject theme)
        {
            if (theme == null) throw new ArgumentNullException(nameof(theme));
            var palette = theme["palette"] as JsonObject ?? theme["Palette"] as JsonObject ?? theme;
            var screen = new JsonObject();

            CopyColor(palette, screen, "Page", "BackColor");
            CopyColor(palette, screen, "Background", "BackColor");

            var itemDefaults = new JsonObject();
            CopyColor(palette, itemDefaults, "Surface", "BackColor");
            CopyColor(palette, itemDefaults, "Text", "ForeColor");
            CopyColor(palette, itemDefaults, "Border", "BorderColor");

            return new JsonObject
            {
                ["screen"] = screen,
                ["items"] = new JsonArray(),
                ["theme"] = new JsonObject
                {
                    ["name"] = theme["name"]?.ToString() ?? theme["Name"]?.ToString() ?? "UnifiedTheme",
                    ["palette"] = CloneObject(palette),
                    ["defaultItemProperties"] = itemDefaults
                },
                ["offlineOnly"] = true,
                ["applyTool"] = "ApplyUnifiedHmiScreenDesignJson"
            };
        }

        public static JsonObject BuildLayoutDesign(JsonObject layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            var grid = GetInt(layout, "grid", "Grid", 8);
            var left = GetInt(layout, "left", "Left", "x", 0);
            var top = GetInt(layout, "top", "Top", "y", 0);
            var gap = GetInt(layout, "gap", "Gap", 8);
            var columns = Math.Max(1, GetInt(layout, "columns", "Columns", "cols", 1));
            var cellWidth = Math.Max(1, GetInt(layout, "cellWidth", "CellWidth", "width", 160));
            var cellHeight = Math.Max(1, GetInt(layout, "cellHeight", "CellHeight", "height", 80));
            var items = layout["items"] as JsonArray ?? layout["Items"] as JsonArray
                ?? throw new ArgumentException("layoutJson.items must be an array.");

            var outputItems = new JsonArray();
            for (var i = 0; i < items.Count; i++)
            {
                var src = items[i] as JsonObject ?? throw new ArgumentException("layoutJson.items[" + i + "] must be an object.");
                var name = src["name"]?.ToString() ?? src["Name"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(name))
                    throw new ArgumentException("layoutJson.items[" + i + "].name is required.");

                var col = GetInt(src, "col", "Col", -1);
                var row = GetInt(src, "row", "Row", -1);
                if (col < 0) col = i % columns;
                if (row < 0) row = i / columns;
                var colSpan = Math.Max(1, GetInt(src, "colSpan", "ColSpan", 1));
                var rowSpan = Math.Max(1, GetInt(src, "rowSpan", "RowSpan", 1));

                var item = new JsonObject
                {
                    ["name"] = name,
                    ["type"] = src["type"]?.DeepClone() ?? src["Type"]?.DeepClone() ?? JsonValue.Create("Rectangle"),
                    ["left"] = Snap(left + col * (cellWidth + gap), grid),
                    ["top"] = Snap(top + row * (cellHeight + gap), grid),
                    ["width"] = Snap(cellWidth * colSpan + gap * (colSpan - 1), grid),
                    ["height"] = Snap(cellHeight * rowSpan + gap * (rowSpan - 1), grid)
                };

                CopyOptional(src, item, "text", "Text");
                CopyOptional(src, item, "culture", "Culture");
                CopyOptional(src, item, "properties", "Properties");
                CopyOptional(src, item, "font", "Font");
                CopyOptional(src, item, "content", "Content");
                CopyOptional(src, item, "padding", "Padding");
                outputItems.Add(item);
            }

            return new JsonObject
            {
                ["screen"] = layout["screen"]?.DeepClone() ?? layout["Screen"]?.DeepClone() ?? new JsonObject(),
                ["items"] = outputItems,
                ["layout"] = new JsonObject
                {
                    ["grid"] = grid,
                    ["columns"] = columns,
                    ["gap"] = gap,
                    ["cellWidth"] = cellWidth,
                    ["cellHeight"] = cellHeight
                },
                ["offlineOnly"] = true,
                ["applyTool"] = "ApplyUnifiedHmiScreenDesignJson"
            };
        }

        public static JsonObject MergeDesigns(JsonObject themeDesign, JsonObject layoutDesign)
        {
            var screen = CloneObject(layoutDesign["screen"] as JsonObject ?? new JsonObject());
            if (themeDesign["screen"] is JsonObject themeScreen)
            {
                foreach (var prop in themeScreen)
                {
                    if (screen[prop.Key] == null)
                        screen[prop.Key] = prop.Value?.DeepClone();
                }
            }

            var defaultProps = themeDesign["theme"]?["defaultItemProperties"] as JsonObject ?? new JsonObject();
            var mergedItems = new JsonArray();
            foreach (var node in layoutDesign["items"] as JsonArray ?? new JsonArray())
            {
                if (node is not JsonObject item) continue;
                var copy = CloneObject(item);
                var props = copy["properties"] as JsonObject ?? copy["Properties"] as JsonObject ?? new JsonObject();
                foreach (var prop in defaultProps)
                {
                    if (props[prop.Key] == null)
                        props[prop.Key] = prop.Value?.DeepClone();
                }
                if (props.Count > 0) copy["properties"] = props;
                mergedItems.Add(copy);
            }

            return new JsonObject
            {
                ["screen"] = screen,
                ["items"] = mergedItems,
                ["theme"] = themeDesign["theme"]?.DeepClone(),
                ["layout"] = layoutDesign["layout"]?.DeepClone(),
                ["offlineOnly"] = true,
                ["applyTool"] = "ApplyUnifiedHmiScreenDesignJson"
            };
        }

        private static void CopyColor(JsonObject source, JsonObject target, string sourceName, string targetName)
        {
            var node = source[sourceName] ?? source[ToCamel(sourceName)];
            if (node == null) return;
            var value = node.ToString();
            if (!Regex.IsMatch(value, @"^0x[0-9a-fA-F]{8}$"))
                throw new ArgumentException("Color '" + sourceName + "' must use TIA ARGB format like 0xFFF4F6F8.");
            target[targetName] = value;
        }

        private static int Snap(int value, int grid)
        {
            if (grid <= 1) return value;
            return (int)Math.Round(value / (double)grid, MidpointRounding.AwayFromZero) * grid;
        }

        private static int GetInt(JsonObject obj, string name, string alias, int fallback)
        {
            return GetInt(obj, name, alias, "", fallback);
        }

        private static int GetInt(JsonObject obj, string name, string alias, string alias2, int fallback)
        {
            var node = obj[name] ?? obj[alias] ?? (string.IsNullOrWhiteSpace(alias2) ? null : obj[alias2]);
            return node != null && int.TryParse(node.ToString(), out var value) ? value : fallback;
        }

        private static void CopyOptional(JsonObject source, JsonObject target, string name, string alias)
        {
            var node = source[name] ?? source[alias];
            if (node != null) target[name] = node.DeepClone();
        }

        private static JsonObject CloneObject(JsonObject source)
        {
            var clone = new JsonObject();
            foreach (var prop in source)
            {
                clone[prop.Key] = prop.Value?.DeepClone();
            }
            return clone;
        }

        private static string ToCamel(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? value : char.ToLowerInvariant(value[0]) + value.Substring(1);
        }
    }
}
